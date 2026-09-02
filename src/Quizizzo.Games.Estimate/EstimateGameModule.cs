using System.Text.Json;
using Quizizzo.GameContracts;

namespace Quizizzo.Games.Estimate;

public sealed class EstimateGameModule(TimeSpan? answerDuration = null) : IGameModule
{
    public const string GameKey = "estimate";
    public const string AnsweringPhase = "Answering";
    public const string ResultsPhase = "Results";
    public const string CompletedPhase = "Completed";

    private readonly TimeSpan answerDuration = answerDuration ?? TimeSpan.FromSeconds(30);

    public GameDescriptor Descriptor { get; } = new(GameKey, "Estimate", 2, 12);

    public GameModuleState Start(GameStartContext context)
    {
        var state = new EstimateState(
            0,
            Questions,
            context.Participants.Select(player =>
                new EstimateParticipant(player.PlayerId, player.DisplayName)).ToArray(),
            new Dictionary<Guid, long>(),
            []);
        return CreateModuleState(
            AnsweringPhase,
            context.StartedAtUtc.Add(answerDuration),
            false,
            state);
    }

    public GameTransition Apply(
        GameModuleState state,
        GameActionContext context,
        IGameAction action)
    {
        var estimate = ReadState(state);
        return action switch
        {
            SubmitEstimateAction submission => Submit(state, estimate, context, submission),
            DeadlineElapsedAction => Reveal(state, estimate),
            AdvanceEstimateAction => Advance(state, estimate, context),
            _ => throw new GameRuleViolationException(
                "unsupported-action", $"Action '{action.Kind}' is not supported by Estimate.")
        };
    }

    public GameViewPayload CreateView(GameModuleState state, GameViewContext context)
    {
        var estimate = ReadState(state);
        var question = estimate.Questions[estimate.RoundIndex];
        return context.Role switch
        {
            GameAudienceRole.Host => new(GameJson.From(CreateHostView(state, estimate, question))),
            GameAudienceRole.Display => new(GameJson.From(CreateDisplayView(state, estimate, question))),
            GameAudienceRole.Player => new(GameJson.From(CreatePlayerView(state, estimate, question, context))),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    public IGameAction DecodeAction(string actionKind, JsonElement payload) => actionKind switch
    {
        SubmitEstimateAction.ActionKind => new SubmitEstimateAction(ReadEstimate(payload)),
        AdvanceEstimateAction.ActionKind => new AdvanceEstimateAction(),
        _ => throw new GameRuleViolationException(
            "unsupported-action", $"Action '{actionKind}' is not supported by Estimate.")
    };

    private static GameTransition Submit(
        GameModuleState current,
        EstimateState state,
        GameActionContext context,
        SubmitEstimateAction action)
    {
        if (current.Phase != AnsweringPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Estimates are not open right now.");
        }
        if (!context.Actor.TryGetPlayerId(out var playerId) ||
            !state.Participants.Any(player => player.PlayerId == playerId))
        {
            throw new GameRuleViolationException("player-required", "A current player must submit the estimate.");
        }
        if (state.Submissions.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-submitted", "Your estimate is already locked in.");
        }

        var question = state.Questions[state.RoundIndex];
        if (action.Value < question.Minimum || action.Value > question.Maximum)
        {
            throw new GameRuleViolationException(
                "estimate-out-of-range",
                $"Enter a value from {question.Minimum:N0} to {question.Maximum:N0}.");
        }

        var submissions = state.Submissions.ToDictionary();
        submissions.Add(playerId, action.Value);
        var updated = state with { Submissions = submissions };
        if (submissions.Count == state.Participants.Count)
        {
            return Reveal(current, updated);
        }

        return new GameTransition(
            current with { Data = GameJson.From(updated) },
            [],
            [new GameEvent("EstimateSubmitted", GameJson.From(new { playerId }))]);
    }

    private static GameTransition Reveal(GameModuleState current, EstimateState state)
    {
        if (current.Phase != AnsweringPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "This Estimate round is not accepting a deadline.");
        }

        var question = state.Questions[state.RoundIndex];
        var submitted = state.Submissions
            .Select(pair => new
            {
                Player = state.Participants.Single(player => player.PlayerId == pair.Key),
                Estimate = pair.Value,
                Distance = Math.Abs(pair.Value - question.Answer)
            })
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Estimate)
            .ToArray();
        var results = new List<EstimateResult>(state.Participants.Count);
        long? previousDistance = null;
        var rank = 0;
        for (var index = 0; index < submitted.Length; index++)
        {
            var item = submitted[index];
            if (previousDistance != item.Distance)
            {
                rank = index + 1;
                previousDistance = item.Distance;
            }
            results.Add(new EstimateResult(
                item.Player.PlayerId,
                item.Player.DisplayName,
                item.Estimate,
                item.Distance,
                rank,
                PointsForRank(rank)));
        }
        results.AddRange(state.Participants
            .Where(player => !state.Submissions.ContainsKey(player.PlayerId))
            .Select(player => new EstimateResult(
                player.PlayerId, player.DisplayName, null, null, null, 0)));

        var revealed = state with { Results = results };
        var awards = results
            .Where(result => result.PointsAwarded > 0)
            .Select(result => new ScoreAward(
                result.PlayerId,
                result.PointsAwarded,
                $"Estimate round {state.RoundIndex + 1} rank {result.Rank}"))
            .ToArray();
        var events = new List<GameEvent>
        {
            new("AnswerRevealed", GameJson.From(new
            {
                round = state.RoundIndex + 1,
                answer = question.Answer,
                question.Suffix
            }))
        };
        if (results.FirstOrDefault(result => result.Rank == 1) is { } winner)
        {
            events.Add(new GameEvent("RoundWon", GameJson.From(new { winner.PlayerId })));
        }

        return new GameTransition(
            CreateModuleState(ResultsPhase, null, false, revealed),
            awards,
            events);
    }

