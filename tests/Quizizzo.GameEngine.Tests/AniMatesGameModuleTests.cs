using System.Text.Json;
using Quizizzo.GameContracts;
using Quizizzo.Games.AniMates;

namespace Quizizzo.GameEngine.Tests;

public sealed class AniMatesGameModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_gives_every_player_a_private_three_frame_prompt_at_the_same_time()
    {
        var game = new Fixture();
        var animator = game.PlayerView(game.PlayerIds[0]);
        var drawing = animator.Controller.Configuration.Deserialize<DrawingControllerConfiguration>();

        Assert.Equal(PlayerControllerKind.Drawing, animator.Controller.Kind);
        Assert.Equal(3, drawing!.FrameCount);
        Assert.Equal(PlayerControllerKind.Drawing, game.PlayerView(game.PlayerIds[1]).Controller.Kind);
        Assert.NotEqual(animator.Instructions, game.PlayerView(game.PlayerIds[1]).Instructions);
        Assert.DoesNotContain(animator.Instructions, game.DisplayView().Prompt, StringComparison.Ordinal);
        Assert.Null(game.DisplayView().Drawing);
    }

    [Fact]
    public void Everyone_draws_and_missing_frames_repeat_the_latest_frame()
    {
        var game = new Fixture();
        var frame = Guid.NewGuid();
        game.Apply(GameActor.Player(game.PlayerIds[0]), new SubmitAnimationAction([frame]));
        Assert.Equal(AniMatesGameModule.DrawingPhase, game.State.Phase);
        Assert.Equal(PlayerControllerKind.Waiting, game.PlayerView(game.PlayerIds[0]).Controller.Kind);
        game.SubmitAnimation(game.PlayerIds[1]);
        game.SubmitAnimation(game.PlayerIds[2]);

        Assert.Equal(AniMatesGameModule.GuessingPhase, game.State.Phase);
        Assert.Equal([frame, frame, frame], game.DisplayView().Drawing!.Animations.Single().FrameAssetIds);
    }

    [Fact]
    public void Guessing_opens_stable_lettered_choices_and_hides_each_players_own_answer()
    {
        var game = new Fixture();
        game.SubmitAllAnimations();
        game.Guess(game.PlayerIds[1], "A dog getting a spanking");
        game.Guess(game.PlayerIds[2], "A dog at the vet");

        var first = game.ChoiceView(game.PlayerIds[1]);
        var recovered = game.ChoiceView(game.PlayerIds[1]);

        Assert.Equal(AniMatesGameModule.ChoosingPhase, game.State.Phase);
        Assert.Equal(first.Options.Select(option => option.Id), recovered.Options.Select(option => option.Id));
        Assert.DoesNotContain(first.Options, option => option.Detail == "A dog getting a spanking");
        Assert.All(game.DisplayView().Entries, entry => Assert.Matches("^[A-Z]$", entry.Label));
    }

    [Fact]
    public void Scoring_pays_guess_writer_and_correct_chooser_and_animator()
    {
        var game = new Fixture();
        game.SubmitAllAnimations();
        game.Guess(game.PlayerIds[1], "A silly dog");
        game.Guess(game.PlayerIds[2], "A confused dog");
        var correct = game.ChoiceView(game.PlayerIds[1]).Options.Single(option =>
            option.Detail == "Spanking a blue dog");
        var firstGuess = game.ChoiceView(game.PlayerIds[2]).Options.Single(option =>
            option.Detail == "A silly dog");

        game.Choose(game.PlayerIds[1], correct.Id);
        var reveal = game.Choose(game.PlayerIds[2], firstGuess.Id);

        Assert.Equal(100, reveal.ScoreAwards.Single(award => award.PlayerId == game.PlayerIds[0]).Points);
        Assert.Equal(150, reveal.ScoreAwards.Single(award => award.PlayerId == game.PlayerIds[1]).Points);
        Assert.DoesNotContain(reveal.ScoreAwards, award => award.PlayerId == game.PlayerIds[2]);
        Assert.Contains(game.DisplayView().Entries,
            entry => entry.Label.Contains("CORRECT ANSWER", StringComparison.Ordinal));
    }

    [Fact]
    public void Forged_self_choices_are_rejected()
    {
        var game = new Fixture();
        game.SubmitAllAnimations();
        game.Guess(game.PlayerIds[1], "Own answer");
        game.Guess(game.PlayerIds[2], "Other answer");
        var own = game.DisplayView().Entries.Single(entry => entry.Value == "Own answer");

        var error = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[1]), new ChooseAnimationAnswerAction(own.PlayerId)));

        Assert.Equal("self-choice", error.Code);
    }

    [Fact]
    public void Host_cycles_every_player_through_animator_then_completes()
    {
        var game = new Fixture();
        game.SubmitAllAnimations();
        for (var turn = 0; turn < game.PlayerIds.Length; turn++)
        {
            game.CompleteTurnWithoutChoices();
            var transition = game.Apply(GameActor.Host("host"), new AdvanceAniMatesAction());
            if (turn < game.PlayerIds.Length - 1)
            {
                Assert.Equal(AniMatesGameModule.GuessingPhase, game.State.Phase);
                Assert.Equal(2, game.PlayerIds.Count(id =>
                    game.PlayerView(id).Controller.Kind == PlayerControllerKind.Text));
            }
            else
            {
                Assert.True(transition.State.IsComplete);
            }
        }
    }

    [Fact]
    public void Deadlines_progress_partial_turns_safely()
    {
        var game = new Fixture();
        game.SubmitAllAnimations();
        game.Guess(game.PlayerIds[1], "One guess");
        game.Deadline();
        Assert.Equal(AniMatesGameModule.ChoosingPhase, game.State.Phase);
        game.Deadline();
        Assert.Equal(AniMatesGameModule.ResultsPhase, game.State.Phase);
        Assert.Empty(game.LastTransition.ScoreAwards);
    }

    [Fact]
    public void Decoder_rejects_malformed_payloads()
    {
        var module = new AniMatesGameModule();
        var tooMany = JsonSerializer.SerializeToElement(new
        {
            frameAssetIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }
        });

        Assert.Equal("invalid-frames", Assert.Throws<GameRuleViolationException>(() =>
            module.DecodeAction(SubmitAnimationAction.ActionKind, tooMany)).Code);
        Assert.Equal("invalid-guess", Assert.Throws<GameRuleViolationException>(() =>
            module.DecodeAction(SubmitAnimationGuessAction.ActionKind, GameJson.Empty)).Code);
        Assert.Equal("invalid-choice", Assert.Throws<GameRuleViolationException>(() =>
            module.DecodeAction(ChooseAnimationAnswerAction.ActionKind, GameJson.Empty)).Code);
    }

    private sealed class Fixture
    {
        private readonly AniMatesGameModule module = new(
            TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        private readonly Guid instanceId = Guid.NewGuid();
        private readonly Guid partyId = Guid.NewGuid();
        private DateTimeOffset now = Now;

        public Fixture()
        {
            PlayerIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
            State = module.Start(new GameStartContext(new GameInstanceId(instanceId), partyId, "host",
                PlayerIds.Select((id, index) => new GameParticipant(id, $"Player {index + 1}")).ToArray(), Now));
            LastTransition = GameTransition.To(State);
        }

        public Guid[] PlayerIds { get; }
        public GameModuleState State { get; private set; }
        public GameTransition LastTransition { get; private set; }

        public GameTransition Apply(GameActor actor, IGameAction action)
        {
            now = now.AddSeconds(1);
            LastTransition = module.Apply(State,
                new GameActionContext(new GameInstanceId(instanceId), partyId, actor, now), action);
            State = LastTransition.State;
            return LastTransition;
        }

        public void SubmitAnimation(Guid playerId) => Apply(GameActor.Player(playerId),
            new SubmitAnimationAction([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]));

        public void SubmitAllAnimations()
        {
            foreach (var playerId in PlayerIds)
            {
                SubmitAnimation(playerId);
            }
        }

        public void Guess(Guid playerId, string value) => Apply(
            GameActor.Player(playerId), new SubmitAnimationGuessAction(value));

        public GameTransition Choose(Guid playerId, string optionId) => Apply(
            GameActor.Player(playerId), new ChooseAnimationAnswerAction(Guid.Parse(optionId)));

        public void Deadline()
        {
            now = State.PhaseEndsAtUtc!.Value;
            LastTransition = module.Apply(State,
                new GameActionContext(new GameInstanceId(instanceId), partyId, GameActor.SystemActor, now),
                new DeadlineElapsedAction(now));
            State = LastTransition.State;
        }

        public void CompleteTurnWithoutChoices()
        {
            Deadline();
            if (State.Phase == AniMatesGameModule.ChoosingPhase)
            {
                Deadline();
            }
        }

        public PlayerGameViewPayload PlayerView(Guid playerId) => module.CreateView(State,
            new GameViewContext(GameAudienceRole.Player, playerId.ToString("N"), playerId))
            .Data.Deserialize<PlayerGameViewPayload>()!;

        public ChoiceControllerConfiguration ChoiceView(Guid playerId) => PlayerView(playerId)
            .Controller.Configuration.Deserialize<ChoiceControllerConfiguration>()!;

        public DisplayGameViewPayload DisplayView() => module.CreateView(State,
            new GameViewContext(GameAudienceRole.Display, "display", null))
            .Data.Deserialize<DisplayGameViewPayload>()!;
    }
}
