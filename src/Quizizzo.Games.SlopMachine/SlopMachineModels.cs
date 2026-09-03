namespace Quizizzo.Games.SlopMachine;

public sealed record SlopThumbnail(
    string Id,
    string ImageUrl,
    string AlternativeText,
    string Category,
    string Composition,
    IReadOnlyList<string> AiTitles);

public sealed record SlopParticipant(Guid PlayerId, string DisplayName, int StartingScore);

public sealed record SlopAssignment(
    string ThumbnailId,
    string Format,
    string? CommentType = null,
    bool RespinUsed = false,
    Guid? SourceSubmissionId = null);

public sealed record SlopSubmission(
    Guid SubmissionId,
    Guid AuthorId,
    string ThumbnailId,
    string Text,
    Guid? PartnerId = null,
    int Votes = 0,
    int PointsAwarded = 0,
    bool WonBonus = false,
    Guid? ParentSubmissionId = null,
    string? CommentType = null);

public sealed record TelephoneMatch(
    Guid MatcherId,
    Guid WriterId,
    Guid SubmissionId,
    string IntendedThumbnailId,
    IReadOnlyList<string> OptionThumbnailIds,
    string? SelectedThumbnailId = null,
    bool IsCorrect = false);

public sealed record SlopOption(
    Guid OptionId,
    string Text,
    Guid? AuthorId,
    bool IsMachine = false,
    string? ThumbnailId = null,
    Guid? PartnerId = null);

public sealed record SlopBonus(Guid PlayerId, string Label, int Points);

public sealed record SlopMachineState(
    IReadOnlyList<SlopParticipant> Participants,
    IReadOnlyList<string> UsedThumbnailIds,
    int FreshHeat,
    string? ActiveThumbnailId,
    IReadOnlyDictionary<Guid, SlopAssignment> Assignments,
    IReadOnlyDictionary<Guid, string> TextSubmissions,
    IReadOnlyList<SlopSubmission> Uploads,
    IReadOnlyList<SlopOption> Options,
    IReadOnlyDictionary<Guid, Guid> Votes,
    IReadOnlyDictionary<Guid, TelephoneMatch> TelephoneMatches,
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> MachineGuesses,
    IReadOnlyList<SlopBonus> Bonuses,
    IReadOnlyDictionary<Guid, int> EarnedViews,
    IReadOnlyDictionary<Guid, int> ScoreReviewStart,
    bool MachineWonFinal,
    string Message,
    int VoteHeat = 0,
    IReadOnlyList<IReadOnlyList<Guid>>? VoteHeats = null,
    IReadOnlyDictionary<Guid, int>? VotingOpportunities = null);
