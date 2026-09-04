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
        var notes = chunk.GetNotes()
            .Select(note => new RawMidiNote(
                note.Time,
                note.Length,
                SecondsAt(note.Time, tempoMap),
                SecondsAt(note.EndTime, tempoMap) - SecondsAt(note.Time, tempoMap),
                note.NoteNumber,
                note.Velocity,
                note.Channel))
            .OrderBy(note => note.StartTicks)
            .ThenBy(note => note.MidiNote)
            .ToArray();
        var isPercussion = notes.Any(note => note.Channel == 9);
        return new RawMidiTrack(index, name, InferRole(name, isPercussion, notes), isPercussion, notes);
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

        if (notes.Count > 0 && notes.Average(note => note.MidiNote) < 48)
        {
            return VoiceChoonTrackRole.Bass;
        }
        return VoiceChoonTrackRole.Other;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(value.Contains);
}
