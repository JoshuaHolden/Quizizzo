using Quizizzo.GameContracts;

namespace Quizizzo.Games.VoiceChoon;

public sealed record ConfirmVoiceRecordingsAction : IGameAction
{
    public const string ActionKind = "voicechoon.recordings-ready";
    public string Kind => ActionKind;
}

public sealed record RegisterVoiceSampleAction(string PromptKey, Guid AssetId) : IGameAction
{
    public const string ActionKind = "voicechoon.register-sample";
    public string Kind => ActionKind;
}

public sealed record ReadyVoiceControllerAction : IGameAction
{
    public const string ActionKind = "voicechoon.ready";
    public string Kind => ActionKind;
}

public sealed record SubmitVoiceInputAction(
    long Sequence,
    int Lane,
    DateTimeOffset ClientTimestamp,
    bool Released = false) : IGameAction
{
    public const string ActionKind = "voicechoon.input";
    public string Kind => ActionKind;
}

public sealed record AdvanceVoiceChoonAction : IGameAction
{
    public const string ActionKind = "voicechoon.advance";
    public string Kind => ActionKind;
}