    private GameTransition Advance(
        GameModuleState current,
        EstimateState state,
        GameActionContext context)
    {
        if (context.Actor.Role != GameActorRole.Host)
        {
            throw new GameRuleViolationException("host-required", "Only the host can advance Estimate.");
        }
        if (current.Phase != ResultsPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Estimate can advance only after results.");
        }

        if (state.RoundIndex >= state.Questions.Count - 1)
        {
            return new GameTransition(
                CreateModuleState(CompletedPhase, null, true, state),
                [],
                [new GameEvent("GameCompleted", GameJson.Empty)]);
        }

        var next = state with
        {
            RoundIndex = state.RoundIndex + 1,
            Submissions = new Dictionary<Guid, long>(),
            Results = []
        };
        return new GameTransition(
            CreateModuleState(
                AnsweringPhase,
                context.ReceivedAtUtc.Add(answerDuration),
                false,
                next),
            [],
            [new GameEvent("RoundStarted", GameJson.From(new { round = next.RoundIndex + 1 }))]);
    }

    private static PlayerGameViewPayload CreatePlayerView(
        GameModuleState current,
        EstimateState state,
        EstimateQuestion question,
        GameViewContext context)
    {
        var playerId = context.PlayerId
            ?? throw new GameRuleViolationException("player-required", "A player view requires a player ID.");
        state.Submissions.TryGetValue(playerId, out var submittedValue);
        var hasSubmitted = state.Submissions.ContainsKey(playerId);
        var ownResult = state.Results.SingleOrDefault(result => result.PlayerId == playerId);

        if (current.Phase == AnsweringPhase && !hasSubmitted)
        {
            return new PlayerGameViewPayload(
                $"Round {state.RoundIndex + 1} of {state.Questions.Count}",
                question.Prompt,
                new PlayerControllerView(
                    PlayerControllerKind.Number,
                    SubmitEstimateAction.ActionKind,
                    true,
                    "Lock in my guess",
                    GameJson.From(new NumberControllerConfiguration(
                        question.Minimum,
                        question.Maximum,
                        1,
                        question.Suffix,
                        null))),
                GameJson.From(new { submitted = false }));
        }

        var instructions = current.Phase == AnsweringPhase
            ? $"Locked in: {submittedValue:N0} {question.Suffix}"
            : ownResult is null || !ownResult.Estimate.HasValue
                ? $"The answer was {question.Answer:N0} {question.Suffix}. No estimate submitted."
                : $"You ranked #{ownResult.Rank} and earned {ownResult.PointsAwarded:N0} points.";
        return new PlayerGameViewPayload(
            current.Phase == AnsweringPhase ? "Estimate locked" : "Round results",
            instructions,
            new PlayerControllerView(
                PlayerControllerKind.Waiting,
                string.Empty,
                false,
                string.Empty,
                GameJson.Empty),
            GameJson.From(new
            {
                submitted = hasSubmitted,
                value = hasSubmitted ? submittedValue : (long?)null,
                answer = current.Phase == AnsweringPhase ? (long?)null : question.Answer,
                rank = ownResult?.Rank,
                points = ownResult?.PointsAwarded ?? 0
            }));
    }

