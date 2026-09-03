using System.Text.Json;
using Quizizzo.GameContracts;
using Quizizzo.GameEngine;

namespace Quizizzo.GameEngine.Tests;

public sealed class SimulationRuntimeTests
{
    [Fact]
    public async Task Opt_in_simulation_ticks_are_serialized_persisted_and_stop_at_completion()
    {
        var module = new SimulationTestModule();
        var store = new InMemoryGameStateStore();
        var instanceId = GameInstanceId.New();
        await using var runtime = new GameRuntimeManager(
            new GameModuleCatalog([module]),
            store,
            TimeProvider.System);
        await runtime.StartAsync(new GameStartRequest(
            instanceId,
            Guid.NewGuid(),
            "host",
            module.Descriptor.Key,
            [new GameParticipant(Guid.NewGuid(), "Player")]));

        var status = await WaitForCompletionAsync(runtime, instanceId);
        var persisted = await store.LoadAsync(instanceId);

        Assert.True(
            status.IsComplete,
            $"Simulation stopped at revision {status.Revision} in phase {status.Phase}; " +
            $"results: {Describe(persisted!)}.");
        Assert.True(status.Revision >= 3, Describe(persisted!));
        Assert.NotNull(persisted);
        Assert.Equal(status.Revision, persisted.Revision);
        Assert.Equal(3, persisted.ModuleState.Data.GetProperty("Ticks").GetInt32());
        Assert.Equal(3, persisted.ProcessedCommands.Values.Count(result =>
            result.Outcome == GameCommandOutcome.Applied));
        Assert.All(persisted.ProcessedCommands.Values, result =>
            Assert.True(result.Outcome == GameCommandOutcome.Applied, Describe(persisted)));
    }

    [Fact]
    public async Task Released_actor_recovers_and_resumes_the_opt_in_simulation()
    {
        var module = new SimulationTestModule(ticksToComplete: 5);
        var store = new InMemoryGameStateStore();
        var instanceId = GameInstanceId.New();
        await using var runtime = new GameRuntimeManager(
            new GameModuleCatalog([module]),
            store,
            TimeProvider.System);
        await runtime.StartAsync(new GameStartRequest(
            instanceId,
            Guid.NewGuid(),
            "host",
            module.Descriptor.Key,
            [new GameParticipant(Guid.NewGuid(), "Player")]));
        await WaitForRevisionAsync(runtime, instanceId, 1);

        await runtime.ReleaseAsync(instanceId);
        _ = await runtime.GetStatusAsync(instanceId);
        var completed = await WaitForCompletionAsync(runtime, instanceId);

        Assert.True(
            completed.IsComplete,
            $"Recovered simulation stopped at revision {completed.Revision} in phase {completed.Phase}.");
        Assert.Equal(5, completed.Revision);
    }

    [Fact]
    public async Task Simulation_interval_outside_engine_bounds_is_rejected_on_start()
    {
        var module = new SimulationTestModule(interval: TimeSpan.FromMilliseconds(5));
        await using var runtime = new GameRuntimeManager(
            new GameModuleCatalog([module]),
            new InMemoryGameStateStore(),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync(new GameStartRequest(
            GameInstanceId.New(),
            Guid.NewGuid(),
            "host",
            module.Descriptor.Key,
            [new GameParticipant(Guid.NewGuid(), "Player")])));
    }

    private static async Task<GameSessionStatus> WaitForCompletionAsync(
        GameRuntimeManager runtime,
        GameInstanceId instanceId)
    {
        var status = await runtime.GetStatusAsync(instanceId);
        for (var attempt = 0; attempt < 100 && !status.IsComplete; attempt++)
        {
            await Task.Delay(10);
            status = await runtime.GetStatusAsync(instanceId);
        }
        return status;
    }

    private static async Task WaitForRevisionAsync(
        GameRuntimeManager runtime,
        GameInstanceId instanceId,
        long revision)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await runtime.GetStatusAsync(instanceId)).Revision >= revision)
            {
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("The simulation revision did not advance.");
    }

    private static string Describe(GameRuntimeSnapshot snapshot) => string.Join(
        ", ",
        snapshot.ProcessedCommands.Values
            .GroupBy(result => $"{result.Outcome}:{result.ErrorCode ?? "ok"}")
            .Select(group => $"{group.Key}={group.Count()}"));

    private sealed class SimulationTestModule(
        int ticksToComplete = 3,
        TimeSpan? interval = null) : IGameModule, IGameSimulationModule
    {
        private readonly TimeSpan tickInterval = interval ?? TimeSpan.FromMilliseconds(25);

        public GameDescriptor Descriptor { get; } = new("simulation-test", "Simulation Test", 1, 1);

        public GameModuleState Start(GameStartContext context) => new(
            1,
            "Playing",
            null,
            false,
            GameJson.From(new SimulationState(0)));

        public GameTransition Apply(
            GameModuleState state,
            GameActionContext context,
            IGameAction action)
        {
            if (action is not SimulationTickElapsedAction)
            {
                throw new GameRuleViolationException("unsupported-action", "Only simulation ticks are supported.");
            }
            var current = state.Data.Deserialize<SimulationState>()
                ?? throw new InvalidOperationException("Simulation state is required.");
            var next = current with { Ticks = current.Ticks + 1 };
            var complete = next.Ticks >= ticksToComplete;
            return GameTransition.To(state with
            {
                Phase = complete ? "Completed" : state.Phase,
                IsComplete = complete,
                Data = GameJson.From(next)
            });
        }

        public GameViewPayload CreateView(GameModuleState state, GameViewContext context) => new(state.Data);

        public IGameAction DecodeAction(string actionKind, JsonElement payload) =>
            throw new GameRuleViolationException("unsupported-action", "No client actions are supported.");

        public TimeSpan? GetSimulationInterval(GameModuleState state) =>
            state.IsComplete ? null : tickInterval;
    }

    private sealed record SimulationState(int Ticks);
}
