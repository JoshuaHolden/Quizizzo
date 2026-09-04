using Quizizzo.GameContracts;

namespace Quizizzo.Games.VoiceChoon;

public enum VoiceNoteRating
{
    Good,
    Great,
    Perfect
}

public sealed record VoiceChoonParticipant(Guid PlayerId, string DisplayName, int PlayerIndex);

public sealed record VoiceNoteJudgement(
    Guid NoteId,
    int Lane,
    VoiceNoteRating Rating,
    int TimingErrorMilliseconds,
    int Points);

public sealed record VoiceChoonResult(
    Guid PlayerId,
    string DisplayName,
    int Rank,
    int Score,
    int JudgedNotes,
    int TotalNotes,
    int AccuracyPercent);

public sealed record VoiceChoonGameState(
    string SongName,
    double SongDurationSeconds,
    IReadOnlyList<MidiSongSection> Sections,
    IReadOnlyList<VoiceChoonParticipant> Participants,
    IReadOnlyList<PlayerChart> Charts,
    IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, Guid>> SampleAssetIdsByPlayer,
    IReadOnlyList<Guid> RecordingReadyPlayerIds,
    IReadOnlyList<Guid> ControllerReadyPlayerIds,
    DateTimeOffset? SongStartsAtUtc,
    IReadOnlyDictionary<Guid, long> LastSequenceByPlayer,
    IReadOnlyDictionary<Guid, IReadOnlyList<VoiceNoteJudgement>> JudgementsByPlayer,
    IReadOnlyDictionary<Guid, int> ScoresByPlayer,
    int BandCombo,
    int MaximumBandCombo,
    int EnergyPercent,
    IReadOnlyList<VoiceChoonResult> Results,
    VoiceChoonDifficulty Difficulty = VoiceChoonDifficulty.Medium,
    bool SoloAutoplayTest = false,
    string SongKey = VoiceChoonSongCatalog.DefaultSongKey);

public sealed record VoiceChoonPlayerState(
    string InstrumentName,
    PlayerChart Chart,
    IReadOnlyList<VoiceNoteJudgement> Judgements,
    int Score,
    int BandCombo,
    int EnergyPercent,
    DateTimeOffset? SongStartsAtUtc,
    long NextSequence);

public sealed record VoiceChoonDisplayState(
    string SongName,
    double SongDurationSeconds,
    IReadOnlyList<MidiSongSection> Sections,
    DateTimeOffset? SongStartsAtUtc,
    int BandScore,
    int BandCombo,
    int MaximumBandCombo,
    int EnergyPercent,
    IReadOnlyList<VoiceChoonResult> Results,
    IReadOnlyList<VoiceChoonDisplayPlayback>? Playback = null);