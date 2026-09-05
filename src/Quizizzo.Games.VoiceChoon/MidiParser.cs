using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace Quizizzo.Games.VoiceChoon;

public sealed class MidiParser
{
    public static RawMidiSong Parse(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The MIDI stream must be readable.", nameof(stream));
        }
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("A source name is required.", nameof(sourceName));
        }

        var midi = MidiFile.Read(stream, new ReadingSettings
        {
            NoHeaderChunkPolicy = NoHeaderChunkPolicy.Abort,
            NotEnoughBytesPolicy = NotEnoughBytesPolicy.Abort,
            InvalidChunkSizePolicy = InvalidChunkSizePolicy.Abort
        });
        var ticksPerQuarter = midi.TimeDivision is TicksPerQuarterNoteTimeDivision division
            ? division.TicksPerQuarterNote
            : throw new NotSupportedException("SMPTE MIDI time divisions are not supported by VoiceChoon.");
        var tempoMap = midi.GetTempoMap();
        var tracks = midi.GetTrackChunks()
            .Select((chunk, index) => ParseTrack(chunk, index, tempoMap))
            .Where(track => track.Notes.Count > 0)
            .ToArray();
        if (tracks.Length == 0)
        {
            throw new InvalidDataException("The MIDI file does not contain playable note tracks.");
        }

        var sections = midi.GetTrackChunks()
            .SelectMany(chunk => chunk.GetTimedEvents())
            .Where(item => item.Event is MarkerEvent)
            .Select(item => new MidiSongSection(
                ((MarkerEvent)item.Event).Text.Trim(),
                item.Time,
                SecondsAt(item.Time, tempoMap)))
            .Where(section => !string.IsNullOrWhiteSpace(section.Name))
            .DistinctBy(section => (section.Name, section.StartTicks))
            .OrderBy(section => section.StartTicks)
            .ToArray();
        var duration = tracks.SelectMany(track => track.Notes)
            .Max(note => note.StartTimeSeconds + note.DurationSeconds);

        return new RawMidiSong(sourceName.Trim(), ticksPerQuarter, duration, tracks, sections);
    }

    private static RawMidiTrack ParseTrack(TrackChunk chunk, int index, TempoMap tempoMap)
    {
        var name = chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Track {index + 1}";
        }
        var detectedNotes = chunk.GetNotes().ToArray();
        var sustainEvents = chunk.GetTimedEvents()
            .Where(item => item.Event is ControlChangeEvent control && (int)control.ControlNumber == 64)
            .Select(item => (item.Time, Control: (ControlChangeEvent)item.Event))
            .OrderBy(item => item.Time)
            .ToArray();
        var notes = detectedNotes
            .Select(note => new RawMidiNote(
                note.Time,
                SustainedEndTime(note, detectedNotes, sustainEvents) - note.Time,
                SecondsAt(note.Time, tempoMap),
                SecondsAt(SustainedEndTime(note, detectedNotes, sustainEvents), tempoMap) - SecondsAt(note.Time, tempoMap),
                note.NoteNumber,
                note.Velocity,
                note.Channel))
            .OrderBy(note => note.StartTicks)
            .ThenBy(note => note.MidiNote)
            .ToArray();
        var isPercussion = notes.Any(note => note.Channel == 9);
        var programEvent = chunk.Events.OfType<ProgramChangeEvent>().FirstOrDefault();
        var programNumber = programEvent is null ? (int?)null : (int)programEvent.ProgramNumber;
        return new RawMidiTrack(index, name, InferRole(name, isPercussion, notes), isPercussion, notes,
            programNumber);
    }

    private static long SustainedEndTime(Note note, IReadOnlyList<Note> notes,
        IReadOnlyList<(long Time, ControlChangeEvent Control)> sustainEvents)
    {
        // Determine whether the sustain pedal is held at note-on: find the last pedal event
        // at or before note.Time on the same channel. If that event is a pedal-down (≥64)
        // the pedal was already held when this note started.
        var pedalAtNoteOn = sustainEvents.LastOrDefault(item =>
            item.Time <= note.Time && item.Control.Channel == note.Channel);
        var pedalDownAtStart = pedalAtNoteOn.Control is not null &&
            (int)pedalAtNoteOn.Control.ControlValue >= 64;

        // The pedal may also have been pressed while the note was already sounding.
        var pedalPressedDuringNote = sustainEvents.Any(item =>
            item.Time > note.Time && item.Time <= note.EndTime &&
            item.Control.Channel == note.Channel && (int)item.Control.ControlValue >= 64);

        if (!pedalDownAtStart && !pedalPressedDuringNote) return note.EndTime;

        // Find the next pedal-up after note-off.
        var pedalUp = sustainEvents.FirstOrDefault(item => item.Time > note.EndTime &&
            item.Control.Channel == note.Channel && (int)item.Control.ControlValue < 64).Time;
        if (pedalUp <= note.EndTime) return note.EndTime;
        var restrike = notes.Where(candidate => candidate.Channel == note.Channel &&
                candidate.NoteNumber == note.NoteNumber && candidate.Time > note.Time)
            .Select(candidate => candidate.Time)
            .DefaultIfEmpty(pedalUp)
            .Min();
        return Math.Min(pedalUp, restrike);
    }

    private static double SecondsAt(long ticks, TempoMap tempoMap) =>
        TimeConverter.ConvertTo<MetricTimeSpan>(ticks, tempoMap).TotalSeconds;

    internal static VoiceChoonTrackRole InferRole(
        string name,
        bool isPercussion,
        IReadOnlyList<RawMidiNote> notes)
    {
        var normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (isPercussion || ContainsAny(normalized, "drum", "beatbox", "kit", "percussion")) return VoiceChoonTrackRole.Drums;
        if (ContainsAny(normalized, "bass")) return VoiceChoonTrackRole.Bass;
        if (ContainsAny(normalized, "chord", "pad", "hum")) return VoiceChoonTrackRole.Chords;
        if (ContainsAny(normalized, "leada", "lead1", "melodya", "melody")) return VoiceChoonTrackRole.LeadA;
        if (ContainsAny(normalized, "leadb", "lead2", "response", "melodyb")) return VoiceChoonTrackRole.LeadB;
        if (ContainsAny(normalized, "percfx", "percussionfx", "fx")) return VoiceChoonTrackRole.PercussionFx;
        if (ContainsAny(normalized, "arp", "sparkle")) return VoiceChoonTrackRole.Arp;
        if (ContainsAny(normalized, "stab", "shout")) return VoiceChoonTrackRole.VocalStabs;

        // Low average pitch suggests bass — but guard against non-channel-10 percussion tracks
        // whose repeated kick/snare hits on a handful of pitches mimic a bass average.
        // If ≥60% of notes share any single MIDI pitch (typical of a kick pattern on an offbeat
        // channel) treat the track as Other rather than Bass.
        if (notes.Count > 0 && notes.Average(note => note.MidiNote) < 48)
        {
            var maxSinglePitchFraction = notes
                .GroupBy(note => note.MidiNote)
                .Max(group => (double)group.Count() / notes.Count);
            return maxSinglePitchFraction < 0.6 ? VoiceChoonTrackRole.Bass : VoiceChoonTrackRole.Other;
        }
        return VoiceChoonTrackRole.Other;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(value.Contains);

    // Exposed for unit tests — mirrors the private SustainedEndTime signature exactly.
    internal static long SustainedEndTimePublic(
        Melanchall.DryWetMidi.Interaction.Note note,
        IReadOnlyList<Melanchall.DryWetMidi.Interaction.Note> notes,
        IReadOnlyList<(long Time, Melanchall.DryWetMidi.Core.ControlChangeEvent Control)> sustainEvents)
        => SustainedEndTime(note, notes, sustainEvents);
}
