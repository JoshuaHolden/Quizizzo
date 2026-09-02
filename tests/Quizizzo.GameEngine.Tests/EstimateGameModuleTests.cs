using System.Text.Json;
using Quizizzo.GameContracts;
using Quizizzo.GameEngine;
using Quizizzo.Games.Estimate;

namespace Quizizzo.GameEngine.Tests;

public sealed class EstimateGameModuleTests
{
    [Fact]
    public async Task Answering_views_use_the_number_controller_and_hide_estimates_from_other_roles()
    {
        await using var game = await EstimateFixture.StartAsync();

        await game.SubmitAsync(game.FirstPlayerId, 10_000);
        var firstPlayer = await game.PlayerViewAsync(game.FirstPlayerId);
        var secondPlayer = await game.PlayerViewAsync(game.SecondPlayerId);
        var display = await game.DisplayViewAsync();
        var firstPayload = firstPlayer.Data.Deserialize<PlayerGameViewPayload>()!;
        var secondPayload = secondPlayer.Data.Deserialize<PlayerGameViewPayload>()!;
        var displayPayload = display.Data.Deserialize<DisplayGameViewPayload>()!;

        Assert.Equal(PlayerControllerKind.Waiting, firstPayload.Controller.Kind);
        Assert.Equal(PlayerControllerKind.Number, secondPayload.Controller.Kind);
        Assert.DoesNotContain("10000", secondPlayer.Data.GetRawText());
        Assert.DoesNotContain("10000", display.Data.GetRawText());
        Assert.Contains(displayPayload.Entries, entry =>
            entry.PlayerId == game.FirstPlayerId && entry.Value == "Locked in");
    }

    [Fact]
    public async Task All_submissions_reveal_rankings_and_award_tied_players_the_same_rank()
    {
        await using var game = await EstimateFixture.StartAsync();

        await game.SubmitAsync(game.FirstPlayerId, 10_000);
        var revealed = await game.SubmitAsync(game.SecondPlayerId, 10_160);
        var display = await game.DisplayViewAsync();
        var payload = display.Data.Deserialize<DisplayGameViewPayload>()!;

        Assert.Equal(EstimateGameModule.ResultsPhase, revealed.Phase);
        Assert.True(payload.ShowRoundRanking);
        Assert.All(payload.Entries, entry => Assert.Equal(1, entry.Rank));
        Assert.All(payload.Entries, entry => Assert.Equal(1000, entry.PointsAwarded));
        Assert.Equal(1000, display.Scores[game.FirstPlayerId]);
        Assert.Equal(1000, display.Scores[game.SecondPlayerId]);
        Assert.Contains("10,080", payload.PhaseMessage);
    }

    [Fact]
    public async Task Invalid_duplicate_and_late_estimates_are_rejected_server_side()
    {
        await using var game = await EstimateFixture.StartAsync();
        var invalidCommandId = GameCommandId.New();

        var invalid = await game.SubmitAsync(game.FirstPlayerId, 50_001, invalidCommandId);
        var invalidRetry = await game.SubmitAsync(game.FirstPlayerId, 50_001, invalidCommandId);
        var accepted = await game.SubmitAsync(game.FirstPlayerId, 10_000);
        var duplicateSubmission = await game.SubmitAsync(game.FirstPlayerId, 10_001);
        await game.SubmitAsync(game.SecondPlayerId, 9_000);
        var late = await game.SubmitAsync(game.FirstPlayerId, 10_080);

        Assert.Equal("estimate-out-of-range", invalid.ErrorCode);
        Assert.True(invalidRetry.IsDuplicate);
        Assert.Equal(GameCommandOutcome.Applied, accepted.Outcome);
        Assert.Equal("already-submitted", duplicateSubmission.ErrorCode);
        Assert.Equal("wrong-phase", late.ErrorCode);
    }

    [Fact]
    public async Task Deadline_reveals_missing_players_with_zero_points()
    {
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
        await using var game = await EstimateFixture.StartAsync(clock);
        await game.SubmitAsync(game.FirstPlayerId, 10_080);
        var deadline = (await game.Manager.GetStatusAsync(game.InstanceId)).PhaseEndsAtUtc!.Value;

        clock.Advance(TimeSpan.FromSeconds(31));
        var result = await game.Manager.ExecuteAsync(game.Command(
            GameActor.SystemActor,
            new DeadlineElapsedAction(deadline)));
        var display = await game.DisplayViewAsync();
        var payload = display.Data.Deserialize<DisplayGameViewPayload>()!;

        Assert.Equal(GameCommandOutcome.Applied, result.Outcome);
        Assert.Equal(EstimateGameModule.ResultsPhase, result.Phase);
        Assert.Contains(payload.Entries, entry =>
            entry.PlayerId == game.SecondPlayerId && entry.Value == "No answer" && entry.PointsAwarded == 0);
    }

