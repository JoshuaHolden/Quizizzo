using System.Text.Json;
using Quizizzo.Domain;
using Quizizzo.GameContracts;
using Quizizzo.GameEngine;
using Quizizzo.Games.Bullshit;

namespace Quizizzo.GameEngine.Tests;

public sealed class BullshitGameModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Bluffing_uses_bounded_text_and_keeps_truth_and_answers_out_of_role_views()
    {
        var game = new Fixture();
        var initial = game.PlayerView(game.PlayerIds[0]);
        var configuration = initial.Controller.Configuration.Deserialize<TextControllerConfiguration>();
        game.Apply(GameActor.Player(game.PlayerIds[0]), new SubmitBluffAction("  The   moon  "));
        game.Apply(GameActor.Player(game.PlayerIds[1]), new SubmitBluffAction("a tittle"));

        var roleViews = string.Join('|',
            JsonSerializer.Serialize(game.PlayerView(game.PlayerIds[0])),
            JsonSerializer.Serialize(game.PlayerView(game.PlayerIds[1])),
            JsonSerializer.Serialize(game.HostView()),
            JsonSerializer.Serialize(game.DisplayView()));

        Assert.Equal(PlayerControllerKind.Text, initial.Controller.Kind);
        Assert.Equal(QuizizzoLimits.TextAnswerLength, configuration!.MaximumLength);
        Assert.DoesNotContain("A tittle", roleViews, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("The moon", roleViews, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Choosing_has_persisted_non_identity_shuffle_and_anonymous_opaque_choices()
    {
        var game = new Fixture();
        var sourceOrder = new[] { "A tittle", "Moon", "Cloud", "Cheese", "Toast" };
        game.SubmitBluffs("Moon", "Cloud", "Cheese", "Toast");

        var firstDisplay = game.DisplayView();
        var recoveredDisplay = game.DisplayView();
        var player = game.PlayerView(game.PlayerIds[0]);
        var configuration = player.Controller.Configuration.Deserialize<ChoiceControllerConfiguration>();
        var firstOrder = firstDisplay.Entries.Select(entry => entry.Value).ToArray();

        Assert.Equal(BullshitGameModule.ChoosingPhase, game.State.Phase);
        Assert.False(sourceOrder.SequenceEqual(firstOrder));
        Assert.Equal(firstOrder, recoveredDisplay.Entries.Select(entry => entry.Value));
        Assert.Equal(PlayerControllerKind.Choice, player.Controller.Kind);
        Assert.Equal("choiceId", configuration!.SelectionProperty);
        Assert.Equal("round-0:choice", configuration.SelectionScope);
        Assert.DoesNotContain(configuration.Options, option => option.Detail == "Moon");
        Assert.All(firstDisplay.Entries, entry => Assert.StartsWith("Answer ", entry.Label));
        Assert.DoesNotContain("Player 1", JsonSerializer.Serialize(firstDisplay), StringComparison.Ordinal);
        Assert.DoesNotContain(firstDisplay.Entries, entry => game.PlayerIds.Contains(entry.PlayerId));
    }

    [Fact]
    public void Duplicate_bluffs_share_one_choice_and_self_choice_is_rejected_for_every_author()
    {
        var game = new Fixture();
        game.SubmitBluffs("Moon", "moon", "Cloud", "Toast");
        var moonChoiceId = game.ChoiceId("Moon");

        var firstOptions = game.ChoiceOptions(game.PlayerIds[0]);
        var secondOptions = game.ChoiceOptions(game.PlayerIds[1]);
        var selfChoice = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[0]),
            new ChooseBullshitAnswerAction(moonChoiceId)));

        Assert.Single(game.DisplayView().Entries, entry =>
            string.Equals(entry.Value, "Moon", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(firstOptions, option => option.Id == moonChoiceId.ToString("N"));
        Assert.DoesNotContain(secondOptions, option => option.Id == moonChoiceId.ToString("N"));
        Assert.Equal("self-choice", selfChoice.Code);

        game.Choose(game.PlayerIds[0], "A tittle");
        game.Choose(game.PlayerIds[1], "A tittle");
        game.Choose(game.PlayerIds[2], "A tittle");
        var result = game.Choose(game.PlayerIds[3], "Moon");
        Assert.Equal(1500, result.ScoreAwards.Single(award => award.PlayerId == game.PlayerIds[0]).Points);
        Assert.Equal(1500, result.ScoreAwards.Single(award => award.PlayerId == game.PlayerIds[1]).Points);
    }

    [Fact]
    public void Reveal_combines_truth_vote_bluff_and_exact_truth_scoring()
    {
        var game = new Fixture();
        game.SubmitBluffs("Moon", "Cloud", "A tittle", "Toast");

        game.Choose(game.PlayerIds[0], "Cloud");
        game.Choose(game.PlayerIds[1], "A tittle");
        var result = game.Choose(game.PlayerIds[3], "Cloud");

        var display = game.DisplayView();
        var secondPlayer = game.PlayerView(game.PlayerIds[1]);
        Assert.Equal(BullshitGameModule.ResultsPhase, game.State.Phase);
        Assert.Equal(2000, result.ScoreAwards.Single(award => award.PlayerId == game.PlayerIds[1]).Points);
        Assert.Equal(1000, result.ScoreAwards.Single(award => award.PlayerId == game.PlayerIds[2]).Points);
        Assert.Contains(display.Entries, entry => entry.Label == "TRUTH" && entry.Value.Contains("A tittle"));
        Assert.Contains(display.Entries, entry => entry.Label == "Bluff by Player 2");
        Assert.Contains(display.Entries, entry => entry.Label == "Exact answer: Player 3");
        Assert.Contains("2,000", secondPlayer.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_duplicate_and_forged_actions_are_rejected_server_side()
    {
        var game = new Fixture();
        var empty = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[0]), new SubmitBluffAction(" ")));
        var oversized = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[0]),
            new SubmitBluffAction(new string('x', QuizizzoLimits.TextAnswerLength + 1))));
        game.Apply(GameActor.Player(game.PlayerIds[0]), new SubmitBluffAction("Moon"));
        var duplicate = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[0]), new SubmitBluffAction("Cloud")));
        game.Apply(GameActor.Player(game.PlayerIds[1]), new SubmitBluffAction("Cloud"));
        game.Apply(GameActor.Player(game.PlayerIds[2]), new SubmitBluffAction("Cheese"));
        game.Apply(GameActor.Player(game.PlayerIds[3]), new SubmitBluffAction("Toast"));
        var forged = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[0]), new ChooseBullshitAnswerAction(Guid.NewGuid())));
        game.Choose(game.PlayerIds[0], "Cloud");
        var repeated = Assert.Throws<GameRuleViolationException>(() => game.Choose(
            game.PlayerIds[0], "Cheese"));

        Assert.Equal("invalid-bluff", empty.Code);
        Assert.Equal("invalid-bluff", oversized.Code);
        Assert.Equal("already-submitted", duplicate.Code);
        Assert.Equal("invalid-choice", forged.Code);
        Assert.Equal("already-chosen", repeated.Code);
    }

    [Fact]
    public void Deadlines_preserve_partial_play_and_reveal_missing_actions_without_points()
    {
        var game = new Fixture();
        game.Apply(GameActor.Player(game.PlayerIds[0]), new SubmitBluffAction("Moon"));
        var bluffDeadline = game.State.PhaseEndsAtUtc!.Value;

        game.Apply(GameActor.SystemActor, new DeadlineElapsedAction(bluffDeadline), bluffDeadline);
        var choosingDeadline = game.State.PhaseEndsAtUtc!.Value;
        var recovered = game.DisplayView();
        game.Apply(GameActor.SystemActor, new DeadlineElapsedAction(choosingDeadline), choosingDeadline);

        Assert.Equal(2, recovered.Entries.Count);
        Assert.Equal(BullshitGameModule.ResultsPhase, game.State.Phase);
        Assert.Empty(game.LastTransition.ScoreAwards);
        Assert.Contains(game.DisplayView().Entries, entry => entry.Label == "TRUTH");
    }

    [Fact]
    public void Three_round_loop_completes_from_reconstructable_results()
    {
        var game = new Fixture();
        for (var round = 0; round < 3; round++)
        {
            game.SubmitBluffs($"Moon {round}", $"Cloud {round}", $"Cheese {round}", $"Toast {round}");
            game.Choose(game.PlayerIds[0], $"Cloud {round}");
            game.Choose(game.PlayerIds[1], $"Moon {round}");
            game.Choose(game.PlayerIds[2], $"Moon {round}");
            game.Choose(game.PlayerIds[3], $"Moon {round}");

            Assert.Contains($"ROUND {round + 1}/3", game.DisplayView().Title, StringComparison.OrdinalIgnoreCase);
            game.Apply(GameActor.Host("host"), new AdvanceBullshitAction());
        }

        Assert.True(game.State.IsComplete);
        Assert.Equal(BullshitGameModule.CompletedPhase, game.State.Phase);
    }

    [Fact]
    public void Transport_decoder_rejects_malformed_text_and_choice_payloads()
    {
        var module = new BullshitGameModule();
        var choiceId = Guid.NewGuid();
        var bluff = module.DecodeAction(
            SubmitBluffAction.ActionKind,
            GameJson.From(new { value = "Moon" }));
        var choice = module.DecodeAction(
            ChooseBullshitAnswerAction.ActionKind,
            GameJson.From(new { choiceId }));

        Assert.Equal("Moon", Assert.IsType<SubmitBluffAction>(bluff).Value);
        Assert.Equal(choiceId, Assert.IsType<ChooseBullshitAnswerAction>(choice).ChoiceId);
        var malformed = Assert.Throws<GameRuleViolationException>(() => module.DecodeAction(
            ChooseBullshitAnswerAction.ActionKind,
            GameJson.From(new { choiceId = "nope" })));
        Assert.Equal("invalid-choice", malformed.Code);
    }

    [Fact]
    public async Task Runtime_makes_bluffs_idempotent_and_rejects_late_submissions()
    {
        var clock = new AdjustableTimeProvider(Now);
        var module = new BullshitGameModule(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
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
            new SubmitBluffAction("Moon"));

        var first = await manager.ExecuteAsync(command);
        var retry = await manager.ExecuteAsync(command);
        clock.Advance(TimeSpan.FromMinutes(2));
        var late = await manager.ExecuteAsync(new GameCommand(
            GameCommandId.New(), instanceId, partyId, GameActor.Player(playerId),
            new SubmitBluffAction("Cloud")));

        Assert.Equal(GameCommandOutcome.Applied, first.Outcome);
        Assert.True(retry.IsDuplicate);
        Assert.Equal(first.Revision, retry.Revision);
        Assert.Equal("action-too-late", late.ErrorCode);
    }

    private sealed class Fixture
    {
        private readonly BullshitGameModule module = new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1));
        private readonly GameInstanceId instanceId = GameInstanceId.New();
        private readonly Guid partyId = Guid.NewGuid();

        public Fixture()
        {
            PlayerIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
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

        public void SubmitBluffs(params string[] answers)
        {
            for (var index = 0; index < answers.Length; index++)
            {
                Apply(GameActor.Player(PlayerIds[index]), new SubmitBluffAction(answers[index]));
            }
        }

        public GameTransition Choose(Guid playerId, string answer) => Apply(
            GameActor.Player(playerId), new ChooseBullshitAnswerAction(ChoiceId(answer)));

        public Guid ChoiceId(string answer) => DisplayView().Entries
            .Single(entry => string.Equals(entry.Value, answer, StringComparison.OrdinalIgnoreCase))
            .PlayerId;

        public IReadOnlyList<ControllerOption> ChoiceOptions(Guid playerId) => PlayerView(playerId)
            .Controller.Configuration.Deserialize<ChoiceControllerConfiguration>()!.Options;

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
