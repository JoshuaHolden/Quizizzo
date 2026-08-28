using System.Text.Json;
using Quizizzo.Domain;
using Quizizzo.GameContracts;
using Quizizzo.GameEngine;
using Quizizzo.Games.MajorityRules;

namespace Quizizzo.GameEngine.Tests;

public sealed class MajorityRulesGameModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Answering_uses_the_reusable_text_controller_and_hides_answer_content()
    {
        var game = new Fixture();
        game.Apply(GameActor.Player(game.PlayerIds[0]), new SubmitMajorityAnswerAction("  In   a lift  "));

        var waiting = game.PlayerView(game.PlayerIds[0]);
        var answering = game.PlayerView(game.PlayerIds[1]);
        var configuration = answering.Controller.Configuration.Deserialize<TextControllerConfiguration>();
        var displayJson = JsonSerializer.Serialize(game.DisplayView());
        var hostJson = JsonSerializer.Serialize(game.HostView());

        Assert.Equal(PlayerControllerKind.Waiting, waiting.Controller.Kind);
        Assert.Equal(PlayerControllerKind.Text, answering.Controller.Kind);
        Assert.Equal(QuizizzoLimits.TextAnswerLength, configuration!.MaximumLength);
        Assert.DoesNotContain("In a lift", displayJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("In a lift", hostJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Answers_are_normalized_and_voting_is_anonymous()
    {
        var game = new Fixture();
        game.SubmitAnswers("  In   a lift  ", "At the dentist", "On a date");

        var view = game.PlayerView(game.PlayerIds[1]);
        var configuration = view.Controller.Configuration.Deserialize<VoteControllerConfiguration>();

        Assert.Equal(MajorityRulesGameModule.VotingPhase, game.State.Phase);
        Assert.Equal(PlayerControllerKind.Vote, view.Controller.Kind);
        Assert.Equal("answerOptionId", configuration!.SelectionProperty);
        Assert.Equal("round-0:vote", configuration.SelectionScope);
        Assert.DoesNotContain(configuration.Options, option => option.Id == game.PlayerIds[1].ToString("N"));
        Assert.Contains(configuration.Options, option => option.Detail == "In a lift");
        Assert.All(configuration.Options, option => Assert.StartsWith("Answer ", option.Label));
        Assert.DoesNotContain("Player 1", JsonSerializer.Serialize(configuration), StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_duplicate_and_self_vote_actions_are_rejected()
    {
        var game = new Fixture();
        var empty = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[0]), new SubmitMajorityAnswerAction("  ")));
        var oversized = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[0]),
            new SubmitMajorityAnswerAction(new string('x', QuizizzoLimits.TextAnswerLength + 1))));
        game.Apply(GameActor.Player(game.PlayerIds[0]), new SubmitMajorityAnswerAction("Lift"));
        var duplicate = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[0]), new SubmitMajorityAnswerAction("Another")));
        game.Apply(GameActor.Player(game.PlayerIds[1]), new SubmitMajorityAnswerAction("Dentist"));
        game.Apply(GameActor.Player(game.PlayerIds[2]), new SubmitMajorityAnswerAction("Date"));
        var ownOption = game.DisplayView().Entries.Single(entry => entry.Value == "Lift").PlayerId;
        var selfVote = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[0]), new VoteForMajorityAnswerAction(ownOption)));

        Assert.Equal("invalid-answer", empty.Code);
        Assert.Equal("invalid-answer", oversized.Code);
        Assert.Equal("already-submitted", duplicate.Code);
        Assert.Equal("self-vote", selfVote.Code);
    }

    [Fact]
    public void Votes_award_points_to_answer_authors_and_reveal_names_only_at_results()
    {
        var game = new Fixture();
        game.SubmitAnswers("Lift", "Dentist", "Date");
        var options = game.VoteOptions();
        game.Apply(GameActor.Player(game.PlayerIds[0]), new VoteForMajorityAnswerAction(options[game.PlayerIds[1]]));
        game.Apply(GameActor.Player(game.PlayerIds[1]), new VoteForMajorityAnswerAction(options[game.PlayerIds[0]]));
        var result = game.Apply(
            GameActor.Player(game.PlayerIds[2]), new VoteForMajorityAnswerAction(options[game.PlayerIds[0]]));

        var display = game.DisplayView();
        Assert.Equal(MajorityRulesGameModule.ResultsPhase, game.State.Phase);
        Assert.Equal(1000, result.ScoreAwards.Single(award => award.PlayerId == game.PlayerIds[0]).Points);
        Assert.Equal(500, result.ScoreAwards.Single(award => award.PlayerId == game.PlayerIds[1]).Points);
        Assert.DoesNotContain(result.ScoreAwards, award => award.PlayerId == game.PlayerIds[2]);
        Assert.Contains(display.Entries, entry => entry.PlayerId == game.PlayerIds[0] && entry.Label == "Player 1");
    }

    [Fact]
    public void Deadlines_advance_answering_and_voting_with_partial_participation()
    {
        var game = new Fixture();
        game.Apply(GameActor.Player(game.PlayerIds[0]), new SubmitMajorityAnswerAction("Lift"));
        game.Apply(GameActor.Player(game.PlayerIds[1]), new SubmitMajorityAnswerAction("Dentist"));
        var answerDeadline = game.State.PhaseEndsAtUtc!.Value;

        game.Apply(GameActor.SystemActor, new DeadlineElapsedAction(answerDeadline), answerDeadline);
        var votingDeadline = game.State.PhaseEndsAtUtc!.Value;
        game.Apply(GameActor.SystemActor, new DeadlineElapsedAction(votingDeadline), votingDeadline);

        Assert.Equal(MajorityRulesGameModule.ResultsPhase, game.State.Phase);
        Assert.Empty(game.LastTransition.ScoreAwards);
    }

    [Fact]
    public void Three_round_loop_completes_and_reconstructed_views_keep_round_state()
    {
        var game = new Fixture();
        for (var round = 0; round < 3; round++)
        {
            game.SubmitAnswers($"Lift {round}", $"Dentist {round}", $"Date {round}");
            var options = game.VoteOptions();
            game.Apply(GameActor.Player(game.PlayerIds[0]), new VoteForMajorityAnswerAction(options[game.PlayerIds[1]]));
            game.Apply(GameActor.Player(game.PlayerIds[1]), new VoteForMajorityAnswerAction(options[game.PlayerIds[0]]));
            game.Apply(GameActor.Player(game.PlayerIds[2]), new VoteForMajorityAnswerAction(options[game.PlayerIds[0]]));

            var recovered = game.DisplayView();
            Assert.Contains($"ROUND {round + 1}/3", recovered.Title, StringComparison.OrdinalIgnoreCase);
            game.Apply(GameActor.Host("host"), new AdvanceMajorityRulesAction());
        }

        Assert.True(game.State.IsComplete);
        Assert.Equal(MajorityRulesGameModule.CompletedPhase, game.State.Phase);
    }

    [Fact]
    public void Transport_decoder_enforces_text_and_vote_shapes()
    {
        var module = new MajorityRulesGameModule();
        var answer = module.DecodeAction(
            SubmitMajorityAnswerAction.ActionKind,
            GameJson.From(new { value = "Hello" }));
        var voteId = Guid.NewGuid();
        var vote = module.DecodeAction(
            VoteForMajorityAnswerAction.ActionKind,
            GameJson.From(new { answerOptionId = voteId }));

        Assert.Equal("Hello", Assert.IsType<SubmitMajorityAnswerAction>(answer).Value);
        Assert.Equal(voteId, Assert.IsType<VoteForMajorityAnswerAction>(vote).AnswerOptionId);
        var malformed = Assert.Throws<GameRuleViolationException>(() => module.DecodeAction(
            VoteForMajorityAnswerAction.ActionKind,
            GameJson.From(new { answerOptionId = "nope" })));
        Assert.Equal("invalid-vote", malformed.Code);
    }

    [Fact]
    public async Task Runtime_makes_text_commands_idempotent_and_rejects_late_actions()
    {
        var clock = new AdjustableTimeProvider(Now);
        var module = new MajorityRulesGameModule(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
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
            [
                new GameParticipant(playerId, "One"),
                new GameParticipant(Guid.NewGuid(), "Two"),
                new GameParticipant(Guid.NewGuid(), "Three")
            ]));
        var command = new GameCommand(
            GameCommandId.New(), instanceId, partyId, GameActor.Player(playerId),
            new SubmitMajorityAnswerAction("Lift"));

        var first = await manager.ExecuteAsync(command);
        var retry = await manager.ExecuteAsync(command);
        clock.Advance(TimeSpan.FromMinutes(2));
        var late = await manager.ExecuteAsync(new GameCommand(
            GameCommandId.New(), instanceId, partyId, GameActor.Player(playerId),
            new SubmitMajorityAnswerAction("Late")));

        Assert.Equal(GameCommandOutcome.Applied, first.Outcome);
        Assert.True(retry.IsDuplicate);
        Assert.Equal(first.Revision, retry.Revision);
        Assert.Equal("action-too-late", late.ErrorCode);
    }

    private sealed class Fixture
    {
        private readonly MajorityRulesGameModule module = new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1));
        private readonly GameInstanceId instanceId = GameInstanceId.New();
        private readonly Guid partyId = Guid.NewGuid();
        private readonly Dictionary<Guid, string> submittedAnswers = [];

        public Fixture()
        {
            PlayerIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
            State = module.Start(new GameStartContext(
                instanceId,
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
                new GameActionContext(instanceId, partyId, actor, at ?? Now.AddSeconds(1)),
                action);
            State = LastTransition.State;
            return LastTransition;
        }

        public void SubmitAnswers(params string[] answers)
        {
            for (var index = 0; index < answers.Length; index++)
            {
                Apply(GameActor.Player(PlayerIds[index]), new SubmitMajorityAnswerAction(answers[index]));
                submittedAnswers[PlayerIds[index]] = answers[index].Trim();
            }
        }

        public Dictionary<Guid, Guid> VoteOptions() => submittedAnswers.ToDictionary(
            answer => answer.Key,
            answer => DisplayView().Entries.Single(entry => entry.Value == answer.Value).PlayerId);

        public PlayerGameViewPayload PlayerView(Guid playerId) => module.CreateView(
            State,
            new GameViewContext(GameAudienceRole.Player, playerId.ToString("N"), playerId))
            .Data.Deserialize<PlayerGameViewPayload>()!;

        public HostGameViewPayload HostView() => module.CreateView(
            State,
            new GameViewContext(GameAudienceRole.Host, "host", null))
            .Data.Deserialize<HostGameViewPayload>()!;

        public DisplayGameViewPayload DisplayView() => module.CreateView(
            State,
            new GameViewContext(GameAudienceRole.Display, "display", null))
            .Data.Deserialize<DisplayGameViewPayload>()!;
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
