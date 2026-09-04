using System.Security.Cryptography;
using System.Text;

namespace Quizizzo.Games.VoiceChoon;

public sealed class ChartGenerator(ChartGenerationOptions? options = null)
{
    private readonly ChartGenerationOptions options = Validate(options ?? new ChartGenerationOptions());

    public IReadOnlyList<PlayerChart> Generate(IReadOnlyList<InstrumentAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        return assignments.Select(GeneratePlayerChart).ToArray();
    }

    private PlayerChart GeneratePlayerChart(InstrumentAssignment assignment)
    {
        var playbackNotes = assignment.Tracks
            .SelectMany(track => CreateCandidates(assignment.PlayerIndex, track))
            .OrderBy(note => note.StartTimeSeconds)
            .ThenBy(note => note.Lane)
            .ToArray();
        var candidates = playbackNotes
            .GroupBy(note => Math.Round(note.StartTimeSeconds / options.QuantizationSeconds))
            .SelectMany(group => group
                .OrderBy(note => InstrumentAssignmentService.Priority(note.SourceRole))
                .ThenByDescending(note => note.Velocity)
                .DistinctBy(note => note.Lane)
                .Take(options.MaximumSimultaneousPads))
            .OrderBy(note => note.StartTimeSeconds)
            .ThenBy(note => note.Lane)
            .ToArray();

        var accepted = new List<RhythmNote>();
        var recentPresses = new Queue<double>();
        var lastPressByLane = new double?[4];
        foreach (var note in candidates)
        {
            while (recentPresses.TryPeek(out var time) && note.StartTimeSeconds - time >= 1)
            {
                recentPresses.Dequeue();
            }
            if (recentPresses.Count >= options.MaximumPressesPerSecond)
            {
                continue;
            }
            if (lastPressByLane[note.Lane] is { } lastPress &&
                note.StartTimeSeconds - lastPress < options.MinimumLaneGapSeconds)
            {
                continue;
            }
            accepted.Add(note);
            recentPresses.Enqueue(note.StartTimeSeconds);
            lastPressByLane[note.Lane] = note.StartTimeSeconds;
        }

        var playableNotes = CollapseRapidRuns(accepted);
        var activeSeconds = playableNotes.Count == 0
            ? 0
            : playableNotes.Select(note => Math.Min(note.DurationSeconds, options.ActivityGapSeconds))
                .Sum();
        var instrumentName = string.Join(" + ", assignment.Tracks.Select(track => track.Name));
        return new PlayerChart(
            assignment.PlayerIndex,
            instrumentName,
            playableNotes,
            playbackNotes,
            assignment.RecordingPrompts,
            activeSeconds);
    }

    private IReadOnlyList<RhythmNote> CollapseRapidRuns(List<RhythmNote> notes)
    {
        if (options.RapidRunGapSeconds is not { } maximumGap)
        {
            return notes;
        }

        var collapsed = new List<RhythmNote>(notes.Count);
        foreach (var lane in notes.GroupBy(note => note.Lane))
        {
            var ordered = lane.OrderBy(note => note.StartTimeSeconds).ToArray();
            for (var index = 0; index < ordered.Length;)
            {
                var end = index + 1;
                while (end < ordered.Length &&
                       ordered[end - 1].Type == RhythmNoteType.Tap &&
                       ordered[end].Type == RhythmNoteType.Tap &&
                       ordered[end].StartTimeSeconds - ordered[end - 1].StartTimeSeconds <= maximumGap)
                {
                    end++;
                }

                var run = ordered[index..end];
                if (run.Length >= options.RapidRunMinimumNotes)
                {
                    var first = run[0];
                    var last = run[^1];
                    collapsed.Add(first with
                    {
                        DurationSeconds = last.StartTimeSeconds + last.DurationSeconds - first.StartTimeSeconds,
                        Type = RhythmNoteType.Hold
                    });
                }
                else
                {
                    collapsed.AddRange(run);
                }
                index = end;
            }
        }

        return collapsed.OrderBy(note => note.StartTimeSeconds).ThenBy(note => note.Lane).ToArray();
    }

    private IEnumerable<RhythmNote> CreateCandidates(int playerIndex, RawMidiTrack track)
    {
        var pitches = track.Notes.Select(note => note.MidiNote).Distinct().Order().ToArray();
        var lastLane = -1;
        var repeatedLaneCount = 0;
        foreach (var (note, noteIndex) in track.Notes.Select((note, index) => (note, index)))
        {
            var lane = track.Role == VoiceChoonTrackRole.Drums || track.IsPercussion
                ? DrumLane(note.MidiNote)
                : PitchLane(note.MidiNote, pitches);
            repeatedLaneCount = lane == lastLane ? repeatedLaneCount + 1 : 1;
            if (repeatedLaneCount >= 3 && pitches.Length > 4)
            {
                lane = lane == 3 ? 2 : lane + 1;
                repeatedLaneCount = 0;
            }
            lastLane = lane;
            var start = Quantize(note.StartTimeSeconds);
            yield return new RhythmNote(
                StableId(playerIndex, track.Index, noteIndex, note.StartTicks, note.MidiNote, lane),
                playerIndex,
                lane,
                start,
                Math.Max(options.QuantizationSeconds, note.DurationSeconds),
                note.MidiNote,
                note.Velocity,
                track.Name,
                track.Role,
                note.DurationSeconds >= options.HoldThresholdSeconds ? RhythmNoteType.Hold : RhythmNoteType.Tap);
        }
    }

    public static int DrumLane(int midiNote) => midiNote switch
    {
        35 or 36 => 0,
        38 or 39 or 40 => 1,
        42 or 44 or 46 => 2,
        _ => 3
    };

    internal static int PitchLane(int midiNote, IReadOnlyList<int> orderedPitches)
    {
        if (orderedPitches.Count == 0) return 0;
        var rank = 0;
        while (rank < orderedPitches.Count && orderedPitches[rank] < midiNote) rank++;
        return Math.Min(3, rank * 4 / orderedPitches.Count);
    }

    private double Quantize(double seconds) =>
        Math.Round(seconds / options.QuantizationSeconds) * options.QuantizationSeconds;

    private static Guid StableId(
        int playerIndex,
        int trackIndex,
        int noteIndex,
        long ticks,
        int midiNote,
        int lane)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"voicechoon:{playerIndex}:{trackIndex}:{noteIndex}:{ticks}:{midiNote}:{lane}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static ChartGenerationOptions Validate(ChartGenerationOptions value)
    {
        value.Validate();
        return value;
    }
}
