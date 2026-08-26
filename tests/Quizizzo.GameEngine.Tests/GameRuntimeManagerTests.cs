using Quizizzo.GameContracts;
using Quizizzo.GameEngine;

namespace Quizizzo.GameEngine.Tests;

public sealed class GameRuntimeManagerTests
{
    [Fact]
    public async Task Start_creates_an_explicit_initial_state_and_role_specific_views()
    {
        await using var fixture = await Fixture.StartAsync();

        var status = await fixture.Manager.GetStatusAsync(fixture.InstanceId);
        var host = await fixture.Manager.GetViewAsync(
            fixture.InstanceId, GameViewRequest.Host(fixture.HostUserId));
        var display = await fixture.Manager.GetViewAsync(
            fixture.InstanceId, GameViewRequest.Display("display-session"));
        var player = await fixture.Manager.GetViewAsync(
            fixture.InstanceId, GameViewRequest.Player(fixture.PlayerId));

        Assert.Equal("Collecting", status.Phase);
        Assert.Equal(0, status.Revision);
        Assert.Equal("host-only-answer", host.Data.GetProperty("secret").GetString());
        Assert.False(display.Data.TryGetProperty("secret", out _));
        Assert.Equal(fixture.PlayerId, player.PlayerId);
        Assert.False(player.Data.TryGetProperty("secret", out _));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Manager.GetViewAsync(fixture.InstanceId, GameViewRequest.Player(Guid.NewGuid())));
    }

    [Fact]
    public async Task Concurrent_commands_are_serialized_through_one_consumer()
    {
        await using var fixture = await Fixture.StartAsync();
        var commands = Enumerable.Range(0, 100)
            .Select(_ => fixture.Command(new IncrementAction()))
            .ToArray();

        var results = await Task.WhenAll(commands.Select(command =>
            fixture.Manager.ExecuteAsync(command)));
        var view = await fixture.PlayerViewAsync();

        Assert.All(results, result => Assert.Equal(GameCommandOutcome.Applied, result.Outcome));
        Assert.Equal(Enumerable.Range(1, 100).Select(value => (long)value),
            results.Select(result => result.Revision).Order());
        Assert.Equal(100, view.Data.GetProperty("count").GetInt32());
        Assert.Equal(100, view.Revision);
    }

    [Fact]
    public async Task Retrying_a_command_id_returns_the_recorded_result_without_applying_twice()
    {
        await using var fixture = await Fixture.StartAsync();
        var command = fixture.Command(new IncrementAction());

        var first = await fixture.Manager.ExecuteAsync(command);
        var retry = await fixture.Manager.ExecuteAsync(command);
        var view = await fixture.PlayerViewAsync();

        Assert.False(first.IsDuplicate);
        Assert.True(retry.IsDuplicate);
        Assert.Equal(first.Revision, retry.Revision);
        Assert.Equal(1, view.Data.GetProperty("count").GetInt32());
        Assert.Equal(1, view.Revision);
    }

    [Fact]
    public async Task Invalid_actor_and_rule_rejections_are_persisted_and_idempotent()
    {
        await using var fixture = await Fixture.StartAsync();
        var forbidden = fixture.Command(
            new IncrementAction(),
            GameActor.Player(Guid.NewGuid()));
        var rejected = fixture.Command(new RejectAction());

        var forbiddenResult = await fixture.Manager.ExecuteAsync(forbidden);
        var firstRejection = await fixture.Manager.ExecuteAsync(rejected);
        var retriedRejection = await fixture.Manager.ExecuteAsync(rejected);
        var view = await fixture.PlayerViewAsync();

        Assert.Equal(GameCommandOutcome.Rejected, forbiddenResult.Outcome);
        Assert.Equal("player-forbidden", forbiddenResult.ErrorCode);
        Assert.Equal(GameCommandOutcome.Rejected, firstRejection.Outcome);
        Assert.Equal("test-rejected", firstRejection.ErrorCode);
        Assert.True(retriedRejection.IsDuplicate);
        Assert.Equal(firstRejection.Revision, retriedRejection.Revision);
        Assert.Equal(0, view.Data.GetProperty("count").GetInt32());
        Assert.Equal(2, view.Revision);
    }

    [Fact]
    public async Task Party_and_host_ownership_are_validated_before_module_logic()
    {
        await using var fixture = await Fixture.StartAsync();
        var wrongParty = fixture.Command(new IncrementAction()) with { PartyId = Guid.NewGuid() };
        var wrongHost = fixture.Command(
            new CompleteAction(),
            GameActor.Host("another-host"));

        var wrongPartyResult = await fixture.Manager.ExecuteAsync(wrongParty);
        var wrongHostResult = await fixture.Manager.ExecuteAsync(wrongHost);
        var status = await fixture.Manager.GetStatusAsync(fixture.InstanceId);

        Assert.Equal("wrong-game-instance", wrongPartyResult.ErrorCode);
        Assert.Equal("host-forbidden", wrongHostResult.ErrorCode);
        Assert.False(status.IsComplete);
        Assert.Equal("Collecting", status.Phase);
    }

    [Fact]
    public async Task Score_awards_are_validated_accumulated_and_not_duplicated()
    {
        await using var fixture = await Fixture.StartAsync(startingScore: 5);
        var award = fixture.Command(new AwardAction(fixture.PlayerId, 10));

        await fixture.Manager.ExecuteAsync(award);
        await fixture.Manager.ExecuteAsync(award);
        await fixture.Manager.ExecuteAsync(fixture.Command(new AwardAction(fixture.PlayerId, -3)));
        var view = await fixture.PlayerViewAsync();

        Assert.Equal(12, view.Scores[fixture.PlayerId]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Manager.ExecuteAsync(
                fixture.Command(new AwardAction(Guid.NewGuid(), 10))));
        Assert.Equal(12, (await fixture.PlayerViewAsync()).Scores[fixture.PlayerId]);
    }

    [Fact]
    public async Task Completion_is_terminal_for_later_semantic_actions()
    {
        await using var fixture = await Fixture.StartAsync();

        var completed = await fixture.Manager.ExecuteAsync(
            fixture.Command(new CompleteAction(), GameActor.Host(fixture.HostUserId)));
        var tooLate = await fixture.Manager.ExecuteAsync(fixture.Command(new IncrementAction()));
        var status = await fixture.Manager.GetStatusAsync(fixture.InstanceId);

        Assert.Equal(GameCommandOutcome.Applied, completed.Outcome);
        Assert.Equal("Completed", status.Phase);
        Assert.True(status.IsComplete);
        Assert.Equal(GameCommandOutcome.Rejected, tooLate.Outcome);
        Assert.Equal("game-complete", tooLate.ErrorCode);
    }

    [Fact]
    public async Task Utc_deadline_enqueues_a_system_command_and_advances_the_state()
    {
        await using var fixture = await Fixture.StartAsync(TimeSpan.FromMilliseconds(50));

        GameSessionStatus status = await fixture.Manager.GetStatusAsync(fixture.InstanceId);
        for (var attempt = 0; attempt < 100 && !status.IsComplete; attempt++)
        {
            await Task.Delay(10);
            status = await fixture.Manager.GetStatusAsync(fixture.InstanceId);
        }

        Assert.True(status.IsComplete);
        Assert.Equal("Completed", status.Phase);
        Assert.Equal(1, status.Revision);
        Assert.Null(status.PhaseEndsAtUtc);
    }

    [Fact]
    public async Task Early_deadline_and_late_player_actions_are_rejected_server_side()
    {
        var clock = new OffsetTimeProvider(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
        var module = new TestGameModule(TimeSpan.FromMinutes(1));
        await using var manager = new GameRuntimeManager(
            new GameModuleCatalog([module]),
            new InMemoryGameStateStore(),
            clock);
        var instanceId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        await manager.StartAsync(new GameStartRequest(
            instanceId,
            partyId,
            "host-user",
            module.Descriptor.Key,
            [new GameParticipant(playerId, "Player")]));
        var deadline = (await manager.GetStatusAsync(instanceId)).PhaseEndsAtUtc!.Value;

        var early = await manager.ExecuteAsync(new GameCommand(
            GameCommandId.New(),
            instanceId,
            partyId,
            GameActor.SystemActor,
            new DeadlineElapsedAction(deadline)));
        clock.Advance(TimeSpan.FromMinutes(2));
        var late = await manager.ExecuteAsync(new GameCommand(
            GameCommandId.New(),
            instanceId,
            partyId,
            GameActor.Player(playerId),
            new IncrementAction()));

        Assert.Equal("early-deadline", early.ErrorCode);
        Assert.Equal("action-too-late", late.ErrorCode);
        Assert.Equal(0, (await manager.GetViewAsync(
            instanceId, GameViewRequest.Player(playerId))).Data.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task A_new_runtime_recovers_snapshot_state_and_command_idempotency()
    {
        var module = new TestGameModule();
        var store = new InMemoryGameStateStore();
        var instanceId = GameInstanceId.New();
        var playerId = Guid.NewGuid();
        var request = new GameStartRequest(
            instanceId,
            Guid.NewGuid(),
            "host-user",
            module.Descriptor.Key,
            [new GameParticipant(playerId, "Player")]);
        var command = new GameCommand(
            GameCommandId.New(),
            instanceId,
            request.PartyId,
            GameActor.Player(playerId),
            new IncrementAction());

        await using (var firstRuntime = new GameRuntimeManager(
            new GameModuleCatalog([module]), store, TimeProvider.System))
        {
            await firstRuntime.StartAsync(request);
            await firstRuntime.ExecuteAsync(command);
        }

        await using var recoveredRuntime = new GameRuntimeManager(
            new GameModuleCatalog([module]), store, TimeProvider.System);
        var recovered = await recoveredRuntime.GetViewAsync(
            instanceId, GameViewRequest.Player(playerId));
        var duplicate = await recoveredRuntime.ExecuteAsync(command);
        var afterRetry = await recoveredRuntime.GetViewAsync(
            instanceId, GameViewRequest.Player(playerId));

        Assert.Equal(1, recovered.Data.GetProperty("count").GetInt32());
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(1, afterRetry.Data.GetProperty("count").GetInt32());
        Assert.Equal(1, afterRetry.Revision);
    }

    [Fact]
    public async Task Start_rejects_duplicate_participants_and_module_player_limit_violations()
    {
        var playerId = Guid.NewGuid();
        await using var manager = new GameRuntimeManager(
            new GameModuleCatalog([new TestGameModule()]),
            new InMemoryGameStateStore(),
            TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.StartAsync(new GameStartRequest(
            GameInstanceId.New(),
            Guid.NewGuid(),
            "host-user",
            "test-game",
            [new GameParticipant(playerId, "One"), new GameParticipant(playerId, "Duplicate")] )));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(new GameStartRequest(
            GameInstanceId.New(),
            Guid.NewGuid(),
            "host-user",
            "test-game",
            [])));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(GameRuntimeManager manager, GameInstanceId instanceId, Guid partyId, Guid playerId)
        {
            Manager = manager;
            InstanceId = instanceId;
            PartyId = partyId;
            PlayerId = playerId;
        }

        public GameRuntimeManager Manager { get; }
        public GameInstanceId InstanceId { get; }
        public Guid PartyId { get; }
        public Guid PlayerId { get; }
        public string HostUserId => "host-user";

        public static async Task<Fixture> StartAsync(
            TimeSpan? deadline = null,
            int startingScore = 0)
        {
            var module = new TestGameModule(deadline);
            var manager = new GameRuntimeManager(
                new GameModuleCatalog([module]),
                new InMemoryGameStateStore(),
                TimeProvider.System);
            var instanceId = GameInstanceId.New();
            var partyId = Guid.NewGuid();
            var playerId = Guid.NewGuid();
            await manager.StartAsync(new GameStartRequest(
                instanceId,
                partyId,
                "host-user",
                module.Descriptor.Key,
                [new GameParticipant(playerId, "Player", startingScore)]));
            return new Fixture(manager, instanceId, partyId, playerId);
        }

        public GameCommand Command(
            IGameAction action,
            GameActor? actor = null,
            GameCommandId? commandId = null) => new(
                commandId ?? GameCommandId.New(),
                InstanceId,
                PartyId,
                actor ?? GameActor.Player(PlayerId),
                action);

        public Task<GameRoleView> PlayerViewAsync() =>
            Manager.GetViewAsync(InstanceId, GameViewRequest.Player(PlayerId));

        public ValueTask DisposeAsync() => Manager.DisposeAsync();
    }

    private sealed class OffsetTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
