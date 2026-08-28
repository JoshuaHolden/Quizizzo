using System.Text.Json;
using Quizizzo.GameContracts;
using Quizizzo.Games.AniMates;

namespace Quizizzo.GameEngine.Tests;

public sealed class AniMatesGameModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_assigns_private_prompts_and_exactly_three_configurable_frames()
    {
        var fixture = new Fixture();
        var firstView = fixture.PlayerView(fixture.PlayerIds[0]);
        var secondView = fixture.PlayerView(fixture.PlayerIds[1]);
        var configuration = firstView.Controller.Configuration.Deserialize<DrawingControllerConfiguration>();
        var display = fixture.DisplayView();

        Assert.Equal(PlayerControllerKind.Drawing, firstView.Controller.Kind);
        Assert.NotNull(configuration);
        Assert.Equal(3, configuration.FrameCount);
        Assert.Equal(512, configuration.LogicalWidth);
        Assert.NotEqual(firstView.Instructions, secondView.Instructions);
        Assert.DoesNotContain(firstView.Instructions, display.Prompt, StringComparison.Ordinal);
        Assert.Null(display.Drawing);
    }

    [Fact]
    public void Missing_frames_copy_the_latest_completed_frame_and_navigation_state_survives_views()
    {
        var fixture = new Fixture();
        var firstFrame = Guid.NewGuid();
        fixture.Apply(
            GameActor.Player(fixture.PlayerIds[0]),
            new SubmitAnimationAction([firstFrame]));
        fixture.Submit(fixture.PlayerIds[1]);
        fixture.Submit(fixture.PlayerIds[2]);

        var voteView = fixture.PlayerView(fixture.PlayerIds[1]);
        var configuration = voteView.Controller.Configuration.Deserialize<VoteControllerConfiguration>();
        var firstAnimation = Assert.Single(
            configuration!.Options,
            option => option.Id == fixture.PlayerIds[0].ToString("N"));

        Assert.Equal(AniMatesGameModule.VotingPhase, fixture.State.Phase);
        Assert.Equal([firstFrame, firstFrame, firstFrame], firstAnimation.FrameAssetIds);
        Assert.Equal("animates:vote", configuration.SelectionScope);
    }

    [Fact]
    public void Submission_validates_player_ownership_phase_and_frame_count()
    {
        var fixture = new Fixture();

        var hostError = Assert.Throws<GameRuleViolationException>(() => fixture.Apply(
            GameActor.Host("host"), new SubmitAnimationAction([Guid.NewGuid()])));
        var frameError = Assert.Throws<GameRuleViolationException>(() => fixture.Apply(
            GameActor.Player(fixture.PlayerIds[0]), new SubmitAnimationAction([])));
        fixture.SubmitAll();
        var phaseError = Assert.Throws<GameRuleViolationException>(() => fixture.Apply(
            GameActor.Player(fixture.PlayerIds[0]), new SubmitAnimationAction([Guid.NewGuid()])));

        Assert.Equal("player-required", hostError.Code);
        Assert.Equal("invalid-frames", frameError.Code);
        Assert.Equal("wrong-phase", phaseError.Code);
    }

    [Fact]
    public void Self_voting_is_forbidden_and_results_score_popularity()
    {
        var fixture = new Fixture();
        fixture.SubmitAll();

        var selfVote = Assert.Throws<GameRuleViolationException>(() => fixture.Apply(
            GameActor.Player(fixture.PlayerIds[0]),
            new VoteForAnimationAction(fixture.PlayerIds[0])));
        fixture.Apply(GameActor.Player(fixture.PlayerIds[0]), new VoteForAnimationAction(fixture.PlayerIds[1]));
        fixture.Apply(GameActor.Player(fixture.PlayerIds[1]), new VoteForAnimationAction(fixture.PlayerIds[0]));
        var reveal = fixture.Apply(
            GameActor.Player(fixture.PlayerIds[2]),
            new VoteForAnimationAction(fixture.PlayerIds[0]));

        Assert.Equal("self-vote", selfVote.Code);
        Assert.Equal(AniMatesGameModule.ResultsPhase, fixture.State.Phase);
        Assert.Equal(1000, reveal.ScoreAwards.Single(award => award.PlayerId == fixture.PlayerIds[0]).Points);
        Assert.Equal(600, reveal.ScoreAwards.Single(award => award.PlayerId == fixture.PlayerIds[1]).Points);
        Assert.DoesNotContain(reveal.ScoreAwards, award => award.PlayerId == fixture.PlayerIds[2]);
        Assert.All(fixture.DisplayView().Drawing!.Animations, animation => Assert.NotNull(animation.CreatorName));
    }

    [Fact]
    public void Deadlines_progress_drawing_and_voting_without_exposing_live_work()
    {
        var fixture = new Fixture();
        fixture.Submit(fixture.PlayerIds[0]);
        var drawingDeadline = fixture.State.PhaseEndsAtUtc!.Value;

        fixture.Apply(GameActor.SystemActor, new DeadlineElapsedAction(drawingDeadline), drawingDeadline);
        var votingDisplay = fixture.DisplayView();
        var votingDeadline = fixture.State.PhaseEndsAtUtc!.Value;
        fixture.Apply(GameActor.SystemActor, new DeadlineElapsedAction(votingDeadline), votingDeadline);

        Assert.Equal("Playback", votingDisplay.Drawing!.Mode);
        Assert.All(votingDisplay.Drawing.Animations, animation => Assert.Null(animation.CreatorName));
        Assert.Equal(AniMatesGameModule.ResultsPhase, fixture.State.Phase);
        Assert.Empty(fixture.LastTransition.ScoreAwards);
    }

    [Fact]
    public void Decoder_rejects_oversized_or_malformed_frame_lists()
    {
        var module = new AniMatesGameModule();
        var tooMany = JsonSerializer.SerializeToElement(new
        {
            frameAssetIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }
        });

        var error = Assert.Throws<GameRuleViolationException>(() =>
            module.DecodeAction(SubmitAnimationAction.ActionKind, tooMany));

        Assert.Equal("invalid-frames", error.Code);
    }

    [Fact]
    public async Task Runtime_makes_animation_submission_idempotent()
    {
        var clock = new AdjustableTimeProvider(Now);
        var module = new AniMatesGameModule(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1));
        await using var manager = new GameRuntimeManager(
            new GameModuleCatalog([module]), new InMemoryGameStateStore(), clock);
        var instanceId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        await manager.StartAsync(new GameStartRequest(
            instanceId,
            partyId,
            "host",
            module.Descriptor.Key,
            [new GameParticipant(playerId, "One"), new GameParticipant(Guid.NewGuid(), "Two")]));
        var command = new GameCommand(
            GameCommandId.New(),
            instanceId,
            partyId,
            GameActor.Player(playerId),
            new SubmitAnimationAction([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]));

        var first = await manager.ExecuteAsync(command);
        var retry = await manager.ExecuteAsync(command);

        Assert.Equal(GameCommandOutcome.Applied, first.Outcome);
        Assert.True(retry.IsDuplicate);
        Assert.Equal(first.Revision, retry.Revision);
    }

    [Fact]
    public async Task Runtime_rejects_late_animation_submission()
    {
        var clock = new AdjustableTimeProvider(Now);
        var module = new AniMatesGameModule(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        await using var manager = new GameRuntimeManager(
            new GameModuleCatalog([module]), new InMemoryGameStateStore(), clock);
        var instanceId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        await manager.StartAsync(new GameStartRequest(
            instanceId,
            partyId,
            "host",
            module.Descriptor.Key,
            [new GameParticipant(playerId, "One"), new GameParticipant(Guid.NewGuid(), "Two")]));
        clock.Advance(TimeSpan.FromMinutes(2));

        var result = await manager.ExecuteAsync(new GameCommand(
            GameCommandId.New(),
            instanceId,
            partyId,
            GameActor.Player(playerId),
            new SubmitAnimationAction([Guid.NewGuid()])));

        Assert.Equal(GameCommandOutcome.Rejected, result.Outcome);
        Assert.Equal("action-too-late", result.ErrorCode);
    }

    private sealed class Fixture
    {
        private readonly AniMatesGameModule module = new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1));
        private readonly Guid instanceId = Guid.NewGuid();
        private readonly Guid partyId = Guid.NewGuid();

        public Fixture()
        {
            PlayerIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
            State = module.Start(new GameStartContext(
                new GameInstanceId(instanceId),
                partyId,
                "host",
                PlayerIds.Select((id, index) => new GameParticipant(id, $"Player {index + 1}")).ToArray(),
                Now));
            LastTransition = GameTransition.To(State);
        }

        public Guid[] PlayerIds { get; }
        public GameModuleState State { get; private set; }
        public GameTransition LastTransition { get; private set; }

        public GameTransition Apply(GameActor actor, IGameAction action, DateTimeOffset? at = null)
        {
            LastTransition = module.Apply(
                State,
                new GameActionContext(new GameInstanceId(instanceId), partyId, actor, at ?? Now.AddSeconds(1)),
                action);
            State = LastTransition.State;
            return LastTransition;
        }

        public void Submit(Guid playerId) => Apply(
            GameActor.Player(playerId),
            new SubmitAnimationAction([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]));

        public void SubmitAll()
        {
            foreach (var playerId in PlayerIds)
            {
                Submit(playerId);
            }
        }

        public PlayerGameViewPayload PlayerView(Guid playerId) => module.CreateView(
            State,
            new GameViewContext(GameAudienceRole.Player, playerId.ToString("N"), playerId))
            .Data.Deserialize<PlayerGameViewPayload>()!;

        public DisplayGameViewPayload DisplayView() => module.CreateView(
            State,
            new GameViewContext(GameAudienceRole.Display, "display", null))
            .Data.Deserialize<DisplayGameViewPayload>()!;
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan amount) => now = now.Add(amount);
    }
}
