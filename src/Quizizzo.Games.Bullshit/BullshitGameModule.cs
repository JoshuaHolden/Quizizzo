using System.Security.Cryptography;
using System.Text.Json;
using Quizizzo.Domain;
using Quizizzo.GameContracts;

namespace Quizizzo.Games.Bullshit;

public sealed class BullshitGameModule(
    TimeSpan? bluffingDuration = null,
    TimeSpan? choosingDuration = null) : IGameModule
{
    public const string GameKey = "bullshit";
    public const string BluffingPhase = "Bluffing";
    public const string ChoosingPhase = "Choosing";
    public const string ResultsPhase = "Results";
    public const string CompletedPhase = "Completed";
    public const int CorrectChoicePoints = 1000;
    public const int SuccessfulBluffPoints = 500;
    public const int ExactTruthBluffPoints = 1000;

    private readonly TimeSpan bluffingDuration = bluffingDuration ?? TimeSpan.FromSeconds(45);
    private readonly TimeSpan choosingDuration = choosingDuration ?? TimeSpan.FromSeconds(30);

    public GameDescriptor Descriptor { get; } = new(GameKey, "Bullshit", 3, 12);

    public GameModuleState Start(GameStartContext context) => ModuleState(
        BluffingPhase,
        context.StartedAtUtc.Add(bluffingDuration),
        false,
        new BullshitState(
            0,
            Questions,
            context.Participants.Select(participant =>
                new BullshitParticipant(participant.PlayerId, participant.DisplayName)).ToArray(),
            new Dictionary<Guid, BluffSubmission>(),
            [],
            new Dictionary<Guid, Guid>(),
            []));

    public GameTransition Apply(
        GameModuleState state,
        GameActionContext context,
        IGameAction action)
    {
        var bullshit = ReadState(state);
        return action switch
        {
            SubmitBluffAction submission => Submit(state, bullshit, context, submission),
            ChooseBullshitAnswerAction choice => Choose(state, bullshit, context, choice),
            DeadlineElapsedAction => Deadline(state, bullshit, context),
            AdvanceBullshitAction => Advance(state, bullshit, context),
            _ => throw new GameRuleViolationException(
                "unsupported-action", $"Action '{action.Kind}' is not supported by Bullshit.")
        };
    }

    public GameViewPayload CreateView(GameModuleState state, GameViewContext context)
    {
        var bullshit = ReadState(state);
        return context.Role switch
        {
            GameAudienceRole.Host => new(GameJson.From(HostView(state, bullshit))),
            GameAudienceRole.Display => new(GameJson.From(DisplayView(state, bullshit))),
            GameAudienceRole.Player => new(GameJson.From(PlayerView(state, bullshit, context))),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    public IGameAction DecodeAction(string actionKind, JsonElement payload) => actionKind switch
    {
        SubmitBluffAction.ActionKind => new SubmitBluffAction(ReadText(payload)),
        ChooseBullshitAnswerAction.ActionKind => new ChooseBullshitAnswerAction(
            ReadGuid(payload, "choiceId")),
        AdvanceBullshitAction.ActionKind => new AdvanceBullshitAction(),
        _ => throw new GameRuleViolationException(
            "unsupported-action", $"Action '{actionKind}' is not supported by Bullshit.")
    };

    private GameTransition Submit(
        GameModuleState current,
        BullshitState state,
        GameActionContext context,
        SubmitBluffAction action)
    {
        if (current.Phase != BluffingPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Bluffs are not open right now.");
        }
        var playerId = RequiredPlayer(state, context);
        if (state.Submissions.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-submitted", "Your bluff is already locked in.");
        }

        var answer = NormalizeAnswer(action.Value);
        var matchesTruth = string.Equals(
            answer,
            state.Questions[state.RoundIndex].Truth,
            StringComparison.OrdinalIgnoreCase);
        var submissions = state.Submissions.ToDictionary();
        submissions.Add(playerId, new BluffSubmission(answer, matchesTruth));
        var updated = state with { Submissions = submissions };
        if (submissions.Count == state.Participants.Count)
        {
            return BeginChoosing(updated, context.ReceivedAtUtc);
        }

        return new GameTransition(
            current with { Data = GameJson.From(updated) },
            [],
            [new GameEvent("BluffSubmitted", GameJson.From(new { playerId }))]);
    }

    private static GameTransition Choose(
        GameModuleState current,
        BullshitState state,
        GameActionContext context,
        ChooseBullshitAnswerAction action)
    {
        if (current.Phase != ChoosingPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Answer choices are not open right now.");
        }
        var playerId = RequiredPlayer(state, context);
        if (!EligibleVoters(state).Contains(playerId))
        {
            throw new GameRuleViolationException(
                "not-eligible", "You are not eligible to choose an answer this round.");
        }
        if (state.Votes.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-chosen", "Your answer choice is already locked in.");
        }

        var selected = state.Choices.SingleOrDefault(choice => choice.ChoiceId == action.ChoiceId);
        if (selected is null)
        {
            throw new GameRuleViolationException("invalid-choice", "That answer is not available.");
        }
        if (selected.AuthorIds.Contains(playerId))
        {
            throw new GameRuleViolationException("self-choice", "You cannot choose your own bluff.");
        }

        var votes = state.Votes.ToDictionary();
        votes.Add(playerId, selected.ChoiceId);
        var updated = state with { Votes = votes };
        if (EligibleVoters(updated).All(votes.ContainsKey))
        {
            return Reveal(updated);
        }

        return new GameTransition(
            current with { Data = GameJson.From(updated) },
            [],
            [new GameEvent("BullshitChoiceSubmitted", GameJson.From(new { playerId }))]);
    }

    private GameTransition Deadline(
        GameModuleState current,
        BullshitState state,
        GameActionContext context) => current.Phase switch
        {
            BluffingPhase => BeginChoosing(state, context.ReceivedAtUtc),
            ChoosingPhase => Reveal(state),
            _ => throw new GameRuleViolationException("wrong-phase", "This phase has no active deadline.")
        };

    private GameTransition BeginChoosing(BullshitState state, DateTimeOffset now)
    {
        var question = state.Questions[state.RoundIndex];
        var choices = new List<BullshitChoice>
        {
            new(Guid.NewGuid(), question.Truth, true, [])
        };
        choices.AddRange(state.Submissions
            .Where(submission => !submission.Value.MatchesTruth)
            .GroupBy(submission => submission.Value.Answer, StringComparer.OrdinalIgnoreCase)
            .Select(group => new BullshitChoice(
                Guid.NewGuid(),
                group.First().Value.Answer,
                false,
                group.Select(submission => submission.Key).Order().ToArray())));
        ShuffleChoices(choices);

        var choosing = state with { Choices = choices };
        if (choices.Count < 2 || EligibleVoters(choosing).Length == 0)
        {
            return Reveal(choosing);
        }
        return new GameTransition(
            ModuleState(ChoosingPhase, now.Add(choosingDuration), false, choosing),
            [],
            [new GameEvent("BullshitChoicesOpened", GameJson.From(new { choices = choices.Count }))]);
    }

    private static GameTransition Reveal(BullshitState state)
    {
        var awards = state.Participants.ToDictionary(
            participant => participant.PlayerId,
            _ => new MutableAward());

        foreach (var submission in state.Submissions.Where(submission => submission.Value.MatchesTruth))
        {
            awards[submission.Key].ExactTruthPoints += ExactTruthBluffPoints;
        }

        foreach (var vote in state.Votes)
        {
            var choice = state.Choices.Single(candidate => candidate.ChoiceId == vote.Value);
            if (choice.IsTruth)
            {
                awards[vote.Key].CorrectChoicePoints += CorrectChoicePoints;
            }
            else
            {
                foreach (var authorId in choice.AuthorIds)
                {
                    awards[authorId].BluffPoints += SuccessfulBluffPoints;
                }
            }
        }

        var roundAwards = awards
            .Where(award => award.Value.Total > 0)
            .Select(award => new BullshitAward(
                award.Key,
                award.Value.CorrectChoicePoints,
                award.Value.BluffPoints,
                award.Value.ExactTruthPoints))
            .OrderByDescending(award => award.Total)
            .ThenBy(award => award.PlayerId)
            .ToArray();
        var revealed = state with { Awards = roundAwards };
        return new GameTransition(
            ModuleState(ResultsPhase, null, false, revealed),
            roundAwards.Select(award => new ScoreAward(
                award.PlayerId,
                award.Total,
                $"Bullshit round {state.RoundIndex + 1}: truth {award.CorrectChoicePoints}, " +
                $"bluffs {award.BluffPoints}, exact answer {award.ExactTruthPoints}"))
                .ToArray(),
            [new GameEvent("BullshitTruthRevealed", GameJson.Empty)]);
    }

    private GameTransition Advance(
        GameModuleState current,
        BullshitState state,
        GameActionContext context)
    {
        if (context.Actor.Role != GameActorRole.Host)
        {
            throw new GameRuleViolationException("host-required", "Only the host can advance Bullshit.");
        }
        if (current.Phase != ResultsPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Results must be revealed first.");
        }
        if (state.RoundIndex >= state.Questions.Count - 1)
        {
            return new GameTransition(
                ModuleState(CompletedPhase, null, true, state),
                [],
                [new GameEvent("GameCompleted", GameJson.Empty)]);
        }

        var next = state with
        {
            RoundIndex = state.RoundIndex + 1,
            Submissions = new Dictionary<Guid, BluffSubmission>(),
            Choices = [],
            Votes = new Dictionary<Guid, Guid>(),
            Awards = []
        };
        return new GameTransition(
            ModuleState(BluffingPhase, context.ReceivedAtUtc.Add(bluffingDuration), false, next),
            [],
            [new GameEvent("RoundStarted", GameJson.From(new { round = next.RoundIndex + 1 }))]);
    }

    private static PlayerGameViewPayload PlayerView(
        GameModuleState current,
        BullshitState state,
        GameViewContext context)
    {
        var playerId = context.PlayerId
            ?? throw new GameRuleViolationException("player-required", "A player identity is required.");
        if (!state.Participants.Any(participant => participant.PlayerId == playerId))
        {
            throw new GameRuleViolationException("player-required", "A current player is required.");
        }

        var question = state.Questions[state.RoundIndex];
        if (current.Phase == BluffingPhase && !state.Submissions.ContainsKey(playerId))
        {
            return new PlayerGameViewPayload(
                $"Round {state.RoundIndex + 1} of {state.Questions.Count}",
                $"Invent a convincing answer: {question.Prompt}",
                new PlayerControllerView(
                    PlayerControllerKind.Text,
                    SubmitBluffAction.ActionKind,
                    true,
                    "Submit bluff",
                    GameJson.From(new TextControllerConfiguration(
                        QuizizzoLimits.TextAnswerLength,
                        "Make up a believable answer..."))),
                GameJson.From(new { submitted = false }));
        }

        if (current.Phase == ChoosingPhase &&
            EligibleVoters(state).Contains(playerId) &&
            !state.Votes.ContainsKey(playerId))
        {
            var options = state.Choices
                .Select((choice, index) => new { Choice = choice, Index = index })
                .Where(item => !item.Choice.AuthorIds.Contains(playerId))
                .Select(item => new ControllerOption(
                    item.Choice.ChoiceId.ToString("N"),
                    $"Answer {item.Index + 1}",
                    item.Choice.Text))
                .ToArray();
            return new PlayerGameViewPayload(
                "Find the truth",
                "Choose the real answer. Your own bluff is hidden from you.",
                new PlayerControllerView(
                    PlayerControllerKind.Choice,
                    ChooseBullshitAnswerAction.ActionKind,
                    true,
                    "Lock in answer",
                    GameJson.From(new ChoiceControllerConfiguration(
                        options,
                        null,
                        "choiceId",
                        $"round-{state.RoundIndex}:choice"))),
                GameJson.From(new { submitted = true, chosen = false }));
        }

        var award = state.Awards.SingleOrDefault(item => item.PlayerId == playerId);
        var instructions = current.Phase switch
        {
            BluffingPhase => "Bluff locked. Waiting for the other players...",
            ChoosingPhase => state.Votes.ContainsKey(playerId)
                ? "Answer locked. Waiting for the truth..."
                : "You found the exact answer while bluffing. Wait for the reveal.",
            ResultsPhase => award is null
                ? "No points this round. The next one is another chance."
                : $"You earned {award.Total:N0}: {award.CorrectChoicePoints:N0} for truth, " +
                  $"{award.BluffPoints:N0} from bluffs, {award.ExactTruthPoints:N0} for an exact answer.",
            _ => "Bullshit complete."
        };
        return Waiting(current.Phase == ResultsPhase ? "Truth revealed" : "Please wait", instructions);
    }

    private static HostGameViewPayload HostView(GameModuleState current, BullshitState state) => new(
        $"Bullshit - Round {state.RoundIndex + 1}/{state.Questions.Count}",
        state.Questions[state.RoundIndex].Prompt,
        PhaseMessage(current, state),
        current.Phase == BluffingPhase ? state.Submissions.Count : state.Votes.Count,
        current.Phase == BluffingPhase ? state.Participants.Count : EligibleVoters(state).Length,
        current.Phase == ResultsPhase,
        current.Phase == ResultsPhase ? AdvanceBullshitAction.ActionKind : null,
        current.Phase == ResultsPhase
            ? state.RoundIndex == state.Questions.Count - 1 ? "Finish Bullshit" : "Next round"
            : null,
        Entries(current, state));

    private static DisplayGameViewPayload DisplayView(GameModuleState current, BullshitState state) => new(
        $"BULLSHIT - ROUND {state.RoundIndex + 1}/{state.Questions.Count}",
        state.Questions[state.RoundIndex].Prompt,
        PhaseMessage(current, state),
        current.Phase == BluffingPhase ? state.Submissions.Count : state.Votes.Count,
        current.Phase == BluffingPhase ? state.Participants.Count : EligibleVoters(state).Length,
        Entries(current, state));

    private static IReadOnlyList<GamePresentationEntry> Entries(
        GameModuleState current,
        BullshitState state)
    {
        if (current.Phase == BluffingPhase)
        {
            return state.Participants.Select(participant => new GamePresentationEntry(
                participant.PlayerId,
                participant.DisplayName,
                state.Submissions.ContainsKey(participant.PlayerId) ? "Ready" : "Writing...",
                null,
                0)).ToArray();
        }
        if (current.Phase == ChoosingPhase)
        {
            return state.Choices.Select((choice, index) => new GamePresentationEntry(
                choice.ChoiceId,
                $"Answer {index + 1}",
                choice.Text,
                null,
                0)).ToArray();
        }

        var voteCounts = state.Votes.Values.GroupBy(choiceId => choiceId)
            .ToDictionary(group => group.Key, group => group.Count());
        var entries = state.Choices
            .OrderByDescending(choice => choice.IsTruth)
            .ThenByDescending(choice => voteCounts.GetValueOrDefault(choice.ChoiceId))
            .Select(choice =>
            {
                var votes = voteCounts.GetValueOrDefault(choice.ChoiceId);
                var authors = string.Join(" & ", choice.AuthorIds.Select(authorId =>
                    state.Participants.Single(participant => participant.PlayerId == authorId).DisplayName));
                var label = choice.IsTruth ? "TRUTH" : $"Bluff by {authors}";
                var payout = choice.IsTruth
                    ? votes * CorrectChoicePoints
                    : votes * SuccessfulBluffPoints;
                return new GamePresentationEntry(
                    choice.ChoiceId,
                    label,
                    $"\"{choice.Text}\" - {votes} pick(s), {payout:N0} points" +
                    (!choice.IsTruth && choice.AuthorIds.Count > 1 ? " per author" : string.Empty),
                    null,
                    payout);
            }).ToList();
        entries.AddRange(state.Submissions
            .Where(submission => submission.Value.MatchesTruth)
            .Select(submission =>
            {
                var participant = state.Participants.Single(player => player.PlayerId == submission.Key);
                return new GamePresentationEntry(
                    participant.PlayerId,
                    $"Exact answer: {participant.DisplayName}",
                    $"Guessed the truth while bluffing - {ExactTruthBluffPoints:N0} points",
                    null,
                    ExactTruthBluffPoints);
            }));
        return entries;
    }

    private static string PhaseMessage(GameModuleState current, BullshitState state) => current.Phase switch
    {
        BluffingPhase => $"{state.Submissions.Count}/{state.Participants.Count} bluffs submitted",
        ChoosingPhase => $"{state.Votes.Count}/{EligibleVoters(state).Length} answers locked in",
        ResultsPhase => "Truth and bluffers revealed!",
        _ => "Bullshit complete"
    };

    private static Guid[] EligibleVoters(BullshitState state) => state.Participants
        .Where(participant =>
            !state.Submissions.TryGetValue(participant.PlayerId, out var submission) ||
            !submission.MatchesTruth)
        .Where(participant => state.Choices.Any(choice => !choice.AuthorIds.Contains(participant.PlayerId)))
        .Select(participant => participant.PlayerId)
        .ToArray();

    private static Guid RequiredPlayer(BullshitState state, GameActionContext context)
    {
        if (!context.Actor.TryGetPlayerId(out var playerId) ||
            !state.Participants.Any(player => player.PlayerId == playerId))
        {
            throw new GameRuleViolationException("player-required", "A current player is required.");
        }
        return playerId;
    }

    private static string NormalizeAnswer(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new GameRuleViolationException("invalid-bluff", "Enter a bluff before submitting.");
        }
        var normalized = string.Join(' ', value.Trim().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length > QuizizzoLimits.TextAnswerLength ||
            normalized.Any(character => char.IsControl(character)))
        {
            throw new GameRuleViolationException(
                "invalid-bluff", $"Bluffs must be at most {QuizizzoLimits.TextAnswerLength} characters.");
        }
        return normalized;
    }

    private static string ReadText(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.String &&
            value.GetString() is { } text)
        {
            return text;
        }
        throw new GameRuleViolationException("invalid-bluff", "A text bluff is required.");
    }

    private static Guid ReadGuid(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            value.TryGetGuid(out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }
        throw new GameRuleViolationException("invalid-choice", "A valid answer choice is required.");
    }

    private static void ShuffleChoices(List<BullshitChoice> choices)
    {
        var original = choices.Select(choice => choice.ChoiceId).ToArray();
        for (var index = choices.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (choices[index], choices[swap]) = (choices[swap], choices[index]);
        }
        if (choices.Count > 1 && choices.Select(choice => choice.ChoiceId).SequenceEqual(original))
        {
            var first = choices[0];
            choices.RemoveAt(0);
            choices.Add(first);
        }
    }

    private static PlayerGameViewPayload Waiting(string heading, string instructions) => new(
        heading,
        instructions,
        new PlayerControllerView(
            PlayerControllerKind.Waiting, string.Empty, false, string.Empty, GameJson.Empty),
        GameJson.Empty);

    private static BullshitState ReadState(GameModuleState state) =>
        state.Data.Deserialize<BullshitState>()
        ?? throw new InvalidOperationException("Bullshit state could not be read.");

    private static GameModuleState ModuleState(
        string phase,
        DateTimeOffset? deadline,
        bool complete,
        BullshitState state) => new(1, phase, deadline, complete, GameJson.From(state));

    private static readonly BullshitQuestion[] Questions =
    [
        new("What is the dot over a lowercase i or j called?", "A tittle"),
        new("What is a group of flamingos called?", "A flamboyance"),
        new("Which animal has fingerprints remarkably similar to humans?", "A koala")
    ];

    private sealed record BullshitState(
        int RoundIndex,
        IReadOnlyList<BullshitQuestion> Questions,
        IReadOnlyList<BullshitParticipant> Participants,
        Dictionary<Guid, BluffSubmission> Submissions,
        IReadOnlyList<BullshitChoice> Choices,
        IReadOnlyDictionary<Guid, Guid> Votes,
        IReadOnlyList<BullshitAward> Awards);

    private sealed record BullshitQuestion(string Prompt, string Truth);

    private sealed record BullshitParticipant(Guid PlayerId, string DisplayName);

    private sealed record BluffSubmission(string Answer, bool MatchesTruth);

    private sealed record BullshitChoice(
        Guid ChoiceId,
        string Text,
        bool IsTruth,
        IReadOnlyList<Guid> AuthorIds);

    private sealed record BullshitAward(
        Guid PlayerId,
        int CorrectChoicePoints,
        int BluffPoints,
        int ExactTruthPoints)
    {
        public int Total => checked(CorrectChoicePoints + BluffPoints + ExactTruthPoints);
    }

    private sealed class MutableAward
    {
        public int CorrectChoicePoints { get; set; }
        public int BluffPoints { get; set; }
        public int ExactTruthPoints { get; set; }
        public int Total => checked(CorrectChoicePoints + BluffPoints + ExactTruthPoints);
    }
}
