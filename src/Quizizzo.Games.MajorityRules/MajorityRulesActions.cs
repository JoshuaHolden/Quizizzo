using Quizizzo.GameContracts;

namespace Quizizzo.Games.MajorityRules;

public sealed record SubmitMajorityAnswerAction(string Value) : IGameAction
{
    public const string ActionKind = "majority-rules.submit-answer";
    public string Kind => ActionKind;
}

public sealed record VoteForMajorityAnswerAction(Guid AnswerOptionId) : IGameAction
{
    public const string ActionKind = "majority-rules.vote";
    public string Kind => ActionKind;
}

public sealed record AdvanceMajorityRulesAction : IGameAction
{
    public const string ActionKind = "majority-rules.advance";
    public string Kind => ActionKind;
}
