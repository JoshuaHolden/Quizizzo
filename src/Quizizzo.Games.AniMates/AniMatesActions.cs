using Quizizzo.GameContracts;

namespace Quizizzo.Games.AniMates;

public sealed record SubmitAnimationAction(IReadOnlyList<Guid> FrameAssetIds) : IGameAction
{
    public const string ActionKind = "animates.submit";
    public string Kind => ActionKind;
}

public sealed record VoteForAnimationAction(Guid SubmissionPlayerId) : IGameAction
{
    public const string ActionKind = "animates.vote";
    public string Kind => ActionKind;
}

public sealed record AdvanceAniMatesAction : IGameAction
{
    public const string ActionKind = "animates.advance";
    public string Kind => ActionKind;
}
