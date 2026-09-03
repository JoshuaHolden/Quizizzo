using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Quizizzo.GameContracts;

namespace Quizizzo.Games.SlopMachine;

public sealed partial class SlopMachineGameModule : IGameModule
{
    private static readonly JsonSerializerOptions CatalogueJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    public const string GameKey = "slop-machine";
    public const string GameIntroPhase = "GameIntro";
    public const string FreshIntroPhase = "FreshSlopIntro";
    public const string FreshWritingPhase = "FreshSlopWriting";
    public const string FreshVotingPhase = "FreshSlopVoting";
    public const string FreshResultsPhase = "FreshSlopResults";
    public const string ScoreReview1Phase = "ScoreReview1";
    public const string RouletteIntroPhase = "AlgorithmRouletteIntro";
    public const string RouletteSpinningPhase = "AlgorithmRouletteSpinning";
    public const string RouletteWritingPhase = "AlgorithmRouletteWriting";
    public const string RouletteVotingPhase = "AlgorithmRouletteVoting";
    public const string RouletteResultsPhase = "AlgorithmRouletteResults";
    public const string ScoreReview2Phase = "ScoreReview2";
    public const string TelephoneIntroPhase = "ThumbnailTelephoneIntro";
    public const string TelephoneWritingPhase = "TelephoneWriting";
    public const string TelephoneMatchingPhase = "TelephoneMatching";
    public const string TelephoneVotingPhase = "TelephoneVoting";
    public const string TelephoneResultsPhase = "TelephoneResults";
    public const string ScoreReview3Phase = "ScoreReview3";
    public const string CommentsIntroPhase = "CommentsIntro";
    public const string CommentsWritingPhase = "CommentsWriting";
    public const string CommentsVotingPhase = "CommentsVoting";
    public const string CommentsResultsPhase = "CommentsResults";
    public const string ScoreReview4Phase = "ScoreReview4";
    public const string FinalIntroPhase = "FinalIntro";
    public const string FinalWritingPhase = "FinalWriting";
    public const string FinalVotingPhase = "FinalVoting";
    public const string FinalMachineGuessPhase = "FinalMachineGuess";
    public const string FinalResultsPhase = "FinalResults";
    public const string FinalScoreReviewPhase = "FinalScoreReview";
    public const string WinnerCelebrationPhase = "WinnerCelebration";
    public const string CompletedPhase = "Completed";

    public const int MaximumTitleLength = 90;
    public const int MaximumCommentLength = 140;

    private static readonly string[] Formats =
    [
        "I tried ___ for 24 hours",
        "The truth about ___",
        "We found ___ inside ___",
        "___ has gone too far",
        "I spent £10,000 on ___",
        "Why nobody talks about ___",
        "Do not ___ at 3 a.m.",
        "I let my children ___"
    ];

    private static readonly string[] CommentTypes =
    [
        "Top comment", "Furious reply", "Pinned correction", "Community note",
        "Creator's excuse", "Fake expert warning"
    ];

    private readonly IReadOnlyList<SlopThumbnail> catalogue;
    private readonly TimeSpan titleDuration;
    private readonly TimeSpan rouletteDuration;
    private readonly TimeSpan telephoneWritingDuration;
    private readonly TimeSpan telephoneMatchingDuration;
    private readonly TimeSpan commentDuration;
    private readonly TimeSpan votingDuration;
    private readonly TimeSpan machineGuessDuration;
    private readonly TimeSpan introDuration;
    private readonly TimeSpan resultsDuration;
    private readonly TimeSpan scoreReviewDuration;
    private readonly TimeSpan winnerDuration;
    private readonly TimeSpan rouletteWritingDuration;

    public SlopMachineGameModule(
        IReadOnlyList<SlopThumbnail>? thumbnails = null,
        TimeSpan? titleDuration = null,
        TimeSpan? rouletteDuration = null,
        TimeSpan? telephoneWritingDuration = null,
        TimeSpan? telephoneMatchingDuration = null,
        TimeSpan? commentDuration = null,
        TimeSpan? votingDuration = null,
        TimeSpan? machineGuessDuration = null,
        TimeSpan? introDuration = null,
        TimeSpan? resultsDuration = null,
        TimeSpan? scoreReviewDuration = null,
        TimeSpan? winnerDuration = null,
        TimeSpan? rouletteWritingDuration = null)
    {
        catalogue = thumbnails ?? LoadCatalogue();
        ValidateCatalogue(catalogue);
        this.titleDuration = titleDuration ?? TimeSpan.FromSeconds(40);
        this.rouletteDuration = rouletteDuration ?? TimeSpan.FromSeconds(8);
        this.telephoneWritingDuration = telephoneWritingDuration ?? TimeSpan.FromSeconds(35);
        this.telephoneMatchingDuration = telephoneMatchingDuration ?? TimeSpan.FromSeconds(15);
        this.commentDuration = commentDuration ?? TimeSpan.FromSeconds(30);
        this.votingDuration = votingDuration ?? TimeSpan.FromSeconds(15);
        this.machineGuessDuration = machineGuessDuration ?? TimeSpan.FromSeconds(12);
        this.introDuration = introDuration ?? TimeSpan.FromSeconds(6);
        this.resultsDuration = resultsDuration ?? TimeSpan.FromSeconds(6);
        this.scoreReviewDuration = scoreReviewDuration ?? TimeSpan.FromSeconds(8);
        this.winnerDuration = winnerDuration ?? TimeSpan.FromSeconds(12);
        this.rouletteWritingDuration = rouletteWritingDuration ?? TimeSpan.FromSeconds(35);
    }

    public GameDescriptor Descriptor { get; } = new(
        GameKey,
        "Slop Machine",
        3,
        12,
        "Create terrible clickbait, steal ideas, fight the algorithm and grow your parody channel.",
        "Writing · Matching · Voting");

    public GameModuleState Start(GameStartContext context)
    {
        if (context.Participants.Count is < 3 or > 12)
        {
            throw new GameRuleViolationException(
                "invalid-player-count", "Slop Machine requires between 3 and 12 players.");
        }
        var participants = context.Participants.Select(participant =>
            new SlopParticipant(participant.PlayerId, participant.DisplayName, participant.StartingScore)).ToArray();
        var state = new SlopMachineState(
            participants, [], 0, null, new Dictionary<Guid, SlopAssignment>(),
            new Dictionary<Guid, string>(), [], [], new Dictionary<Guid, Guid>(),
            new Dictionary<Guid, TelephoneMatch>(), new Dictionary<Guid, IReadOnlyList<Guid>>(), [],
            participants.ToDictionary(item => item.PlayerId, _ => 0),
            participants.ToDictionary(item => item.PlayerId, item => item.StartingScore),
            false, "Feed the algorithm. Harvest the views.");
        return ModuleState(GameIntroPhase, context.StartedAtUtc.Add(this.introDuration), false, state);
    }

    public GameTransition Apply(GameModuleState state, GameActionContext context, IGameAction action)
    {
        var slop = ReadState(state);
        var transition = action switch
        {
            SubmitSlopTextAction submission => SubmitText(state, slop, context, submission),
            VoteForSlopAction vote => Vote(state, slop, context, vote),
            RespinSlopReelAction respin => Respin(state, slop, context, respin),
            MatchTelephoneThumbnailAction match => MatchTelephone(state, slop, context, match),
            IdentifyMachineTitleAction guess => IdentifyMachineTitle(state, slop, context, guess),
            AdvanceSlopMachineAction => Advance(state, slop, context),
            DeadlineElapsedAction => Deadline(state, slop, context),
            _ => throw new GameRuleViolationException(
                "unsupported-action", $"Action '{action.Kind}' is not supported by Slop Machine.")
        };
        return EnsureAutomaticDeadline(transition, context.ReceivedAtUtc);
    }

