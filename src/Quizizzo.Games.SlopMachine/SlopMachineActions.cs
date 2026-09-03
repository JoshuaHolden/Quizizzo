using Quizizzo.GameContracts;

namespace Quizizzo.Games.SlopMachine;

public sealed record SubmitSlopTextAction(
    string Value,
    IReadOnlyList<string>? Values = null) : IGameAction
{
    public const string ActionKind = "slop-machine.submit-text";
    public string Kind => ActionKind;
}

public sealed record VoteForSlopAction(Guid OptionId) : IGameAction
{
    public const string ActionKind = "slop-machine.vote";
    public string Kind => ActionKind;
}

public sealed record RespinSlopReelAction(string Reel) : IGameAction
{
    public const string ActionKind = "slop-machine.respin";
    public string Kind => ActionKind;
}

public sealed record MatchTelephoneThumbnailAction(string ThumbnailId) : IGameAction
{
    public const string ActionKind = "slop-machine.telephone-match";
    public string Kind => ActionKind;
}

public sealed record IdentifyMachineTitleAction(Guid OptionId) : IGameAction
{
    public const string ActionKind = "slop-machine.identify-machine";
    public string Kind => ActionKind;
}

public sealed record AdvanceSlopMachineAction : IGameAction
{
    public const string ActionKind = "slop-machine.advance";
    public string Kind => ActionKind;
}
