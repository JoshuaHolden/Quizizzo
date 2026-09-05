namespace Quizizzo.Games.VoiceChoon;

public enum VoiceChoonTrackRole
{
    Drums,
    Bass,
    Chords,
    LeadA,
    LeadB,
    PercussionFx,
    Arp,
    VocalStabs,
    Other
}

public enum RhythmNoteType
{
    Tap,
    Hold
}

public enum RecordingStyle
{
    OneShot,
    Sustained,
    Percussion
}

public enum VoiceChoonDifficulty
{
    Easy,
    Medium,
    Hard
}

public sealed record VoiceChoonGameConfiguration(
    VoiceChoonDifficulty Difficulty = VoiceChoonDifficulty.Medium,
    string SongKey = VoiceChoonSongCatalog.DefaultSongKey);

public sealed record RawMidiSong(
    string SourceName,
    int TicksPerQuarterNote,
    double DurationSeconds,
    IReadOnlyList<RawMidiTrack> Tracks,
    IReadOnlyList<MidiSongSection> Sections);

public sealed record RawMidiTrack(
    int Index,
    string Name,
    VoiceChoonTrackRole Role,
    bool IsPercussion,
    IReadOnlyList<RawMidiNote> Notes);

public sealed record RawMidiNote(
    long StartTicks,
    long DurationTicks,
    double StartTimeSeconds,
    double DurationSeconds,
    int MidiNote,
    int Velocity,
    int Channel);

public sealed record MidiSongSection(string Name, long StartTicks, double StartTimeSeconds);

public sealed record InstrumentAssignment(
    int PlayerIndex,
    IReadOnlyList<RawMidiTrack> Tracks,
    IReadOnlyList<SoundRecordingPrompt> RecordingPrompts);

public sealed record SoundRecordingPrompt(
    string Key,
    string Label,
    string Example,
    RecordingStyle Style,
    int RootMidiNote,
    string Guidance);

public sealed record PlayerChart(
    int PlayerIndex,
    string InstrumentName,
    IReadOnlyList<RhythmNote> Notes,
    IReadOnlyList<RhythmNote> PlaybackNotes,
    IReadOnlyList<SoundRecordingPrompt> RecordingPrompts,
    double ActiveSeconds);

public sealed record RhythmNote(
    Guid Id,
    int PlayerIndex,
    int Lane,
    double StartTimeSeconds,
    double DurationSeconds,
    int TargetMidiNote,
    int Velocity,
    string SourceTrack,
    VoiceChoonTrackRole SourceRole,
    RhythmNoteType Type);

public sealed record ChartGenerationOptions
{
    public double HoldThresholdSeconds { get; init; } = 0.5;
    public double QuantizationSeconds { get; init; } = 0.01;
    public int MaximumPressesPerSecond { get; init; } = 5;
    public int MaximumSimultaneousPads { get; init; } = 2;
    public double MinimumLaneGapSeconds { get; init; } = 0.08;
    public double ActivityGapSeconds { get; init; } = 4;
    public double? RapidRunGapSeconds { get; init; }
    public int RapidRunMinimumNotes { get; init; } = 3;

    public void Validate()
    {
        if (HoldThresholdSeconds <= 0 || QuantizationSeconds <= 0 ||
            MinimumLaneGapSeconds < 0 || ActivityGapSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HoldThresholdSeconds));
        }
        if (MaximumPressesPerSecond is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPressesPerSecond));
        }
        if (MaximumSimultaneousPads is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSimultaneousPads));
        }
        if (RapidRunGapSeconds is <= 0 || RapidRunMinimumNotes is < 2 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(RapidRunGapSeconds));
        }
    }
}

public sealed record RecordedSample(
    string Key,
    int RootMidiNote,
    RecordingStyle Style,
    double DurationSeconds);

public sealed record PitchShiftPlan(
    string SampleKey,
    int RequestedMidiNote,
    int PlaybackMidiNote,
    int SemitoneShift,
    double PlaybackRate,
    bool Loop,
    double? LoopStartSeconds,
    double? LoopEndSeconds);
