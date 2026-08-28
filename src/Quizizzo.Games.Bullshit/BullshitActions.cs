using Quizizzo.GameContracts;

namespace Quizizzo.Games.Bullshit;

public sealed record SubmitBluffAction(string Value) : IGameAction
{
    public const string ActionKind = "bullshit.submit-bluff";
    public string Kind => ActionKind;
}

public sealed record ChooseBullshitAnswerAction(Guid ChoiceId) : IGameAction
{
    public const string ActionKind = "bullshit.choose-answer";
    public string Kind => ActionKind;
}

public sealed record AdvanceBullshitAction : IGameAction
{
    public const string ActionKind = "bullshit.advance";
    public string Kind => ActionKind;
}
