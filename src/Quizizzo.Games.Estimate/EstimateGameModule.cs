using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quizizzo.GameContracts;

namespace Quizizzo.Games.Estimate;

public sealed class EstimateGameModule(
    TimeSpan? answerDuration = null,
    TimeSpan? resultsDuration = null,
    IReadOnlyList<EstimateGameModule.EstimateQuestion>? questionOverride = null) : IGameModule
{
    public const string GameKey = "estimate";
    public const string AnsweringPhase = "Answering";
    public const string ResultsPhase = "Results";
    public const string CompletedPhase = "Completed";

    private readonly TimeSpan answerDuration = answerDuration ?? TimeSpan.FromSeconds(30);
    private readonly TimeSpan resultsDuration = resultsDuration ?? TimeSpan.FromSeconds(10);
    private readonly IReadOnlyList<EstimateQuestion>? questionOverride = questionOverride;

    public GameDescriptor Descriptor { get; } = new(
        GameKey,
        "Estimate",
        2,
        12,
        "Make your best numerical guess. Closest answers score while wild confidence gets exposed.",
        "Numbers · 3 rounds");

    public GameModuleState Start(GameStartContext context)
    {
        var questions = questionOverride ?? PickQuestions(context.GameInstanceId.Value, 3);
        var state = new EstimateState(
            0,
            questions,
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
            DeadlineElapsedAction => Deadline(state, estimate, context),
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

    private GameTransition Submit(
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
            return Reveal(current, updated, context.ReceivedAtUtc);
        }

        return new GameTransition(
            current with { Data = GameJson.From(updated) },
            [],
            [new GameEvent("EstimateSubmitted", GameJson.From(new { playerId }))]);
    }

    private GameTransition Deadline(
        GameModuleState current,
        EstimateState state,
        GameActionContext context) => current.Phase switch
        {
            AnsweringPhase => Reveal(current, state, context.ReceivedAtUtc),
            ResultsPhase => Progress(state, context.ReceivedAtUtc),
            _ => throw new GameRuleViolationException("wrong-phase", "This Estimate phase has no deadline.")
        };

    private GameTransition Reveal(
        GameModuleState current,
        EstimateState state,
        DateTimeOffset now)
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
            CreateModuleState(ResultsPhase, now.Add(resultsDuration), false, revealed),
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

        return Progress(state, context.ReceivedAtUtc);
    }

    private GameTransition Progress(EstimateState state, DateTimeOffset now)
    {
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
                now.Add(answerDuration),
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
        current.Phase == ResultsPhase ? "Continue now" : null,
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


    private const string QuestionResourceName =
        "Quizizzo.Games.Estimate.Assets.estimate-questions.json";

    // Exposed so tests can inject a deterministic fixed set via the constructor override.
    public static readonly EstimateQuestion[] LegacyThreeQuestions =
    [
        new("How many minutes are in one week?", 10_080, 0, 50_000, "minutes"),
        new("How many keys are on a standard piano?", 88, 0, 500, "keys"),
        new("About how many kilometres of blood vessels are in the human body?", 100_000, 0, 1_000_000, "km")
    ];

    private static List<EstimateQuestion> PickQuestions(Guid seed, int count)
    {
        var catalogue = QuestionCatalogue.Value;
        var indices = Enumerable.Range(0, catalogue.Length).ToArray();
        for (var i = 0; i < count; i++)
        {
            var swap = RandomNumberGenerator.GetInt32(i, indices.Length);
            (indices[i], indices[swap]) = (indices[swap], indices[i]);
        }
        return indices.Take(count).Select(index => catalogue[index]).ToList();
    }

    private static EstimateQuestion[] LoadQuestionCatalogue()
    {
        using var stream = typeof(EstimateGameModule).Assembly
            .GetManifestResourceStream(QuestionResourceName)
            ?? throw new InvalidOperationException("The Estimate question catalogue is missing.");
        var questions = JsonSerializer.Deserialize<EstimateQuestion[]>(stream, CatalogueJsonOptions)
            ?? throw new InvalidOperationException("The Estimate question catalogue is invalid.");
        if (questions.Length == 0 || questions.Any(q =>
                string.IsNullOrWhiteSpace(q.Prompt) ||
                q.Maximum <= q.Minimum ||
                q.Prompt.Any(char.IsControl)))
        {
            throw new InvalidOperationException("The Estimate question catalogue failed validation.");
        }
        return questions;
    }

    private static readonly Lazy<EstimateQuestion[]> QuestionCatalogue = new(LoadQuestionCatalogue);
    private static readonly JsonSerializerOptions CatalogueJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };


    private static List<EstimateQuestion> PickQuestions(Guid seed, int count)
    {
        var rng = new Random(seed.GetHashCode());
        var pool = AllQuestions;
        var picked = new List<EstimateQuestion>(count);
        var indices = Enumerable.Range(0, pool.Length).ToList();
        for (var i = 0; i < count && indices.Count > 0; i++)
        {
            var pos = rng.Next(indices.Count);
            picked.Add(pool[indices[pos]]);
            indices.RemoveAt(pos);
        }
        return picked;
    }

    // Exposed so tests can inject a deterministic fixed set via the constructor override.
    public static readonly EstimateQuestion[] LegacyThreeQuestions =
    [
        new("How many minutes are in one week?", 10_080, 0, 50_000, "minutes"),
        new("How many keys are on a standard piano?", 88, 0, 500, "keys"),
        new("About how many kilometres of blood vessels are in the human body?", 100_000, 0, 1_000_000, "km")
    ];

    private static readonly EstimateQuestion[] AllQuestions =
    [
        // Time & calendars
        new("How many minutes are in one week?", 10_080, 0, 50_000, "minutes"),
        new("How many seconds are in one day?", 86_400, 0, 500_000, "seconds"),
        new("How many hours are in one year (non-leap)?", 8_760, 0, 50_000, "hours"),
        new("How many days are in 100 years (ignoring leap years)?", 36_500, 0, 100_000, "days"),
        new("How many weeks are in a decade?", 521, 0, 2_000, "weeks"),
        new("How many minutes are in one calendar year?", 525_600, 0, 2_000_000, "minutes"),
        new("How many seconds are in one week?", 604_800, 0, 2_000_000, "seconds"),
        new("How many months are in 25 years?", 300, 0, 1_000, "months"),
        new("How many days are in 5 years (ignoring leap years)?", 1_825, 0, 5_000, "days"),
        new("How many hours are in a leap year?", 8_784, 0, 50_000, "hours"),

        // Human body
        new("About how many bones does an adult human have?", 206, 0, 1_000, "bones"),
        new("About how many muscles does the human body have?", 600, 0, 2_000, "muscles"),
        new("About how many kilometres of blood vessels are in the human body?", 100_000, 0, 1_000_000, "km"),
        new("About how many hairs are on a typical human head?", 100_000, 0, 500_000, "hairs"),
        new("How many teeth does an adult human typically have?", 32, 0, 100, "teeth"),
        new("About how many cells are in the human body (in trillions)?", 37, 0, 200, "trillion cells"),
        new("How many times does the average heart beat per minute at rest?", 72, 0, 300, "bpm"),
        new("About how many breaths does a person take per day?", 20_000, 0, 100_000, "breaths"),
        new("How long is the human small intestine in centimetres?", 600, 0, 2_000, "cm"),
        new("How many litres of blood does an average adult human body contain?", 5, 0, 20, "litres"),
        new("How many chromosomes does a typical human cell have?", 46, 0, 200, "chromosomes"),
        new("How many bones are in the human foot?", 26, 0, 100, "bones"),
        new("About how fast do nerve signals travel in km/h?", 430, 0, 2_000, "km/h"),
        new("How many taste buds does the average human tongue have?", 10_000, 0, 50_000, "taste buds"),
        new("How many times per day does the average person blink?", 28_800, 0, 100_000, "times"),

        // Animals
        new("How many legs does a centipede typically have?", 42, 0, 200, "legs"),
        new("How many eyes does a spider typically have?", 8, 0, 20, "eyes"),
        new("About how many species of fish exist?", 35_000, 0, 100_000, "species"),
        new("How many chambers does a human heart have?", 4, 0, 20, "chambers"),
        new("About how many species of birds exist?", 10_000, 0, 50_000, "species"),
        new("How many teeth does an adult great white shark have in its mouth at once?", 300, 0, 1_000, "teeth"),
        new("About how long can a Galapagos tortoise live (in years)?", 150, 0, 300, "years"),
        new("About how many eggs does a queen bee lay per day?", 2_000, 0, 10_000, "eggs"),
        new("How fast can a cheetah run in km/h?", 112, 0, 300, "km/h"),
        new("How many arms does an octopus have?", 8, 0, 20, "arms"),
        new("About how many species of insects exist?", 1_000_000, 0, 5_000_000, "species"),
        new("How many metres can a flea jump (vertically)?", 20, 0, 100, "cm"),
        new("How long is the gestation period of an African elephant in days?", 660, 0, 2_000, "days"),
        new("How many feathers does a typical bald eagle have?", 7_000, 0, 30_000, "feathers"),
        new("How fast can a peregrine falcon dive in km/h?", 389, 0, 600, "km/h"),

        // Geography
        new("How many countries are in the world?", 195, 0, 300, "countries"),
        new("What is the length of the Great Wall of China in kilometres?", 21_196, 0, 100_000, "km"),
        new("What is the area of Russia in thousands of square kilometres?", 17_098, 0, 50_000, "thousand km²"),
        new("How deep is the Mariana Trench in metres?", 11_034, 0, 20_000, "m"),
        new("How tall is Mount Everest in metres?", 8_849, 0, 15_000, "m"),
        new("What is the length of the Amazon River in kilometres?", 6_992, 0, 15_000, "km"),
        new("What is the area of the Sahara Desert in thousands of square kilometres?", 9_200, 0, 30_000, "thousand km²"),
        new("How long is the Nile River in kilometres?", 6_650, 0, 15_000, "km"),
        new("How many islands make up Indonesia?", 17_508, 0, 30_000, "islands"),
        new("What is the population of Iceland (in thousands)?", 370, 0, 5_000, "thousands"),
        new("What is the elevation of the Dead Sea (metres below sea level)?", 430, 0, 1_000, "m below sea level"),
        new("How many time zones does Russia span?", 11, 0, 30, "time zones"),
        new("What is the area of Australia in thousands of square kilometres?", 7_692, 0, 20_000, "thousand km²"),
        new("What is the length of the Yangtze River in kilometres?", 6_300, 0, 12_000, "km"),
        new("How many active volcanoes exist on Earth?", 1_500, 0, 5_000, "volcanoes"),

        // Space & astronomy
        new("How many kilometres from Earth to the Moon (approximate)?", 384_400, 0, 2_000_000, "km"),
        new("How many kilometres from Earth to the Sun (approximate)?", 150_000_000, 0, 1_000_000_000, "km"),
        new("How many planets are in our solar system?", 8, 0, 20, "planets"),
        new("About how old is the universe in billions of years?", 14, 0, 50, "billion years"),
        new("How long does it take light to travel from the Sun to Earth in minutes?", 8, 0, 30, "minutes"),
        new("About how many stars are in the Milky Way (in billions)?", 200, 0, 1_000, "billion stars"),
        new("How many known moons does Jupiter have?", 95, 0, 200, "moons"),
        new("What is the diameter of the Sun in kilometres (thousands)?", 1_392, 0, 5_000, "thousand km"),
        new("How long does it take Saturn to orbit the Sun in Earth years?", 29, 0, 100, "years"),
        new("About how many kilometres per second does light travel?", 300_000, 0, 1_000_000, "km/s"),
        new("How many known moons does Saturn have?", 146, 0, 300, "moons"),
        new("What is the surface temperature of the Sun in Celsius (thousands)?", 5_500, 0, 20_000, "°C"),
        new("How long is one Martian day in Earth hours (approximate)?", 25, 0, 100, "hours"),
        new("How many light years away is the nearest star (Proxima Centauri, rounded)?", 4, 0, 50, "light years"),
        new("In what year did humans first land on the Moon?", 1_969, 1_900, 2_030, ""),

        // Science & physics
        new("What is the boiling point of water in Fahrenheit?", 212, 0, 500, "°F"),
        new("What is absolute zero in Celsius?", -273, -500, 0, "°C"),
        new("How many elements are on the periodic table?", 118, 0, 300, "elements"),
        new("How many protons does a gold atom have?", 79, 0, 200, "protons"),
        new("What is the speed of sound in air at sea level in m/s?", 343, 0, 1_000, "m/s"),
        new("How many watts does a standard LED light bulb use?", 10, 0, 200, "watts"),
        new("What is the atomic number of carbon?", 6, 0, 50, ""),
        new("How many decibels is a normal conversation?", 60, 0, 200, "dB"),
        new("What percentage of Earth's atmosphere is nitrogen?", 78, 0, 100, "%"),
        new("How long does it take for a plastic bottle to decompose in years?", 450, 0, 2_000, "years"),
        new("What is the melting point of iron in Celsius?", 1_538, 0, 5_000, "°C"),
        new("About how many Hz is the lowest note a human can hear?", 20, 0, 200, "Hz"),
        new("How many volts does a standard AA battery produce?", 2, 0, 20, "volts"),
        new("What is the half-life of Carbon-14 in years?", 5_730, 0, 20_000, "years"),
        new("What percentage of Earth's surface is covered by water?", 71, 0, 100, "%"),

        // Food & drink
        new("About how many calories are in a Big Mac?", 550, 0, 2_000, "kcal"),
        new("About how many litres of water does the average person drink per day?", 2, 0, 20, "litres"),
        new("About how many cups of coffee are consumed globally per day (in millions)?", 2_250, 0, 10_000, "million cups"),
        new("How many calories are in one gram of fat?", 9, 0, 50, "kcal"),
        new("About how many grams of sugar are in a standard can of Coca-Cola?", 39, 0, 200, "grams"),
        new("About how many years can honey stay edible (with proper storage)?", 3_000, 0, 10_000, "years"),
        new("About how many Scoville units does a jalapeño pepper measure?", 8_000, 0, 100_000, "SHU"),
        new("How many grams of protein are in one large egg?", 6, 0, 50, "grams"),
        new("About how many milligrams of caffeine does a standard espresso shot contain?", 63, 0, 300, "mg"),
        new("How many calories are in one tablespoon of olive oil?", 120, 0, 500, "kcal"),
        new("About how many grapes does it take to make one bottle of wine?", 700, 0, 3_000, "grapes"),
        new("What percentage of a cucumber is water?", 96, 0, 100, "%"),
        new("How many seeds does an average strawberry have?", 200, 0, 1_000, "seeds"),
        new("About how many litres of milk does a cow produce per day?", 28, 0, 100, "litres"),
        new("About how many kilograms of chocolate does the average Swiss person eat per year?", 11, 0, 50, "kg"),

        // History
        new("In what year was the Eiffel Tower completed?", 1_889, 1_800, 2_000, ""),
        new("In what year did World War I begin?", 1_914, 1_850, 1_980, ""),
        new("In what year did the Berlin Wall fall?", 1_989, 1_940, 2_010, ""),
        new("About how many people died in World War II?", 70_000_000, 0, 200_000_000, "people"),
        new("In what year was the printing press invented by Gutenberg?", 1_440, 1_000, 1_800, ""),
        new("About how many years ago did the Roman Empire fall?", 1_550, 0, 3_000, "years ago"),
        new("In what year did the Titanic sink?", 1_912, 1_850, 1_960, ""),
        new("About how many years ago was the Great Pyramid of Giza built?", 4_500, 0, 10_000, "years ago"),
        new("In what year did Neil Armstrong walk on the Moon?", 1_969, 1_940, 2_000, ""),
        new("About how long did the Hundred Years War actually last (in years)?", 116, 0, 500, "years"),
        new("In what year did the French Revolution begin?", 1_789, 1_700, 1_850, ""),
        new("About how many people were killed by the Black Death in Europe (in millions)?", 25, 0, 100, "million"),
        new("In what year was the United States Declaration of Independence signed?", 1_776, 1_700, 1_850, ""),
        new("About how many years did the Byzantine Empire last?", 1_123, 0, 2_000, "years"),
        new("In what year was the first iPhone released?", 2_007, 1_990, 2_030, ""),

        // Sport
        new("How many players are on a standard football (soccer) team?", 11, 0, 30, "players"),
        new("How long is an Olympic swimming pool in metres?", 50, 0, 200, "m"),
        new("How many points is a touchdown worth in American football?", 6, 0, 20, "points"),
        new("In what year were the first modern Olympic Games held?", 1_896, 1_850, 1_950, ""),
        new("How high is the net in the middle of a tennis court in centimetres?", 91, 0, 300, "cm"),
        new("How many holes are on a standard golf course?", 18, 0, 50, "holes"),
        new("What is the diameter of a basketball hoop in centimetres?", 46, 0, 200, "cm"),
        new("How many dimples does a standard golf ball have?", 336, 0, 1_000, "dimples"),
        new("How long is a marathon in kilometres?", 42, 0, 200, "km"),
        new("How many players are on a volleyball team on the court at once?", 6, 0, 20, "players"),
        new("In what year did women first compete in the Olympic Games?", 1_900, 1_850, 1_980, ""),
        new("How many times has Brazil won the FIFA World Cup?", 5, 0, 20, "times"),
        new("What is the maximum score in tenpin bowling?", 300, 0, 1_000, "points"),
        new("How many players are on a rugby union team?", 15, 0, 30, "players"),
        new("How long is an NBA basketball court in metres?", 29, 0, 100, "m"),

        // Music
        new("How many keys are on a standard piano?", 88, 0, 500, "keys"),
        new("How many strings does a standard violin have?", 4, 0, 20, "strings"),
        new("How many frets does a standard guitar have?", 22, 0, 50, "frets"),
        new("About how many songs does the average person know by heart?", 200, 0, 2_000, "songs"),
        new("How many musicians are in a typical symphony orchestra?", 80, 0, 300, "musicians"),
        new("About how many albums has The Beatles released?", 13, 0, 50, "studio albums"),
        new("In what year was Beethoven born?", 1_770, 1_700, 1_850, ""),
        new("About how many notes per second can a skilled pianist play?", 14, 0, 100, "notes/s"),
        new("How many keys are on a standard organ manual?", 61, 0, 200, "keys"),
        new("About how many songs has Michael Jackson sold worldwide (in millions)?", 400, 0, 2_000, "million"),

        // Technology & internet
        new("About how many websites are on the internet (in millions)?", 2_000, 0, 10_000, "million"),
        new("In what year was the World Wide Web invented?", 1_989, 1_950, 2_010, ""),
        new("About how many emails are sent per day worldwide (in billions)?", 360, 0, 2_000, "billion"),
        new("About how many active users does Facebook have (in billions)?", 3, 0, 10, "billion"),
        new("In what year was the first text message sent?", 1_992, 1_970, 2_010, ""),
        new("About how many transistors are on a modern CPU (in billions)?", 50, 0, 300, "billion"),
        new("How many bytes are in one gigabyte (in millions)?", 1_024, 0, 5_000, "million bytes"),
        new("In what year was the first commercial smartphone released?", 1_994, 1_970, 2_010, ""),
        new("About how many Google searches are made per day (in billions)?", 9, 0, 50, "billion"),
        new("About how many people in the world own a smartphone (in billions)?", 7, 0, 20, "billion"),

        // Transport & vehicles
        new("About how many kilometres can a commercial airplane fly on one tank of fuel?", 15_000, 0, 50_000, "km"),
        new("How fast does the fastest commercial train travel in km/h?", 603, 0, 2_000, "km/h"),
        new("About how many cars are in the world (in billions)?", 2, 0, 10, "billion"),
        new("How many litres of fuel does a Boeing 747 burn per hour?", 12_000, 0, 50_000, "litres"),
        new("About how many kilometres does the average car travel in a year?", 15_000, 0, 100_000, "km"),
        new("How many wheels does a standard 18-wheeler truck have?", 18, 0, 50, "wheels"),
        new("About how fast does the International Space Station travel in km/h?", 28_000, 0, 100_000, "km/h"),
        new("How long is the Channel Tunnel in kilometres?", 50, 0, 200, "km"),
        new("About how many passengers does a standard double-decker bus hold?", 87, 0, 300, "passengers"),
        new("How fast can a Formula 1 car travel in km/h (top speed)?", 370, 0, 600, "km/h"),

        // Money & economics
        new("About how many US dollars is the world's GDP (in trillions)?", 100, 0, 500, "trillion USD"),
        new("About how many tonnes of gold has humanity ever mined?", 190_000, 0, 1_000_000, "tonnes"),
        new("In what year did the euro banknotes first enter circulation?", 2_002, 1_980, 2_030, ""),
        new("About how many billionaires are there in the world?", 3_000, 0, 10_000, "billionaires"),
        new("About how many credit cards are in circulation worldwide (in billions)?", 3, 0, 20, "billion"),
        new("About how many US dollars does Apple's market cap represent (in trillions)?", 3, 0, 20, "trillion USD"),
        new("About how many tonnes of gold are held in US Federal Reserve vaults?", 6_000, 0, 50_000, "tonnes"),
        new("About what percentage of the world's population lives on less than $2.15/day?", 9, 0, 50, "%"),
        new("How many countries use the US dollar as their official currency?", 11, 0, 50, "countries"),
        new("About how many tonnes of physical currency notes are printed globally each year?", 10_000, 0, 100_000, "tonnes"),

        // Literature & art
        new("How many plays did William Shakespeare write?", 37, 0, 100, "plays"),
        new("How many pages does the King James Bible have (approximately)?", 1_200, 0, 5_000, "pages"),
        new("About how many books are published worldwide per year (in millions)?", 4, 0, 20, "million"),
        new("About how many words are in the complete Harry Potter series?", 1_084_170, 0, 5_000_000, "words"),
        new("In what year was Leonardo da Vinci born?", 1_452, 1_300, 1_600, ""),
        new("About how many artworks does the Louvre have in its collection?", 550_000, 0, 2_000_000, "artworks"),
        new("About how many languages has the Bible been translated into?", 700, 0, 3_000, "languages"),
        new("About how many words are in the English language?", 170_000, 0, 1_000_000, "words"),
        new("In what year was Hamlet written?", 1_600, 1_500, 1_700, ""),
        new("About how many copies has the Agatha Christie book series sold (in billions)?", 2, 0, 10, "billion"),

        // Miscellaneous measurements
        new("How many centimetres are in one mile?", 160_934, 0, 1_000_000, "cm"),
        new("How many grams are in one pound?", 454, 0, 2_000, "grams"),
        new("How many millilitres are in one US gallon?", 3_785, 0, 10_000, "mL"),
        new("How many inches are in one kilometre?", 39_370, 0, 100_000, "inches"),
        new("How many square metres are in one acre?", 4_047, 0, 10_000, "m²"),
        new("How many centimetres are in one foot?", 30, 0, 200, "cm"),
        new("How many calories are in one kilocalorie?", 1_000, 0, 5_000, "cal"),
        new("How many millimetres are in one foot?", 305, 0, 1_000, "mm"),
        new("How many grams are in one stone?", 6_350, 0, 20_000, "grams"),
        new("How many square feet are in one acre?", 43_560, 0, 200_000, "sq ft"),

        // Environment & Earth
        new("About how many trees are on Earth (in trillions)?", 3, 0, 20, "trillion"),
        new("About how many tonnes of plastic are produced globally per year (in millions)?", 400, 0, 2_000, "million tonnes"),
        new("About what percentage of Earth's land is forest?", 31, 0, 100, "%"),
        new("About how many species go extinct per year?", 10_000, 0, 100_000, "species"),
        new("How many tonnes of CO₂ does the world emit per year (in billions)?", 37, 0, 100, "billion tonnes"),
        new("About how deep is the average ocean in metres?", 3_688, 0, 10_000, "m"),
        new("About how many lightning strikes hit Earth per day (in millions)?", 8, 0, 50, "million"),
        new("How many tonnes does a hurricane release in energy per day (in millions)?", 600, 0, 5_000, "million tonnes equivalent"),
        new("About what percentage of fresh water is locked in glaciers and ice caps?", 69, 0, 100, "%"),
        new("About how many earthquakes occur on Earth per year?", 500_000, 0, 2_000_000, "earthquakes"),

        // Population
        new("About how many people are born every day worldwide?", 385_000, 0, 1_000_000, "people"),
        new("About how many people die every day worldwide?", 163_000, 0, 500_000, "people"),
        new("What is the current approximate world population (in billions)?", 8, 0, 20, "billion"),
        new("About what percentage of the world's population speaks Mandarin as their native language?", 12, 0, 50, "%"),
        new("About how many people live in Tokyo metropolitan area (in millions)?", 37, 0, 100, "million"),
        new("About how many languages are spoken in the world today?", 7_000, 0, 20_000, "languages"),
        new("About what percentage of the world's population has internet access?", 67, 0, 100, "%"),
        new("About how many people have ever lived on Earth (in billions)?", 108, 0, 500, "billion"),
        new("About what percentage of the world's population is left-handed?", 10, 0, 50, "%"),
        new("About how many people in the world are over 100 years old?", 600_000, 0, 3_000_000, "people"),

        // Buildings & architecture
        new("How tall is the Burj Khalifa in metres?", 828, 0, 2_000, "m"),
        new("How many floors does the Empire State Building have?", 102, 0, 300, "floors"),
        new("About how many bricks were used to build the Great Wall of China (in billions)?", 4, 0, 20, "billion"),
        new("How tall is the Statue of Liberty from base to torch tip in metres?", 93, 0, 300, "m"),
        new("How many steps are in the Eiffel Tower (to the top)?", 1_665, 0, 5_000, "steps"),
        new("How tall is the Colosseum in Rome in metres?", 48, 0, 200, "m"),
        new("About how many rooms does Buckingham Palace have?", 775, 0, 3_000, "rooms"),
        new("How long did it take to build the Great Pyramid of Giza in years?", 20, 0, 200, "years"),
        new("How tall is Big Ben's clock tower in metres?", 96, 0, 300, "m"),
        new("About how many workers built the Taj Mahal?", 20_000, 0, 100_000, "workers"),

        // Medicine & health
        new("About how many hours of sleep does the average adult need per night?", 8, 0, 20, "hours"),
        new("How long does it take for a broken bone to heal on average in weeks?", 6, 0, 30, "weeks"),
        new("About how many calories does the brain use per day?", 320, 0, 1_000, "kcal"),
        new("About how many milligrams of aspirin are in a standard tablet?", 500, 0, 2_000, "mg"),
        new("How long is the human lifespan in developed countries on average (in years)?", 80, 0, 150, "years"),
        new("About how many viruses are estimated to exist on Earth (in millions of trillions)?", 10, 0, 100, "× 10^31"),
        new("At what temperature is a fever defined in Celsius?", 38, 0, 50, "°C"),
        new("About how many calories does the average person burn per day at rest?", 1_600, 0, 5_000, "kcal"),
        new("How many vertebrae does the human spine have?", 33, 0, 100, "vertebrae"),
        new("About what percentage of the human brain is water?", 75, 0, 100, "%"),

        // Random trivia
        new("How many sides does a snowflake have?", 6, 0, 20, "sides"),
        new("How many colours are in a rainbow?", 7, 0, 30, "colours"),
        new("About how many different scents can a human nose detect?", 1_000_000, 0, 5_000_000, "scents"),
        new("How many squares are on a standard chessboard?", 64, 0, 200, "squares"),
        new("About how many playing cards are sold globally per year (in billions)?", 25, 0, 100, "billion cards"),
        new("How many tiles are in a standard Scrabble set?", 100, 0, 300, "tiles"),
        new("How many dots are on a standard set of double-six dominoes in total?", 168, 0, 500, "dots"),
        new("How many faces does a standard die have?", 6, 0, 30, "faces"),
        new("About how many Lego bricks are produced per year (in billions)?", 36, 0, 200, "billion"),
        new("How many keys are on a standard computer keyboard?", 104, 0, 300, "keys"),

        // Countries & flags
        new("How many stars are on the US flag?", 50, 0, 200, "stars"),
        new("How many stripes are on the US flag?", 13, 0, 50, "stripes"),
        new("How many countries are in the European Union?", 27, 0, 100, "countries"),
        new("How many languages are officially recognised in South Africa?", 12, 0, 50, "languages"),
        new("How many stars are on the Australian flag?", 6, 0, 30, "stars"),
        new("How many countries share a land border with Germany?", 9, 0, 30, "countries"),
        new("About how many islands does the Philippines have?", 7_641, 0, 20_000, "islands"),
        new("How many member states does the United Nations have?", 193, 0, 300, "members"),
        new("How many provinces does Canada have?", 10, 0, 30, "provinces"),
        new("How many states does Australia have?", 6, 0, 20, "states"),

        // Movies & entertainment
        new("How many films has James Bond appeared in (EON productions)?", 25, 0, 60, "films"),
        new("About how many films are made worldwide per year?", 10_000, 0, 50_000, "films"),
        new("In what year was the first Star Wars film released?", 1_977, 1_950, 2_010, ""),
        new("About how many Oscar ceremonies have there been?", 96, 0, 200, "ceremonies"),
        new("How many minutes long is the longest Oscar-winning Best Picture film?", 222, 0, 500, "minutes"),
        new("About how many streaming subscribers does Netflix have worldwide (in millions)?", 300, 0, 1_000, "million"),
        new("In what year was the first sound film (talkie) released commercially?", 1_927, 1_900, 1_960, ""),
        new("About how many Disney animated feature films have been released?", 62, 0, 200, "films"),
        new("How many Harry Potter main films are there?", 8, 0, 30, "films"),
        new("About how many people watch the Super Bowl each year (in millions)?", 120, 0, 500, "million"),

        // Mathematics
        new("How many digits does pi have (that have been computed, in trillions)?", 100, 0, 500, "trillion digits"),
        new("What is the 10th prime number?", 29, 0, 100, ""),
        new("How many faces does a dodecahedron have?", 12, 0, 50, "faces"),
        new("What is the sum of interior angles of a hexagon in degrees?", 720, 0, 2_000, "°"),
        new("How many edges does a cube have?", 12, 0, 50, "edges"),
        new("What is 2 to the power of 10?", 1_024, 0, 5_000, ""),
        new("How many degrees are in a full rotation?", 360, 0, 1_000, "°"),
        new("What is the square root of 10000?", 100, 0, 1_000, ""),
        new("How many vertices does an icosahedron have?", 12, 0, 50, "vertices"),
        new("What is 12 factorial?", 479_001_600, 0, 2_000_000_000, ""),

        // Language & writing
        new("How many letters are in the English alphabet?", 26, 0, 100, "letters"),
        new("How many letters are in the Russian alphabet?", 33, 0, 100, "letters"),
        new("How many letters are in the Greek alphabet?", 24, 0, 100, "letters"),
        new("About how many words are spoken by the average person per day?", 16_000, 0, 100_000, "words"),
        new("How many letters are in the Hawaiian alphabet?", 13, 0, 50, "letters"),
        new("About how many recognised writing systems exist in the world?", 300, 0, 1_000, "writing systems"),
        new("How many letters are in the Arabic alphabet?", 28, 0, 100, "letters"),
        new("About how many languages have no written form?", 1_500, 0, 5_000, "languages"),
        new("How many letters are in the Korean Hangul alphabet?", 24, 0, 100, "letters"),
        new("About how many words does an educated English speaker know actively?", 20_000, 0, 100_000, "words"),

        // Everyday life
        new("About how many steps does the average person walk per day?", 8_000, 0, 30_000, "steps"),
        new("About how many hours does the average person spend sleeping in their lifetime?", 227_760, 0, 500_000, "hours"),
        new("About how many times does the average person laugh per day?", 17, 0, 100, "times"),
        new("About how many hours per day does the average person spend looking at screens?", 7, 0, 24, "hours"),
        new("About how many litres of water are used in a typical 10-minute shower?", 60, 0, 300, "litres"),
        new("About how many minutes does the average commute take in the US?", 28, 0, 120, "minutes"),
        new("About how many meals does a person eat in their lifetime?", 75_000, 0, 300_000, "meals"),
        new("About how many hours of video are uploaded to YouTube every minute?", 500, 0, 5_000, "hours"),
        new("About how many photographs are taken globally per year (in trillions)?", 2, 0, 10, "trillion"),
        new("About how many tonnes of rubbish does the average person generate per year?", 1, 0, 10, "tonnes"),

        // Oceans & sea life
        new("How many oceans are on Earth?", 5, 0, 15, "oceans"),
        new("About how many species of coral reef fish exist?", 8_000, 0, 30_000, "species"),
        new("How deep can a sperm whale dive in metres?", 3_000, 0, 10_000, "m"),
        new("About how many tonnes does an adult blue whale weigh?", 150, 0, 500, "tonnes"),
        new("How long can a blue whale be in metres?", 30, 0, 100, "m"),
        new("About how many species of shark exist?", 500, 0, 2_000, "species"),
        new("About what percentage of the ocean has been explored by humans?", 20, 0, 100, "%"),
        new("How many arms does a starfish typically have?", 5, 0, 20, "arms"),
        new("How many species of sea turtles are there?", 7, 0, 30, "species"),
        new("About how deep is the Pacific Ocean on average in metres?", 4_000, 0, 12_000, "m"),

        // Plants & nature
        new("About how many species of flowering plants exist?", 300_000, 0, 1_000_000, "species"),
        new("About how old is the world's oldest known living tree in years?", 5_000, 0, 15_000, "years"),
        new("How tall can a giant sequoia grow in metres?", 95, 0, 300, "m"),
        new("About how many petals does a rose typically have?", 30, 0, 200, "petals"),
        new("About how many known species of mushroom exist?", 20_000, 0, 100_000, "species"),
        new("About how many years can a redwood tree live?", 2_000, 0, 5_000, "years"),
        new("About how fast does bamboo grow per day in centimetres?", 90, 0, 300, "cm/day"),
        new("About how many known plant species exist on Earth?", 400_000, 0, 2_000_000, "species"),
        new("How many leaves does a typical mature oak tree have?", 200_000, 0, 1_000_000, "leaves"),
        new("About what percentage of land plants are flowering plants?", 80, 0, 100, "%"),

        // Countries by number
        new("About what is the population of China (in billions)?", 1, 0, 5, "billion"),
        new("About what is the population of the United States (in millions)?", 335, 0, 1_000, "million"),
        new("About what is the population of India (in billions)?", 1, 0, 5, "billion"),
        new("About what is the population of Brazil (in millions)?", 215, 0, 500, "million"),
        new("About what is the population of Nigeria (in millions)?", 220, 0, 500, "million"),
        new("About what is the area of China in thousands of square kilometres?", 9_597, 0, 30_000, "thousand km²"),
        new("About what is the area of Canada in thousands of square kilometres?", 10_000, 0, 30_000, "thousand km²"),
        new("What is the approximate elevation of Mexico City in metres?", 2_240, 0, 5_000, "m"),
        new("About how many people live in New York City (in millions)?", 8, 0, 30, "million"),
        new("About what percentage of Antarctica is covered by ice?", 98, 0, 100, "%"),

        // Games & toys
        new("How many squares are on a standard Monopoly board?", 40, 0, 100, "squares"),
        new("How many cards are in a standard deck including jokers?", 54, 0, 200, "cards"),
        new("How many possible combinations are there on a standard Rubik's Cube (in quintillions)?", 43, 0, 200, "quintillion"),
        new("How many possible positions can a chess game reach after 3 moves each?", 9_000_000, 0, 50_000_000, "positions"),
        new("How many dots are on a pair of dice?", 42, 0, 100, "dots"),
        new("How many Tetris pieces (tetrominoes) are there?", 7, 0, 30, "pieces"),
        new("How many squares are on a standard Scrabble board?", 225, 0, 500, "squares"),
        new("In what year was the first video game, Pong, released?", 1_972, 1_950, 2_000, ""),
        new("How many pins are in tenpin bowling?", 10, 0, 30, "pins"),
        new("About how many copies has Minecraft sold worldwide (in millions)?", 300, 0, 1_000, "million"),

        // Chemistry
        new("How many protons does a uranium atom have?", 92, 0, 200, "protons"),
        new("At what Celsius temperature does ethanol boil?", 78, 0, 300, "°C"),
        new("How many protons does iron have?", 26, 0, 100, "protons"),
        new("At what temperature does dry ice sublimate in Celsius?", -79, -200, 0, "°C"),
        new("How many protons does oxygen have?", 8, 0, 50, "protons"),
        new("What is the pH of pure water?", 7, 0, 14, "pH"),
        new("About how many known chemical compounds exist (in millions)?", 100, 0, 500, "million"),
        new("What is the atomic mass of carbon?", 12, 0, 50, "amu"),
        new("How many electrons does a neutral sodium atom have?", 11, 0, 50, "electrons"),
        new("At what Celsius temperature does mercury become liquid?", -39, -100, 100, "°C"),

        // Electricity & energy
        new("How many watts does a standard microwave use?", 1_000, 0, 5_000, "watts"),
        new("About how many kilowatt-hours of electricity does the average UK home use per year?", 3_500, 0, 20_000, "kWh"),
        new("How many volts does a standard European electrical outlet provide?", 230, 0, 500, "V"),
        new("About what percentage of global electricity comes from renewables?", 30, 0, 100, "%"),
        new("How many volts does a standard car battery have?", 12, 0, 100, "V"),
        new("About how many nuclear power plants exist worldwide?", 440, 0, 2_000, "plants"),
        new("How many watts does a hair dryer typically use?", 1_500, 0, 5_000, "watts"),
        new("About how many megawatts does the Hoover Dam generate?", 2_080, 0, 10_000, "MW"),
        new("How many volts does a standard US electrical outlet provide?", 120, 0, 300, "V"),
        new("About what percentage of global energy comes from fossil fuels?", 80, 0, 100, "%"),

        // Weather
        new("About what is the highest temperature ever recorded on Earth in Celsius?", 57, 0, 100, "°C"),
        new("About what is the lowest temperature ever recorded on Earth in Celsius?", -89, -150, 0, "°C"),
        new("About how fast can a tornado's winds travel in km/h?", 500, 0, 2_000, "km/h"),
        new("About how fast do trade winds blow in km/h?", 24, 0, 100, "km/h"),
        new("About how many centimetres of rain does the Amazon rainforest receive per year?", 250, 0, 1_000, "cm"),
        new("About what is the average temperature at the North Pole in winter (°C)?", -40, -80, 0, "°C"),
        new("About how fast can a category 5 hurricane's winds travel in km/h?", 250, 0, 600, "km/h"),
        new("About how many millimetres of rain falls in the driest place on Earth (Atacama) per year?", 1, 0, 50, "mm"),
        new("About how high can a cumulonimbus cloud reach in kilometres?", 16, 0, 50, "km"),
        new("About how many centimetres of annual snowfall does Aomori, Japan receive?", 800, 0, 3_000, "cm"),
    ];

    private sealed record EstimateState(
        int RoundIndex,
        IReadOnlyList<EstimateQuestion> Questions,
        IReadOnlyList<EstimateParticipant> Participants,
        Dictionary<Guid, long> Submissions,
        IReadOnlyList<EstimateResult> Results);

    public sealed record EstimateQuestion(
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
