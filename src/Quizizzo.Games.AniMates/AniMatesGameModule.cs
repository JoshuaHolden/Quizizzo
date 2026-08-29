using System.Text.Json;
using Quizizzo.GameContracts;

namespace Quizizzo.Games.AniMates;

public sealed class AniMatesGameModule(
    TimeSpan? drawingDuration = null,
    TimeSpan? votingDuration = null) : IGameModule
{
    public const string GameKey = "animates";
    public const string DrawingPhase = "Drawing";
    public const string VotingPhase = "Voting";
    public const string ResultsPhase = "Results";
    public const string CompletedPhase = "Completed";
    public const int RequiredFrameCount = 3;
    public const int LogicalSize = 512;
    public const int MaximumSubmissionPayloadBytes = 6 * 1024 * 1024;

    private readonly TimeSpan drawingDuration = drawingDuration ?? TimeSpan.FromSeconds(90);
    private readonly TimeSpan votingDuration = votingDuration ?? TimeSpan.FromSeconds(30);

    public GameDescriptor Descriptor { get; } = new(GameKey, "AniMates", 2, 12);

    public GameModuleState Start(GameStartContext context)
    {
        var participants = context.Participants.Select((participant, index) =>
            new AnimateParticipant(
                participant.PlayerId,
                participant.DisplayName,
                Prompts[index % Prompts.Length])).ToArray();
        return ModuleState(
            DrawingPhase,
            context.StartedAtUtc.Add(drawingDuration),
            false,
            new AnimateState(participants, new Dictionary<Guid, AnimationSubmission>(),
                new Dictionary<Guid, Guid>(), []));
    }

    public GameTransition Apply(
        GameModuleState state,
        GameActionContext context,
        IGameAction action)
    {
        var animate = ReadState(state);
        return action switch
        {
            SubmitAnimationAction submission => Submit(state, animate, context, submission),
            VoteForAnimationAction vote => Vote(state, animate, context, vote),
            DeadlineElapsedAction => Deadline(state, animate, context),
            AdvanceAniMatesAction => Advance(state, animate, context),
            _ => throw new GameRuleViolationException(
                "unsupported-action", $"Action '{action.Kind}' is not supported by AniMates.")
        };
    }

    public GameViewPayload CreateView(GameModuleState state, GameViewContext context)
    {
        var animate = ReadState(state);
        return context.Role switch
        {
            GameAudienceRole.Host => new(GameJson.From(HostView(state, animate))),
            GameAudienceRole.Display => new(GameJson.From(DisplayView(state, animate))),
            GameAudienceRole.Player => new(GameJson.From(PlayerView(state, animate, context))),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    public IGameAction DecodeAction(string actionKind, JsonElement payload) => actionKind switch
    {
        SubmitAnimationAction.ActionKind => new SubmitAnimationAction(ReadFrameAssetIds(payload)),
        VoteForAnimationAction.ActionKind => new VoteForAnimationAction(
            ReadGuid(payload, "submissionPlayerId", "invalid-vote")),
        AdvanceAniMatesAction.ActionKind => new AdvanceAniMatesAction(),
        _ => throw new GameRuleViolationException(
            "unsupported-action", $"Action '{actionKind}' is not supported by AniMates.")
    };

    private GameTransition Submit(
        GameModuleState current,
        AnimateState state,
        GameActionContext context,
        SubmitAnimationAction action)
    {
        if (current.Phase != DrawingPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Drawing submissions are closed.");
        }
        var playerId = RequiredPlayer(state, context);
        if (state.Submissions.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-submitted", "Your animation is already submitted.");
        }
        if (action.FrameAssetIds.Count is < 1 or > RequiredFrameCount ||
            action.FrameAssetIds.Any(assetId => assetId == Guid.Empty))
        {
            throw new GameRuleViolationException(
                "invalid-frames", "Submit between one and three valid animation frames.");
        }

        var normalizedFrames = action.FrameAssetIds.ToList();
        while (normalizedFrames.Count < RequiredFrameCount)
        {
            normalizedFrames.Add(normalizedFrames[^1]);
        }
        var submissions = state.Submissions.ToDictionary();
        submissions.Add(playerId, new AnimationSubmission(playerId, normalizedFrames));
        var updated = state with { Submissions = submissions };
        if (submissions.Count == state.Participants.Count)
        {
            return BeginVoting(updated, context.ReceivedAtUtc);
        }

        return new GameTransition(
            current with { Data = GameJson.From(updated) },
            [],
            [new GameEvent("AnimationSubmitted", GameJson.From(new { playerId }))]);
    }

    private static GameTransition Vote(
        GameModuleState current,
        AnimateState state,
        GameActionContext context,
        VoteForAnimationAction action)
    {
        if (current.Phase != VotingPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Animation voting is not open.");
        }
        var playerId = RequiredPlayer(state, context);
        if (state.Votes.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-voted", "Your vote is already locked in.");
        }
        if (action.SubmissionPlayerId == playerId)
        {
            throw new GameRuleViolationException("self-vote", "You cannot vote for your own animation.");
        }
        if (!state.Submissions.ContainsKey(action.SubmissionPlayerId))
        {
            throw new GameRuleViolationException("invalid-vote", "That animation is not available.");
        }

        var votes = state.Votes.ToDictionary();
        votes.Add(playerId, action.SubmissionPlayerId);
        var updated = state with { Votes = votes };
        if (EligibleVoters(updated).All(votes.ContainsKey))
        {
            return Reveal(updated);
        }

        return new GameTransition(
            current with { Data = GameJson.From(updated) },
            [],
            [new GameEvent("AnimationVoteSubmitted", GameJson.From(new { playerId }))]);
    }

    private GameTransition Deadline(
        GameModuleState current,
        AnimateState state,
        GameActionContext context) => current.Phase switch
        {
            DrawingPhase => BeginVoting(state, context.ReceivedAtUtc),
            VotingPhase => Reveal(state),
            _ => throw new GameRuleViolationException("wrong-phase", "This phase has no active deadline.")
        };

    private GameTransition BeginVoting(AnimateState state, DateTimeOffset now)
    {
        if (state.Submissions.Count == 0 || EligibleVoters(state).Length == 0)
        {
            return Reveal(state);
        }
        return new GameTransition(
            ModuleState(VotingPhase, now.Add(votingDuration), false, state),
            [],
            [new GameEvent("AnimationVotingStarted", GameJson.From(new
            {
                submissions = state.Submissions.Count
            }))]);
    }

    private static GameTransition Reveal(AnimateState state)
    {
        var voteCounts = state.Votes.Values
            .GroupBy(playerId => playerId)
            .ToDictionary(group => group.Key, group => group.Count());
        var ordered = state.Submissions.Keys
            .Select(playerId => new { PlayerId = playerId, Votes = voteCounts.GetValueOrDefault(playerId) })
            .OrderByDescending(result => result.Votes)
            .ThenBy(result => result.PlayerId)
            .ToArray();
        var results = new List<AnimationResult>(ordered.Length);
        int? previousVotes = null;
        var rank = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            if (previousVotes != ordered[index].Votes)
            {
                rank = index + 1;
                previousVotes = ordered[index].Votes;
            }
            results.Add(new AnimationResult(
                ordered[index].PlayerId,
                ordered[index].Votes,
                rank,
                ordered[index].Votes > 0 ? PointsForRank(rank) : 0));
        }
        var revealed = state with { Results = results };
        return new GameTransition(
            ModuleState(ResultsPhase, null, false, revealed),
            results.Where(result => result.PointsAwarded > 0)
                .Select(result => new ScoreAward(
                    result.PlayerId,
                    result.PointsAwarded,
                    $"AniMates rank {result.Rank}"))
                .ToArray(),
            [new GameEvent("AnimationCreatorsRevealed", GameJson.Empty)]);
    }

    private static GameTransition Advance(
        GameModuleState current,
        AnimateState state,
        GameActionContext context)
    {
        if (context.Actor.Role != GameActorRole.Host)
        {
            throw new GameRuleViolationException("host-required", "Only the host can finish AniMates.");
        }
        if (current.Phase != ResultsPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Results must be revealed first.");
        }
        return new GameTransition(
            ModuleState(CompletedPhase, null, true, state),
            [],
            [new GameEvent("GameCompleted", GameJson.Empty)]);
    }

    private static PlayerGameViewPayload PlayerView(
        GameModuleState current,
        AnimateState state,
        GameViewContext context)
    {
        var playerId = context.PlayerId
            ?? throw new GameRuleViolationException("player-required", "A player view requires an identity.");
        var participant = state.Participants.Single(player => player.PlayerId == playerId);
        if (current.Phase == DrawingPhase && !state.Submissions.ContainsKey(playerId))
        {
            return new PlayerGameViewPayload(
                "Animate with your mates!",
                participant.Prompt,
                new PlayerControllerView(
                    PlayerControllerKind.Drawing,
                    SubmitAnimationAction.ActionKind,
                    true,
                    "Send my animation",
                    GameJson.From(new DrawingControllerConfiguration(
                        LogicalSize,
                        LogicalSize,
                        RequiredFrameCount,
                        "animates-drawing",
                        true))),
                GameJson.From(new { submitted = false, requiredFrames = RequiredFrameCount }));
        }

        if (current.Phase == VotingPhase && !state.Votes.ContainsKey(playerId))
        {
            var options = state.Submissions.Values
                .Where(submission => submission.PlayerId != playerId)
                .OrderBy(submission => submission.PlayerId)
                .Select((submission, index) => new ControllerOption(
                    submission.PlayerId.ToString("N"),
                    $"Animation {index + 1}",
                    null,
                    submission.FrameAssetIds))
                .ToArray();
            if (options.Length > 0)
            {
                return new PlayerGameViewPayload(
                    "Vote for your favourite",
                    "Animations are anonymous. You cannot vote for yourself.",
                    new PlayerControllerView(
                        PlayerControllerKind.Vote,
                        VoteForAnimationAction.ActionKind,
                        true,
                        "Cast my vote",
                        GameJson.From(new VoteControllerConfiguration(
                            options,
                            null,
                            "submissionPlayerId",
                            "animates:vote"))),
                    GameJson.From(new { submitted = true, voted = false }));
            }
        }

        var ownResult = state.Results.SingleOrDefault(result => result.PlayerId == playerId);
        var instructions = current.Phase switch
        {
            DrawingPhase => "Animation submitted. Waiting for everyone else...",
            VotingPhase => state.Votes.ContainsKey(playerId)
                ? "Vote locked. Waiting for the reveal..."
                : "There is no eligible animation for you to vote on.",
            ResultsPhase => ownResult is null
                ? "Watch the creator reveal on the main screen."
                : $"You received {ownResult.Votes} vote(s) and earned {ownResult.PointsAwarded:N0} points.",
            _ => "AniMates complete."
        };
        return Waiting(current.Phase == ResultsPhase ? "Results" : "Please wait", instructions);
    }

    private static HostGameViewPayload HostView(GameModuleState current, AnimateState state) => new(
        "AniMates",
        current.Phase == DrawingPhase ? "Players are creating three-frame animations" : "Favourite animation",
        PhaseMessage(current, state),
        current.Phase == DrawingPhase ? state.Submissions.Count : state.Votes.Count,
        current.Phase == DrawingPhase ? state.Participants.Count : EligibleVoters(state).Length,
        current.Phase == ResultsPhase,
        current.Phase == ResultsPhase ? AdvanceAniMatesAction.ActionKind : null,
        current.Phase == ResultsPhase ? "Finish AniMates" : null,
        Entries(current, state));

    private static DisplayGameViewPayload DisplayView(GameModuleState current, AnimateState state) => new(
        "AniMates",
        current.Phase == DrawingPhase ? "Draw your prompts!" : "Vote for your favourite animation",
        PhaseMessage(current, state),
        current.Phase == DrawingPhase ? state.Submissions.Count : state.Votes.Count,
        current.Phase == DrawingPhase ? state.Participants.Count : EligibleVoters(state).Length,
        Entries(current, state),
        current.Phase is VotingPhase or ResultsPhase
            ? new DrawingPresentationView(
                current.Phase == VotingPhase ? "Playback" : "Reveal",
                150,
                AnimationViews(current, state))
            : null);

    private static DrawingAnimationView[] AnimationViews(
        GameModuleState current,
        AnimateState state) => state.Submissions.Values
        .OrderBy(submission => submission.PlayerId)
        .Select(submission =>
        {
            var participant = state.Participants.Single(player => player.PlayerId == submission.PlayerId);
            var result = state.Results.SingleOrDefault(item => item.PlayerId == submission.PlayerId);
            return new DrawingAnimationView(
                submission.PlayerId,
                current.Phase == ResultsPhase ? participant.DisplayName : null,
                participant.Prompt,
                submission.FrameAssetIds,
                result?.Votes ?? 0,
                result?.Rank,
                result?.PointsAwarded ?? 0);
        }).ToArray();

    private static GamePresentationEntry[] Entries(
        GameModuleState current,
        AnimateState state)
    {
        if (current.Phase == DrawingPhase)
        {
            return state.Participants.Select(player => new GamePresentationEntry(
                player.PlayerId,
                player.DisplayName,
                state.Submissions.ContainsKey(player.PlayerId) ? "Ready ✓" : "Drawing...",
                null,
                0)).ToArray();
        }
        if (current.Phase == VotingPhase)
        {
            return [];
        }
        return state.Results.OrderBy(result => result.Rank).Select(result =>
        {
            var player = state.Participants.Single(participant => participant.PlayerId == result.PlayerId);
            return new GamePresentationEntry(
                result.PlayerId,
                player.DisplayName,
                $"{result.Votes} vote(s)",
                result.Rank,
                result.PointsAwarded);
        }).ToArray();
    }

    private static string PhaseMessage(GameModuleState current, AnimateState state) => current.Phase switch
    {
        DrawingPhase => $"{state.Submissions.Count}/{state.Participants.Count} animations submitted",
        VotingPhase => $"{state.Votes.Count}/{EligibleVoters(state).Length} votes locked in",
        ResultsPhase => "Creators revealed!",
        _ => "AniMates complete"
    };

    private static Guid[] EligibleVoters(AnimateState state) => state.Participants
        .Where(player => state.Submissions.Keys.Any(ownerId => ownerId != player.PlayerId))
        .Select(player => player.PlayerId)
        .ToArray();

    private static Guid RequiredPlayer(AnimateState state, GameActionContext context)
    {
        if (!context.Actor.TryGetPlayerId(out var playerId) ||
            !state.Participants.Any(player => player.PlayerId == playerId))
        {
            throw new GameRuleViolationException("player-required", "A current player is required.");
        }
        return playerId;
    }

    private static PlayerGameViewPayload Waiting(string heading, string instructions) => new(
        heading,
        instructions,
        new PlayerControllerView(
            PlayerControllerKind.Waiting,
            string.Empty,
            false,
            string.Empty,
            GameJson.Empty),
        GameJson.Empty);

    private static List<Guid> ReadFrameAssetIds(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("frameAssetIds", out var frames) ||
            frames.ValueKind != JsonValueKind.Array ||
            frames.GetArrayLength() is < 1 or > RequiredFrameCount)
        {
            throw new GameRuleViolationException("invalid-frames", "One to three frame asset IDs are required.");
        }
        var result = new List<Guid>(frames.GetArrayLength());
        foreach (var frame in frames.EnumerateArray())
        {
            if (frame.ValueKind != JsonValueKind.String || !frame.TryGetGuid(out var assetId) || assetId == Guid.Empty)
            {
                throw new GameRuleViolationException("invalid-frames", "Every frame requires a valid asset ID.");
            }
            result.Add(assetId);
        }
        return result;
    }

    private static Guid ReadGuid(JsonElement payload, string propertyName, string errorCode)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            value.TryGetGuid(out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }
        throw new GameRuleViolationException(errorCode, "A valid choice is required.");
    }

    private static int PointsForRank(int rank) => rank switch
    {
        1 => 1000,
        2 => 600,
        3 => 300,
        _ => 0
    };

    private static AnimateState ReadState(GameModuleState state) =>
        state.Data.Deserialize<AnimateState>()
        ?? throw new InvalidOperationException("AniMates state could not be read.");

    private static GameModuleState ModuleState(
        string phase,
        DateTimeOffset? deadline,
        bool complete,
        AnimateState state) => new(1, phase, deadline, complete, GameJson.From(state));

    private static readonly string[] Prompts =
    [
        "Spanking a blue dog",
        "Escaping from a giant sandwich",
        "Trying to open an umbrella indoors",
        "A penguin learning to skateboard",
        "Fighting with a stubborn deckchair",
        "Celebrating after finding the TV remote",
        "A robot attempting a cartwheel",
        "Running away from an angry goose",
        "A wizard whose spell backfires",
        "Dancing on a very slippery floor",
        "Waking a dragon with an alarm clock",
        "Trying to carry too many balloons"
    ];

    private sealed record AnimateState(
        IReadOnlyList<AnimateParticipant> Participants,
        IReadOnlyDictionary<Guid, AnimationSubmission> Submissions,
        IReadOnlyDictionary<Guid, Guid> Votes,
        IReadOnlyList<AnimationResult> Results);

    private sealed record AnimateParticipant(Guid PlayerId, string DisplayName, string Prompt);

    private sealed record AnimationSubmission(Guid PlayerId, IReadOnlyList<Guid> FrameAssetIds);

    private sealed record AnimationResult(Guid PlayerId, int Votes, int Rank, int PointsAwarded);
}
