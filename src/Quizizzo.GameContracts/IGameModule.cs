namespace Quizizzo.GameContracts;

public interface IGameModule
{
    GameDescriptor Descriptor { get; }

    GameModuleState Start(GameStartContext context);

    GameTransition Apply(
        GameModuleState state,
        GameActionContext context,
        IGameAction action);

    GameViewPayload CreateView(
        GameModuleState state,
        GameViewContext context);
}

public sealed class GameRuleViolationException(string code, string message) : Exception(message)
{
    public string Code { get; } = string.IsNullOrWhiteSpace(code)
        ? throw new ArgumentException("A rule violation code is required.", nameof(code))
        : code;
}
