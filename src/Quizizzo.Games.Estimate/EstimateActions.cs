using Quizizzo.GameContracts;

namespace Quizizzo.Games.Estimate;

public sealed record SubmitEstimateAction(long Value) : IGameAction
{
    public const string ActionKind = "estimate.submit";
    public string Kind => ActionKind;
}

public sealed record AdvanceEstimateAction : IGameAction
{
    public const string ActionKind = "estimate.advance";
    public string Kind => ActionKind;
}
