namespace Quizizzo.Games.VoiceChoon;

public static class PitchShiftPlanner
{
    public const int MaximumSemitoneShift = 18;

    public static PitchShiftPlan Plan(
        int targetMidiNote,
        double noteDurationSeconds,
        IReadOnlyList<RecordedSample> samples)
    {
        if (targetMidiNote is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(targetMidiNote));
        }
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("At least one recorded sample is required.", nameof(samples));
        }

        var sample = samples.MinBy(candidate => Math.Abs(targetMidiNote - candidate.RootMidiNote))!;
        if (sample.Style == RecordingStyle.Percussion)
        {
            return new PitchShiftPlan(sample.Key, targetMidiNote, sample.RootMidiNote, 0, 1, false, null, null);
        }

        var playbackNote = FoldNearRoot(targetMidiNote, sample.RootMidiNote);
        var semitones = playbackNote - sample.RootMidiNote;
        var loop = sample.Style is (RecordingStyle.Sustained or RecordingStyle.SoftSustain or
            RecordingStyle.Brass or RecordingStyle.Woodwind) && noteDurationSeconds >= 0.5;
        return new PitchShiftPlan(
            sample.Key,
            targetMidiNote,
            playbackNote,
            semitones,
            Math.Pow(2, semitones / 12d),
            loop,
            loop ? sample.DurationSeconds * 0.3 : null,
            loop ? sample.DurationSeconds * 0.7 : null);
    }

    public static int FoldNearRoot(int targetMidiNote, int rootMidiNote)
    {
        var folded = targetMidiNote;
        while (folded - rootMidiNote > MaximumSemitoneShift) folded -= 12;
        while (folded - rootMidiNote < -MaximumSemitoneShift) folded += 12;
        return Math.Clamp(folded, 0, 127);
    }
}
