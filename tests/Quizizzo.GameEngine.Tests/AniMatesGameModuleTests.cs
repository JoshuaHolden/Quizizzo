using System.Text.Json;
using Quizizzo.GameContracts;
using Quizizzo.Games.AniMates;

namespace Quizizzo.GameEngine.Tests;

public sealed class AniMatesGameModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Descriptor_limits_an_animates_party_to_six_players()
    {
        var module = new AniMatesGameModule();

        Assert.Equal(6, module.Descriptor.MaximumPlayers);
    }

    [Fact]
    public void Prompt_catalogue_is_embedded_with_one_thousand_complete_pairs()
    {
        using var stream = typeof(AniMatesGameModule).Assembly.GetManifestResourceStream(
            "Quizizzo.Games.AniMates.Assets.drawing-prompts-1000.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream);

        Assert.Equal(1000, document.RootElement.GetArrayLength());
        Assert.All(document.RootElement.EnumerateArray(), item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("drawingPrompt").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("distractor").GetString()));
        });
    }

    [Fact]
    public void Presenter_briefing_has_a_deadline_and_starts_round_one_automatically()
    {
        var module = new AniMatesGameModule();
        var playerId = Guid.NewGuid();
        var state = module.Start(new GameStartContext(
            GameInstanceId.New(), Guid.NewGuid(), "host",
            [new GameParticipant(playerId, "One"), new GameParticipant(Guid.NewGuid(), "Two")], Now));
        var display = module.CreateView(state, new GameViewContext(GameAudienceRole.Display, "display", null))
            .Data.Deserialize<DisplayGameViewPayload>()!;

        Assert.Equal(AniMatesGameModule.BriefingPhase, state.Phase);
        Assert.Contains("different secret prompt", display.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, display.Tutorial!.FrameCount);
        Assert.Contains(display.Tutorial.Steps, step => step.Contains("onion skin", StringComparison.OrdinalIgnoreCase));
        var briefingDeadline = state.PhaseEndsAtUtc ??
            throw new InvalidOperationException("The briefing must have an automatic deadline.");
        Assert.Equal(Now.AddSeconds(12), briefingDeadline);

        var drawing = module.Apply(state, new GameActionContext(
            GameInstanceId.New(), Guid.NewGuid(), GameActor.SystemActor, briefingDeadline),
            new DeadlineElapsedAction(briefingDeadline));

        Assert.Equal(AniMatesGameModule.DrawingPhase, drawing.State.Phase);
        Assert.NotNull(drawing.State.PhaseEndsAtUtc);
    }

    [Fact]
    public void Drawing_deadlines_multiply_the_configured_seconds_by_each_rounds_frame_count()
    {
        var game = new Fixture(20);

        Assert.Equal(TimeSpan.FromSeconds(60), game.State.PhaseEndsAtUtc - game.CurrentTime);

        game.ReachShowdownBriefing();
        game.Apply(GameActor.Host("host"), new AdvanceAniMatesAction());

        Assert.Equal(TimeSpan.FromSeconds(100), game.State.PhaseEndsAtUtc - game.CurrentTime);
    }

    [Fact]
    public void Drawing_time_defaults_to_45_seconds_per_frame_and_rejects_unsafe_values()
    {
        var module = new AniMatesGameModule();
        var participants = new[]
        {
            new GameParticipant(Guid.NewGuid(), "One"),
            new GameParticipant(Guid.NewGuid(), "Two")
        };
        var state = module.Start(new GameStartContext(
            GameInstanceId.New(), Guid.NewGuid(), "host", participants, Now));
        var drawing = module.Apply(state, new GameActionContext(
            GameInstanceId.New(), Guid.NewGuid(), GameActor.Host("host"), Now),
            new AdvanceAniMatesAction());

        Assert.Equal(TimeSpan.FromSeconds(135), drawing.State.PhaseEndsAtUtc - Now);
        var error = Assert.Throws<GameRuleViolationException>(() => module.Start(new GameStartContext(
            GameInstanceId.New(), Guid.NewGuid(), "host", participants, Now,
            GameJson.From(new AniMatesGameConfiguration(9)))));
        Assert.Equal("invalid-configuration", error.Code);
    }

    [Fact]
    public void Start_gives_every_player_a_private_three_frame_prompt_at_the_same_time()
    {
        var game = new Fixture();
        var animator = game.PlayerView(game.PlayerIds[0]);
        var drawing = animator.Controller.Configuration.Deserialize<DrawingControllerConfiguration>();
        var assigned = game.AssignedPrompt(game.PlayerIds[0]);

        Assert.Equal(PlayerControllerKind.Drawing, animator.Controller.Kind);
        Assert.Equal(3, drawing!.FrameCount);
        Assert.Equal(PlayerControllerKind.Drawing, game.PlayerView(game.PlayerIds[1]).Controller.Kind);
        Assert.NotEqual(animator.Instructions, game.PlayerView(game.PlayerIds[1]).Instructions);
        Assert.Equal(assigned.DrawingPrompt, animator.Instructions);
        Assert.False(string.IsNullOrWhiteSpace(assigned.Distractor));
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
        var drawingPrompt = game.AssignedPrompt(game.PlayerIds[0]).DrawingPrompt;
        game.SubmitAllAnimations();
        game.Guess(game.PlayerIds[1], "A silly dog");
        game.Guess(game.PlayerIds[2], "A confused dog");
        var correct = game.ChoiceView(game.PlayerIds[1]).Options.Single(option =>
            option.Detail == drawingPrompt);
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
    public void Built_in_distractor_is_available_but_awards_no_points_to_anyone()
    {
        var game = new Fixture();
        var distractor = game.AssignedPrompt(game.PlayerIds[0]).Distractor;
        game.SubmitAllAnimations();
        game.Guess(game.PlayerIds[1], "A human guess");
        game.Guess(game.PlayerIds[2], "Another human guess");
        var decoy = game.ChoiceView(game.PlayerIds[1]).Options.Single(option =>
            option.Detail == distractor);

        game.Choose(game.PlayerIds[1], decoy.Id);
        var reveal = game.Choose(game.PlayerIds[2], decoy.Id);

        Assert.Empty(reveal.ScoreAwards);
        Assert.Contains(game.DisplayView().Entries, entry =>
            entry.Label.Contains("BUILT-IN DECOY", StringComparison.Ordinal) &&
            entry.PointsAwarded == 0);
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
    public void Host_cycles_every_round_one_animation_then_opens_the_round_two_briefing()
    {
        var game = new Fixture();
        game.SubmitAllAnimations();
        for (var turn = 0; turn < game.PlayerIds.Length; turn++)
        {
            game.CompleteTurnWithoutChoices();
            Assert.Equal(turn == game.PlayerIds.Length - 1, game.DisplayView().ShowRoundRanking);
            game.Apply(GameActor.Host("host"), new AdvanceAniMatesAction());
            if (turn < game.PlayerIds.Length - 1)
            {
                Assert.Equal(AniMatesGameModule.GuessingPhase, game.State.Phase);
                Assert.Equal(2, game.PlayerIds.Count(id =>
                    game.PlayerView(id).Controller.Kind == PlayerControllerKind.Text));
            }
            else
            {
                Assert.Equal(AniMatesGameModule.ShowdownBriefingPhase, game.State.Phase);
            }
        }
    }

    [Fact]
    public void Same_prompt_showdown_uses_five_frames_anonymous_previews_and_simple_vote_scoring()
    {
        var game = new Fixture();
        game.ReachShowdownBriefing();
        Assert.Equal(5, game.DisplayView().Tutorial!.FrameCount);
        game.Apply(GameActor.Host("host"), new AdvanceAniMatesAction());
        var drawing = game.PlayerView(game.PlayerIds[0]).Controller.Configuration
            .Deserialize<DrawingControllerConfiguration>();
        Assert.Equal(5, drawing!.FrameCount);
        var showdownPrompt = game.PlayerView(game.PlayerIds[0]).Instructions;
        Assert.All(game.PlayerIds, id => Assert.Equal(
            showdownPrompt, game.PlayerView(id).Instructions));
        Assert.False(string.IsNullOrWhiteSpace(showdownPrompt));

        game.SubmitAllAnimations(5);
        Assert.Equal(AniMatesGameModule.ShowdownVotingPhase, game.State.Phase);
        Assert.Equal(TimeSpan.FromSeconds(90), game.State.PhaseEndsAtUtc - game.CurrentTime);
        Assert.All(game.DisplayView().Drawing!.Animations, animation => Assert.Null(animation.CreatorName));
        Assert.Equal(1, game.DisplayView().Drawing!.LoopsPerAnimation);

        var firstVote = game.VoteView(game.PlayerIds[0]);
        Assert.DoesNotContain(firstVote.Options, option => option.Id == game.PlayerIds[0].ToString("N"));
        var selfVote = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[0]), new VoteForShowdownAnimationAction(game.PlayerIds[0])));
        Assert.Equal("self-vote", selfVote.Code);
        game.Vote(game.PlayerIds[0], game.PlayerIds[1]);
        game.Vote(game.PlayerIds[1], game.PlayerIds[0]);
        var reveal = game.Vote(game.PlayerIds[2], game.PlayerIds[0]);

        Assert.Equal(AniMatesGameModule.ShowdownResultsPhase, game.State.Phase);
        Assert.Equal(400, reveal.ScoreAwards.Single(award => award.PlayerId == game.PlayerIds[0]).Points);
        Assert.Equal(100, reveal.ScoreAwards.Single(award => award.PlayerId == game.PlayerIds[1]).Points);
        var revealedDrawing = game.DisplayView().Drawing!;
        Assert.All(revealedDrawing.Animations, animation => Assert.NotNull(animation.CreatorName));
        Assert.True(revealedDrawing.Animations.Single(animation =>
            animation.SubmissionPlayerId == game.PlayerIds[0]).Rank == 1);
        game.Deadline();

        Assert.Equal(AniMatesGameModule.FinalCelebrationPhase, game.State.Phase);
        Assert.False(game.State.IsComplete);
        Assert.Equal(TimeSpan.FromSeconds(15), game.State.PhaseEndsAtUtc - game.CurrentTime);
        var finalDisplay = game.DisplayView();
        Assert.True(finalDisplay.ShowRoundRanking);
        Assert.Null(finalDisplay.Drawing);
        Assert.All(finalDisplay.Entries, entry => Assert.NotNull(entry.Rank));
        Assert.Collection(
            finalDisplay.Statistics!,
            statistic =>
            {
                Assert.Equal("FASTEST ANIMATOR", statistic.Label);
                Assert.Contains("Player 1", statistic.Value, StringComparison.Ordinal);
                Assert.Contains("average", statistic.Value, StringComparison.Ordinal);
            },
            statistic =>
            {
                Assert.Equal("MOST LOVED ANIMATION", statistic.Label);
                Assert.Contains("Player 1", statistic.Value, StringComparison.Ordinal);
                Assert.Contains("2 votes", statistic.Value, StringComparison.Ordinal);
            },
            statistic =>
            {
                Assert.Equal("BEST BLUFFER", statistic.Label);
                Assert.Equal("Nobody fell for a bluff", statistic.Value);
            });

        game.Deadline();

        Assert.True(game.State.IsComplete);
    }

    [Fact]
    public void Same_prompt_showdown_reveals_at_the_deadline_and_discards_missing_votes()
    {
        var game = new Fixture();
        game.ReachShowdownBriefing();
        game.Apply(GameActor.Host("host"), new AdvanceAniMatesAction());
        game.SubmitAllAnimations(5);
        game.Vote(game.PlayerIds[0], game.PlayerIds[1]);

        game.Deadline();

        Assert.Equal(AniMatesGameModule.ShowdownResultsPhase, game.State.Phase);
        Assert.Equal(300, game.LastTransition.ScoreAwards.Single().Points);
    }

    [Fact]
    public void Final_statistics_retain_the_best_round_one_bluffer()
    {
        var game = new Fixture();
        game.SubmitAllAnimations();
        game.Guess(game.PlayerIds[1], "A suspicious banana");
        game.Guess(game.PlayerIds[2], "A dancing pineapple");
        var correct = game.ChoiceView(game.PlayerIds[1]).Options.Single(option =>
            option.Detail == game.AssignedPrompt(game.PlayerIds[0]).DrawingPrompt);
        var bluff = game.ChoiceView(game.PlayerIds[2]).Options.Single(option =>
            option.Detail == "A suspicious banana");
        game.Choose(game.PlayerIds[1], correct.Id);
        game.Choose(game.PlayerIds[2], bluff.Id);

        for (var turn = 1; turn < game.PlayerIds.Length; turn++)
        {
            game.Apply(GameActor.Host("host"), new AdvanceAniMatesAction());
            game.CompleteTurnWithoutChoices();
        }
        game.Apply(GameActor.Host("host"), new AdvanceAniMatesAction());
        game.Apply(GameActor.Host("host"), new AdvanceAniMatesAction());
        game.SubmitAllAnimations(5);
        game.Deadline();
        game.Deadline();

        var statistic = game.DisplayView().Statistics!.Single(item => item.Label == "BEST BLUFFER");
        Assert.Contains("Player 2", statistic.Value, StringComparison.Ordinal);
        Assert.Contains("fooled 1 player", statistic.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_prompt_showdown_awards_the_winner_bonus_to_every_tied_winner()
    {
        var game = new Fixture();
        game.ReachShowdownBriefing();
        game.Apply(GameActor.Host("host"), new AdvanceAniMatesAction());
        game.SubmitAllAnimations(5);

        game.Vote(game.PlayerIds[0], game.PlayerIds[1]);
        game.Vote(game.PlayerIds[1], game.PlayerIds[2]);
        var reveal = game.Vote(game.PlayerIds[2], game.PlayerIds[0]);

        Assert.Equal(3, reveal.ScoreAwards.Count);
        Assert.All(reveal.ScoreAwards, award => Assert.Equal(300, award.Points));
        Assert.All(game.DisplayView().Drawing!.Animations, animation => Assert.Equal(1, animation.Rank));
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
            frameAssetIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray()
        });

        Assert.Equal("invalid-frames", Assert.Throws<GameRuleViolationException>(() =>
            module.DecodeAction(SubmitAnimationAction.ActionKind, tooMany)).Code);
        Assert.Equal("invalid-guess", Assert.Throws<GameRuleViolationException>(() =>
            module.DecodeAction(SubmitAnimationGuessAction.ActionKind, GameJson.Empty)).Code);
        Assert.Equal("invalid-choice", Assert.Throws<GameRuleViolationException>(() =>
            module.DecodeAction(ChooseAnimationAnswerAction.ActionKind, GameJson.Empty)).Code);
    }

    [Fact]
    public void Decoder_accepts_compact_option_ids_emitted_by_phone_controllers()
    {
        var module = new AniMatesGameModule();
        var answerOptionId = Guid.NewGuid();
        var submissionPlayerId = Guid.NewGuid();

        var choice = module.DecodeAction(
            ChooseAnimationAnswerAction.ActionKind,
            GameJson.From(new { answerOptionId = answerOptionId.ToString("N") }));
        var vote = module.DecodeAction(
            VoteForShowdownAnimationAction.ActionKind,
            GameJson.From(new { submissionPlayerId = submissionPlayerId.ToString("N") }));

        Assert.Equal(
            answerOptionId,
            Assert.IsType<ChooseAnimationAnswerAction>(choice).AnswerOptionId);
        Assert.Equal(
            submissionPlayerId,
            Assert.IsType<VoteForShowdownAnimationAction>(vote).SubmissionPlayerId);
    }

    private sealed class Fixture
    {
        private readonly AniMatesGameModule module;
        private readonly Guid instanceId = Guid.NewGuid();
        private readonly Guid partyId = Guid.NewGuid();
        private DateTimeOffset now = Now;

        public Fixture(int? drawingSecondsPerFrame = null)
        {
            module = drawingSecondsPerFrame.HasValue
                ? new AniMatesGameModule()
                : new AniMatesGameModule(
                    TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            PlayerIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
            State = module.Start(new GameStartContext(new GameInstanceId(instanceId), partyId, "host",
                PlayerIds.Select((id, index) => new GameParticipant(id, $"Player {index + 1}")).ToArray(), Now,
                drawingSecondsPerFrame.HasValue
                    ? GameJson.From(new AniMatesGameConfiguration(drawingSecondsPerFrame.Value))
                    : GameJson.Empty));
            LastTransition = GameTransition.To(State);
            Apply(GameActor.Host("host"), new AdvanceAniMatesAction());
        }

        public Guid[] PlayerIds { get; }
        public DateTimeOffset CurrentTime => now;
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

        public void SubmitAnimation(Guid playerId, int frames = 3) => Apply(GameActor.Player(playerId),
            new SubmitAnimationAction(Enumerable.Range(0, frames).Select(_ => Guid.NewGuid()).ToArray()));

        public void SubmitAllAnimations(int frames = 3)
        {
            foreach (var playerId in PlayerIds)
            {
                SubmitAnimation(playerId, frames);
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

        public VoteControllerConfiguration VoteView(Guid playerId) => PlayerView(playerId)
            .Controller.Configuration.Deserialize<VoteControllerConfiguration>()!;

        public GameTransition Vote(Guid playerId, Guid submissionPlayerId) => Apply(
            GameActor.Player(playerId), new VoteForShowdownAnimationAction(submissionPlayerId));

        public (string DrawingPrompt, string Distractor) AssignedPrompt(Guid playerId)
        {
            var prompt = State.Data.GetProperty("RoundOnePrompts").GetProperty(playerId.ToString());
            return (prompt.GetProperty("DrawingPrompt").GetString()!,
                prompt.GetProperty("Distractor").GetString()!);
        }

        public void ReachShowdownBriefing()
        {
            SubmitAllAnimations();
            for (var turn = 0; turn < PlayerIds.Length; turn++)
            {
                CompleteTurnWithoutChoices();
                Apply(GameActor.Host("host"), new AdvanceAniMatesAction());
            }
        }

        public DisplayGameViewPayload DisplayView() => module.CreateView(State,
            new GameViewContext(GameAudienceRole.Display, "display", null))
            .Data.Deserialize<DisplayGameViewPayload>()!;
    }
}
