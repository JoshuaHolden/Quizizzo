using System.Security.Cryptography;
using System.Text.Json;
using Quizizzo.GameContracts;

namespace Quizizzo.Games.AniMates;

public sealed class AniMatesGameModule(
    TimeSpan? drawingDuration = null,
    TimeSpan? guessingDuration = null,
    TimeSpan? choosingDuration = null) : IGameModule
{
    public const string GameKey = "animates";
    public const string DrawingPhase = "Drawing";
    public const string GuessingPhase = "Guessing";
    public const string ChoosingPhase = "Choosing";
    public const string ResultsPhase = "Results";
    public const string CompletedPhase = "Completed";
    public const int RequiredFrameCount = 3;
    public const int LogicalSize = 512;
    public const int MaximumSubmissionPayloadBytes = 6 * 1024 * 1024;
    public const int MaximumGuessLength = 200;
    public const int GuessChosenPoints = 100;
    public const int CorrectChoicePoints = 50;
    public const int AnimatorCorrectChoicePoints = 100;

    private readonly TimeSpan drawingDuration = drawingDuration ?? TimeSpan.FromSeconds(90);
    private readonly TimeSpan guessingDuration = guessingDuration ?? TimeSpan.FromSeconds(45);
    private readonly TimeSpan choosingDuration = choosingDuration ?? TimeSpan.FromSeconds(30);

    public GameDescriptor Descriptor { get; } = new(GameKey, "AniMates", 2, 12);

    public GameModuleState Start(GameStartContext context)
    {
        var participants = context.Participants
            .Select(player => new AnimateParticipant(player.PlayerId, player.DisplayName)).ToArray();
        return ModuleState(DrawingPhase, context.StartedAtUtc.Add(drawingDuration), false,
            new AnimateState(0, participants, new Dictionary<Guid, AnimationSubmission>(),
                new Dictionary<Guid, string>(), [], new Dictionary<Guid, Guid>(), []));
    }

    public GameTransition Apply(GameModuleState state, GameActionContext context, IGameAction action)
    {
        var animate = ReadState(state);
        return action switch
        {
            SubmitAnimationAction submission => SubmitAnimation(state, animate, context, submission),
            SubmitAnimationGuessAction guess => SubmitGuess(state, animate, context, guess),
            ChooseAnimationAnswerAction choice => Choose(state, animate, context, choice),
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
        if (action.FrameAssetIds.Count is < 1 or > RequiredFrameCount ||
            action.FrameAssetIds.Any(id => id == Guid.Empty))
        {
            throw new GameRuleViolationException("invalid-frames", "Submit between one and three valid frames.");
        }

        var frames = action.FrameAssetIds.ToList();
        while (frames.Count < RequiredFrameCount)
        {
            frames.Add(frames[^1]);
        }
        var submissions = state.Submissions.ToDictionary();
        submissions.Add(playerId, new AnimationSubmission(playerId, frames));
        var updated = state with { Submissions = submissions };
        return submissions.Count == state.Participants.Count
            ? BeginGuessing(updated, context.ReceivedAtUtc)
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

    private static GameTransition Choose(
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
            ? Reveal(updated)
            : new GameTransition(current with { Data = GameJson.From(updated) }, [],
                [new GameEvent("AnimationAnswerChosen", GameJson.From(new { playerId }))]);
    }

    private GameTransition Deadline(
        GameModuleState current,
        AnimateState state,
        GameActionContext context) => current.Phase switch
        {
            DrawingPhase => state.Submissions.Count == 0
                ? CompleteWithoutAnimations(state)
                : BeginGuessing(MoveToNextSubmittedAnimation(state, -1), context.ReceivedAtUtc),
            GuessingPhase => BeginChoosing(state, context.ReceivedAtUtc),
            ChoosingPhase => Reveal(state),
            _ => throw new GameRuleViolationException("wrong-phase", "This phase has no active deadline.")
        };

    private GameTransition BeginGuessing(AnimateState state, DateTimeOffset now) => new(
        ModuleState(GuessingPhase, now.Add(guessingDuration), false, state), [],
        [new GameEvent("AnimationGuessingStarted", GameJson.Empty)]);

    private static GameTransition CompleteWithoutAnimations(AnimateState state) => new(
        ModuleState(CompletedPhase, null, true, state), [],
        [new GameEvent("GameCompleted", GameJson.Empty)]);

    private GameTransition BeginChoosing(AnimateState state, DateTimeOffset now)
    {
        var options = new List<AnimationAnswerOption>
        {
            new(Guid.NewGuid(), Prompt(state), true, null)
        };
        options.AddRange(state.Guesses.Select(guess =>
            new AnimationAnswerOption(Guid.NewGuid(), guess.Value, false, guess.Key)));
        Shuffle(options);
        var choosing = state with { Options = options };
        return options.Count < 2 || EligibleChoosers(choosing).Length == 0
            ? Reveal(choosing)
            : new GameTransition(ModuleState(ChoosingPhase, now.Add(choosingDuration), false, choosing), [],
                [new GameEvent("AnimationAnswersOpened", GameJson.From(new { answers = options.Count }))]);
    }

    private static GameTransition Reveal(AnimateState state)
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
        var awards = points.Where(item => item.Value > 0)
            .Select(item => new AnimationAward(item.Key, item.Value))
            .OrderByDescending(item => item.Points).ThenBy(item => item.PlayerId).ToArray();
        var revealed = state with { Awards = awards };
        return new GameTransition(
            ModuleState(ResultsPhase, null, false, revealed),
            awards.Select(award => new ScoreAward(
                award.PlayerId, award.Points, $"AniMates turn {state.TurnIndex + 1}")).ToArray(),
            [new GameEvent("AnimationAnswerRevealed", GameJson.Empty)]);
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
        RequirePhase(current, ResultsPhase, "Results must be revealed first.");
        var nextIndex = NextSubmittedIndex(state, state.TurnIndex);
        if (nextIndex < 0)
        {
            return new GameTransition(ModuleState(CompletedPhase, null, true, state), [],
                [new GameEvent("GameCompleted", GameJson.Empty)]);
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
            ModuleState(GuessingPhase, context.ReceivedAtUtc.Add(guessingDuration), false, next), [],
            [new GameEvent("AniMatesTurnStarted", GameJson.From(new { turn = next.TurnIndex + 1 }))]);
    }

    private static PlayerGameViewPayload PlayerView(
        GameModuleState current,
        AnimateState state,
        GameViewContext context)
    {
        var playerId = context.PlayerId
            ?? throw new GameRuleViolationException("player-required", "A player identity is required.");
        RequiredParticipant(state, playerId);
        var animator = Animator(state);

        if (current.Phase == DrawingPhase && !state.Submissions.ContainsKey(playerId))
        {
            return new PlayerGameViewPayload(
                "Create your animation", PromptForPlayer(state, playerId),
                new PlayerControllerView(PlayerControllerKind.Drawing, SubmitAnimationAction.ActionKind, true,
                    "Send my animation", GameJson.From(new DrawingControllerConfiguration(
                        LogicalSize, LogicalSize, RequiredFrameCount, "animates-drawing", true))),
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
            _ => "AniMates complete."
        };
        return Waiting(current.Phase == ResultsPhase ? "Answer revealed" : "Please wait", instructions);
    }

    private static HostGameViewPayload HostView(GameModuleState current, AnimateState state) => new(
        current.Phase == DrawingPhase
            ? "AniMates — Drawing"
            : $"AniMates — Reveal {state.TurnIndex + 1}/{state.Submissions.Count}",
        current.Phase == ResultsPhase ? Prompt(state) : current.Phase == DrawingPhase
            ? "Everyone is animating at once"
            : $"{Animator(state).DisplayName}'s animation",
        PhaseMessage(current, state), SubmittedCount(current, state), RequiredCount(current, state),
        current.Phase == ResultsPhase,
        current.Phase == ResultsPhase ? AdvanceAniMatesAction.ActionKind : null,
        current.Phase == ResultsPhase
            ? NextSubmittedIndex(state, state.TurnIndex) < 0 ? "Finish AniMates" : "Next animation"
            : null,
        Entries(current, state));

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
        return new DisplayGameViewPayload(
            current.Phase == DrawingPhase
                ? "ANIMATES — EVERYONE DRAW!"
                : $"ANIMATES — {Animator(state).DisplayName.ToUpperInvariant()}'S ANIMATION",
            current.Phase == ResultsPhase ? Prompt(state) : DisplayPrompt(current),
            PhaseMessage(current, state), SubmittedCount(current, state), RequiredCount(current, state),
            Entries(current, state), drawing);
    }

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
                var label = option.IsCorrect ? $"{Letter(index)} — CORRECT ANSWER" : $"{Letter(index)} — {author}";
                var points = option.IsCorrect
                    ? picks * (CorrectChoicePoints + AnimatorCorrectChoicePoints)
                    : picks * GuessChosenPoints;
                return new GamePresentationEntry(
                    option.OptionId, label, $"{option.Text} — {picks} pick(s)", null, points);
            }).ToArray();
        }
        return [];
    }

    private static string DisplayPrompt(GameModuleState current) => current.Phase switch
    {
        DrawingPhase => "An absurd animation is being created...",
        GuessingPhase => "What do you think this is?",
        ChoosingPhase => "Choose the best-fitting answer",
        _ => "The answer is..."
    };

    private static string PhaseMessage(GameModuleState current, AnimateState state) => current.Phase switch
    {
        DrawingPhase => $"{state.Submissions.Count}/{state.Participants.Count} animations ready",
        GuessingPhase => $"{state.Guesses.Count}/{Guessers(state).Length} guesses locked in",
        ChoosingPhase => $"{state.Choices.Count}/{EligibleChoosers(state).Length} choices locked in",
        ResultsPhase => "Correct answer and writers revealed!",
        _ => "AniMates complete"
    };

    private static int SubmittedCount(GameModuleState current, AnimateState state) => current.Phase switch
    {
        DrawingPhase => state.Submissions.Count,
        GuessingPhase => state.Guesses.Count,
        _ => state.Choices.Count
    };

    private static int RequiredCount(GameModuleState current, AnimateState state) => current.Phase switch
    {
        DrawingPhase => state.Participants.Count,
        GuessingPhase => Guessers(state).Length,
        _ => EligibleChoosers(state).Length
    };

    private static Guid[] Guessers(AnimateState state) => state.Participants
        .Where(player => player.PlayerId != Animator(state).PlayerId)
        .Select(player => player.PlayerId).ToArray();

    private static Guid[] EligibleChoosers(AnimateState state) => Guessers(state)
        .Where(id => state.Options.Any(option => option.AuthorPlayerId != id)).ToArray();

    private static AnimateParticipant Animator(AnimateState state) => state.Participants[state.TurnIndex];
    private static string Prompt(AnimateState state) => Prompts[state.TurnIndex % Prompts.Length];
    private static string PromptForPlayer(AnimateState state, Guid playerId)
    {
        var index = state.Participants.ToList().FindIndex(player => player.PlayerId == playerId);
        return Prompts[index % Prompts.Length];
    }

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
            frames.GetArrayLength() is < 1 or > RequiredFrameCount)
        {
            throw new GameRuleViolationException("invalid-frames", "One to three frame asset IDs are required.");
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
            value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id) && id != Guid.Empty)
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

    private static GameModuleState ModuleState(
        string phase, DateTimeOffset? deadline, bool complete, AnimateState state) =>
        new(2, phase, deadline, complete, GameJson.From(state));

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
        int TurnIndex,
        IReadOnlyList<AnimateParticipant> Participants,
        Dictionary<Guid, AnimationSubmission> Submissions,
        Dictionary<Guid, string> Guesses,
        IReadOnlyList<AnimationAnswerOption> Options,
        IReadOnlyDictionary<Guid, Guid> Choices,
        IReadOnlyList<AnimationAward> Awards);

    private sealed record AnimateParticipant(Guid PlayerId, string DisplayName);
    private sealed record AnimationSubmission(Guid PlayerId, IReadOnlyList<Guid> FrameAssetIds);
    private sealed record AnimationAnswerOption(Guid OptionId, string Text, bool IsCorrect, Guid? AuthorPlayerId);
    private sealed record AnimationAward(Guid PlayerId, int Points);
}
