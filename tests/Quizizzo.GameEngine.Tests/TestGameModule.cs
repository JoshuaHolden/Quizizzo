using System.Text.Json;
using Quizizzo.GameContracts;

namespace Quizizzo.GameEngine.Tests;

internal sealed class TestGameModule(TimeSpan? initialDeadline = null) : IGameModule
{
    public GameDescriptor Descriptor { get; } = new("test-game", "Test Game", 1, 12);

    public GameModuleState Start(GameStartContext context) => new(
        1,
        "Collecting",
        initialDeadline.HasValue ? context.StartedAtUtc.Add(initialDeadline.Value) : null,
        false,
        GameJson.From(new TestState(0, "host-only-answer")));

    public GameTransition Apply(
        GameModuleState state,
        GameActionContext context,
        IGameAction action)
    {
        var data = state.Data.Deserialize<TestState>()
            ?? throw new InvalidOperationException("Test state is required.");
        return action switch
        {
            IncrementAction increment when increment.Amount > 0 => new GameTransition(
                state with { Data = GameJson.From(data with { Count = data.Count + increment.Amount }) },
                [],
                [new GameEvent("counter.changed", GameJson.From(new { increment.Amount }))]),
            IncrementAction => throw new GameRuleViolationException(
                "invalid-amount", "The increment must be positive."),
            AwardAction award => new GameTransition(
                state,
                [new ScoreAward(award.PlayerId, award.Points, "test-award")],
                []),
            RejectAction => throw new GameRuleViolationException(
                "test-rejected", "The test module rejected this action."),
            CompleteAction => GameTransition.To(state with
            {
                Phase = "Completed",
                PhaseEndsAtUtc = null,
                IsComplete = true
            }),
            DeadlineElapsedAction => GameTransition.To(state with
            {
                Phase = "Completed",
                PhaseEndsAtUtc = null,
                IsComplete = true
            }),
            _ => throw new GameRuleViolationException(
                "unsupported-action", $"Action '{action.Kind}' is not supported.")
        };
    }

    public GameViewPayload CreateView(GameModuleState state, GameViewContext context)
    {
        var data = state.Data.Deserialize<TestState>()
            ?? throw new InvalidOperationException("Test state is required.");
        return context.Role switch
        {
            GameAudienceRole.Host => new(GameJson.From(new
            {
                count = data.Count,
                secret = data.Secret
            })),
            GameAudienceRole.Display => new(GameJson.From(new { count = data.Count })),
            GameAudienceRole.Player => new(GameJson.From(new
            {
                count = data.Count,
                playerId = context.PlayerId
            })),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    public IGameAction DecodeAction(string actionKind, JsonElement payload) => actionKind switch
    {
        "test.increment" => payload.Deserialize<IncrementAction>() ?? new IncrementAction(),
        "test.reject" => new RejectAction(),
        "test.complete" => new CompleteAction(),
        _ => throw new GameRuleViolationException("unsupported-action", "Unsupported test action.")
    };

    private sealed record TestState(int Count, string Secret);
}

internal sealed record IncrementAction(int Amount = 1) : IGameAction
{
    public string Kind => "test.increment";
}

internal sealed record AwardAction(Guid PlayerId, int Points) : IGameAction
{
    public string Kind => "test.award";
}

internal sealed record RejectAction : IGameAction
{
    public string Kind => "test.reject";
}

internal sealed record CompleteAction : IGameAction
{
    public string Kind => "test.complete";
}