    public GameViewPayload CreateView(GameModuleState state, GameViewContext context)
    {
        var slop = ReadState(state);
        return context.Role switch
        {
            GameAudienceRole.Player => new(GameJson.From(PlayerView(state, slop, context))),
            GameAudienceRole.Host => new(GameJson.From(HostView(state, slop))),
            GameAudienceRole.Display => new(GameJson.From(DisplayView(state, slop))),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    public IGameAction DecodeAction(string actionKind, JsonElement payload) => actionKind switch
    {
        SubmitSlopTextAction.ActionKind => new SubmitSlopTextAction(
            ReadString(payload, "value"), ReadOptionalStrings(payload, "values")),
        VoteForSlopAction.ActionKind => new VoteForSlopAction(ReadGuid(payload, "optionId")),
        RespinSlopReelAction.ActionKind => new RespinSlopReelAction(ReadString(payload, "reel")),
        MatchTelephoneThumbnailAction.ActionKind =>
            new MatchTelephoneThumbnailAction(ReadString(payload, "thumbnailId")),
        IdentifyMachineTitleAction.ActionKind =>
            new IdentifyMachineTitleAction(ReadGuid(payload, "optionId")),
        AdvanceSlopMachineAction.ActionKind => new AdvanceSlopMachineAction(),
        _ => throw new GameRuleViolationException(
            "unsupported-action", $"Action '{actionKind}' is not supported by Slop Machine.")
    };

    public static void ValidateCatalogue(IReadOnlyList<SlopThumbnail> thumbnails)
    {
        if (thumbnails.Count < 24)
        {
            throw new InvalidOperationException("Slop Machine requires at least 24 thumbnail records.");
        }
        if (thumbnails.Any(item => string.IsNullOrWhiteSpace(item.Id) ||
            !item.ImageUrl.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            item.AiTitles.Count < 2 || item.AiTitles.Any(title => string.IsNullOrWhiteSpace(title)) ||
            string.IsNullOrWhiteSpace(item.AlternativeText)))
        {
            throw new InvalidOperationException(
                "Every Slop Machine thumbnail must be a WebP with alt text and two machine titles.");
        }
        if (thumbnails.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != thumbnails.Count)
        {
            throw new InvalidOperationException("Slop Machine thumbnail identifiers must be unique.");
        }
    }

    private GameTransition Advance(
        GameModuleState current,
        SlopMachineState state,
        GameActionContext context)
    {
        RequireHost(context);
        return Progress(current, state, context.ReceivedAtUtc);
    }

    private GameTransition Progress(
        GameModuleState current,
        SlopMachineState state,
        DateTimeOffset now)
    {
        return current.Phase switch
        {
            GameIntroPhase => Transition(FreshIntroPhase, state with
            {
                Message = "Your channels are live."
            }),
            FreshIntroPhase => BeginFreshWriting(state, now, 0),
            FreshResultsPhase when HasNextVoteHeat(state) =>
                BeginNextVotingHeat(state, now, FreshVotingPhase),
            FreshResultsPhase when state.FreshHeat == 0 =>
                BeginFreshWriting(ClearRoundInput(state), now, 1),
            FreshResultsPhase => ScoreReview(state, ScoreReview1Phase,
                "The algorithm has chosen its favourites."),
            ScoreReview1Phase => Transition(RouletteIntroPhase, ResetReview(state,
                "The algorithm demands more content.")),
            RouletteIntroPhase => BeginRoulette(state, now),
            RouletteSpinningPhase => BeginRouletteWriting(state, now),
            RouletteResultsPhase when HasNextVoteHeat(state) =>
                BeginNextVotingHeat(state, now, RouletteVotingPhase),
            RouletteResultsPhase => GrowthReview(state, ScoreReview2Phase),
            ScoreReview2Phase => Transition(TelephoneIntroPhase, ResetReview(state,
                "Creators have started stealing each other's ideas.")),
            TelephoneIntroPhase => BeginTelephoneWriting(state, now),
            TelephoneResultsPhase when HasNextVoteHeat(state) =>
                BeginNextVotingHeat(state, now, TelephoneVotingPhase),
            TelephoneResultsPhase => ScoreReview(state, ScoreReview3Phase,
                "Several viewers have already complained."),
            ScoreReview3Phase => Transition(CommentsIntroPhase, ResetReview(state,
                "Unfortunately, people watched your videos.")),
            CommentsIntroPhase => BeginCommentsWriting(state, now),
            CommentsResultsPhase when HasNextVoteHeat(state) =>
                BeginNextVotingHeat(state, now, CommentsVotingPhase),
            CommentsResultsPhase => GrowthReview(state, ScoreReview4Phase),
            ScoreReview4Phase => Transition(FinalIntroPhase, ResetReview(state,
                "The machine wants its platform back.")),
            FinalIntroPhase => BeginFinalWriting(state, now),
            FinalResultsPhase => Transition(FinalScoreReviewPhase, state with
            {
                Message = state.MachineWonFinal
                    ? "The Slop Machine beat humanity. It is being unbearable about it."
                    : "Humanity has survived the content mill. For now."
            }),
            FinalScoreReviewPhase => Transition(WinnerCelebrationPhase, state with
            {
                Message = WinnerCelebrationMessage(state)
            }),
            WinnerCelebrationPhase => new GameTransition(
                ModuleState(CompletedPhase, null, true, state), [],
                [new GameEvent("SlopMachineCompleted", GameJson.Empty)]),
            _ => throw new GameRuleViolationException(
                "wrong-phase", "The host cannot advance Slop Machine from this phase.")
        };
    }

    private GameTransition Deadline(
        GameModuleState current,
        SlopMachineState state,
        GameActionContext context) => current.Phase switch
        {
            FreshWritingPhase => BeginTextVoting(state, FreshVotingPhase, context.ReceivedAtUtc),
            FreshVotingPhase => CompletePopularityVote(state, FreshResultsPhase, 1000, 1000,
                "Viral Bonus"),
            RouletteSpinningPhase => BeginRouletteWriting(state, context.ReceivedAtUtc),
            RouletteWritingPhase => BeginRouletteVoting(state, context.ReceivedAtUtc),
            RouletteVotingPhase => CompletePopularityVote(state, RouletteResultsPhase, 1000, 1000,
                "Algorithm Bonus"),
            TelephoneWritingPhase => BeginTelephoneMatching(state, context.ReceivedAtUtc),
            TelephoneMatchingPhase => CompleteTelephoneMatching(state, context.ReceivedAtUtc),
            TelephoneVotingPhase => CompleteTelephoneVote(state),
            CommentsWritingPhase => BeginCommentsVoting(state, context.ReceivedAtUtc),
            CommentsVotingPhase => CompletePopularityVote(state, CommentsResultsPhase, 1000, 1000,
                "Engagement Bonus"),
            FinalWritingPhase => BeginFinalVoting(state, context.ReceivedAtUtc),
            FinalVotingPhase => CompleteFinalVote(state, context.ReceivedAtUtc),
            FinalMachineGuessPhase => CompleteMachineGuess(state),
            _ when HostCanAdvance(current.Phase) => Progress(current, state, context.ReceivedAtUtc),
            _ => throw new GameRuleViolationException(
                "wrong-phase", "This Slop Machine phase has no active deadline.")
        };

    private GameTransition EnsureAutomaticDeadline(GameTransition transition, DateTimeOffset now)
    {
        if (transition.State.IsComplete || transition.State.PhaseEndsAtUtc is not null)
        {
            return transition;
        }

        var duration = AutomaticDuration(transition.State.Phase);
        return duration is null
            ? transition
            : transition with
            {
                State = transition.State with { PhaseEndsAtUtc = now.Add(duration.Value) }
            };
    }

    private TimeSpan? AutomaticDuration(string phase) => phase switch
    {
        GameIntroPhase or FreshIntroPhase or RouletteIntroPhase or TelephoneIntroPhase or
            CommentsIntroPhase or FinalIntroPhase => introDuration,
        FreshResultsPhase or RouletteResultsPhase or TelephoneResultsPhase or CommentsResultsPhase or
            FinalResultsPhase => resultsDuration,
        ScoreReview1Phase or ScoreReview3Phase or FinalScoreReviewPhase => scoreReviewDuration,
        ScoreReview2Phase or ScoreReview4Phase => TimeSpan.FromSeconds(5),
        WinnerCelebrationPhase => winnerDuration,
        _ => null
    };

    private GameTransition SubmitText(
        GameModuleState current,
        SlopMachineState state,
        GameActionContext context,
        SubmitSlopTextAction action)
    {
        var playerId = RequiredPlayer(state, context);
        if (current.Phase is not (FreshWritingPhase or RouletteWritingPhase or TelephoneWritingPhase or
            CommentsWritingPhase or FinalWritingPhase))
        {
            throw new GameRuleViolationException("wrong-phase", "Text submissions are not open right now.");
        }
        if (state.TextSubmissions.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-submitted", "Your upload is already locked in.");
        }
        var maxLength = current.Phase == CommentsWritingPhase ? MaximumCommentLength : MaximumTitleLength;
        var text = current.Phase == RouletteWritingPhase
            ? CompleteFormat(state.Assignments[playerId].Format, action.Values)
            : NormalizeText(action.Value, maxLength);

        var submissions = state.TextSubmissions.ToDictionary();
        submissions.Add(playerId, text);
        var updated = state with { TextSubmissions = submissions };
        if (submissions.Count == state.Participants.Count)
        {
            return current.Phase switch
            {
                FreshWritingPhase => BeginTextVoting(updated, FreshVotingPhase, context.ReceivedAtUtc),
                RouletteWritingPhase => BeginRouletteVoting(updated, context.ReceivedAtUtc),
                TelephoneWritingPhase => BeginTelephoneMatching(updated, context.ReceivedAtUtc),
                CommentsWritingPhase => BeginCommentsVoting(updated, context.ReceivedAtUtc),
                FinalWritingPhase => BeginFinalVoting(updated, context.ReceivedAtUtc),
                _ => GameTransition.To(current with { Data = GameJson.From(updated) })
            };
        }
        return Changed(current, updated, "SlopTextSubmitted", playerId);
    }

    private GameTransition Respin(
        GameModuleState current,
        SlopMachineState state,
        GameActionContext context,
        RespinSlopReelAction action)
    {
        if (current.Phase != RouletteSpinningPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "The roulette reels are not spinning.");
        }
        var playerId = RequiredPlayer(state, context);
        var assignment = state.Assignments[playerId];
        if (assignment.RespinUsed)
        {
            throw new GameRuleViolationException("respin-used", "Your one free re-spin is already used.");
        }
        var reel = action.Reel.Trim().ToLowerInvariant();
        if (reel is not ("thumbnail" or "format"))
        {
            throw new GameRuleViolationException("invalid-reel", "Choose exactly one valid reel.");
        }
        var random = RandomFor(state, $"respin-{playerId:N}-{reel}");
        var updatedAssignment = reel switch
        {
            "thumbnail" => assignment with
            {
                ThumbnailId = PickUnusedThumbnail(state, random, [assignment.ThumbnailId]).Id
            },
            _ => assignment with
            {
                Format = PickDifferent(Formats, assignment.Format, random)
            }
        };
        updatedAssignment = updatedAssignment with { RespinUsed = true };
        var assignments = state.Assignments.ToDictionary();
        assignments[playerId] = updatedAssignment;
        var used = reel == "thumbnail"
            ? state.UsedThumbnailIds.Append(updatedAssignment.ThumbnailId).ToArray()
            : state.UsedThumbnailIds;
        return Changed(current, state with { Assignments = assignments, UsedThumbnailIds = used },
            "SlopReelRespinned", playerId);
    }

    private GameTransition Vote(
        GameModuleState current,
        SlopMachineState state,
        GameActionContext context,
        VoteForSlopAction action)
    {
        if (current.Phase is not (FreshVotingPhase or RouletteVotingPhase or TelephoneVotingPhase or
            CommentsVotingPhase or FinalVotingPhase))
        {
            throw new GameRuleViolationException("wrong-phase", "Voting is not open right now.");
        }
        var playerId = RequiredPlayer(state, context);
        if (state.Votes.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-voted", "Your vote is already locked in.");
        }
        var option = state.Options.SingleOrDefault(candidate => candidate.OptionId == action.OptionId);
        if (option is null)
        {
            throw new GameRuleViolationException("invalid-choice", "That upload is not available.");
        }
        if (option.AuthorId == playerId)
        {
            throw new GameRuleViolationException("self-vote", "You cannot vote for content you helped create.");
        }
        var eligibleOptions = OptionsForPlayer(state, playerId);
        if (!eligibleOptions.Any(candidate => candidate.OptionId == option.OptionId))
        {
            throw new GameRuleViolationException("invalid-choice", "That upload is not available to you.");
        }
        var votes = state.Votes.ToDictionary();
        votes.Add(playerId, option.OptionId);
        var updated = state with { Votes = votes };
        if (EligibleVoters(updated).All(votes.ContainsKey))
        {
            return current.Phase switch
            {
                FreshVotingPhase => CompletePopularityVote(updated, FreshResultsPhase, 1000, 1000,
                    "Viral Bonus"),
                RouletteVotingPhase => CompletePopularityVote(updated, RouletteResultsPhase, 1000, 1000,
                    "Algorithm Bonus"),
                TelephoneVotingPhase => CompleteTelephoneVote(updated),
                CommentsVotingPhase => CompletePopularityVote(updated, CommentsResultsPhase, 1000, 1000,
                    "Engagement Bonus"),
                FinalVotingPhase => CompleteFinalVote(updated, context.ReceivedAtUtc),
                _ => throw new InvalidOperationException("Unexpected Slop Machine voting phase.")
            };
        }
        return Changed(current, updated, "SlopVoteSubmitted", playerId);
    }

    private GameTransition MatchTelephone(
        GameModuleState current,
        SlopMachineState state,
        GameActionContext context,
        MatchTelephoneThumbnailAction action)
    {
        if (current.Phase != TelephoneMatchingPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Thumbnail matching is not open right now.");
        }
        var playerId = RequiredPlayer(state, context);
        if (!state.TelephoneMatches.TryGetValue(playerId, out var match))
        {
            throw new GameRuleViolationException("not-eligible", "No telephone upload was assigned to you.");
        }
        if (match.SelectedThumbnailId is not null)
        {
            throw new GameRuleViolationException("already-submitted", "Your match is already locked in.");
        }
        if (!match.OptionThumbnailIds.Contains(action.ThumbnailId, StringComparer.Ordinal))
        {
            throw new GameRuleViolationException("invalid-choice", "That thumbnail is not available.");
        }
        var matches = state.TelephoneMatches.ToDictionary();
        matches[playerId] = match with
        {
            SelectedThumbnailId = action.ThumbnailId,
            IsCorrect = string.Equals(action.ThumbnailId, match.IntendedThumbnailId, StringComparison.Ordinal)
        };
        var updated = state with { TelephoneMatches = matches };
        return matches.Values.All(item => item.SelectedThumbnailId is not null)
            ? CompleteTelephoneMatching(updated, context.ReceivedAtUtc)
            : Changed(current, updated, "TelephoneMatchSubmitted", playerId);
    }

    private static GameTransition IdentifyMachineTitle(
        GameModuleState current,
        SlopMachineState state,
        GameActionContext context,
        IdentifyMachineTitleAction action)
    {
        if (current.Phase != FinalMachineGuessPhase)
        {
            throw new GameRuleViolationException("wrong-phase", "Machine-title guesses are not open right now.");
        }
        var playerId = RequiredPlayer(state, context);
        var option = state.Options.SingleOrDefault(candidate => candidate.OptionId == action.OptionId);
        if (option is null)
        {
            throw new GameRuleViolationException("invalid-choice", "That title is not available.");
        }
        var existing = state.MachineGuesses.GetValueOrDefault(playerId, []);
        if (existing.Contains(action.OptionId))
        {
            throw new GameRuleViolationException("already-chosen", "You already selected that title.");
        }
        if (existing.Count >= 2)
        {
            throw new GameRuleViolationException("already-submitted", "Both machine guesses are locked in.");
        }
        var guesses = state.MachineGuesses.ToDictionary();
        guesses[playerId] = [.. existing, action.OptionId];
        var updated = state with { MachineGuesses = guesses };
        return state.Participants.All(participant => guesses.GetValueOrDefault(participant.PlayerId, []).Count >= 2)
            ? CompleteMachineGuess(updated)
            : Changed(current, updated, "MachineTitleIdentified", playerId);
    }

    private GameTransition BeginFreshWriting(SlopMachineState state, DateTimeOffset now, int heat)
    {
        var random = RandomFor(state, $"fresh-{heat}");
        var thumbnail = PickUnusedThumbnail(state, random);
        var used = state.UsedThumbnailIds.Append(thumbnail.Id).ToArray();
        var assignments = state.Participants.ToDictionary(
            participant => participant.PlayerId,
            _ => new SlopAssignment(thumbnail.Id, string.Empty));
        var updated = ClearRoundInput(state) with
        {
            FreshHeat = heat,
            ActiveThumbnailId = thumbnail.Id,
            UsedThumbnailIds = used,
            Assignments = assignments,
            Message = $"Fresh Slop heat {heat + 1} of 2"
        };
        return Transition(FreshWritingPhase, updated, now.Add(titleDuration));
    }

    private GameTransition BeginTextVoting(
        SlopMachineState state,
        string phase,
        DateTimeOffset now)
    {
        var uploads = state.TextSubmissions.Select(item => new SlopSubmission(
            Guid.NewGuid(), item.Key, state.Assignments[item.Key].ThumbnailId, item.Value)).ToArray();
        var options = uploads.Select(upload => new SlopOption(
            upload.SubmissionId, upload.Text, upload.AuthorId, false, upload.ThumbnailId)).ToList();
        Shuffle(options, RandomFor(state, $"reveal-{phase}-{state.FreshHeat}"));
        return BeginVotingHeats(state with
        {
            Uploads = [.. state.Uploads, .. uploads],
            Message = "Anonymous uploads are entering the feed."
        }, options, phase, now);
    }

    private GameTransition BeginRoulette(SlopMachineState state, DateTimeOffset now)
    {
        var random = RandomFor(state, "roulette");
        var used = state.UsedThumbnailIds.ToList();
        var assignments = new Dictionary<Guid, SlopAssignment>();
        foreach (var participant in state.Participants)
        {
            var thumbnail = PickUnusedThumbnail(state with { UsedThumbnailIds = used }, random);
            used.Add(thumbnail.Id);
            assignments[participant.PlayerId] = new SlopAssignment(
                thumbnail.Id,
                Formats[random.Next(Formats.Length)]);
        }
        return Transition(RouletteSpinningPhase, ClearRoundInput(state) with
        {
            UsedThumbnailIds = used,
            Assignments = assignments,
            ActiveThumbnailId = null,
            Message = "The reels are choosing your fate. One re-spin, one reel."
        }, now.Add(rouletteDuration));
    }

    private GameTransition BeginRouletteWriting(SlopMachineState state, DateTimeOffset now) =>
        Transition(RouletteWritingPhase, state with
        {
            TextSubmissions = new Dictionary<Guid, string>(),
            Message = "Package the chaos into one irresistible upload."
        }, now.Add(rouletteWritingDuration));

    private GameTransition BeginRouletteVoting(SlopMachineState state, DateTimeOffset now)
    {
        var uploads = state.TextSubmissions.Select(item => new SlopSubmission(
            Guid.NewGuid(), item.Key, state.Assignments[item.Key].ThumbnailId, item.Value)).ToArray();
        var options = uploads.Select(upload => new SlopOption(
            upload.SubmissionId, upload.Text, upload.AuthorId, false, upload.ThumbnailId)).ToArray();
        return BeginVotingHeats(state with
        {
            Uploads = [.. state.Uploads, .. uploads],
            Message = "Uploads complete."
        }, options, RouletteVotingPhase, now);
    }

    private GameTransition BeginTelephoneWriting(SlopMachineState state, DateTimeOffset now)
    {
        var random = RandomFor(state, "telephone-thumbnails");
        var used = state.UsedThumbnailIds.ToList();
        var assignments = new Dictionary<Guid, SlopAssignment>();
        foreach (var participant in state.Participants)
        {
            var thumbnail = PickUnusedThumbnail(state with { UsedThumbnailIds = used }, random);
            used.Add(thumbnail.Id);
            assignments[participant.PlayerId] = new SlopAssignment(
                thumbnail.Id, string.Empty);
        }
        return Transition(TelephoneWritingPhase, ClearRoundInput(state) with
        {
            UsedThumbnailIds = used,
            Assignments = assignments,
            Message = "Describe your thumbnail before the machine scrambles the wires."
        }, now.Add(telephoneWritingDuration));
    }

    private GameTransition BeginTelephoneMatching(SlopMachineState state, DateTimeOffset now)
    {
        var writers = state.TextSubmissions.Keys.ToList();
        if (writers.Count < 2)
        {
            return Transition(TelephoneResultsPhase, state with
            {
                TelephoneMatches = new Dictionary<Guid, TelephoneMatch>(),
                Message = "Not enough uploads survived the telephone line."
            });
        }
        var random = RandomFor(state, "telephone-derangement");
        var receivers = Derange(writers, random);
        var used = state.UsedThumbnailIds.ToList();
        var matches = new Dictionary<Guid, TelephoneMatch>();
        for (var index = 0; index < writers.Count; index++)
        {
            var writerId = writers[index];
            var matcherId = receivers[index];
            var intended = state.Assignments[writerId].ThumbnailId;
            var decoys = PickDecoys(intended, state with { UsedThumbnailIds = used }, random, 3);
            var choices = new List<string> { intended };
            choices.AddRange(decoys);
            Shuffle(choices, random);
            matches[matcherId] = new TelephoneMatch(
                matcherId, writerId, Guid.NewGuid(), intended, choices);
            foreach (var decoy in decoys)
            {
                if (!used.Contains(decoy, StringComparer.Ordinal))
                {
                    used.Add(decoy);
                }
            }
        }
        return Transition(TelephoneMatchingPhase, state with
        {
            UsedThumbnailIds = used,
            TelephoneMatches = matches,
            Message = "Match the mystery title to its original thumbnail."
        }, now.Add(telephoneMatchingDuration));
    }

    private GameTransition CompleteTelephoneMatching(SlopMachineState state, DateTimeOffset now)
    {
        var awards = new List<ScoreAward>();
        var bonuses = new List<SlopBonus>();
        var uploads = new List<SlopSubmission>();
        foreach (var match in state.TelephoneMatches.Values)
        {
            var text = state.TextSubmissions[match.WriterId];
            var thumbnailId = match.SelectedThumbnailId ?? match.IntendedThumbnailId;
            uploads.Add(new SlopSubmission(match.SubmissionId, match.WriterId, thumbnailId, text,
                match.MatcherId));
            if (!match.IsCorrect)
            {
                continue;
            }
            awards.Add(new ScoreAward(match.WriterId, 1500, "Telephone: recognisable title"));
            awards.Add(new ScoreAward(match.MatcherId, 1500, "Telephone: correct match"));
            bonuses.Add(new SlopBonus(match.WriterId, "Recognisable upload", 1500));
            bonuses.Add(new SlopBonus(match.MatcherId, "Correct match", 1500));
        }
        var updated = ApplyAwards(state with
        {
            Uploads = [.. state.Uploads, .. uploads],
            Bonuses = bonuses,
            Message = "Vote for the funniest mangled upload."
        }, awards);
        var options = uploads
            .Select(upload => new SlopOption(upload.SubmissionId, upload.Text, upload.AuthorId,
                false, upload.ThumbnailId, upload.PartnerId)).ToArray();
        var voting = BeginVotingHeats(updated, options, TelephoneVotingPhase, now);
        return new GameTransition(voting.State, awards,
            [new GameEvent("TelephoneMatchesRevealed", GameJson.From(new { count = uploads.Count }))]);
    }

    private static GameTransition CompleteTelephoneVote(SlopMachineState state)
    {
        var counts = VoteCounts(state);
        var winners = AdjustedVoteWinners(state, counts);
        var awards = new List<ScoreAward>();
        var bonuses = state.Bonuses.ToList();
        foreach (var option in state.Options)
        {
            var votes = counts.GetValueOrDefault(option.OptionId);
            if (option.AuthorId is not { } writer)
            {
                continue;
            }
            if (votes > 0)
            {
                awards.Add(new ScoreAward(writer, votes * 500, "Telephone pairing votes"));
            }
            if (winners.Contains(option.OptionId))
            {
                awards.Add(new ScoreAward(writer, 1000, "Telephone Disaster Bonus"));
                bonuses.Add(new SlopBonus(writer, "Telephone Disaster Bonus", 1000));
            }
        }
        var uploadUpdates = UpdateUploads(state, counts, winners, 500, 1000);
        var updated = ApplyAwards(state with
        {
            Uploads = uploadUpdates,
            Bonuses = bonuses,
            Message = "The funniest misunderstanding has gone viral."
        }, awards);
        return new GameTransition(ModuleState(TelephoneResultsPhase, null, false, updated), awards,
            [new GameEvent("TelephoneVoteRevealed", GameJson.Empty)]);
    }

    private GameTransition BeginCommentsWriting(SlopMachineState state, DateTimeOffset now)
    {
        var rankedCandidates = state.Uploads.OrderByDescending(upload => upload.Votes)
            .ThenByDescending(upload => upload.PointsAwarded).ThenBy(upload => upload.SubmissionId)
            .ToArray();
        if (rankedCandidates.Length == 0)
        {
            var fallbackThumbnailId = state.UsedThumbnailIds.Count > 0
                ? state.UsedThumbnailIds[0]
                : PickUnusedThumbnail(state, RandomFor(state, "comments-fallback")).Id;
            rankedCandidates =
            [
                new SlopSubmission(Guid.NewGuid(), Guid.Empty, fallbackThumbnailId,
                    "The algorithm uploaded this by itself")
            ];
        }
        var random = RandomFor(state, "comments");
        var assignments = new Dictionary<Guid, SlopAssignment>();
        var assignmentCounts = new Dictionary<Guid, int>();
        for (var index = 0; index < state.Participants.Count; index++)
        {
            var participant = state.Participants[index];
            var available = rankedCandidates.Where(upload => upload.AuthorId != participant.PlayerId)
                .Take(3).ToArray();
            var eligible = available.Length > 0 ? available : rankedCandidates.Take(3).ToArray();
            var selected = eligible
                .OrderBy(upload => assignmentCounts.GetValueOrDefault(upload.SubmissionId))
                .ThenByDescending(upload => upload.Votes)
                .ThenByDescending(upload => upload.PointsAwarded)
                .First();
            assignmentCounts[selected.SubmissionId] =
                assignmentCounts.GetValueOrDefault(selected.SubmissionId) + 1;
            assignments[participant.PlayerId] = new SlopAssignment(
                selected.ThumbnailId,
                selected.Text,
                CommentTypes[random.Next(CommentTypes.Length)],
                SourceSubmissionId: selected.SubmissionId);
        }
        return Transition(CommentsWritingPhase, ClearRoundInput(state) with
        {
            Assignments = assignments,
            Message = "The comments section is now regrettably open."
        }, now.Add(commentDuration));
    }

    private GameTransition BeginCommentsVoting(SlopMachineState state, DateTimeOffset now)
    {
        var uploads = state.TextSubmissions.Select(item => new SlopSubmission(
            Guid.NewGuid(), item.Key, state.Assignments[item.Key].ThumbnailId, item.Value,
            ParentSubmissionId: state.Assignments[item.Key].SourceSubmissionId,
            CommentType: state.Assignments[item.Key].CommentType)).ToArray();
        var options = uploads.Select(upload => new SlopOption(
            upload.SubmissionId, upload.Text, upload.AuthorId, false, upload.ThumbnailId)).ToList();
        Shuffle(options, RandomFor(state, "comment-reveal"));
        return BeginVotingHeats(state with
        {
            Uploads = [.. state.Uploads, .. uploads],
            Message = "A completely healthy comments section."
        }, options, CommentsVotingPhase, now);
    }

    private GameTransition BeginFinalWriting(SlopMachineState state, DateTimeOffset now)
    {
        var random = RandomFor(state, "final-thumbnail");
        var thumbnail = PickUnusedThumbnail(state, random, requireMachineTitles: true);
        var assignments = state.Participants.ToDictionary(
            participant => participant.PlayerId,
            _ => new SlopAssignment(thumbnail.Id, string.Empty));
        return Transition(FinalWritingPhase, ClearRoundInput(state) with
        {
            ActiveThumbnailId = thumbnail.Id,
            UsedThumbnailIds = state.UsedThumbnailIds.Append(thumbnail.Id).ToArray(),
            Assignments = assignments,
            Message = "One thumbnail. Two machine titles. Humanity's last upload."
        }, now.Add(titleDuration));
    }

    private GameTransition BeginFinalVoting(SlopMachineState state, DateTimeOffset now)
    {
        var thumbnail = Thumbnail(state.ActiveThumbnailId!);
        var options = state.TextSubmissions.Select(item => new SlopOption(
            Guid.NewGuid(), item.Value, item.Key, false, thumbnail.Id)).ToList();
        options.AddRange(thumbnail.AiTitles.Take(2).Select(title =>
            new SlopOption(Guid.NewGuid(), title, null, true, thumbnail.Id)));
        Shuffle(options, RandomFor(state, "final-options"));
        var uploads = state.TextSubmissions.Select(item => new SlopSubmission(
            options.Single(option => option.AuthorId == item.Key).OptionId,
            item.Key, thumbnail.Id, item.Value)).ToArray();
        var updated = state with
        {
            Uploads = [.. state.Uploads, .. uploads],
            Options = options,
            Votes = new Dictionary<Guid, Guid>(),
            VoteHeat = 0,
            VoteHeats = [options.Select(option => option.OptionId).ToArray()],
            VotingOpportunities = VotingOpportunities(state, options),
            Message = "Human and machine titles are now indistinguishable. Perfect."
        };
        return Transition(FinalVotingPhase, updated, now.Add(votingDuration));
    }

    private GameTransition CompleteFinalVote(SlopMachineState state, DateTimeOffset now)
    {
        var counts = VoteCounts(state);
        var max = counts.Values.DefaultIfEmpty().Max();
        var publicWinners = counts.Where(item => item.Value == max && max > 0).Select(item => item.Key).ToArray();
        var machineWon = publicWinners.Length > 0 && publicWinners.All(id =>
            state.Options.Single(option => option.OptionId == id).IsMachine);
        var humanCounts = state.Options.Where(option => option.AuthorId.HasValue)
            .ToDictionary(option => option.OptionId, option => counts.GetValueOrDefault(option.OptionId));
        var bestHuman = humanCounts.Values.DefaultIfEmpty().Max();
        var humanWinners = bestHuman == 0 || machineWon ? [] : humanCounts
            .Where(item => item.Value == bestHuman).Select(item => item.Key).ToArray();
        var awards = new List<ScoreAward>();
        var bonuses = new List<SlopBonus>();
        foreach (var option in state.Options.Where(option => option.AuthorId.HasValue))
        {
            var votes = counts.GetValueOrDefault(option.OptionId);
            if (votes > 0)
            {
                awards.Add(new ScoreAward(option.AuthorId!.Value, votes * 2000, "Final title votes"));
            }
            if (humanWinners.Contains(option.OptionId))
            {
                awards.Add(new ScoreAward(option.AuthorId!.Value, 3000, "Humanity Bonus"));
                bonuses.Add(new SlopBonus(option.AuthorId.Value, "Humanity Bonus", 3000));
            }
        }
        var updated = ApplyAwards(state with
        {
            Uploads = UpdateUploads(state, counts, humanWinners, 2000, 3000),
            MachineWonFinal = machineWon,
            Bonuses = bonuses,
            MachineGuesses = new Dictionary<Guid, IReadOnlyList<Guid>>(),
            Message = machineWon
                ? "THE MACHINE WON. It has already posted an apology video."
                : "Humanity wins the feed. Now spot both machine titles."
        }, awards);
        return new GameTransition(
            ModuleState(FinalMachineGuessPhase, now.Add(machineGuessDuration), false, updated), awards,
            [new GameEvent(machineWon ? "SlopMachineVictory" : "HumanityVictory", GameJson.Empty)]);
    }

    private static GameTransition CompleteMachineGuess(SlopMachineState state)
    {
        var awards = new List<ScoreAward>();
        var bonuses = state.Bonuses.ToList();
        foreach (var participant in state.Participants)
        {
            var correct = state.MachineGuesses.GetValueOrDefault(participant.PlayerId, [])
                .Count(id => state.Options.Single(option => option.OptionId == id).IsMachine);
            if (correct > 0)
            {
                awards.Add(new ScoreAward(participant.PlayerId, correct * 1000,
                    "Machine-title identification"));
                bonuses.Add(new SlopBonus(participant.PlayerId, $"Spotted {correct} machine title(s)",
                    correct * 1000));
            }
        }
        var updated = ApplyAwards(state with
        {
            Bonuses = bonuses,
            Message = "The machine has been identified. It denies everything."
        }, awards);
        return new GameTransition(ModuleState(FinalResultsPhase, null, false, updated), awards,
            [new GameEvent("MachineTitlesRevealed", GameJson.Empty)]);
    }

    private GameTransition BeginVotingHeats(
        SlopMachineState state,
        IReadOnlyList<SlopOption> options,
        string phase,
        DateTimeOffset now)
    {
        if (options.Count == 0)
        {
            return Transition(ResultPhaseForVoting(phase), state with
            {
                Options = [],
                Votes = new Dictionary<Guid, Guid>(),
                VoteHeat = 0,
                VoteHeats = [],
                VotingOpportunities = new Dictionary<Guid, int>()
            });
        }
        var heatCount = state.Participants.Count >= 7 && phase != FinalVotingPhase
            ? (int)Math.Ceiling(options.Count / 4d)
            : 1;
        var heats = Enumerable.Range(0, heatCount)
            .Select(index => (IReadOnlyList<Guid>)options.Where((_, optionIndex) =>
                optionIndex % heatCount == index).Select(option => option.OptionId).ToArray())
            .ToArray();
        var firstOptions = options.Where(option => heats[0].Contains(option.OptionId)).ToArray();
        var updated = OpenVoteHeat(state with
        {
            VoteHeat = 0,
            VoteHeats = heats
        }, firstOptions);
        return EligibleVoters(updated).Length == 0
            ? phase switch
            {
                FreshVotingPhase => CompletePopularityVote(updated, FreshResultsPhase, 1000, 1000,
                    "Viral Bonus"),
                RouletteVotingPhase => CompletePopularityVote(updated, RouletteResultsPhase, 1000, 1000,
                    "Algorithm Bonus"),
                CommentsVotingPhase => CompletePopularityVote(updated, CommentsResultsPhase, 1000, 1000,
                    "Engagement Bonus"),
                FinalVotingPhase => CompleteFinalVote(updated, now),
                _ => Transition(phase, updated)
            }
            : Transition(phase, updated, now.Add(votingDuration));
    }

    private GameTransition BeginNextVotingHeat(SlopMachineState state, DateTimeOffset now, string phase)
    {
        var nextHeat = state.VoteHeat + 1;
        var heatIds = state.VoteHeats![nextHeat];
        var options = state.Uploads.Where(upload => heatIds.Contains(upload.SubmissionId))
            .Select(upload => new SlopOption(upload.SubmissionId, upload.Text, upload.AuthorId,
                false, upload.ThumbnailId, upload.PartnerId)).ToArray();
        var updated = OpenVoteHeat(state with { VoteHeat = nextHeat }, options);
        return Transition(phase, updated, now.Add(votingDuration));
    }

    private static SlopMachineState OpenVoteHeat(
        SlopMachineState state,
        IReadOnlyList<SlopOption> options) => state with
        {
            Options = options,
            Votes = new Dictionary<Guid, Guid>(),
            VotingOpportunities = VotingOpportunities(state, options),
            Bonuses = [],
            Message = "Vote with your heart. The machine prefers rage."
        };

    private static bool HasNextVoteHeat(SlopMachineState state) =>
        state.VoteHeats is { Count: > 0 } && state.VoteHeat + 1 < state.VoteHeats.Count;

    private static string ResultPhaseForVoting(string phase) => phase switch
    {
        FreshVotingPhase => FreshResultsPhase,
        RouletteVotingPhase => RouletteResultsPhase,
        TelephoneVotingPhase => TelephoneResultsPhase,
        CommentsVotingPhase => CommentsResultsPhase,
        FinalVotingPhase => FinalResultsPhase,
        _ => throw new InvalidOperationException("Unexpected Slop Machine voting phase.")
    };

    private static GameTransition CompletePopularityVote(
        SlopMachineState state,
        string resultPhase,
        int pointsPerVote,
        int winnerBonus,
        string bonusName)
    {
        var counts = VoteCounts(state);
        var winners = AdjustedVoteWinners(state, counts);
        var awards = new List<ScoreAward>();
        var bonuses = new List<SlopBonus>();
        foreach (var option in state.Options.Where(option => option.AuthorId.HasValue))
        {
            var votes = counts.GetValueOrDefault(option.OptionId);
            if (votes > 0)
            {
                awards.Add(new ScoreAward(option.AuthorId!.Value, votes * pointsPerVote,
                    $"{votes} vote(s)"));
            }
            if (winners.Contains(option.OptionId))
            {
                awards.Add(new ScoreAward(option.AuthorId!.Value, winnerBonus, bonusName));
                bonuses.Add(new SlopBonus(option.AuthorId.Value, bonusName, winnerBonus));
            }
        }
        var uploadUpdates = UpdateUploads(state, counts, winners, pointsPerVote, winnerBonus);
        var updated = ApplyAwards(state with
        {
            Uploads = uploadUpdates,
            Bonuses = bonuses,
            Message = winners.Length > 1 ? "The algorithm has declared a joint favourite."
                : "The algorithm has chosen its favourite."
        }, awards);
        return new GameTransition(ModuleState(resultPhase, null, false, updated), awards,
            [new GameEvent("SlopVoteRevealed", GameJson.From(new { winners = winners.Length }))]);
    }

    private PlayerGameViewPayload PlayerView(
        GameModuleState current,
        SlopMachineState state,
        GameViewContext context)
    {
        if (context.PlayerId is not { } playerId || state.Participants.All(item => item.PlayerId != playerId))
        {
            throw new GameRuleViolationException("unknown-player", "That player is not in this game.");
        }
        var waiting = WaitingController();
        var controller = current.Phase switch
        {
            FreshWritingPhase or RouletteWritingPhase or TelephoneWritingPhase or
                CommentsWritingPhase or FinalWritingPhase => TextController(current, state, playerId),
            RouletteSpinningPhase => RespinController(state, playerId),
            FreshVotingPhase or RouletteVotingPhase or TelephoneVotingPhase or
                CommentsVotingPhase or FinalVotingPhase => VoteController(current, state, playerId),
            TelephoneMatchingPhase => TelephoneMatchController(state, playerId),
            FinalMachineGuessPhase => MachineGuessController(state, playerId),
            _ => waiting
        };
        var heading = current.Phase switch
        {
            FreshWritingPhase => $"Fresh Slop · Heat {state.FreshHeat + 1}",
            RouletteSpinningPhase => "Algorithm Roulette",
            RouletteWritingPhase => "Package your upload",
            TelephoneWritingPhase => "Write a recognisable title",
            TelephoneMatchingPhase => "Repair the telephone line",
            CommentsWritingPhase => "Enter the comments section",
            FinalWritingPhase => "Beat the Machine",
            FinalMachineGuessPhase => "Spot the Slop Machine",
            WinnerCelebrationPhase => "The algorithm has chosen",
            _ => DisplayTitle(current.Phase)
        };
        var instructions = PlayerInstructions(current, state, playerId);
        return new PlayerGameViewPayload(
            heading,
            instructions,
            controller,
            GameJson.From(new
            {
                phase = current.Phase,
                locked = !controller.IsEnabled,
                earnedViews = state.EarnedViews.GetValueOrDefault(playerId),
                totalViews = TotalScore(state, playerId),
                bonuses = state.Bonuses.Where(item => item.PlayerId == playerId).ToArray()
            }),
            PlayerMedia(current, state, playerId),
            "views");
    }

    private HostGameViewPayload HostView(GameModuleState current, SlopMachineState state)
    {
        var display = DisplayView(current, state);
        return new HostGameViewPayload(
            display.Title,
            display.Prompt,
            display.PhaseMessage,
            SubmittedCount(current.Phase, state),
            state.Participants.Count,
            HostCanAdvance(current.Phase),
            HostCanAdvance(current.Phase) ? AdvanceSlopMachineAction.ActionKind : null,
            AdvanceLabel(current.Phase),
            display.Entries);
    }

    private DisplayGameViewPayload DisplayView(GameModuleState current, SlopMachineState state)
    {
        var scoreScreen = IsScoreScreen(current.Phase) || current.Phase == WinnerCelebrationPhase;
        return new DisplayGameViewPayload(
            "SLOP MACHINE",
            DisplayPrompt(current.Phase, state),
            state.Message,
            SubmittedCount(current.Phase, state),
            state.Participants.Count,
            DisplayEntries(current.Phase, state),
            ShowRoundRanking: scoreScreen,
            Media: DisplayMedia(current, state),
            ScoreUnit: "views");
    }

    private static PlayerControllerView TextController(
        GameModuleState current,
        SlopMachineState state,
        Guid playerId)
    {
        var submitted = state.TextSubmissions.GetValueOrDefault(playerId);
        var isComment = current.Phase == CommentsWritingPhase;
        return new PlayerControllerView(
            PlayerControllerKind.Text,
            SubmitSlopTextAction.ActionKind,
            submitted is null,
            isComment ? "Post comment" : "Upload title",
            GameJson.From(new TextControllerConfiguration(
                isComment ? MaximumCommentLength : MaximumTitleLength,
                isComment ? "Type something the creator will regret…" : "Make it impossible not to click…",
                submitted,
                current.Phase == RouletteWritingPhase
                    ? FormatSegments(state.Assignments[playerId].Format)
                    : null)));
    }

    private static PlayerControllerView RespinController(SlopMachineState state, Guid playerId)
    {
        var assignment = state.Assignments[playerId];
        if (assignment.RespinUsed)
        {
            return WaitingController("Re-spin used. The reels are locking in…");
        }
        return new PlayerControllerView(
            PlayerControllerKind.Choice,
            RespinSlopReelAction.ActionKind,
            true,
            "Re-spin this reel",
            GameJson.From(new ChoiceControllerConfiguration(
            [
                new ControllerOption("thumbnail", "Thumbnail", "Get a different image",
                    ImageUrl: null),
                new ControllerOption("format", "Format", assignment.Format)
            ], SelectionProperty: "reel", SelectionScope: "roulette-respin")));
    }

    private PlayerControllerView VoteController(
        GameModuleState current,
        SlopMachineState state,
        Guid playerId)
    {
        var options = OptionsForPlayer(state, playerId).Select((option, index) =>
            new ControllerOption(
                option.OptionId.ToString("N"),
                ((char)('A' + index)).ToString(),
                option.Text,
                ImageUrl: current.Phase is FreshVotingPhase or FinalVotingPhase
                    || option.ThumbnailId is null
                        ? null
                        : Thumbnail(option.ThumbnailId).ImageUrl))
            .ToArray();
        var submitted = state.Votes.GetValueOrDefault(playerId);
        return new PlayerControllerView(
            PlayerControllerKind.Vote,
            VoteForSlopAction.ActionKind,
            submitted == Guid.Empty && options.Length > 0,
            "Feed this to the algorithm",
            GameJson.From(new VoteControllerConfiguration(
                options,
                submitted == Guid.Empty ? null : submitted.ToString("N"),
                "optionId",
                $"slop-vote-{string.Join('-', state.Options.Select(item => item.OptionId))}")));
    }

    private PlayerControllerView TelephoneMatchController(SlopMachineState state, Guid playerId)
    {
        if (!state.TelephoneMatches.TryGetValue(playerId, out var match))
        {
            return WaitingController("No telephone upload was assigned to you.");
        }
        var options = match.OptionThumbnailIds.Select((thumbnailId, index) =>
        {
            var thumbnail = Thumbnail(thumbnailId);
            return new ControllerOption(thumbnail.Id, $"Image {(char)('A' + index)}", null,
                ImageUrl: thumbnail.ImageUrl);
        }).ToArray();
        return new PlayerControllerView(
            PlayerControllerKind.Choice,
            MatchTelephoneThumbnailAction.ActionKind,
            match.SelectedThumbnailId is null,
            "Lock this thumbnail",
            GameJson.From(new ChoiceControllerConfiguration(
                options,
                match.SelectedThumbnailId,
                "thumbnailId",
                $"telephone-{match.SubmissionId:N}")));
    }

    private static PlayerControllerView MachineGuessController(SlopMachineState state, Guid playerId)
    {
        var selected = state.MachineGuesses.GetValueOrDefault(playerId, []);
        if (selected.Count >= 2)
        {
            return WaitingController("Both machine titles are locked in.");
        }
        var options = state.Options.Where(option => !selected.Contains(option.OptionId))
            .Select((option, index) => new ControllerOption(
                option.OptionId.ToString("N"), ((char)('A' + index)).ToString(), option.Text)).ToArray();
        return new PlayerControllerView(
            PlayerControllerKind.Choice,
            IdentifyMachineTitleAction.ActionKind,
            true,
            selected.Count == 0 ? "Pick first machine title" : "Pick second machine title",
            GameJson.From(new ChoiceControllerConfiguration(
                options, SelectionProperty: "optionId",
                SelectionScope: $"machine-{selected.Count}")));
    }

    private GameMediaPresentationView? PlayerMedia(
        GameModuleState current,
        SlopMachineState state,
        Guid playerId)
    {
        if (current.Phase == TelephoneMatchingPhase &&
            state.TelephoneMatches.TryGetValue(playerId, out var match))
        {
            return new GameMediaPresentationView("telephone-title",
            [
                new GameMediaItem(match.SubmissionId.ToString("N"), string.Empty,
                    "Mystery title", "MYSTERY TITLE", state.TextSubmissions[match.WriterId], "PHONE LINE")
            ]);
        }
        if (state.Assignments.TryGetValue(playerId, out var assignment) &&
            current.Phase is FreshWritingPhase or RouletteSpinningPhase or RouletteWritingPhase or
                TelephoneWritingPhase or CommentsWritingPhase or FinalWritingPhase)
        {
            var thumbnail = Thumbnail(assignment.ThumbnailId);
            var body = current.Phase switch
            {
                RouletteSpinningPhase or RouletteWritingPhase =>
                    assignment.Format,
                CommentsWritingPhase => $"{assignment.CommentType}: {assignment.Format}",
                _ => null
            };
            return new GameMediaPresentationView("single",
            [
                new GameMediaItem(thumbnail.Id, thumbnail.ImageUrl, thumbnail.AlternativeText,
                    current.Phase == CommentsWritingPhase ? assignment.Format : null,
                    body, assignment.RespinUsed ? "RE-SPIN LOCKED" : null)
            ]);
        }
        if (current.Phase is FreshVotingPhase or RouletteVotingPhase or TelephoneVotingPhase or
            CommentsVotingPhase)
        {
            return new GameMediaPresentationView("single",
                state.ActiveThumbnailId is { } id
                    ? [MediaItem(Thumbnail(id))]
                    : []);
        }
        return null;
    }

    private GameMediaPresentationView? DisplayMedia(GameModuleState current, SlopMachineState state)
    {
        if (IsScoreScreen(current.Phase) || current.Phase is GameIntroPhase or WinnerCelebrationPhase)
        {
            return null;
        }
        if (current.Phase is FreshIntroPhase or FreshWritingPhase or FreshVotingPhase or FreshResultsPhase or
            FinalIntroPhase or FinalWritingPhase or FinalVotingPhase or
            FinalMachineGuessPhase or FinalResultsPhase)
        {
            return state.ActiveThumbnailId is { } id
                ? new GameMediaPresentationView("hero", [MediaItem(Thumbnail(id))])
                : null;
        }
        if (current.Phase is CommentsVotingPhase or CommentsResultsPhase)
        {
            return new GameMediaPresentationView("comment-feed", state.Options.Select(option =>
            {
                var comment = state.Uploads.Single(upload => upload.SubmissionId == option.OptionId);
                var parent = state.Uploads.SingleOrDefault(upload =>
                    upload.SubmissionId == comment.ParentSubmissionId);
                var thumbnail = Thumbnail(option.ThumbnailId!);
                var badge = current.Phase.EndsWith("Results", StringComparison.Ordinal) && comment.WonBonus
                    ? "PINNED COMMENT"
                    : comment.CommentType?.ToUpperInvariant();
                return new GameMediaItem(option.OptionId.ToString("N"), thumbnail.ImageUrl,
                    thumbnail.AlternativeText, parent?.Text ?? "A returning upload", option.Text, badge);
            }).ToArray());
        }
        if (current.Phase is RouletteVotingPhase or RouletteResultsPhase)
        {
            return new GameMediaPresentationView("gallery", state.Options.Select(option =>
            {
                var thumbnail = Thumbnail(option.ThumbnailId!);
                var upload = state.Uploads.SingleOrDefault(item => item.SubmissionId == option.OptionId);
                return new GameMediaItem(option.OptionId.ToString("N"), thumbnail.ImageUrl,
                    thumbnail.AlternativeText, option.Text,
                    current.Phase.EndsWith("Results", StringComparison.Ordinal)
                        ? ResultDetail(state, option)
                        : null,
                    current.Phase.EndsWith("Results", StringComparison.Ordinal) && upload?.WonBonus == true
                        ? "HEAT WINNER"
                        : null);
            }).ToArray());
        }
        if (current.Phase is TelephoneVotingPhase or TelephoneResultsPhase)
        {
            var items = state.Options.Select(option =>
            {
                var match = state.TelephoneMatches.Values.Single(item =>
                    item.SubmissionId == option.OptionId);
                var selected = Thumbnail(match.SelectedThumbnailId ?? match.IntendedThumbnailId);
                var upload = state.Uploads.SingleOrDefault(item => item.SubmissionId == option.OptionId);
                return new GameMediaItem(match.SubmissionId.ToString("N"), selected.ImageUrl,
                    selected.AlternativeText, state.TextSubmissions[match.WriterId],
                    match.SelectedThumbnailId is null ? "No match submitted"
                        : match.IsCorrect ? "ALGORITHM MATCHED" : "ALGORITHM MANGLED",
                    current.Phase.EndsWith("Results", StringComparison.Ordinal) && upload?.WonBonus == true
                        ? "TELEPHONE WINNER"
                        : match.IsCorrect ? "MATCH" : "MANGLED");
            }).ToArray();
            return new GameMediaPresentationView("gallery", items);
        }
        return null;
    }

    private static IReadOnlyList<GamePresentationEntry> DisplayEntries(string phase, SlopMachineState state)
    {
        if (phase is ScoreReview2Phase or ScoreReview4Phase)
        {
            var leader = GrowthLeader(state);
            return
            [
                new GamePresentationEntry(
                    leader.PlayerId,
                    leader.DisplayName,
                    $"NOW TRENDING AT #{leader.Rank}",
                    leader.Rank,
                    leader.Gained)
            ];
        }
        if (IsScoreScreen(phase) || phase == WinnerCelebrationPhase)
        {
            return RankedParticipants(state);
        }
        if (phase.EndsWith("Results", StringComparison.Ordinal) ||
            phase.EndsWith("Voting", StringComparison.Ordinal) || phase == FinalMachineGuessPhase)
        {
            var counts = VoteCounts(state);
            var revealing = phase.EndsWith("Results", StringComparison.Ordinal) ||
                phase == FinalMachineGuessPhase;
            var opportunities = state.VotingOpportunities ?? VotingOpportunities(state, state.Options);
            var ranked = revealing
                ? state.Options.OrderByDescending(option => phase.StartsWith("Final", StringComparison.Ordinal)
                    ? counts.GetValueOrDefault(option.OptionId)
                    : opportunities.GetValueOrDefault(option.OptionId) == 0
                        ? 0m
                        : (decimal)counts.GetValueOrDefault(option.OptionId) /
                            opportunities.GetValueOrDefault(option.OptionId))
                    .ThenBy(option => option.OptionId).ToArray()
                : state.Options.ToArray();
            var entries = new List<GamePresentationEntry>();
            decimal? previousScore = null;
            var rank = 0;
            for (var index = 0; index < ranked.Length; index++)
            {
                var option = ranked[index];
                var score = phase.StartsWith("Final", StringComparison.Ordinal)
                    ? counts.GetValueOrDefault(option.OptionId)
                    : opportunities.GetValueOrDefault(option.OptionId) == 0
                        ? 0m
                        : (decimal)counts.GetValueOrDefault(option.OptionId) /
                            opportunities.GetValueOrDefault(option.OptionId);
                if (revealing && score != previousScore)
                {
                    rank = index + 1;
                    previousScore = score;
                }
                var upload = state.Uploads.SingleOrDefault(item => item.SubmissionId == option.OptionId);
                entries.Add(new GamePresentationEntry(
                    option.AuthorId ?? Guid.Empty,
                    ((char)('A' + index)).ToString(),
                    revealing
                        ? $"{option.Text} · {AuthorLabel(state, option)} · " +
                            $"{counts.GetValueOrDefault(option.OptionId)} vote(s)"
                        : option.Text,
                    revealing ? rank : null,
                    upload?.PointsAwarded ?? 0));
            }
            return entries;
        }
        return state.Participants.Select(participant => new GamePresentationEntry(
            participant.PlayerId, participant.DisplayName, "CHANNEL ONLINE", null, 0)).ToArray();
    }

    private static List<GamePresentationEntry> RankedParticipants(SlopMachineState state)
    {
        var ordered = state.Participants.OrderByDescending(item => TotalScore(state, item.PlayerId))
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal).ToArray();
        var entries = new List<GamePresentationEntry>();
        int? previous = null;
        var rank = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var score = TotalScore(state, ordered[index].PlayerId);
            if (score != previous)
            {
                rank = index + 1;
                previous = score;
            }
            var earnedThisRound = score - state.ScoreReviewStart.GetValueOrDefault(ordered[index].PlayerId);
            var hits = state.Uploads.Count(upload =>
                upload.AuthorId == ordered[index].PlayerId && upload.WonBonus);
            entries.Add(new GamePresentationEntry(
                ordered[index].PlayerId,
                ordered[index].DisplayName,
                $"{score:N0} views · {hits} viral hit{(hits == 1 ? string.Empty : "s")}",
                rank,
                earnedThisRound));
        }
        return entries;
    }

    private static string PlayerInstructions(GameModuleState current, SlopMachineState state, Guid playerId)
    {
        if (current.Phase == TelephoneMatchingPhase && state.TelephoneMatches.TryGetValue(playerId, out var match))
        {
            return $"Which thumbnail inspired: “{state.TextSubmissions[match.WriterId]}”?";
        }
        if (current.Phase == FinalMachineGuessPhase)
        {
            return "Pick the two titles secretly written by the Slop Machine.";
        }
        if (state.Assignments.TryGetValue(playerId, out var assignment))
        {
            return current.Phase switch
            {
                RouletteSpinningPhase =>
                    $"FORMAT: {assignment.Format}",
                RouletteWritingPhase =>
                    $"Complete the blank{(FormatSegments(assignment.Format).Length == 3 ? "s" : string.Empty)} in “{assignment.Format}”.",
                CommentsWritingPhase =>
                    $"Write {assignment.CommentType?.ToLowerInvariant()} beneath “{assignment.Format}”.",
                TelephoneWritingPhase => "Make the title funny but recognisable.",
                FreshWritingPhase => "Make it impossible not to click.",
                FinalWritingPhase => "Write humanity's last clickbait title.",
                _ => state.Message
            };
        }
        return state.Message;
    }

    private static bool HostCanAdvance(string phase) => phase is GameIntroPhase or FreshIntroPhase or
        FreshResultsPhase or ScoreReview1Phase or RouletteIntroPhase or RouletteSpinningPhase or
        RouletteResultsPhase or ScoreReview2Phase or TelephoneIntroPhase or TelephoneResultsPhase or
        ScoreReview3Phase or CommentsIntroPhase or CommentsResultsPhase or ScoreReview4Phase or
        FinalIntroPhase or FinalResultsPhase or FinalScoreReviewPhase or
        WinnerCelebrationPhase;

    private static string? AdvanceLabel(string phase) => phase switch
    {
        RouletteSpinningPhase => "Stop reels now",
        GameIntroPhase or FreshIntroPhase or FreshResultsPhase or ScoreReview1Phase or
            RouletteIntroPhase or RouletteResultsPhase or ScoreReview2Phase or TelephoneIntroPhase or
            TelephoneResultsPhase or ScoreReview3Phase or CommentsIntroPhase or CommentsResultsPhase or
            ScoreReview4Phase or FinalIntroPhase or FinalResultsPhase or FinalScoreReviewPhase or
            WinnerCelebrationPhase => "Continue now",
        _ => null
    };

    private static bool IsScoreScreen(string phase) =>
        phase is ScoreReview1Phase or ScoreReview3Phase or FinalScoreReviewPhase;

    private static string DisplayTitle(string phase) => phase switch
    {
        GameIntroPhase => "SLOP MACHINE",
        FreshIntroPhase or FreshWritingPhase or FreshVotingPhase or FreshResultsPhase =>
            "FRESH SLOP",
        RouletteIntroPhase or RouletteSpinningPhase or RouletteWritingPhase or
            RouletteVotingPhase or RouletteResultsPhase => "ALGORITHM ROULETTE",
        TelephoneIntroPhase or TelephoneWritingPhase or TelephoneMatchingPhase or
            TelephoneVotingPhase or TelephoneResultsPhase => "THUMBNAIL TELEPHONE",
        CommentsIntroPhase or CommentsWritingPhase or CommentsVotingPhase or
            CommentsResultsPhase => "COMMENTS SECTION",
        FinalIntroPhase or FinalWritingPhase or FinalVotingPhase or
            FinalMachineGuessPhase or FinalResultsPhase => "BEAT THE MACHINE",
        WinnerCelebrationPhase => "THE ALGORITHM HAS CHOSEN ITS HUMAN",
        ScoreReview2Phase or ScoreReview4Phase => "CHANNEL UPDATE",
        _ => "CURRENT STANDINGS"
    };

    private static string DisplayPrompt(string phase, SlopMachineState state) => phase switch
    {
        GameIntroPhase => "Feed the algorithm. Harvest the views.",
        FreshIntroPhase => "Everyone captions the same irresistible thumbnail.",
        FreshWritingPhase => $"Fresh Slop · Heat {state.FreshHeat + 1} of 2",
        FreshVotingPhase => "Which title would you click?",
        FreshResultsPhase => "Creators revealed. Views harvested.",
        RouletteIntroPhase => "Two reels. One free re-spin. Infinite regret.",
        RouletteSpinningPhase => "THUMBNAIL · TITLE FORMAT",
        RouletteWritingPhase => "Turn your cursed assignment into content.",
        RouletteVotingPhase => "Feed one upload to the algorithm.",
        RouletteResultsPhase => "Algorithm Bonus awarded.",
        TelephoneIntroPhase => "Write it. Scramble it. Try to recognise it.",
        TelephoneWritingPhase => "Make the title funny but recognisable.",
        TelephoneMatchingPhase => "Reconnect a mystery title to its thumbnail.",
        TelephoneVotingPhase => "Vote for the funniest resulting pairing.",
        TelephoneResultsPhase => "The telephone line has stopped screaming.",
        CommentsIntroPhase => "The worst part of every upload is now open.",
        CommentsWritingPhase => "Write beneath a returning viral upload.",
        CommentsVotingPhase => "Reward the most engaging mistake.",
        CommentsResultsPhase => "Engagement Bonus awarded.",
        FinalIntroPhase => "Humans versus two stored machine titles.",
        FinalWritingPhase => "Write humanity's last clickbait title.",
        FinalVotingPhase => "Vote for the funniest title.",
        FinalMachineGuessPhase => "Identify both machine-generated titles.",
        FinalResultsPhase => "Machine authorship revealed.",
        ScoreReview2Phase or ScoreReview4Phase => GrowthHeadline(state),
        FinalScoreReviewPhase => "FINAL VIEW COUNT",
        WinnerCelebrationPhase => state.Message,
        _ when IsScoreScreen(phase) => "CURRENT CHANNEL RANKINGS",
        _ => DisplayTitle(phase)
    };

    private static int SubmittedCount(string phase, SlopMachineState state) => phase switch
    {
        FreshWritingPhase or RouletteWritingPhase or TelephoneWritingPhase or CommentsWritingPhase or
            FinalWritingPhase => state.TextSubmissions.Count,
        FreshVotingPhase or RouletteVotingPhase or TelephoneVotingPhase or CommentsVotingPhase or
            FinalVotingPhase => state.Votes.Count,
        TelephoneMatchingPhase => state.TelephoneMatches.Values.Count(item => item.SelectedThumbnailId is not null),
        FinalMachineGuessPhase => state.MachineGuesses.Values.Count(item => item.Count >= 2),
        RouletteSpinningPhase => state.Assignments.Values.Count(item => item.RespinUsed),
        _ => 0
    };

    private static string AuthorLabel(SlopMachineState state, SlopOption option)
    {
        if (option.IsMachine)
        {
            return "SLOP MACHINE";
        }
        return option.AuthorId is { } author
            ? state.Participants.Single(item => item.PlayerId == author).DisplayName
            : "UNKNOWN";
    }

    private static string ResultDetail(SlopMachineState state, SlopOption option)
    {
        var votes = VoteCounts(state).GetValueOrDefault(option.OptionId);
        return $"{AuthorLabel(state, option)} · {votes} vote(s)";
    }

    private static string WinnerCelebrationMessage(SlopMachineState state)
    {
        var bestScore = state.Participants.Max(participant => TotalScore(state, participant.PlayerId));
        var winners = state.Participants
            .Where(participant => TotalScore(state, participant.PlayerId) == bestScore)
            .Select(participant => participant.DisplayName)
            .ToArray();
        var names = string.Join(" & ", winners);
        var rank = winners.Length > 1 ? "JOINT GLOBAL SLOP BARONS" : "GLOBAL SLOP BARON";
        return $"{names} · {rank} · {bestScore:N0} views";
    }

    private static PlayerControllerView WaitingController(string message = "Your upload is locked.") =>
        new(PlayerControllerKind.Waiting, string.Empty, false, message, GameJson.Empty);

    private static GameTransition ScoreReview(SlopMachineState state, string phase, string message) =>
        Transition(phase, state with { Message = message });

    private static GameTransition GrowthReview(SlopMachineState state, string phase)
    {
        var leader = GrowthLeader(state);
        return Transition(phase, state with
        {
            Message = $"NOW TRENDING AT #{leader.Rank}"
        });
    }

    private static string GrowthHeadline(SlopMachineState state)
    {
        var leader = GrowthLeader(state);
        return $"{leader.DisplayName.ToUpperInvariant()} GAINED {leader.Gained:N0} VIEWS";
    }

    private static (Guid PlayerId, string DisplayName, int Gained, int Rank) GrowthLeader(
        SlopMachineState state)
    {
        var ranked = state.Participants
            .OrderByDescending(participant => TotalScore(state, participant.PlayerId))
            .ThenBy(participant => participant.DisplayName, StringComparer.Ordinal)
            .ToArray();
        var leader = state.Participants
            .OrderByDescending(participant => TotalScore(state, participant.PlayerId) -
                state.ScoreReviewStart.GetValueOrDefault(participant.PlayerId))
            .ThenByDescending(participant => TotalScore(state, participant.PlayerId))
            .ThenBy(participant => participant.DisplayName, StringComparer.Ordinal)
            .First();
        return (
            leader.PlayerId,
            leader.DisplayName,
            TotalScore(state, leader.PlayerId) - state.ScoreReviewStart.GetValueOrDefault(leader.PlayerId),
            Array.FindIndex(ranked, participant => participant.PlayerId == leader.PlayerId) + 1);
    }

    private static SlopMachineState ResetReview(SlopMachineState state, string message) => state with
    {
        ScoreReviewStart = state.Participants.ToDictionary(
            participant => participant.PlayerId,
            participant => TotalScore(state, participant.PlayerId)),
        Bonuses = [],
        Options = [],
        Votes = new Dictionary<Guid, Guid>(),
        VoteHeat = 0,
        VoteHeats = [],
        VotingOpportunities = new Dictionary<Guid, int>(),
        Message = message
    };

    private static SlopMachineState ClearRoundInput(SlopMachineState state) => state with
    {
        Assignments = new Dictionary<Guid, SlopAssignment>(),
        TextSubmissions = new Dictionary<Guid, string>(),
        Options = [],
        Votes = new Dictionary<Guid, Guid>(),
        TelephoneMatches = new Dictionary<Guid, TelephoneMatch>(),
        MachineGuesses = new Dictionary<Guid, IReadOnlyList<Guid>>(),
        Bonuses = [],
        VoteHeat = 0,
        VoteHeats = [],
        VotingOpportunities = new Dictionary<Guid, int>()
    };

    private static SlopMachineState ApplyAwards(
        SlopMachineState state,
        IReadOnlyList<ScoreAward> awards)
    {
        var earned = state.EarnedViews.ToDictionary();
        foreach (var award in awards)
        {
            earned[award.PlayerId] = earned.GetValueOrDefault(award.PlayerId) + award.Points;
        }
        return state with { EarnedViews = earned };
    }

    private static int TotalScore(SlopMachineState state, Guid playerId)
    {
        var participant = state.Participants.Single(item => item.PlayerId == playerId);
        return participant.StartingScore + state.EarnedViews.GetValueOrDefault(playerId);
    }

    private static Dictionary<Guid, int> VotingOpportunities(
        SlopMachineState state,
        IReadOnlyList<SlopOption> options) => options.ToDictionary(
            option => option.OptionId,
            option => state.Participants.Count(participant => option.AuthorId != participant.PlayerId));

    private static Guid[] AdjustedVoteWinners(
        SlopMachineState state,
        IReadOnlyDictionary<Guid, int> counts)
    {
        if (counts.Values.DefaultIfEmpty().Max() == 0)
        {
            return [];
        }
        var opportunities = state.VotingOpportunities ?? VotingOpportunities(state, state.Options);
        var adjusted = state.Options.ToDictionary(
            option => option.OptionId,
            option => opportunities.GetValueOrDefault(option.OptionId) == 0
                ? 0m
                : (decimal)counts.GetValueOrDefault(option.OptionId) /
                    opportunities.GetValueOrDefault(option.OptionId));
        var best = adjusted.Values.Max();
        return adjusted.Where(item => item.Value == best && best > 0)
            .Select(item => item.Key).ToArray();
    }

    private static SlopSubmission[] UpdateUploads(
        SlopMachineState state,
        IReadOnlyDictionary<Guid, int> counts,
        IReadOnlyCollection<Guid> winners,
        int pointsPerVote,
        int winnerBonus) => state.Uploads.Select(upload =>
        {
            if (state.Options.All(option => option.OptionId != upload.SubmissionId))
            {
                return upload;
            }
            var votes = counts.GetValueOrDefault(upload.SubmissionId);
            return upload with
            {
                Votes = votes,
                PointsAwarded = votes * pointsPerVote +
                    (winners.Contains(upload.SubmissionId) ? winnerBonus : 0),
                WonBonus = winners.Contains(upload.SubmissionId)
            };
        }).ToArray();

    private static Guid[] EligibleVoters(SlopMachineState state) => state.Participants
        .Where(participant => OptionsForPlayer(state, participant.PlayerId).Length > 0)
        .Select(participant => participant.PlayerId).ToArray();

    private static SlopOption[] OptionsForPlayer(SlopMachineState state, Guid playerId) =>
        state.Options.Where(option => option.AuthorId != playerId).ToArray();

    private static Dictionary<Guid, int> VoteCounts(SlopMachineState state) =>
        state.Votes.Values.GroupBy(optionId => optionId).ToDictionary(group => group.Key, group => group.Count());

    private static Guid RequiredPlayer(SlopMachineState state, GameActionContext context)
    {
        if (!context.Actor.TryGetPlayerId(out var playerId) ||
            state.Participants.All(participant => participant.PlayerId != playerId))
        {
            throw new GameRuleViolationException("player-required", "A current player is required.");
        }
        return playerId;
    }

    private static void RequireHost(GameActionContext context)
    {
        if (context.Actor.Role != GameActorRole.Host)
        {
            throw new GameRuleViolationException("host-required", "Only the host can advance the machine.");
        }
    }

    private static string NormalizeText(string value, int maximumLength)
    {
        var normalized = WhitespaceRegex().Replace(value ?? string.Empty, " ").Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new GameRuleViolationException(
                "invalid-text", $"Enter between 1 and {maximumLength} characters.");
        }
        if (normalized.Any(character => char.IsControl(character)))
        {
            throw new GameRuleViolationException("invalid-text", "That text contains unsupported characters.");
        }
        return normalized;
    }

    private static string CompleteFormat(string format, IReadOnlyList<string>? values)
    {
        var segments = FormatSegments(format);
        var blankCount = segments.Length - 1;
        if (blankCount is < 1 or > 2 || values is null || values.Count != blankCount)
        {
            throw new GameRuleViolationException(
                "invalid-format", "Complete every blank in the assigned title format.");
        }
        var normalizedValues = values.Select(value => NormalizeText(value, MaximumTitleLength)).ToArray();
        var completed = string.Concat(segments.Select((segment, index) =>
            index < normalizedValues.Length ? segment + normalizedValues[index] : segment));
        return NormalizeText(completed, MaximumTitleLength);
    }

    private static string[] FormatSegments(string format)
    {
        var segments = format.Split("___", StringSplitOptions.None);
        if (segments.Length is < 2 or > 3)
        {
            throw new InvalidOperationException("Slop Machine title formats require one or two blanks.");
        }
        return segments;
    }

    private static List<Guid> Derange(List<Guid> writers, Random random)
    {
        if (writers.Count == 2)
        {
            return [writers[1], writers[0]];
        }
        var receivers = writers.ToList();
        do
        {
            Shuffle(receivers, random);
        }
        while (receivers.Where((receiver, index) => receiver == writers[index]).Any());
        return receivers;
    }

    private string[] PickDecoys(
        string intendedId,
        SlopMachineState state,
        Random random,
        int count)
    {
        var intended = Thumbnail(intendedId);
        var used = state.UsedThumbnailIds.ToHashSet(StringComparer.Ordinal);
        var candidates = catalogue.Where(item => item.Id != intendedId && !used.Contains(item.Id))
            .OrderByDescending(item => string.Equals(item.Category, intended.Category, StringComparison.Ordinal))
            .ThenByDescending(item => SharedWords(item.Composition, intended.Composition))
            .ThenBy(_ => random.Next()).Select(item => item.Id).Distinct(StringComparer.Ordinal)
            .Take(count).ToArray();
        if (candidates.Length != count)
        {
            throw new InvalidOperationException("The thumbnail catalogue cannot provide unique decoys.");
        }
        return candidates;
    }

    private static int SharedWords(string left, string right)
    {
        var words = left.Split(['-', ',', ' '], StringSplitOptions.RemoveEmptyEntries).ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        return right.Split(['-', ',', ' '], StringSplitOptions.RemoveEmptyEntries).Count(words.Contains);
    }

    private SlopThumbnail PickUnusedThumbnail(
        SlopMachineState state,
        Random random,
        IReadOnlyList<string>? additionallyExcluded = null,
        bool requireMachineTitles = false)
    {
        var excluded = state.UsedThumbnailIds.ToHashSet(StringComparer.Ordinal);
        if (additionallyExcluded is not null)
        {
            excluded.UnionWith(additionallyExcluded);
        }
        var choices = catalogue.Where(item => !excluded.Contains(item.Id) &&
            (!requireMachineTitles || item.AiTitles.Count >= 2)).ToArray();
        if (choices.Length == 0)
        {
            throw new GameRuleViolationException("content-exhausted", "No unused thumbnails remain.");
        }
        return choices[random.Next(choices.Length)];
    }

    private SlopThumbnail Thumbnail(string id) => catalogue.Single(item => item.Id == id);

    private static GameMediaItem MediaItem(SlopThumbnail thumbnail) => new(
        thumbnail.Id, thumbnail.ImageUrl, thumbnail.AlternativeText);

    private static Random RandomFor(SlopMachineState state, string scope)
    {
        var seed = HashCode.Combine(scope, state.Participants.Count,
            string.Join('|', state.Participants.Select(item => item.PlayerId)),
            string.Join('|', state.UsedThumbnailIds));
        return new Random(seed);
    }

    private static T PickDifferent<T>(IReadOnlyList<T> choices, T current, Random random)
    {
        var available = choices.Where(item => !EqualityComparer<T>.Default.Equals(item, current)).ToArray();
        return available[random.Next(available.Length)];
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }

    private static GameTransition Transition(
        string phase,
        SlopMachineState state,
        DateTimeOffset? deadline = null) =>
        GameTransition.To(ModuleState(phase, deadline, false, state));

    private static GameTransition Changed(
        GameModuleState current,
        SlopMachineState state,
        string eventName,
        Guid playerId) => new(
            current with { Data = GameJson.From(state) }, [],
            [new GameEvent(eventName, GameJson.From(new { playerId }))]);

    private static GameModuleState ModuleState(
        string phase,
        DateTimeOffset? deadline,
        bool complete,
        SlopMachineState state) => new(1, phase, deadline, complete, GameJson.From(state));

    private static SlopMachineState ReadState(GameModuleState state) =>
        state.Data.Deserialize<SlopMachineState>() ??
        throw new InvalidOperationException("The Slop Machine snapshot is invalid.");

    private static string ReadString(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String && property.GetString() is { } value)
        {
            return value;
        }
        throw new GameRuleViolationException("invalid-payload", $"A valid {propertyName} is required.");
    }

    private static string[]? ReadOptionalStrings(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (property.ValueKind != JsonValueKind.Array ||
            property.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            throw new GameRuleViolationException("invalid-payload", $"Valid {propertyName} are required.");
        }
        return property.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static Guid ReadGuid(JsonElement payload, string propertyName)
    {
        var value = ReadString(payload, propertyName);
        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new GameRuleViolationException("invalid-payload", $"A valid {propertyName} is required.");
    }

    private static SlopThumbnail[] LoadCatalogue()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Quizizzo.Games.SlopMachine.Assets.thumbnails.json") ??
            throw new InvalidOperationException("The Slop Machine thumbnail manifest is missing.");
        return JsonSerializer.Deserialize<SlopThumbnail[]>(stream, CatalogueJsonOptions) ??
            throw new InvalidOperationException("The Slop Machine thumbnail manifest is invalid.");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
