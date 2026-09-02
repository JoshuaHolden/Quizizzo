using System.Text.Json;
using Quizizzo.Domain;
using Quizizzo.GameContracts;

namespace Quizizzo.Games.MajorityRules;

public sealed class MajorityRulesGameModule(
    TimeSpan? answeringDuration = null,
    TimeSpan? votingDuration = null,
    TimeSpan? resultsDuration = null) : IGameModule
{
    public const string GameKey = "majority-rules";
    public const string AnsweringPhase = "Answering";
    public const string VotingPhase = "Voting";
    public const string ResultsPhase = "Results";
    public const string CompletedPhase = "Completed";
    public const int PointsPerVote = 500;

    private readonly TimeSpan answeringDuration = answeringDuration ?? TimeSpan.FromSeconds(45);
    private readonly TimeSpan votingDuration = votingDuration ?? TimeSpan.FromSeconds(30);
    private readonly TimeSpan resultsDuration = resultsDuration ?? TimeSpan.FromSeconds(10);

    public GameDescriptor Descriptor { get; } = new(GameKey, "Majority Rules", 3, 12);

    public GameModuleState Start(GameStartContext context) => ModuleState(
        AnsweringPhase,
        context.StartedAtUtc.Add(answeringDuration),
        false,
        new MajorityState(
            0,
            Questions,
            context.Participants.Select(player =>
                new MajorityParticipant(player.PlayerId, player.DisplayName)).ToArray(),
            new Dictionary<Guid, MajorityAnswer>(),
            new Dictionary<Guid, Guid>(),
            []));

    public GameTransition Apply(
        GameModuleState state,
        GameActionContext context,
        IGameAction action)
    {
        var majority = ReadState(state);
        return action switch
        {
            SubmitMajorityAnswerAction submission => Submit(state, majority, context, submission),
            VoteForMajorityAnswerAction vote => Vote(state, majority, context, vote),
            DeadlineElapsedAction => Deadline(state, majority, context),
            AdvanceMajorityRulesAction => Advance(state, majority, context),
            _ => throw new GameRuleViolationException(
                "unsupported-action", $"Action '{action.Kind}' is not supported by Majority Rules.")
        };
    }

    public GameViewPayload CreateView(GameModuleState state, GameViewContext context)
    {
        var majority = ReadState(state);
        return context.Role switch
        {
            GameAudienceRole.Host => new(GameJson.From(HostView(state, majority))),
            GameAudienceRole.Display => new(GameJson.From(DisplayView(state, majority))),
            GameAudienceRole.Player => new(GameJson.From(PlayerView(state, majority, context))),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    public IGameAction DecodeAction(string actionKind, JsonElement payload) => actionKind switch
    {
        SubmitMajorityAnswerAction.ActionKind => new SubmitMajorityAnswerAction(ReadText(payload)),
        VoteForMajorityAnswerAction.ActionKind => new VoteForMajorityAnswerAction(
            ReadGuid(payload, "answerOptionId")),
        AdvanceMajorityRulesAction.ActionKind => new AdvanceMajorityRulesAction(),
        _ => throw new GameRuleViolationException(
            "unsupported-action", $"Action '{actionKind}' is not supported by Majority Rules.")
    };

    private GameTransition Submit(
        GameModuleState current,
        MajorityState state,
        GameActionContext context,
        SubmitMajorityAnswerAction action)
    {
        if (current.Phase != AnsweringPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Answers are not open right now.");
        }
        var playerId = RequiredPlayer(state, context);
        if (state.Answers.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-submitted", "Your answer is already locked in.");
        }
        var answer = NormalizeAnswer(action.Value);
        var answers = state.Answers.ToDictionary();
        answers.Add(playerId, new MajorityAnswer(Guid.NewGuid(), answer));
        var updated = state with { Answers = answers };
        if (answers.Count == state.Participants.Count)
        {
            return BeginVoting(updated, context.ReceivedAtUtc);
        }
        return new GameTransition(
            current with { Data = GameJson.From(updated) },
            [],
            [new GameEvent("MajorityAnswerSubmitted", GameJson.From(new { playerId }))]);
    }

    private GameTransition Vote(
        GameModuleState current,
        MajorityState state,
        GameActionContext context,
        VoteForMajorityAnswerAction action)
    {
        if (current.Phase != VotingPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Voting is not open right now.");
        }
        var playerId = RequiredPlayer(state, context);
        if (state.Votes.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-voted", "Your vote is already locked in.");
        }
        var selectedAnswer = state.Answers.SingleOrDefault(
            answer => answer.Value.OptionId == action.AnswerOptionId);
        if (selectedAnswer.Key == Guid.Empty)
        {
            throw new GameRuleViolationException("invalid-vote", "That answer is not available.");
        }
        if (selectedAnswer.Key == playerId)
        {
            throw new GameRuleViolationException("self-vote", "You cannot vote for your own answer.");
        }

        var votes = state.Votes.ToDictionary();
        votes.Add(playerId, selectedAnswer.Key);
        var updated = state with { Votes = votes };
        if (EligibleVoters(updated).All(votes.ContainsKey))
        {
            return Reveal(updated, context.ReceivedAtUtc);
        }
        return new GameTransition(
            current with { Data = GameJson.From(updated) },
            [],
            [new GameEvent("MajorityVoteSubmitted", GameJson.From(new { playerId }))]);
    }

    private GameTransition Deadline(
        GameModuleState current,
        MajorityState state,
        GameActionContext context) => current.Phase switch
        {
            AnsweringPhase => BeginVoting(state, context.ReceivedAtUtc),
            VotingPhase => Reveal(state, context.ReceivedAtUtc),
            ResultsPhase => Progress(state, context.ReceivedAtUtc),
            _ => throw new GameRuleViolationException("wrong-phase", "This phase has no deadline.")
        };

    private GameTransition BeginVoting(MajorityState state, DateTimeOffset now)
    {
        if (state.Answers.Count == 0 || EligibleVoters(state).Length == 0)
        {
            return Reveal(state, now);
        }
        return new GameTransition(
            ModuleState(VotingPhase, now.Add(votingDuration), false, state),
            [],
            [new GameEvent("MajorityVotingStarted", GameJson.From(new { answers = state.Answers.Count }))]);
    }

    private GameTransition Reveal(MajorityState state, DateTimeOffset now)
    {
        var counts = state.Votes.Values.GroupBy(value => value)
            .ToDictionary(group => group.Key, group => group.Count());
        var ordered = state.Answers.Select(answer => new
        {
            PlayerId = answer.Key,
            Answer = answer.Value.Text,
            Votes = counts.GetValueOrDefault(answer.Key)
        }).OrderByDescending(item => item.Votes).ThenBy(item => item.Answer).ToArray();
        var results = new List<MajorityResult>(ordered.Length);
        int? previousVotes = null;
        var rank = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            if (previousVotes != ordered[index].Votes)
            {
                rank = index + 1;
                previousVotes = ordered[index].Votes;
            }
            results.Add(new MajorityResult(
                ordered[index].PlayerId,
                ordered[index].Answer,
                ordered[index].Votes,
                rank,
                checked(ordered[index].Votes * PointsPerVote)));
        }
        var revealed = state with { Results = results };
        return new GameTransition(
            ModuleState(ResultsPhase, now.Add(resultsDuration), false, revealed),
            results.Where(result => result.PointsAwarded > 0)
                .Select(result => new ScoreAward(
                    result.PlayerId,
                    result.PointsAwarded,
                    $"Majority Rules round {state.RoundIndex + 1}: {result.Votes} vote(s)"))
                .ToArray(),
            [new GameEvent("MajorityAnswersRevealed", GameJson.Empty)]);
    }

    private GameTransition Advance(
        GameModuleState current,
        MajorityState state,
        GameActionContext context)
    {
        if (context.Actor.Role != GameActorRole.Host)
        {
            throw new GameRuleViolationException("host-required", "Only the host can advance Majority Rules.");
        }
        if (current.Phase != ResultsPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Results must be revealed first.");
        }
        return Progress(state, context.ReceivedAtUtc);
    }

    private GameTransition Progress(MajorityState state, DateTimeOffset now)
    {
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
            Answers = new Dictionary<Guid, MajorityAnswer>(),
            Votes = new Dictionary<Guid, Guid>(),
            Results = []
        };
        return new GameTransition(
            ModuleState(AnsweringPhase, now.Add(answeringDuration), false, next),
            [],
            [new GameEvent("RoundStarted", GameJson.From(new { round = next.RoundIndex + 1 }))]);
    }

    private static PlayerGameViewPayload PlayerView(
        GameModuleState current,
        MajorityState state,
        GameViewContext context)
    {
        var playerId = context.PlayerId
            ?? throw new GameRuleViolationException("player-required", "A player identity is required.");
        if (current.Phase == AnsweringPhase && !state.Answers.ContainsKey(playerId))
        {
            return new PlayerGameViewPayload(
                $"Round {state.RoundIndex + 1} of {state.Questions.Count}",
                state.Questions[state.RoundIndex],
                new PlayerControllerView(
                    PlayerControllerKind.Text,
                    SubmitMajorityAnswerAction.ActionKind,
                    true,
                    "Send my answer",
                    GameJson.From(new TextControllerConfiguration(
                        QuizizzoLimits.TextAnswerLength,
                        "Type a funny answer..."))),
                GameJson.From(new { submitted = false }));
        }
        if (current.Phase == VotingPhase && !state.Votes.ContainsKey(playerId))
        {
            var options = state.Answers
                .Where(answer => answer.Key != playerId)
                .OrderBy(answer => answer.Value.OptionId)
                .Select((answer, index) => new ControllerOption(
                    answer.Value.OptionId.ToString("N"),
                    $"Answer {index + 1}",
                    answer.Value.Text))
                .ToArray();
            if (options.Length > 0)
            {
                return new PlayerGameViewPayload(
                    "Vote for the best answer",
                    "Answers are anonymous until results.",
                    new PlayerControllerView(
                        PlayerControllerKind.Vote,
                        VoteForMajorityAnswerAction.ActionKind,
                        true,
                        "Cast my vote",
                        GameJson.From(new VoteControllerConfiguration(
                            options,
                            null,
                            "answerOptionId",
                            $"round-{state.RoundIndex}:vote"))),
                    GameJson.From(new { submitted = true, voted = false }));
            }
        }

        var ownResult = state.Results.SingleOrDefault(result => result.PlayerId == playerId);
        var instructions = current.Phase switch
        {
            AnsweringPhase => "Answer locked. Waiting for everyone else...",
            VotingPhase => state.Votes.ContainsKey(playerId)
                ? "Vote locked. Waiting for results..."
                : "No eligible answer is available for you to vote on.",
            ResultsPhase => ownResult is null
                ? "See which answers won on the main screen."
                : $"Your answer received {ownResult.Votes} vote(s): +{ownResult.PointsAwarded:N0} points.",
            _ => "Majority Rules complete."
        };
        return Waiting(current.Phase == ResultsPhase ? "Round results" : "Please wait", instructions);
    }

    private static HostGameViewPayload HostView(GameModuleState current, MajorityState state) => new(
        $"Majority Rules - Round {state.RoundIndex + 1}/{state.Questions.Count}",
        state.Questions[state.RoundIndex],
        PhaseMessage(current, state),
        current.Phase == AnsweringPhase ? state.Answers.Count : state.Votes.Count,
        current.Phase == AnsweringPhase ? state.Participants.Count : EligibleVoters(state).Length,
        current.Phase == ResultsPhase,
        current.Phase == ResultsPhase ? AdvanceMajorityRulesAction.ActionKind : null,
        current.Phase == ResultsPhase ? "Continue now" : null,
        Entries(current, state));

    private static DisplayGameViewPayload DisplayView(GameModuleState current, MajorityState state) => new(
        $"MAJORITY RULES - ROUND {state.RoundIndex + 1}/{state.Questions.Count}",
        state.Questions[state.RoundIndex],
        PhaseMessage(current, state),
        current.Phase == AnsweringPhase ? state.Answers.Count : state.Votes.Count,
        current.Phase == AnsweringPhase ? state.Participants.Count : EligibleVoters(state).Length,
        Entries(current, state),
        ShowRoundRanking: current.Phase == ResultsPhase);

    private static GamePresentationEntry[] Entries(GameModuleState current, MajorityState state)
    {
        if (current.Phase == AnsweringPhase)
        {
            return state.Participants.Select(player => new GamePresentationEntry(
                player.PlayerId,
                player.DisplayName,
                state.Answers.ContainsKey(player.PlayerId) ? "Ready" : "Writing...",
                null,
                0)).ToArray();
        }
        if (current.Phase == VotingPhase)
        {
            return state.Answers.OrderBy(answer => answer.Value.OptionId)
                .Select((answer, index) => new GamePresentationEntry(
                    answer.Value.OptionId,
                    $"Answer {index + 1}",
                    answer.Value.Text,
                    null,
                    0)).ToArray();
        }
        return state.Results.OrderBy(result => result.Rank).Select(result =>
        {
            var player = state.Participants.Single(participant => participant.PlayerId == result.PlayerId);
            return new GamePresentationEntry(
                result.PlayerId,
                player.DisplayName,
                $"\"{result.Answer}\" - {result.Votes} vote(s)",
                result.Rank,
                result.PointsAwarded);
        }).ToArray();
    }

    private static string PhaseMessage(GameModuleState current, MajorityState state) => current.Phase switch
    {
        AnsweringPhase => $"{state.Answers.Count}/{state.Participants.Count} answers submitted",
        VotingPhase => $"{state.Votes.Count}/{EligibleVoters(state).Length} votes locked in",
        ResultsPhase => "The majority has spoken!",
        _ => "Majority Rules complete"
    };

    private static Guid[] EligibleVoters(MajorityState state) => state.Participants
        .Where(player => state.Answers.Keys.Any(ownerId => ownerId != player.PlayerId))
        .Select(player => player.PlayerId)
        .ToArray();

    private static Guid RequiredPlayer(MajorityState state, GameActionContext context)
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
            throw new GameRuleViolationException("invalid-answer", "Enter an answer before submitting.");
        }
        var normalized = string.Join(' ', value.Trim().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length > QuizizzoLimits.TextAnswerLength ||
            normalized.Any(character => char.IsControl(character)))
        {
            throw new GameRuleViolationException(
                "invalid-answer", $"Answers must be at most {QuizizzoLimits.TextAnswerLength} characters.");
        }
        return normalized;
    }

    private static string ReadText(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.String && value.GetString() is { } text)
        {
            return text;
        }
        throw new GameRuleViolationException("invalid-answer", "A text answer is required.");
    }

    private static Guid ReadGuid(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            Guid.TryParse(value.GetString(), out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }
        throw new GameRuleViolationException("invalid-vote", "A valid answer choice is required.");
    }

    private static PlayerGameViewPayload Waiting(string heading, string instructions) => new(
        heading,
        instructions,
        new PlayerControllerView(
            PlayerControllerKind.Waiting, string.Empty, false, string.Empty, GameJson.Empty),
        GameJson.Empty);

    private static MajorityState ReadState(GameModuleState state) =>
        state.Data.Deserialize<MajorityState>()
        ?? throw new InvalidOperationException("Majority Rules state could not be read.");

    private static GameModuleState ModuleState(
        string phase,
        DateTimeOffset? deadline,
        bool complete,
        MajorityState state) => new(1, phase, deadline, complete, GameJson.From(state));

    private static readonly string[] Questions =
    [
        "Where is the worst place to accidentally meet your boss?",
        "What is the least convincing excuse for being late?",
        "What should never be served at a fancy wedding?"
    ];

    private sealed record MajorityState(
        int RoundIndex,
        IReadOnlyList<string> Questions,
        IReadOnlyList<MajorityParticipant> Participants,
        IReadOnlyDictionary<Guid, MajorityAnswer> Answers,
        IReadOnlyDictionary<Guid, Guid> Votes,
        IReadOnlyList<MajorityResult> Results);

    private sealed record MajorityParticipant(Guid PlayerId, string DisplayName);

    private sealed record MajorityAnswer(Guid OptionId, string Text);

    private sealed record MajorityResult(
        Guid PlayerId,
        string Answer,
        int Votes,
        int Rank,
        int PointsAwarded);
}