    [Fact]
    public async Task Three_round_loop_completes_with_cumulative_scores()
    {
        await using var game = await EstimateFixture.StartAsync();

        await game.PlayRoundAsync(10_000, 5_000);
        await game.AdvanceAsync();
        await game.PlayRoundAsync(100, 90);
        await game.AdvanceAsync();
        await game.PlayRoundAsync(100_000, 99_999);
        var completed = await game.AdvanceAsync();
        var status = await game.Manager.GetStatusAsync(game.InstanceId);
        var display = await game.DisplayViewAsync();

        Assert.Equal(GameCommandOutcome.Applied, completed.Outcome);
        Assert.True(status.IsComplete);
        Assert.Equal(EstimateGameModule.CompletedPhase, status.Phase);
        Assert.Equal(2600, display.Scores[game.FirstPlayerId]);
        Assert.Equal(2200, display.Scores[game.SecondPlayerId]);
    }

    [Fact]
    public void Transport_actions_are_decoded_and_malformed_values_are_rejected()
    {
        var module = new EstimateGameModule();
        var action = module.DecodeAction(
            SubmitEstimateAction.ActionKind,
            GameJson.From(new { value = 42 }));

        Assert.Equal(42, Assert.IsType<SubmitEstimateAction>(action).Value);
        var exception = Assert.Throws<GameRuleViolationException>(() =>
            module.DecodeAction(SubmitEstimateAction.ActionKind, GameJson.From(new { value = "nope" })));
        Assert.Equal("invalid-estimate", exception.Code);
    }

    private sealed class EstimateFixture : IAsyncDisposable
    {
        private EstimateFixture(
            GameRuntimeManager manager,
            GameInstanceId instanceId,
            Guid partyId,
            Guid firstPlayerId,
            Guid secondPlayerId)
        {
            Manager = manager;
            InstanceId = instanceId;
            PartyId = partyId;
            FirstPlayerId = firstPlayerId;
            SecondPlayerId = secondPlayerId;
        }

        public GameRuntimeManager Manager { get; }
        public GameInstanceId InstanceId { get; }
        public Guid PartyId { get; }
        public Guid FirstPlayerId { get; }
        public Guid SecondPlayerId { get; }

        public static async Task<EstimateFixture> StartAsync(TimeProvider? clock = null)
        {
            var module = new EstimateGameModule();
            var manager = new GameRuntimeManager(
                new GameModuleCatalog([module]),
                new InMemoryGameStateStore(),
                clock ?? TimeProvider.System);
            var instanceId = GameInstanceId.New();
            var partyId = Guid.NewGuid();
            var firstPlayerId = Guid.NewGuid();
            var secondPlayerId = Guid.NewGuid();
            await manager.StartAsync(new GameStartRequest(
                instanceId,
                partyId,
                "host-user",
                module.Descriptor.Key,
                [
                    new GameParticipant(firstPlayerId, "First"),
                    new GameParticipant(secondPlayerId, "Second")
                ]));
            return new EstimateFixture(
                manager, instanceId, partyId, firstPlayerId, secondPlayerId);
        }

        public GameCommand Command(GameActor actor, IGameAction action, GameCommandId? id = null) => new(
            id ?? GameCommandId.New(), InstanceId, PartyId, actor, action);

        public Task<GameCommandResult> SubmitAsync(
            Guid playerId,
            long value,
            GameCommandId? id = null) => Manager.ExecuteAsync(Command(
                GameActor.Player(playerId), new SubmitEstimateAction(value), id));

        public Task<GameCommandResult> AdvanceAsync() => Manager.ExecuteAsync(Command(
            GameActor.Host("host-user"), new AdvanceEstimateAction()));

        public async Task PlayRoundAsync(long first, long second)
        {
            await SubmitAsync(FirstPlayerId, first);
            await SubmitAsync(SecondPlayerId, second);
        }

        public Task<GameRoleView> PlayerViewAsync(Guid playerId) =>
            Manager.GetViewAsync(InstanceId, GameViewRequest.Player(playerId));

        public Task<GameRoleView> DisplayViewAsync() =>
            Manager.GetViewAsync(InstanceId, GameViewRequest.Display("display-session"));

        public ValueTask DisposeAsync() => Manager.DisposeAsync();
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
