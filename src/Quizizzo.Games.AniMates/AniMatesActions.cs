using Quizizzo.GameContracts;

namespace Quizizzo.Games.AniMates;

public sealed record SubmitAnimationAction(IReadOnlyList<Guid> FrameAssetIds) : IGameAction
{
    public const string ActionKind = "animates.submit";
    public string Kind => ActionKind;
}

public sealed record SubmitAnimationGuessAction(string Value) : IGameAction
{
    public const string ActionKind = "animates.submit-guess";
    public string Kind => ActionKind;
}

public sealed record ChooseAnimationAnswerAction(Guid AnswerOptionId) : IGameAction
{
    public const string ActionKind = "animates.choose-answer";
    public string Kind => ActionKind;
}

public sealed record AdvanceAniMatesAction : IGameAction
{
    public const string ActionKind = "animates.advance";
    public string Kind => ActionKind;
}