    private static HostGameViewPayload CreateHostView(
        GameModuleState current,
        EstimateState state,
        EstimateQuestion question) => new(
        $"Estimate - Round {state.RoundIndex + 1}/{state.Questions.Count}",
        question.Prompt,
        PhaseMessage(current, state, question),
        state.Submissions.Count,
        state.Participants.Count,
        current.Phase == ResultsPhase,
        current.Phase == ResultsPhase ? AdvanceEstimateAction.ActionKind : null,
        current.Phase == ResultsPhase
            ? state.RoundIndex == state.Questions.Count - 1 ? "Finish Estimate" : "Next round"
            : null,
        CreateEntries(current, state, question));

    private static DisplayGameViewPayload CreateDisplayView(
        GameModuleState current,
        EstimateState state,
        EstimateQuestion question) => new(
        $"ESTIMATE - ROUND {state.RoundIndex + 1}/{state.Questions.Count}",
        question.Prompt,
        PhaseMessage(current, state, question),
        state.Submissions.Count,
        state.Participants.Count,
        CreateEntries(current, state, question),
        ShowRoundRanking: current.Phase == ResultsPhase);

    private static string PhaseMessage(
        GameModuleState current,
        EstimateState state,
        EstimateQuestion question) => current.Phase switch
        {
            AnsweringPhase => $"{state.Submissions.Count}/{state.Participants.Count} estimates locked in",
            ResultsPhase => $"Correct answer: {question.Answer:N0} {question.Suffix}",
            _ => "Estimate complete"
        };

    private static GamePresentationEntry[] CreateEntries(
        GameModuleState current,
        EstimateState state,
        EstimateQuestion question)
    {
        if (current.Phase == AnsweringPhase)
        {
            return state.Participants.Select(player => new GamePresentationEntry(
                player.PlayerId,
                player.DisplayName,
                state.Submissions.ContainsKey(player.PlayerId) ? "Locked in" : "Thinking...",
                null,
                0)).ToArray();
        }

        return state.Results
            .OrderBy(result => result.Rank ?? int.MaxValue)
            .ThenBy(result => result.DisplayName)
            .Select(result => new GamePresentationEntry(
                result.PlayerId,
                result.DisplayName,
                result.Estimate.HasValue
                    ? $"{result.Estimate:N0} {question.Suffix} - off by {result.Distance:N0}"
                    : "No answer",
                result.Rank,
                result.PointsAwarded))
            .ToArray();
    }

    private static int PointsForRank(int rank) => rank switch
    {
        1 => 1000,
        2 => 600,
        3 => 300,
        _ => 0
    };

    private static long ReadEstimate(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var estimate))
        {
            return estimate;
        }
        throw new GameRuleViolationException("invalid-estimate", "Enter a whole-number estimate.");
    }

    private static EstimateState ReadState(GameModuleState state) =>
        state.Data.Deserialize<EstimateState>()
        ?? throw new InvalidOperationException("Estimate state could not be read.");

    private static GameModuleState CreateModuleState(
        string phase,
        DateTimeOffset? deadline,
        bool complete,
        EstimateState state) => new(1, phase, deadline, complete, GameJson.From(state));

    private static readonly EstimateQuestion[] Questions =
    [
        new("How many minutes are in one week?", 10_080, 0, 50_000, "minutes"),
        new("How many keys are on a standard piano?", 88, 0, 500, "keys"),
        new("About how many kilometres of blood vessels are in the human body?", 100_000, 0, 1_000_000, "km")
    ];

    private sealed record EstimateState(
        int RoundIndex,
        IReadOnlyList<EstimateQuestion> Questions,
        IReadOnlyList<EstimateParticipant> Participants,
        Dictionary<Guid, long> Submissions,
        IReadOnlyList<EstimateResult> Results);

    private sealed record EstimateQuestion(
        string Prompt,
        long Answer,
        long Minimum,
        long Maximum,
        string Suffix);

    private sealed record EstimateParticipant(Guid PlayerId, string DisplayName);

    private sealed record EstimateResult(
        Guid PlayerId,
        string DisplayName,
        long? Estimate,
        long? Distance,
        int? Rank,
        int PointsAwarded);
}
