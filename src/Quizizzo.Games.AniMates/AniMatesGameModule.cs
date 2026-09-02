using System.Security.Cryptography;
using System.Text.Json;
using Quizizzo.GameContracts;

namespace Quizizzo.Games.AniMates;

public sealed class AniMatesGameModule(
    TimeSpan? drawingDuration = null,
    TimeSpan? guessingDuration = null,
    TimeSpan? choosingDuration = null,
    TimeSpan? showdownVotingDuration = null,
    TimeSpan? briefingDuration = null,
    TimeSpan? resultsDuration = null,
    TimeSpan? showdownResultsDuration = null,
    TimeSpan? celebrationDuration = null) : IGameModule
{
    public const string GameKey = "animates";
    public const string BriefingPhase = "Briefing";
    public const string ShowdownBriefingPhase = "ShowdownBriefing";
    public const string DrawingPhase = "Drawing";
    public const string GuessingPhase = "Guessing";
    public const string ChoosingPhase = "Choosing";
    public const string ResultsPhase = "Results";
    public const string ShowdownPlaybackPhase = "ShowdownPlayback";
    public const string ShowdownVotingPhase = "ShowdownVoting";
    public const string ShowdownResultsPhase = "ShowdownResults";
    public const string FinalCelebrationPhase = "FinalCelebration";
    public const string CompletedPhase = "Completed";
    public const int StateSchemaVersion = 4;
    public const int RoundOneFrameCount = 3;
    public const int RoundTwoFrameCount = 5;
    public const int MaximumFrameCount = RoundTwoFrameCount;
    public const int RequiredFrameCount = RoundOneFrameCount;
    public const int LogicalSize = 512;
    public const int MaximumSubmissionPayloadBytes = 6 * 1024 * 1024;
    public const int MaximumGuessLength = 200;
    public const int MaximumPlayers = 6;
    public const int GuessChosenPoints = 100;
    public const int CorrectChoicePoints = 50;
    public const int AnimatorCorrectChoicePoints = 100;
    public const int ShowdownVotePoints = 100;
    public const int ShowdownWinnerBonus = 200;
    public const int DefaultDrawingSecondsPerFrame = 45;
    public const int MinimumDrawingSecondsPerFrame = 10;
    public const int MaximumDrawingSecondsPerFrame = 180;

    private const string PromptResourceName =
        "Quizizzo.Games.AniMates.Assets.drawing-prompts-1000.json";
    private const int ExpectedPromptCount = 1000;

    private readonly TimeSpan? fixedDrawingDuration = drawingDuration;
    private readonly TimeSpan guessingDuration = guessingDuration ?? TimeSpan.FromSeconds(45);
    private readonly TimeSpan choosingDuration = choosingDuration ?? TimeSpan.FromSeconds(30);
    private readonly TimeSpan showdownVotingDuration = showdownVotingDuration ?? TimeSpan.FromSeconds(90);
    private readonly TimeSpan briefingDuration = briefingDuration ?? TimeSpan.FromSeconds(12);
    private readonly TimeSpan resultsDuration = resultsDuration ?? TimeSpan.FromSeconds(10);
    private readonly TimeSpan showdownResultsDuration = showdownResultsDuration ?? TimeSpan.FromSeconds(12);
    private readonly TimeSpan celebrationDuration = celebrationDuration ?? TimeSpan.FromSeconds(15);

    public GameDescriptor Descriptor { get; } = new(GameKey, "AniMates", 2, MaximumPlayers);

    public GameModuleState Start(GameStartContext context)
    {
        var configuration = ReadConfiguration(context.Configuration);
        var participants = context.Participants
            .Select(player => new AnimateParticipant(player.PlayerId, player.DisplayName)).ToArray();
        var selectedPrompts = SelectPrompts(participants.Length + 1);
        var roundOnePrompts = participants.Select((player, index) =>
                new KeyValuePair<Guid, DrawingPromptPair>(player.PlayerId, selectedPrompts[index]))
            .ToDictionary();
        return ModuleState(BriefingPhase, context.StartedAtUtc.Add(briefingDuration), false,
            new AnimateState(1, configuration.DrawingSecondsPerFrame, 0, participants,
                new Dictionary<Guid, AnimationSubmission>(),
                new Dictionary<Guid, string>(), [], new Dictionary<Guid, Guid>(), [],
                new Dictionary<Guid, Guid>(), [], roundOnePrompts,
                selectedPrompts[^1].DrawingPrompt,
                new Dictionary<Guid, long>(), new Dictionary<Guid, int>(),
                new Dictionary<Guid, int>()));
    }

    public GameTransition Apply(GameModuleState state, GameActionContext context, IGameAction action)
    {
        var animate = ReadState(state);
        return action switch
        {
            SubmitAnimationAction submission => SubmitAnimation(state, animate, context, submission),
            SubmitAnimationGuessAction guess => SubmitGuess(state, animate, context, guess),
            ChooseAnimationAnswerAction choice => Choose(state, animate, context, choice),
            VoteForShowdownAnimationAction vote => VoteForShowdown(state, animate, context, vote),
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
        SubmitAnimationGuessAction.ActionKind => new SubmitAnimationGuessAction(ReadText(payload)),
        ChooseAnimationAnswerAction.ActionKind => new ChooseAnimationAnswerAction(
            ReadGuid(payload, "answerOptionId")),
        VoteForShowdownAnimationAction.ActionKind => new VoteForShowdownAnimationAction(
            ReadGuid(payload, "submissionPlayerId")),
        AdvanceAniMatesAction.ActionKind => new AdvanceAniMatesAction(),
        _ => throw new GameRuleViolationException(
            "unsupported-action", $"Action '{actionKind}' is not supported by AniMates.")
    };

    private GameTransition SubmitAnimation(
        GameModuleState current,
        AnimateState state,
        GameActionContext context,
        SubmitAnimationAction action)
    {
        RequirePhase(current, DrawingPhase, "Drawing submissions are closed.");
        var playerId = RequiredPlayer(state, context);
        if (state.Submissions.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-submitted", "Your animation is already submitted.");
        }
        var requiredFrames = FrameCount(state);
        if (action.FrameAssetIds.Count < 1 || action.FrameAssetIds.Count > requiredFrames ||
            action.FrameAssetIds.Any(id => id == Guid.Empty))
        {
            throw new GameRuleViolationException(
                "invalid-frames", $"Submit between one and {requiredFrames} valid frames.");
        }

        var frames = action.FrameAssetIds.ToList();
        while (frames.Count < requiredFrames)
        {
            frames.Add(frames[^1]);
        }
        var submissions = state.Submissions.ToDictionary();
        submissions.Add(playerId, new AnimationSubmission(playerId, frames));
        var drawingMilliseconds = state.DrawingMilliseconds?.ToDictionary() ?? [];
        var drawingCounts = state.DrawingCounts?.ToDictionary() ?? [];
        var phaseStartedAt = current.PhaseEndsAtUtc.GetValueOrDefault(context.ReceivedAtUtc) -
            DrawingDuration(state);
        var elapsed = Math.Max(0, (long)(context.ReceivedAtUtc - phaseStartedAt).TotalMilliseconds);
        drawingMilliseconds[playerId] = drawingMilliseconds.GetValueOrDefault(playerId) + elapsed;
        drawingCounts[playerId] = drawingCounts.GetValueOrDefault(playerId) + 1;
        var updated = state with
        {
            Submissions = submissions,
            DrawingMilliseconds = drawingMilliseconds,
            DrawingCounts = drawingCounts
        };
        return submissions.Count == state.Participants.Count
            ? state.RoundNumber == 1
                ? BeginGuessing(updated, context.ReceivedAtUtc)
                : BeginShowdownVoting(updated, context.ReceivedAtUtc)
            : new GameTransition(current with { Data = GameJson.From(updated) }, [],
                [new GameEvent("AnimationSubmitted", GameJson.From(new { playerId }))]);
    }

    private GameTransition SubmitGuess(
        GameModuleState current,
        AnimateState state,
        GameActionContext context,
        SubmitAnimationGuessAction action)
    {
        RequirePhase(current, GuessingPhase, "Guesses are not open right now.");
        var playerId = RequiredPlayer(state, context);
        if (playerId == Animator(state).PlayerId)
        {
            throw new GameRuleViolationException("animator-ineligible", "The animator cannot submit a guess.");
        }
        if (state.Guesses.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-submitted", "Your guess is already locked in.");
        }

        var guesses = state.Guesses.ToDictionary();
        guesses.Add(playerId, NormalizeGuess(action.Value));
        var updated = state with { Guesses = guesses };
        return Guessers(updated).All(guesses.ContainsKey)
            ? BeginChoosing(updated, context.ReceivedAtUtc)
            : new GameTransition(current with { Data = GameJson.From(updated) }, [],
                [new GameEvent("AnimationGuessSubmitted", GameJson.From(new { playerId }))]);
    }

    private GameTransition Choose(
        GameModuleState current,
        AnimateState state,
        GameActionContext context,
        ChooseAnimationAnswerAction action)
    {
        RequirePhase(current, ChoosingPhase, "Answer choices are not open right now.");
        var playerId = RequiredPlayer(state, context);
        if (!EligibleChoosers(state).Contains(playerId))
        {
            throw new GameRuleViolationException("not-eligible", "You are not eligible to choose this turn.");
        }
        if (state.Choices.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-chosen", "Your answer is already locked in.");
        }
        var option = state.Options.SingleOrDefault(candidate => candidate.OptionId == action.AnswerOptionId);
        if (option is null)
        {
            throw new GameRuleViolationException("invalid-choice", "That answer is not available.");
        }
        if (option.AuthorPlayerId == playerId)
        {
            throw new GameRuleViolationException("self-choice", "You cannot choose your own answer.");
        }

        var choices = state.Choices.ToDictionary();
        choices.Add(playerId, option.OptionId);
        var updated = state with { Choices = choices };
        return EligibleChoosers(updated).All(choices.ContainsKey)
            ? Reveal(updated, context.ReceivedAtUtc)
            : new GameTransition(current with { Data = GameJson.From(updated) }, [],
                [new GameEvent("AnimationAnswerChosen", GameJson.From(new { playerId }))]);
    }

    private GameTransition VoteForShowdown(
        GameModuleState current,
        AnimateState state,
        GameActionContext context,
        VoteForShowdownAnimationAction action)
    {
        RequirePhase(current, ShowdownVotingPhase, "Showdown voting is not open right now.");
        var playerId = RequiredPlayer(state, context);
        if (state.ShowdownVotes.ContainsKey(playerId))
        {
            throw new GameRuleViolationException("already-voted", "Your showdown vote is already locked in.");
        }
        if (action.SubmissionPlayerId == playerId)
        {
            throw new GameRuleViolationException("self-vote", "You cannot vote for your own animation.");
        }
        if (!state.Submissions.ContainsKey(action.SubmissionPlayerId))
        {
            throw new GameRuleViolationException("invalid-vote", "That animation is not available.");
        }

        var votes = state.ShowdownVotes.ToDictionary();
        votes.Add(playerId, action.SubmissionPlayerId);
        var updated = state with { ShowdownVotes = votes };
        return ShowdownVoters(updated).All(votes.ContainsKey)
            ? RevealShowdown(updated, context.ReceivedAtUtc)
            : new GameTransition(current with { Data = GameJson.From(updated) }, [],
                [new GameEvent("ShowdownVoteSubmitted", GameJson.From(new { playerId }))]);
    }

    private GameTransition Deadline(
        GameModuleState current,
        AnimateState state,
        GameActionContext context) => current.Phase switch
        {
            DrawingPhase => state.Submissions.Count == 0
                ? CompleteWithoutAnimations(state)
                : state.RoundNumber == 1
                    ? BeginGuessing(MoveToNextSubmittedAnimation(state, -1), context.ReceivedAtUtc)
                    : BeginShowdownVoting(state, context.ReceivedAtUtc),
            GuessingPhase => BeginChoosing(state, context.ReceivedAtUtc),
            ChoosingPhase => Reveal(state, context.ReceivedAtUtc),
            ShowdownVotingPhase => RevealShowdown(state, context.ReceivedAtUtc),
            BriefingPhase or ShowdownBriefingPhase or ResultsPhase or ShowdownResultsPhase or
                FinalCelebrationPhase =>
                Progress(current, state, context.ReceivedAtUtc),
            _ => throw new GameRuleViolationException("wrong-phase", "This phase has no active deadline.")
        };

    private GameTransition BeginGuessing(AnimateState state, DateTimeOffset now) => new(
        ModuleState(GuessingPhase, now.Add(guessingDuration), false, state), [],
        [new GameEvent("AnimationGuessingStarted", GameJson.Empty)]);

    private static GameTransition CompleteWithoutAnimations(AnimateState state) => new(
        ModuleState(CompletedPhase, null, true, state), [],
        [new GameEvent("GameCompleted", GameJson.Empty)]);

    private GameTransition BeginShowdownVoting(AnimateState state, DateTimeOffset now) => new(
        ModuleState(ShowdownVotingPhase, now.Add(showdownVotingDuration), false, state), [],
        [new GameEvent("ShowdownVotingStarted", GameJson.Empty)]);

    private GameTransition BeginChoosing(AnimateState state, DateTimeOffset now)
    {
        var prompt = PromptPair(state);
        var options = new List<AnimationAnswerOption>
        {
            new(Guid.NewGuid(), prompt.DrawingPrompt, true, null),
            new(Guid.NewGuid(), prompt.Distractor, false, null, true)
        };
        options.AddRange(state.Guesses.Select(guess =>
            new AnimationAnswerOption(Guid.NewGuid(), guess.Value, false, guess.Key)));
        Shuffle(options);
        var choosing = state with { Options = options };
        return options.Count < 2 || EligibleChoosers(choosing).Length == 0
            ? Reveal(choosing, now)
            : new GameTransition(ModuleState(ChoosingPhase, now.Add(choosingDuration), false, choosing), [],
                [new GameEvent("AnimationAnswersOpened", GameJson.From(new { answers = options.Count }))]);
    }

    private GameTransition Reveal(AnimateState state, DateTimeOffset now)
    {
        var points = state.Participants.ToDictionary(player => player.PlayerId, _ => 0);
        foreach (var choice in state.Choices)
        {
            var option = state.Options.Single(candidate => candidate.OptionId == choice.Value);
            if (option.IsCorrect)
            {
                points[choice.Key] += CorrectChoicePoints;
                points[Animator(state).PlayerId] += AnimatorCorrectChoicePoints;
            }
            else if (option.AuthorPlayerId is { } authorId)
            {
                points[authorId] += GuessChosenPoints;
            }
        }
        var bluffPicks = state.BluffPicks?.ToDictionary() ?? [];
        foreach (var choice in state.Choices.Values)
        {
            var option = state.Options.Single(candidate => candidate.OptionId == choice);
            if (option.AuthorPlayerId is { } authorId)
            {
                bluffPicks[authorId] = bluffPicks.GetValueOrDefault(authorId) + 1;
            }
        }
        var awards = points.Where(item => item.Value > 0)
            .Select(item => new AnimationAward(item.Key, item.Value))
            .OrderByDescending(item => item.Points).ThenBy(item => item.PlayerId).ToArray();
        var revealed = state with { Awards = awards, BluffPicks = bluffPicks };
        return new GameTransition(
            ModuleState(ResultsPhase, now.Add(resultsDuration), false, revealed),
            awards.Select(award => new ScoreAward(
                award.PlayerId, award.Points, $"AniMates turn {state.TurnIndex + 1}")).ToArray(),
            [new GameEvent("AnimationAnswerRevealed", GameJson.Empty)]);
    }

    private GameTransition RevealShowdown(AnimateState state, DateTimeOffset now)
    {
        var voteCounts = state.ShowdownVotes.Values.GroupBy(id => id)
            .ToDictionary(group => group.Key, group => group.Count());
        var maximumVotes = state.Submissions.Count == 0
            ? 0
            : state.Submissions.Keys.Max(id => voteCounts.GetValueOrDefault(id));
        var ordered = state.Submissions.Keys
            .Select(id => new { PlayerId = id, Votes = voteCounts.GetValueOrDefault(id) })
            .OrderByDescending(item => item.Votes).ThenBy(item => item.PlayerId).ToArray();
        var results = new List<ShowdownResult>(ordered.Length);
        int? previousVotes = null;
        var rank = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            if (previousVotes != ordered[index].Votes)
            {
                rank = index + 1;
                previousVotes = ordered[index].Votes;
            }
            var bonus = maximumVotes > 0 && ordered[index].Votes == maximumVotes
                ? ShowdownWinnerBonus
                : 0;
            results.Add(new ShowdownResult(
                ordered[index].PlayerId, ordered[index].Votes, rank,
                ordered[index].Votes * ShowdownVotePoints + bonus));
        }
        var revealed = state with { ShowdownResults = results };
        return new GameTransition(
            ModuleState(ShowdownResultsPhase, now.Add(showdownResultsDuration), false, revealed),
            results.Where(result => result.Points > 0).Select(result => new ScoreAward(
                result.PlayerId, result.Points,
                $"AniMates showdown: {result.Votes} votes{(result.Rank == 1 ? " + winner bonus" : string.Empty)}"))
                .ToArray(),
            [new GameEvent("ShowdownCreatorsRevealed", GameJson.Empty)]);
    }

    private GameTransition Advance(
        GameModuleState current,
        AnimateState state,
        GameActionContext context)
    {
        if (context.Actor.Role != GameActorRole.Host)
        {
            throw new GameRuleViolationException("host-required", "Only the host can advance AniMates.");
        }
        return Progress(current, state, context.ReceivedAtUtc);
    }

    private GameTransition Progress(
        GameModuleState current,
        AnimateState state,
        DateTimeOffset now)
    {
        if (current.Phase == BriefingPhase)
        {
            return StartDrawing(state, now);
        }
        if (current.Phase == ShowdownBriefingPhase)
        {
            return StartDrawing(state, now);
        }
        if (current.Phase == ShowdownResultsPhase)
        {
            return new GameTransition(
                ModuleState(FinalCelebrationPhase, now.Add(celebrationDuration), false, state), [],
                [new GameEvent("AniMatesFinalCelebrationStarted", GameJson.Empty)]);
        }
        if (current.Phase == FinalCelebrationPhase)
        {
            return new GameTransition(ModuleState(CompletedPhase, null, true, state), [],
                [new GameEvent("GameCompleted", GameJson.Empty)]);
        }
        RequirePhase(current, ResultsPhase, "Results must be revealed first.");
        var nextIndex = NextSubmittedIndex(state, state.TurnIndex);
        if (nextIndex < 0)
        {
            var showdown = state with
            {
                RoundNumber = 2,
                TurnIndex = 0,
                Submissions = new Dictionary<Guid, AnimationSubmission>(),
                Guesses = new Dictionary<Guid, string>(),
                Options = [],
                Choices = new Dictionary<Guid, Guid>(),
                Awards = [],
                ShowdownVotes = new Dictionary<Guid, Guid>(),
                ShowdownResults = []
            };
            return new GameTransition(
                ModuleState(ShowdownBriefingPhase, now.Add(briefingDuration), false, showdown), [],
                [new GameEvent("ShowdownBriefingStarted", GameJson.Empty)]);
        }

        var next = state with
        {
            TurnIndex = nextIndex,
            Guesses = new Dictionary<Guid, string>(),
            Options = [],
            Choices = new Dictionary<Guid, Guid>(),
            Awards = []
        };
        return new GameTransition(
            ModuleState(GuessingPhase, now.Add(guessingDuration), false, next), [],
            [new GameEvent("AniMatesTurnStarted", GameJson.From(new { turn = next.TurnIndex + 1 }))]);
    }

    private GameTransition StartDrawing(AnimateState state, DateTimeOffset now) => new(
        ModuleState(DrawingPhase, now.Add(DrawingDuration(state)), false, state), [],
        [new GameEvent("AniMatesDrawingStarted", GameJson.From(new { round = state.RoundNumber }))]);

    private TimeSpan DrawingDuration(AnimateState state) => fixedDrawingDuration ?? TimeSpan.FromSeconds(
        (long)FrameCount(state) * EffectiveDrawingSecondsPerFrame(state));

    private static PlayerGameViewPayload PlayerView(
        GameModuleState current,
        AnimateState state,
        GameViewContext context)
    {
        var playerId = context.PlayerId
            ?? throw new GameRuleViolationException("player-required", "A player identity is required.");
        RequiredParticipant(state, playerId);
        var animator = Animator(state);

        if (current.Phase is BriefingPhase or ShowdownBriefingPhase)
        {
            return Waiting("Listen up!", "The presenter is explaining this round on the main screen.");
        }

        if (current.Phase == DrawingPhase && !state.Submissions.ContainsKey(playerId))
        {
            return new PlayerGameViewPayload(
                state.RoundNumber == 1 ? "Create your animation" : "Same Prompt Showdown",
                state.RoundNumber == 1 ? PromptForPlayer(state, playerId) : ShowdownPrompt(state),
                new PlayerControllerView(PlayerControllerKind.Drawing, SubmitAnimationAction.ActionKind, true,
                    "Send my animation", GameJson.From(new DrawingControllerConfiguration(
                        LogicalSize, LogicalSize, FrameCount(state), $"animates-round-{state.RoundNumber}", true))),
                GameJson.From(new { animator = true }));
        }
        if (current.Phase == GuessingPhase && playerId != animator.PlayerId && !state.Guesses.ContainsKey(playerId))
        {
            return new PlayerGameViewPayload(
                "What is the animation?", "Watch the main screen and write your best guess.",
                new PlayerControllerView(PlayerControllerKind.Text, SubmitAnimationGuessAction.ActionKind, true,
                    "Send my guess", GameJson.From(new TextControllerConfiguration(
                        MaximumGuessLength, "Describe what you think is happening..."))),
                GameJson.From(new { submitted = false, turn = state.TurnIndex }));
        }
        if (current.Phase == ChoosingPhase && EligibleChoosers(state).Contains(playerId) &&
            !state.Choices.ContainsKey(playerId))
        {
            var options = state.Options.Select((option, index) => new { Option = option, Index = index })
                .Where(item => item.Option.AuthorPlayerId != playerId)
                .Select(item => new ControllerOption(
                    item.Option.OptionId.ToString("N"), Letter(item.Index), item.Option.Text)).ToArray();
            return new PlayerGameViewPayload(
                "Choose the best answer", "Pick the answer that best fits. Your own guess is hidden.",
                new PlayerControllerView(PlayerControllerKind.Choice, ChooseAnimationAnswerAction.ActionKind, true,
                    "Lock in my answer", GameJson.From(new ChoiceControllerConfiguration(
                        options, null, "answerOptionId", $"animates-turn-{state.TurnIndex}:choice"))),
                GameJson.From(new { guessed = true, chosen = false, turn = state.TurnIndex }));
        }
        if (current.Phase == ShowdownVotingPhase && !state.ShowdownVotes.ContainsKey(playerId))
        {
            var options = state.Submissions.Values
                .OrderBy(submission => submission.PlayerId)
                .Select((submission, index) => new { Submission = submission, Index = index })
                .Where(item => item.Submission.PlayerId != playerId)
                .Select(item => new ControllerOption(
                    item.Submission.PlayerId.ToString("N"), Letter(item.Index), null,
                    item.Submission.FrameAssetIds)).ToArray();
            return new PlayerGameViewPayload(
                "Vote for your favourite", "Choose the animation you enjoyed most. Your own is hidden.",
                new PlayerControllerView(PlayerControllerKind.Vote, VoteForShowdownAnimationAction.ActionKind, true,
                    "Cast my vote", GameJson.From(new VoteControllerConfiguration(
                        options, null, "submissionPlayerId", "animates-showdown:vote"))),
                GameJson.From(new { voted = false }));
        }

        var award = state.Awards.SingleOrDefault(item => item.PlayerId == playerId);
        var instructions = current.Phase switch
        {
            DrawingPhase => "Animation submitted. Relax while everyone else finishes...",
            GuessingPhase => playerId == animator.PlayerId
                ? "Your animation is on the main screen. Wait for everyone to guess."
                : "Guess locked. Waiting for the other players...",
            ChoosingPhase => playerId == animator.PlayerId
                ? "Watch everyone choose on the main screen."
                : "Answer locked. Waiting for the reveal...",
            ResultsPhase => award is null
                ? "No points this turn. The next animator is up soon."
                : $"You earned {award.Points:N0} points this turn.",
            ShowdownVotingPhase => "Vote locked. Waiting for the creator reveal...",
            ShowdownResultsPhase => ShowdownResultFor(state, playerId) is { } result
                ? $"Your animation received {result.Votes} vote(s): +{result.Points:N0} points."
                : "Watch the winner reveal on the main screen.",
            FinalCelebrationPhase => "The final standings are celebrating on the main screen.",
            _ => "AniMates complete."
        };
        return Waiting(
            current.Phase is ResultsPhase or ShowdownResultsPhase or FinalCelebrationPhase
                ? "Results"
                : "Please wait",
            instructions);
    }

    private static HostGameViewPayload HostView(GameModuleState current, AnimateState state)
    {
        var canAdvance = current.Phase is BriefingPhase or ShowdownBriefingPhase or ResultsPhase or
            ShowdownResultsPhase or FinalCelebrationPhase;
        var advanceLabel = current.Phase switch
        {
            BriefingPhase or ShowdownBriefingPhase => "Start now",
            ResultsPhase => "Continue now",
            ShowdownResultsPhase => "Show final standings now",
            FinalCelebrationPhase => "Finish now",
            _ => null
        };
        return new HostGameViewPayload(
            $"AniMates — Round {state.RoundNumber}/2",
            HostPrompt(current, state), PhaseMessage(current, state),
            SubmittedCount(current, state), RequiredCount(current, state), canAdvance,
            canAdvance ? AdvanceAniMatesAction.ActionKind : null, advanceLabel, Entries(current, state));
    }

    private static DisplayGameViewPayload DisplayView(GameModuleState current, AnimateState state)
    {
        DrawingPresentationView? drawing = null;
        if (current.Phase is GuessingPhase or ChoosingPhase or ResultsPhase && CurrentSubmission(state) is { } submission)
        {
            drawing = new DrawingPresentationView(
                current.Phase == ResultsPhase ? "Reveal" : "Playback", 150,
                [new DrawingAnimationView(
                    Animator(state).PlayerId,
                    current.Phase == ResultsPhase ? Animator(state).DisplayName : null,
                    current.Phase == ResultsPhase ? Prompt(state) : "Mystery animation",
                    submission.FrameAssetIds, 0, null,
                    state.Awards.SingleOrDefault(award => award.PlayerId == Animator(state).PlayerId)?.Points ?? 0)]);
        }
        else if (current.Phase is ShowdownPlaybackPhase or ShowdownVotingPhase or ShowdownResultsPhase)
        {
            drawing = new DrawingPresentationView(
                current.Phase == ShowdownResultsPhase ? "ShowdownReveal" : "ShowdownPlayback", 150,
                ShowdownAnimations(current, state), 1);
        }
        return new DisplayGameViewPayload(
            DisplayTitle(current, state), DisplayPrompt(current, state),
            PhaseMessage(current, state), SubmittedCount(current, state), RequiredCount(current, state),
            Entries(current, state), drawing,
            current.Phase is BriefingPhase or ShowdownBriefingPhase
                ? DrawingTutorial(state)
                : null,
            current.Phase == FinalCelebrationPhase ||
            current.Phase == ResultsPhase && NextSubmittedIndex(state, state.TurnIndex) < 0,
            Statistics: current.Phase == FinalCelebrationPhase ? FinalStatistics(state) : null);
    }

    private static TutorialPresentationView DrawingTutorial(AnimateState state) => new(
        state.RoundNumber == 1 ? "HOW TO ANIMATE" : "FIVE-FRAME SHOWDOWN",
        FrameCount(state),
        [
            "Draw frame 1 with pen, colour, and size tools",
            "Move forward and use onion skin to trace the previous frame",
            "Undo a stroke or switch to the eraser whenever you need",
            "Preview every frame, then send your finished animation"
        ]);

    private static GamePresentationEntry[] Entries(GameModuleState current, AnimateState state)
    {
        if (current.Phase == DrawingPhase)
        {
            return state.Participants.Select(player => new GamePresentationEntry(
                player.PlayerId, player.DisplayName,
                state.Submissions.ContainsKey(player.PlayerId) ? "Idle" : "Thinking", null, 0)).ToArray();
        }
        if (current.Phase == GuessingPhase)
        {
            return Guessers(state).Select(id =>
            {
                var player = RequiredParticipant(state, id);
                return new GamePresentationEntry(
                    id, player.DisplayName, state.Guesses.ContainsKey(id) ? "Guess locked" : "Writing...", null, 0);
            }).ToArray();
        }
        if (current.Phase == ChoosingPhase)
        {
            return state.Options.Select((option, index) => new GamePresentationEntry(
                option.OptionId, Letter(index), option.Text, null, 0)).ToArray();
        }
        if (current.Phase == ResultsPhase)
        {
            var counts = state.Choices.Values.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
            return state.Options.Select((option, index) =>
            {
                var picks = counts.GetValueOrDefault(option.OptionId);
                var author = option.AuthorPlayerId is { } id
                    ? RequiredParticipant(state, id).DisplayName : Animator(state).DisplayName;
                var label = option.IsCorrect
                    ? $"{Letter(index)} — CORRECT ANSWER"
                    : option.IsDistractor
                        ? $"{Letter(index)} — BUILT-IN DECOY"
                        : $"{Letter(index)} — {author}";
                var points = option.IsCorrect
                    ? picks * (CorrectChoicePoints + AnimatorCorrectChoicePoints)
                    : option.IsDistractor ? 0 : picks * GuessChosenPoints;
                return new GamePresentationEntry(
                    option.OptionId, label, $"{option.Text} — {picks} pick(s)", null, points);
            }).ToArray();
        }
        if (current.Phase == ShowdownVotingPhase)
        {
            return [];
        }
        if (current.Phase is ShowdownResultsPhase or FinalCelebrationPhase)
        {
            return state.Submissions.Values.OrderBy(submission => submission.PlayerId).Select((submission, index) =>
            {
                var result = ShowdownResultFor(state, submission.PlayerId)!;
                var player = RequiredParticipant(state, result.PlayerId);
                return new GamePresentationEntry(
                    result.PlayerId, $"{Letter(index)} — {player.DisplayName.ToUpperInvariant()}",
                    $"{result.Votes} vote(s) — +{result.Points:N0} points",
                    result.Rank, result.Points);
            }).ToArray();
        }
        return [];
    }

    private static string DisplayTitle(GameModuleState current, AnimateState state) => current.Phase switch
    {
        BriefingPhase => "ANIMATES — ROUND 1",
        ShowdownBriefingPhase => "ANIMATES — ROUND 2",
        DrawingPhase => "ANIMATES — EVERYONE DRAW!",
        ShowdownPlaybackPhase => "SAME PROMPT SHOWDOWN",
        ShowdownVotingPhase => "VOTE FOR YOUR FAVOURITE",
        ShowdownResultsPhase => "CREATORS REVEALED!",
        FinalCelebrationPhase => "ANIMATES — FINAL RESULTS",
        _ => $"ANIMATES — {Animator(state).DisplayName.ToUpperInvariant()}'S ANIMATION"
    };

    private static string DisplayPrompt(GameModuleState current, AnimateState state) => current.Phase switch
    {
        BriefingPhase => RoundOneBriefing,
        ShowdownBriefingPhase => RoundTwoBriefing,
        DrawingPhase => state.RoundNumber == 1
            ? "Everyone has a different secret prompt"
            : ShowdownPrompt(state),
        GuessingPhase => "What do you think this is?",
        ChoosingPhase => "Choose the best-fitting answer",
        ShowdownPlaybackPhase => $"EVERYONE WAS ASKED TO ANIMATE… {ShowdownPrompt(state)}",
        ShowdownVotingPhase => $"EVERYONE WAS ASKED TO ANIMATE… {ShowdownPrompt(state)}",
        ShowdownResultsPhase => ShowdownPrompt(state),
        FinalCelebrationPhase => "THE FINAL SCORES ARE IN",
        _ => "The answer is..."
    };

    private static string HostPrompt(GameModuleState current, AnimateState state) => current.Phase switch
    {
        BriefingPhase => RoundOneBriefing,
        ShowdownBriefingPhase => RoundTwoBriefing,
        DrawingPhase => state.RoundNumber == 1 ? "Everyone is animating at once" : ShowdownPrompt(state),
        ResultsPhase => Prompt(state),
        ShowdownVotingPhase => "Players have 90 seconds to choose their favourite",
        ShowdownResultsPhase => "Showdown creators and winner revealed",
        FinalCelebrationPhase => "Final standings and winner celebration",
        _ => $"{Animator(state).DisplayName}'s animation"
    };

    private static string PhaseMessage(GameModuleState current, AnimateState state) => current.Phase switch
    {
        DrawingPhase => $"{state.Submissions.Count}/{state.Participants.Count} animations ready",
        GuessingPhase => $"{state.Guesses.Count}/{Guessers(state).Length} guesses locked in",
        ChoosingPhase => $"{state.Choices.Count}/{EligibleChoosers(state).Length} choices locked in",
        ResultsPhase => "Correct answer and writers revealed!",
        BriefingPhase => "Presenter briefing — start when everyone is ready",
        ShowdownBriefingPhase => "Presenter briefing — one prompt, five frames",
        ShowdownVotingPhase => $"{state.ShowdownVotes.Count}/{ShowdownVoters(state).Length} votes locked in",
        ShowdownResultsPhase => "Votes counted and creators revealed!",
        FinalCelebrationPhase => "Winner celebrating — final standings",
        _ => "AniMates complete"
    };

    private static int SubmittedCount(GameModuleState current, AnimateState state) => current.Phase switch
    {
        DrawingPhase => state.Submissions.Count,
        GuessingPhase => state.Guesses.Count,
        ShowdownVotingPhase => state.ShowdownVotes.Count,
        _ => state.Choices.Count
    };

    private static int RequiredCount(GameModuleState current, AnimateState state) => current.Phase switch
    {
        DrawingPhase => state.Participants.Count,
        GuessingPhase => Guessers(state).Length,
        ShowdownVotingPhase => ShowdownVoters(state).Length,
        _ => EligibleChoosers(state).Length
    };

    private static Guid[] Guessers(AnimateState state) => state.Participants
        .Where(player => player.PlayerId != Animator(state).PlayerId)
        .Select(player => player.PlayerId).ToArray();

    private static Guid[] EligibleChoosers(AnimateState state) => Guessers(state)
        .Where(id => state.Options.Any(option => option.AuthorPlayerId != id)).ToArray();

    private static Guid[] ShowdownVoters(AnimateState state) => state.Participants
        .Where(player => state.Submissions.Keys.Any(ownerId => ownerId != player.PlayerId))
        .Select(player => player.PlayerId).ToArray();

    private static DrawingAnimationView[] ShowdownAnimations(GameModuleState current, AnimateState state) =>
        state.Submissions.Values.OrderBy(submission => submission.PlayerId).Select((submission, index) =>
        {
            var result = ShowdownResultFor(state, submission.PlayerId);
            return new DrawingAnimationView(
                submission.PlayerId,
                current.Phase == ShowdownResultsPhase
                    ? RequiredParticipant(state, submission.PlayerId).DisplayName : null,
                $"ANIMATION {Letter(index)}", submission.FrameAssetIds,
                result?.Votes ?? 0, result?.Rank, result?.Points ?? 0);
        }).ToArray();

    private static ShowdownResult? ShowdownResultFor(AnimateState state, Guid playerId) =>
        state.ShowdownResults.SingleOrDefault(result => result.PlayerId == playerId);

    private static GameStatisticView[] FinalStatistics(AnimateState state)
    {
        var drawingTimes = state.DrawingMilliseconds ?? [];
        var drawingCounts = state.DrawingCounts ?? [];
        var completedDrawings = drawingCounts.Count == 0 ? 0 : drawingCounts.Values.Max();
        var fastestMilliseconds = state.Participants
            .Where(player => completedDrawings > 0 &&
                drawingCounts.GetValueOrDefault(player.PlayerId) == completedDrawings)
            .Select(player => drawingTimes.GetValueOrDefault(player.PlayerId) / completedDrawings)
            .DefaultIfEmpty(0).Min();
        var fastest = state.Participants
            .Where(player => completedDrawings > 0 &&
                drawingCounts.GetValueOrDefault(player.PlayerId) == completedDrawings &&
                drawingTimes.GetValueOrDefault(player.PlayerId) / completedDrawings == fastestMilliseconds)
            .Select(player => player.DisplayName).ToArray();

        var mostVotes = state.ShowdownResults.Count == 0
            ? 0
            : state.ShowdownResults.Max(result => result.Votes);
        var favourites = state.ShowdownResults
            .Where(result => result.Votes == mostVotes)
            .Select(result => RequiredParticipant(state, result.PlayerId).DisplayName).ToArray();

        var bluffPicks = state.BluffPicks ?? [];
        var mostBluffs = bluffPicks.Count == 0 ? 0 : bluffPicks.Values.Max();
        var bluffers = state.Participants
            .Where(player => mostBluffs > 0 && bluffPicks.GetValueOrDefault(player.PlayerId) == mostBluffs)
            .Select(player => player.DisplayName).ToArray();

        return
        [
            new GameStatisticView(
                "FASTEST ANIMATOR",
                $"{Names(fastest)} · {TimeSpan.FromMilliseconds(fastestMilliseconds).TotalSeconds:0.#}s average"),
            new GameStatisticView(
                "MOST LOVED ANIMATION",
                $"{Names(favourites)} · {mostVotes} vote{(mostVotes == 1 ? string.Empty : "s")}"),
            new GameStatisticView(
                "BEST BLUFFER",
                mostBluffs > 0
                    ? $"{Names(bluffers)} · fooled {mostBluffs} player{(mostBluffs == 1 ? string.Empty : "s")}"
                    : "Nobody fell for a bluff")
        ];
    }

    private static string Names(string[] names) => names.Length == 0
        ? "Nobody"
        : string.Join(" & ", names);

    private static int FrameCount(AnimateState state) =>
        state.RoundNumber == 1 ? RoundOneFrameCount : RoundTwoFrameCount;

    private static int EffectiveDrawingSecondsPerFrame(AnimateState state) =>
        state.DrawingSecondsPerFrame is >= MinimumDrawingSecondsPerFrame and <= MaximumDrawingSecondsPerFrame
            ? state.DrawingSecondsPerFrame
            : DefaultDrawingSecondsPerFrame;

    private static AnimateParticipant Animator(AnimateState state) => state.Participants[state.TurnIndex];
    private static string Prompt(AnimateState state) => PromptPair(state).DrawingPrompt;
    private static string PromptForPlayer(AnimateState state, Guid playerId)
    {
        return PromptPairForPlayer(state, playerId).DrawingPrompt;
    }

    private static DrawingPromptPair PromptPair(AnimateState state) =>
        PromptPairForPlayer(state, Animator(state).PlayerId);

    private static DrawingPromptPair PromptPairForPlayer(AnimateState state, Guid playerId)
    {
        if (state.RoundOnePrompts?.GetValueOrDefault(playerId) is { } assigned)
        {
            return assigned;
        }
        var index = state.Participants.ToList().FindIndex(player => player.PlayerId == playerId);
        if (index < 0)
        {
            throw new GameRuleViolationException("player-required", "A current player is required.");
        }
        return LegacyPrompts[index % LegacyPrompts.Length];
    }

    private static string ShowdownPrompt(AnimateState state) =>
        string.IsNullOrWhiteSpace(state.ShowdownDrawingPrompt)
            ? LegacyShowdownPrompt
            : state.ShowdownDrawingPrompt;

    private static AnimationSubmission? CurrentSubmission(AnimateState state) =>
        state.Submissions.GetValueOrDefault(Animator(state).PlayerId);

    private static int NextSubmittedIndex(AnimateState state, int afterIndex)
    {
        for (var index = afterIndex + 1; index < state.Participants.Count; index++)
        {
            if (state.Submissions.ContainsKey(state.Participants[index].PlayerId))
            {
                return index;
            }
        }
        return -1;
    }

    private static AnimateState MoveToNextSubmittedAnimation(AnimateState state, int afterIndex) =>
        state with { TurnIndex = NextSubmittedIndex(state, afterIndex) };

    private static AnimateParticipant RequiredParticipant(AnimateState state, Guid playerId) =>
        state.Participants.SingleOrDefault(player => player.PlayerId == playerId)
        ?? throw new GameRuleViolationException("player-required", "A current player is required.");

    private static Guid RequiredPlayer(AnimateState state, GameActionContext context)
    {
        if (!context.Actor.TryGetPlayerId(out var playerId))
        {
            throw new GameRuleViolationException("player-required", "A current player is required.");
        }
        RequiredParticipant(state, playerId);
        return playerId;
    }

    private static void RequirePhase(GameModuleState state, string phase, string message)
    {
        if (state.Phase != phase)
        {
            throw new GameRuleViolationException("wrong-phase", message);
        }
    }

    private static string NormalizeGuess(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new GameRuleViolationException("invalid-guess", "Enter a guess before submitting.");
        }
        var normalized = string.Join(' ', value.Trim().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length > MaximumGuessLength || normalized.Any(char.IsControl))
        {
            throw new GameRuleViolationException(
                "invalid-guess", $"Guesses must be at most {MaximumGuessLength} characters.");
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
        throw new GameRuleViolationException("invalid-guess", "A text guess is required.");
    }

    private static List<Guid> ReadFrameAssetIds(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("frameAssetIds", out var frames) || frames.ValueKind != JsonValueKind.Array ||
            frames.GetArrayLength() is < 1 or > MaximumFrameCount)
        {
            throw new GameRuleViolationException(
                "invalid-frames", $"One to {MaximumFrameCount} frame asset IDs are required.");
        }
        var result = new List<Guid>(frames.GetArrayLength());
        foreach (var frame in frames.EnumerateArray())
        {
            if (frame.ValueKind != JsonValueKind.String || !frame.TryGetGuid(out var id) || id == Guid.Empty)
            {
                throw new GameRuleViolationException("invalid-frames", "Every frame requires a valid asset ID.");
            }
            result.Add(id);
        }
        return result;
    }

    private static Guid ReadGuid(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            Guid.TryParse(value.GetString(), out var id) && id != Guid.Empty)
        {
            return id;
        }
        throw new GameRuleViolationException("invalid-choice", "A valid answer choice is required.");
    }

    private static void Shuffle(List<AnimationAnswerOption> options)
    {
        for (var index = options.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (options[index], options[swap]) = (options[swap], options[index]);
        }
    }

    private static string Letter(int index) => ((char)('A' + index)).ToString();

    private static PlayerGameViewPayload Waiting(string heading, string instructions) => new(
        heading, instructions,
        new PlayerControllerView(PlayerControllerKind.Waiting, string.Empty, false, string.Empty, GameJson.Empty),
        GameJson.Empty);

    private static AnimateState ReadState(GameModuleState state) => state.Data.Deserialize<AnimateState>()
        ?? throw new InvalidOperationException("AniMates state could not be read.");

    private static AniMatesGameConfiguration ReadConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
            configuration.ValueKind == JsonValueKind.Object && !configuration.EnumerateObject().Any())
        {
            return new AniMatesGameConfiguration();
        }
        AniMatesGameConfiguration? value;
        try
        {
            value = configuration.Deserialize<AniMatesGameConfiguration>();
        }
        catch (JsonException)
        {
            throw new GameRuleViolationException(
                "invalid-configuration", "AniMates game settings are invalid.");
        }
        if (value?.DrawingSecondsPerFrame is not
            (>= MinimumDrawingSecondsPerFrame and <= MaximumDrawingSecondsPerFrame))
        {
            throw new GameRuleViolationException(
                "invalid-configuration",
                $"Drawing time per frame must be between {MinimumDrawingSecondsPerFrame} and " +
                $"{MaximumDrawingSecondsPerFrame} seconds.");
        }
        return value;
    }

    private static GameModuleState ModuleState(
        string phase, DateTimeOffset? deadline, bool complete, AnimateState state) =>
        new(StateSchemaVersion, phase, deadline, complete, GameJson.From(state));

    private static DrawingPromptPair[] SelectPrompts(int count)
    {
        var catalogue = PromptCatalogue.Value;
        if (count > catalogue.Length)
        {
            throw new InvalidOperationException("The AniMates prompt catalogue is too small.");
        }
        var indexes = Enumerable.Range(0, catalogue.Length).ToArray();
        for (var index = 0; index < count; index++)
        {
            var swap = RandomNumberGenerator.GetInt32(index, indexes.Length);
            (indexes[index], indexes[swap]) = (indexes[swap], indexes[index]);
        }
        return indexes.Take(count).Select(index => catalogue[index]).ToArray();
    }

    private static DrawingPromptPair[] LoadPromptCatalogue()
    {
        using var stream = typeof(AniMatesGameModule).Assembly
            .GetManifestResourceStream(PromptResourceName)
            ?? throw new InvalidOperationException("The AniMates prompt catalogue is missing.");
        var prompts = JsonSerializer.Deserialize<DrawingPromptPair[]>(stream, PromptJsonOptions)
            ?? throw new InvalidOperationException("The AniMates prompt catalogue is invalid.");
        if (prompts.Length != ExpectedPromptCount || prompts.Any(prompt =>
                string.IsNullOrWhiteSpace(prompt.DrawingPrompt) ||
                string.IsNullOrWhiteSpace(prompt.Distractor) ||
                prompt.DrawingPrompt.Length > MaximumGuessLength ||
                prompt.Distractor.Length > MaximumGuessLength ||
                prompt.DrawingPrompt.Any(char.IsControl) ||
                prompt.Distractor.Any(char.IsControl)))
        {
            throw new InvalidOperationException("The AniMates prompt catalogue failed validation.");
        }
        return prompts;
    }

    private static readonly Lazy<DrawingPromptPair[]> PromptCatalogue = new(LoadPromptCatalogue);
    private static readonly JsonSerializerOptions PromptJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly DrawingPromptPair[] LegacyPrompts =
    [
        new("Spanking a blue dog", "Polishing a bowling ball with cheese"),
        new("Escaping from a giant sandwich", "Octopus directing airport traffic"),
        new("Trying to open an umbrella indoors", "Kissing a burnt doughnut"),
        new("A penguin learning to skateboard", "Robot skating on two bananas"),
        new("Fighting with a stubborn deckchair", "A driving test at a car wash"),
        new("Celebrating after finding the TV remote", "Vicar replacing their hands with rubber chickens")
    ];

    private const string LegacyShowdownPrompt = "A grandma escaping from prison";
    private const string RoundOneBriefing =
        "Everyone gets a different secret prompt. Make a three-frame animation, then fool your friends with guesses.";
    private const string RoundTwoBriefing =
        "Same Prompt Showdown! Everyone animates the same prompt in five frames. Watch them all, then vote for your favourite.";

    private sealed record AnimateState(
        int RoundNumber,
        int DrawingSecondsPerFrame,
        int TurnIndex,
        IReadOnlyList<AnimateParticipant> Participants,
        Dictionary<Guid, AnimationSubmission> Submissions,
        Dictionary<Guid, string> Guesses,
        IReadOnlyList<AnimationAnswerOption> Options,
        IReadOnlyDictionary<Guid, Guid> Choices,
        IReadOnlyList<AnimationAward> Awards,
        IReadOnlyDictionary<Guid, Guid> ShowdownVotes,
        List<ShowdownResult> ShowdownResults,
        Dictionary<Guid, DrawingPromptPair>? RoundOnePrompts = null,
        string? ShowdownDrawingPrompt = null,
        Dictionary<Guid, long>? DrawingMilliseconds = null,
        Dictionary<Guid, int>? BluffPicks = null,
        Dictionary<Guid, int>? DrawingCounts = null);

    private sealed record AnimateParticipant(Guid PlayerId, string DisplayName);
    private sealed record AnimationSubmission(Guid PlayerId, IReadOnlyList<Guid> FrameAssetIds);
    private sealed record AnimationAnswerOption(
        Guid OptionId,
        string Text,
        bool IsCorrect,
        Guid? AuthorPlayerId,
        bool IsDistractor = false);
    private sealed record DrawingPromptPair(string DrawingPrompt, string Distractor);
    private sealed record AnimationAward(Guid PlayerId, int Points);
    private sealed record ShowdownResult(Guid PlayerId, int Votes, int Rank, int Points);
}
