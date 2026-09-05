using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quizizzo.GameContracts;

namespace Quizizzo.Games.VoiceChoon;

public sealed record VoiceChoonFlowOptions
{
    public TimeSpan BriefingDuration { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan RecordingDuration { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan ControllerReadyDuration { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan CountdownDuration { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ResultsDuration { get; init; } = TimeSpan.FromSeconds(12);

    public void Validate()
    {
        if (new[] { BriefingDuration, RecordingDuration, ControllerReadyDuration, CountdownDuration, ResultsDuration }
            .Any(duration => duration <= TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(BriefingDuration), "VoiceChoon durations must be positive.");
        }
    }
}

public sealed class VoiceChoonGameModule(VoiceChoonFlowOptions? flowOptions = null) : IGameModule
{
    public const string BriefingPhase = "Briefing";
    public const string RecordingPhase = "Recording";
    public const string ControllerReadyPhase = "ControllerReady";
    public const string CountdownPhase = "Countdown";
    public const string PlayingPhase = "Playing";
    public const string ResultsPhase = "Results";
    public const string CompletedPhase = "Completed";

    private const int SchemaVersion = 1;
    private readonly VoiceChoonFlowOptions flowOptions = ValidateFlowOptions(flowOptions);

    public GameDescriptor Descriptor => VoiceChoonGameDefinition.Descriptor;

    public GameModuleState Start(GameStartContext context)
    {
        var configuration = ReadConfiguration(context.Configuration);
        var songDefinition = VoiceChoonSongCatalog.GetDefinition(configuration.SongKey);
        if (context.Participants.Count is < 1 || context.Participants.Count > songDefinition.MaximumPlayers)
        {
            throw new GameRuleViolationException(
                "invalid-player-count",
                $"{songDefinition.DisplayName} supports one to {songDefinition.MaximumPlayers} people; Moosik Bots fill its empty minimum seats.");
        }

        var difficulty = DifficultySettings.For(configuration.Difficulty);
        var song = VoiceChoonSongCatalog.Load(songDefinition.Key);
        var performerCount = Math.Max(context.Participants.Count, songDefinition.MinimumPlayers);
        Func<VoiceChoonTrackRole, IReadOnlyList<SoundRecordingPrompt>>? promptFactory =
            string.Equals(songDefinition.Key, VoiceChoonSongCatalog.WubquakeSongKey, StringComparison.Ordinal)
                ? role => InstrumentSoundGuide.For(role)
                    .Select(prompt => prompt with { Guidance = songDefinition.RecordingMessage }).ToArray()
                : null;
        var assignments = InstrumentAssignmentService.Assign(song, performerCount, promptFactory);
        var generatedCharts = new ChartGenerator(difficulty.ChartOptions).Generate(assignments).ToArray();
        var humans = context.Participants.Select((participant, index) =>
            new VoiceChoonParticipant(participant.PlayerId, participant.DisplayName, index)).ToArray();
        var bots = Enumerable.Range(0, performerCount - humans.Length)
            .Select(index => new VoiceChoonParticipant(
                BotPlayerId(context.GameInstanceId, index),
                $"Moosik Bot {index + 1}",
                humans.Length + index,
                true,
                humans[index % humans.Length].PlayerId))
            .ToArray();
        var participants = humans.Concat(bots).ToArray();
        var charts = generatedCharts.Select(chart =>
        {
            if (chart.PlayerIndex >= humans.Length) return chart;
            var human = humans[chart.PlayerIndex];
            var suppliedBotIndexes = bots
                .Where(bot => bot.SampleOwnerPlayerId == human.PlayerId)
                .Select(bot => bot.PlayerIndex)
                .ToHashSet();
            var prompts = generatedCharts
                .Where(candidate => candidate.PlayerIndex == chart.PlayerIndex ||
                                    suppliedBotIndexes.Contains(candidate.PlayerIndex))
                .SelectMany(candidate => candidate.RecordingPrompts)
                .DistinctBy(prompt => prompt.Key, StringComparer.Ordinal)
                .ToArray();
            return chart with { RecordingPrompts = prompts };
        }).ToArray();
        var state = new VoiceChoonGameState(
            song.SourceName,
            song.DurationSeconds,
            song.Sections,
            participants,
            charts,
            participants.ToDictionary(
                player => player.PlayerId,
                _ => (IReadOnlyDictionary<string, Guid>)new Dictionary<string, Guid>(StringComparer.Ordinal)),
            [],
            [],
            null,
            participants.ToDictionary(player => player.PlayerId, _ => 0L),
            participants.ToDictionary(
                player => player.PlayerId,
                _ => (IReadOnlyList<VoiceNoteJudgement>)[]),
            participants.ToDictionary(player => player.PlayerId, _ => 0),
            0,
            0,
            50,
            [],
            configuration.Difficulty,
            songDefinition.Key);
        return ModuleState(BriefingPhase, context.StartedAtUtc.Add(flowOptions.BriefingDuration), false, state);
    }

    public GameTransition Apply(GameModuleState state, GameActionContext context, IGameAction action)
    {
        var game = ReadState(state);
        return action switch
        {
            RegisterVoiceSampleAction sample => RegisterSample(state, game, context, sample),
            ConfirmVoiceRecordingsAction => ConfirmRecordings(state, game, context),
            ReadyVoiceControllerAction => ReadyController(state, game, context),
            SubmitVoiceInputAction input => SubmitInput(state, game, context, input),
            DeadlineElapsedAction => Progress(state, game, context.ReceivedAtUtc),
            AdvanceVoiceChoonAction => Advance(state, game, context),
            _ => throw new GameRuleViolationException(
                "unsupported-action", $"Action '{action.Kind}' is not supported by VoiceChoon.")
        };
    }

    public GameViewPayload CreateView(GameModuleState state, GameViewContext context)
    {
        var game = ReadState(state);
        return context.Role switch
        {
            GameAudienceRole.Host => new(GameJson.From(HostView(state, game))),
            GameAudienceRole.Display => new(GameJson.From(DisplayView(state, game))),
            GameAudienceRole.Player => new(GameJson.From(PlayerView(state, game, context))),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    public IGameAction DecodeAction(string actionKind, JsonElement payload) => actionKind switch
    {
        RegisterVoiceSampleAction.ActionKind => ReadRegisteredSample(payload),
        ConfirmVoiceRecordingsAction.ActionKind => new ConfirmVoiceRecordingsAction(),
        ReadyVoiceControllerAction.ActionKind => new ReadyVoiceControllerAction(),
        SubmitVoiceInputAction.ActionKind => ReadInput(payload),
        AdvanceVoiceChoonAction.ActionKind => new AdvanceVoiceChoonAction(),
        _ => throw new GameRuleViolationException(
            "unsupported-action", $"Action '{actionKind}' is not supported by VoiceChoon.")
    };

    private static GameTransition RegisterSample(
        GameModuleState current,
        VoiceChoonGameState game,
        GameActionContext context,
        RegisterVoiceSampleAction action)
    {
        RequirePhase(current, RecordingPhase);
        var playerId = RequirePlayer(game, context);
        if (game.RecordingReadyPlayerIds.Contains(playerId))
        {
            throw new GameRuleViolationException("recordings-locked", "Your VoiceChoon sounds are already locked.");
        }
        if (action.AssetId == Guid.Empty)
        {
            throw new GameRuleViolationException("invalid-sample", "A valid VoiceChoon sample asset is required.");
        }
        var participant = game.Participants.Single(player => player.PlayerId == playerId);
        var chart = game.Charts.Single(item => item.PlayerIndex == participant.PlayerIndex);
        if (!chart.RecordingPrompts.Any(prompt => string.Equals(prompt.Key, action.PromptKey, StringComparison.Ordinal)))
        {
            throw new GameRuleViolationException("invalid-prompt", "That recording prompt is not assigned to this player.");
        }

        var allSamples = game.SampleAssetIdsByPlayer.ToDictionary();
        var playerSamples = allSamples[playerId].ToDictionary(StringComparer.Ordinal);
        playerSamples[action.PromptKey] = action.AssetId;
        allSamples[playerId] = playerSamples;
        return new GameTransition(
            current with { Data = GameJson.From(game with { SampleAssetIdsByPlayer = allSamples }) },
            [],
            [new GameEvent("VoiceSampleRegistered", GameJson.From(new { playerId, action.PromptKey }))]);
    }

    private GameTransition ConfirmRecordings(
        GameModuleState current,
        VoiceChoonGameState game,
        GameActionContext context)
    {
        RequirePhase(current, RecordingPhase);
        var playerId = RequirePlayer(game, context);
        if (game.RecordingReadyPlayerIds.Contains(playerId))
        {
            throw new GameRuleViolationException("recordings-already-ready", "Your recordings are already confirmed.");
        }
        var participant = game.Participants.Single(player => player.PlayerId == playerId);
        var requiredPrompts = game.Charts
            .Single(item => item.PlayerIndex == participant.PlayerIndex)
            .RecordingPrompts.Select(prompt => prompt.Key);
        if (requiredPrompts.Any(prompt => !game.SampleAssetIdsByPlayer[playerId].ContainsKey(prompt)))
        {
            throw new GameRuleViolationException(
                "recordings-incomplete", "Record every assigned VoiceChoon sound before locking them in.");
        }

        var ready = game.RecordingReadyPlayerIds.Append(playerId).ToArray();
        var updated = game with { RecordingReadyPlayerIds = ready };
        return ready.Length == game.Participants.Count(player => !player.IsBot)
            ? new GameTransition(
                Countdown(updated, context.ReceivedAtUtc).State,
                [],
                [
                    new GameEvent("VoiceRecordingsReady", GameJson.Empty),
                    new GameEvent("VoiceCountdownStarted", GameJson.Empty)
                ])
            : GameTransition.To(current with { Data = GameJson.From(updated) });
    }

    private GameTransition ReadyController(
        GameModuleState current,
        VoiceChoonGameState game,
        GameActionContext context)
    {
        RequirePhase(current, ControllerReadyPhase);
        var playerId = RequirePlayer(game, context);
        if (game.ControllerReadyPlayerIds.Contains(playerId))
        {
            throw new GameRuleViolationException("controller-already-ready", "Your controller is already ready.");
        }

        var ready = game.ControllerReadyPlayerIds.Append(playerId).ToArray();
        var updated = game with { ControllerReadyPlayerIds = ready };
        return ready.Length == game.Participants.Count(player => !player.IsBot)
            ? Countdown(updated, context.ReceivedAtUtc)
            : GameTransition.To(current with { Data = GameJson.From(updated) });
    }

    private static GameTransition SubmitInput(
        GameModuleState current,
        VoiceChoonGameState game,
        GameActionContext context,
        SubmitVoiceInputAction action)
    {
        RequirePhase(current, PlayingPhase);
        var playerId = RequirePlayer(game, context);
        if (action.Lane is < 0 or > 3)
        {
            throw new GameRuleViolationException("invalid-lane", "A VoiceChoon lane must be between zero and three.");
        }
        if (action.Sequence <= game.LastSequenceByPlayer.GetValueOrDefault(playerId))
        {
            throw new GameRuleViolationException("stale-input", "That VoiceChoon input has already been handled.");
        }

        var participant = game.Participants.Single(player => player.PlayerId == playerId);
        var chart = game.Charts.Single(item => item.PlayerIndex == participant.PlayerIndex);
        var difficulty = DifficultySettings.For(game.Difficulty);
        var songStartedAt = game.SongStartsAtUtc
            ?? throw new InvalidOperationException("VoiceChoon has no authoritative song start.");
        var judged = game.JudgementsByPlayer[playerId];
        var activeHolds = (game.ActiveHoldsByPlayer ?? new Dictionary<Guid, VoiceActiveHold>()).ToDictionary();
        if (action.Released)
        {
            if (!activeHolds.Remove(playerId, out var active))
            {
                throw new GameRuleViolationException("no-active-hold", "There is no active hold in that lane.");
            }
            if (active.Lane != action.Lane)
            {
                throw new GameRuleViolationException("wrong-hold-lane", "Release the lane that started the hold.");
            }
            var heldNote = chart.Notes.Single(item => item.Id == active.NoteId);
            // The controller deliberately waits through its 100 ms interruption grace before
            // transmitting a release. Remove that known delay from musical judgement time.
            var effectiveReleaseAt = context.ReceivedAtUtc.AddMilliseconds(-100);
            var expectedRelease = songStartedAt.AddSeconds(heldNote.StartTimeSeconds + heldNote.DurationSeconds);
            var releaseError = Math.Abs((effectiveReleaseAt - expectedRelease).TotalMilliseconds);
            var heldMilliseconds = Math.Max(0, (effectiveReleaseAt - active.StartedAtUtc).TotalMilliseconds);
            var targetMilliseconds = heldNote.DurationSeconds * 1000;
            var maintainedRatio = Math.Clamp(heldMilliseconds / Math.Max(1, targetMilliseconds), 0, 1);
            var durationPoints = (int)Math.Round(400 * maintainedRatio, MidpointRounding.AwayFromZero);
            var releasePoints = releaseError <= difficulty.PerfectWindowMilliseconds ? 200
                : releaseError <= difficulty.GreatWindowMilliseconds ? 150
                : releaseError <= difficulty.GoodWindowMilliseconds ? 100 : 0;
            var holdPoints = active.StartPoints + durationPoints + releasePoints;
            var holdRating = holdPoints >= 900 ? VoiceNoteRating.Perfect : holdPoints >= 700 ? VoiceNoteRating.Great : VoiceNoteRating.Good;
            var holdJudgement = new VoiceNoteJudgement(active.NoteId, active.Lane, holdRating,
                (int)Math.Round(releaseError, MidpointRounding.AwayFromZero), holdPoints);
            var releasedJudgements = game.JudgementsByPlayer.ToDictionary();
            releasedJudgements[playerId] = [.. judged, holdJudgement];
            var releasedScores = game.ScoresByPlayer.ToDictionary();
            releasedScores[playerId] += holdPoints;
            var releasedSequences = game.LastSequenceByPlayer.ToDictionary();
            releasedSequences[playerId] = action.Sequence;
            var releasedCombo = game.BandCombo + 1;
            var released = game with
            {
                ActiveHoldsByPlayer = activeHolds,
                LastSequenceByPlayer = releasedSequences,
                JudgementsByPlayer = releasedJudgements,
                ScoresByPlayer = releasedScores,
                BandCombo = releasedCombo,
                MaximumBandCombo = Math.Max(game.MaximumBandCombo, releasedCombo),
                EnergyPercent = Math.Min(100, game.EnergyPercent + (holdRating == VoiceNoteRating.Perfect ? 2 : 1))
            };
            return new GameTransition(current with { Data = GameJson.From(released) }, [],
                [new GameEvent("VoiceHoldJudged", GameJson.From(new { playerId, holdJudgement.NoteId, rating = holdRating, points = holdPoints }))]);
        }
        var elapsedMilliseconds = (context.ReceivedAtUtc - songStartedAt).TotalMilliseconds;
        var note = chart.Notes
            .Where(item => item.Lane == action.Lane && judged.All(result => result.NoteId != item.Id))
            .Select(item => new
            {
                Note = item,
                Error = Math.Abs(elapsedMilliseconds - (item.StartTimeSeconds * 1000))
            })
            .Where(item => item.Error <= difficulty.GoodWindowMilliseconds)
            .OrderBy(item => item.Error)
            .ThenBy(item => item.Note.StartTimeSeconds)
            .FirstOrDefault()
            ?? throw new GameRuleViolationException("no-note-in-window", "There is no playable note in that lane right now.");

        if (note.Note.Type == RhythmNoteType.Hold)
        {
            if (activeHolds.ContainsKey(playerId))
            {
                throw new GameRuleViolationException("hold-already-active", "Finish the current hold first.");
            }
            var startPoints = note.Error <= difficulty.PerfectWindowMilliseconds ? 400
                : note.Error <= difficulty.GreatWindowMilliseconds ? 300 : 200;
            activeHolds[playerId] = new VoiceActiveHold(note.Note.Id, note.Note.Lane, context.ReceivedAtUtc, startPoints);
            var holdSequences = game.LastSequenceByPlayer.ToDictionary();
            holdSequences[playerId] = action.Sequence;
            return GameTransition.To(current with
            {
                Data = GameJson.From(game with
                {
                    ActiveHoldsByPlayer = activeHolds,
                    LastSequenceByPlayer = holdSequences
                })
            });
        }

        var rating = note.Error <= difficulty.PerfectWindowMilliseconds
            ? VoiceNoteRating.Perfect
            : note.Error <= difficulty.GreatWindowMilliseconds
                ? VoiceNoteRating.Great
                : VoiceNoteRating.Good;
        var points = rating switch
        {
            VoiceNoteRating.Perfect => 1000,
            VoiceNoteRating.Great => 750,
            _ => 500
        };
        var judgement = new VoiceNoteJudgement(
            note.Note.Id,
            note.Note.Lane,
            rating,
            (int)Math.Round(note.Error, MidpointRounding.AwayFromZero),
            points);
        var judgements = game.JudgementsByPlayer.ToDictionary();
        judgements[playerId] = [.. judged, judgement];
        var scores = game.ScoresByPlayer.ToDictionary();
        scores[playerId] += points;
        var sequences = game.LastSequenceByPlayer.ToDictionary();
        sequences[playerId] = action.Sequence;
        var combo = game.BandCombo + 1;
        var updated = game with
        {
            LastSequenceByPlayer = sequences,
            JudgementsByPlayer = judgements,
            ScoresByPlayer = scores,
            BandCombo = combo,
            MaximumBandCombo = Math.Max(game.MaximumBandCombo, combo),
            EnergyPercent = Math.Min(100, game.EnergyPercent + (rating == VoiceNoteRating.Perfect ? 2 : 1))
        };
        return new GameTransition(
            current with { Data = GameJson.From(updated) },
            [],
            [new GameEvent("VoiceNoteJudged", GameJson.From(new { playerId, judgement.NoteId, rating, points }))]);
    }

    private GameTransition Advance(
        GameModuleState current,
        VoiceChoonGameState game,
        GameActionContext context)
    {
        if (context.Actor.Role != GameActorRole.Host)
        {
            throw new GameRuleViolationException("host-required", "Only the host can continue VoiceChoon early.");
        }
        if (current.Phase == PlayingPhase)
        {
            throw new GameRuleViolationException("playing-cannot-skip", "The VoiceChoon performance cannot be skipped.");
        }
        return Progress(current, game, context.ReceivedAtUtc);
    }

    private GameTransition Progress(GameModuleState current, VoiceChoonGameState game, DateTimeOffset now) =>
        current.Phase switch
        {
            BriefingPhase => GameTransition.To(ModuleState(
                RecordingPhase,
                now.Add(flowOptions.RecordingDuration),
                false,
                game)),
            RecordingPhase when game.RecordingReadyPlayerIds.Count == game.Participants.Count(player => !player.IsBot) => Countdown(game, now),
            RecordingPhase => GameTransition.To(ModuleState(
                RecordingPhase,
                now.Add(flowOptions.RecordingDuration),
                false,
                game)),
            ControllerReadyPhase => Countdown(game, now),
            CountdownPhase => StartSong(game, now),
            PlayingPhase => Results(game, now),
            ResultsPhase => Complete(game),
            _ => throw new GameRuleViolationException("wrong-phase", "This VoiceChoon phase cannot advance.")
        };

    private GameTransition Countdown(VoiceChoonGameState game, DateTimeOffset now) => new(
        ModuleState(CountdownPhase, now.Add(flowOptions.CountdownDuration), false, game),
        [],
        [new GameEvent("VoiceCountdownStarted", GameJson.Empty)]);

    private static GameTransition StartSong(VoiceChoonGameState game, DateTimeOffset now)
    {
        var started = ApplyPerfectBots(game, now);
        return new GameTransition(
            ModuleState(PlayingPhase, now.AddSeconds(game.SongDurationSeconds), false, started),
            [],
            [new GameEvent("VoicePerformanceStarted", GameJson.From(new { songStartsAtUtc = now }))]);
    }

    private static VoiceChoonGameState ApplyPerfectBots(VoiceChoonGameState game, DateTimeOffset now)
    {
        var judgements = game.JudgementsByPlayer.ToDictionary();
        var scores = game.ScoresByPlayer.ToDictionary();
        foreach (var bot in game.Participants.Where(player => player.IsBot))
        {
            var perfect = game.Charts.Single(chart => chart.PlayerIndex == bot.PlayerIndex).Notes
                .Select(note => new VoiceNoteJudgement(note.Id, note.Lane, VoiceNoteRating.Perfect, 0, 1000))
                .ToArray();
            judgements[bot.PlayerId] = perfect;
            scores[bot.PlayerId] = perfect.Length * 1000;
        }
        var botNotes = game.Participants.Where(player => player.IsBot)
            .Sum(bot => judgements[bot.PlayerId].Count);
        return game with
        {
            SongStartsAtUtc = now,
            JudgementsByPlayer = judgements,
            ScoresByPlayer = scores,
            BandCombo = botNotes,
            MaximumBandCombo = Math.Max(game.MaximumBandCombo, botNotes),
            EnergyPercent = Math.Min(100, game.EnergyPercent + botNotes)
        };
    }

    private GameTransition Results(VoiceChoonGameState game, DateTimeOffset now)
    {
        var results = game.Participants
            .Select(player =>
            {
                var chart = game.Charts.Single(item => item.PlayerIndex == player.PlayerIndex);
                var judged = game.JudgementsByPlayer[player.PlayerId].Count;
                return new
                {
                    Player = player,
                    Score = game.ScoresByPlayer[player.PlayerId],
                    Judged = judged,
                    Total = chart.Notes.Count,
                    Accuracy = chart.Notes.Count == 0 ? 0 : (int)Math.Round(judged * 100d / chart.Notes.Count)
                };
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Accuracy)
            .ThenBy(item => item.Player.PlayerId)
            .ToArray();
        var ranked = results.Select((item, index) => new VoiceChoonResult(
            item.Player.PlayerId,
            item.Player.DisplayName,
            index + 1,
            item.Score,
            item.Judged,
            item.Total,
            item.Accuracy)).ToArray();
        var updated = game with { Results = ranked, BandCombo = 0 };
        return new GameTransition(
            ModuleState(ResultsPhase, now.Add(flowOptions.ResultsDuration), false, updated),
            [],
            [new GameEvent("VoicePerformanceEnded", GameJson.From(new { bandScore = ranked.Sum(item => item.Score) }))]);
    }

    private static GameTransition Complete(VoiceChoonGameState game) => new(
        ModuleState(CompletedPhase, null, true, game),
        game.Results
            .Where(result => result.Score > 0 && game.Participants.Any(player =>
                player.PlayerId == result.PlayerId && !player.IsBot))
            .Select(result => new ScoreAward(result.PlayerId, result.Score, "VoiceChoon performance score"))
            .ToArray(),
        [new GameEvent("GameCompleted", GameJson.Empty)]);

    private static HostGameViewPayload HostView(GameModuleState current, VoiceChoonGameState game) => new(
        "VoiceChoon",
        game.SongName,
        PhaseMessage(current, game),
        ActivityCount(current, game),
        game.Participants.Count(player => !player.IsBot),
        false,
        null,
        null,
        Entries(game));

    private static DisplayGameViewPayload DisplayView(GameModuleState current, VoiceChoonGameState game) => new(
        "VoiceChoon",
        game.SongName,
        PhaseMessage(current, game),
        ActivityCount(current, game),
        game.Participants.Count,
        Entries(game),
        ShowRoundRanking: current.Phase == ResultsPhase,
        Statistics:
        [
            new("Band score", game.ScoresByPlayer.Values.Sum().ToString("N0", CultureInfo.InvariantCulture)),
            new("Energy", $"{game.EnergyPercent}%"),
            new("Best combo", game.MaximumBandCombo.ToString(CultureInfo.InvariantCulture))
        ],
        State: GameJson.From(new VoiceChoonDisplayState(
            game.SongName,
            game.SongDurationSeconds,
            game.Sections,
            game.SongStartsAtUtc,
            game.ScoresByPlayer.Values.Sum(),
            game.BandCombo,
            game.MaximumBandCombo,
            game.EnergyPercent,
            game.Results,
            current.Phase is PlayingPhase or ResultsPhase
                ? game.Charts.SelectMany(chart => chart.PlaybackNotes.Select(note =>
                {
                    var participant = game.Participants.Single(player => player.PlayerIndex == chart.PlayerIndex);
                    var sampleOwnerId = participant.SampleOwnerPlayerId ?? participant.PlayerId;
                    var playback = PlaybackNote(note, chart, game.SampleAssetIdsByPlayer[sampleOwnerId]);
                    var judgementNote = chart.Notes.FirstOrDefault(candidate =>
                        candidate.Id == note.Id ||
                        (candidate.Lane == note.Lane &&
                         note.StartTimeSeconds >= candidate.StartTimeSeconds &&
                         note.StartTimeSeconds <= candidate.StartTimeSeconds + candidate.DurationSeconds + 0.01));
                    return new VoiceChoonDisplayPlayback(
                        playback.Id,
                        playback.StartTimeSeconds,
                        playback.DurationSeconds,
                        playback.SampleAssetId ?? Guid.Empty,
                        playback.PlaybackRate,
                        playback.Loop,
                        playback.LoopStartSeconds,
                        playback.LoopEndSeconds,
                        participant.PlayerId,
                        judgementNote?.Id,
                        judgementNote?.StartTimeSeconds,
                        note.Velocity,
                        note.SourceRole == VoiceChoonTrackRole.Drums);
                })).Where(note => note.SampleAssetId != Guid.Empty).ToArray()
                : null,
            current.Phase is PlayingPhase or ResultsPhase
                ? game.Participants.Select(participant =>
                {
                    var chart = game.Charts.Single(item => item.PlayerIndex == participant.PlayerIndex);
                    return new VoiceChoonDisplayPerformer(
                        participant.PlayerId,
                        chart.Notes.Select(note => new VoiceChoonDisplayNote(note.Id, note.StartTimeSeconds)).ToArray(),
                        game.JudgementsByPlayer[participant.PlayerId].Select(item => item.NoteId).ToArray());
                }).ToArray()
                : null)));

    private static PlayerGameViewPayload PlayerView(
        GameModuleState current,
        VoiceChoonGameState game,
        GameViewContext context)
    {
        var playerId = context.PlayerId
            ?? throw new GameRuleViolationException("player-required", "A VoiceChoon player view requires a player ID.");
        var player = game.Participants.SingleOrDefault(item => item.PlayerId == playerId)
            ?? throw new GameRuleViolationException("player-required", "That player is not in VoiceChoon.");
        var chart = game.Charts.Single(item => item.PlayerIndex == player.PlayerIndex);
        var recordingReady = game.RecordingReadyPlayerIds.Contains(playerId);
        var controller = current.Phase switch
        {
            RecordingPhase when !recordingReady => RecordingController(chart, game.SampleAssetIdsByPlayer[playerId]),
            PlayingPhase => RhythmController(game, playerId, chart),
            _ => WaitingController()
        };
        return new PlayerGameViewPayload(
            chart.InstrumentName,
            PlayerInstructions(current, game, recordingReady),
            controller,
            GameJson.From(new VoiceChoonPlayerState(
                chart.InstrumentName,
                chart,
                game.JudgementsByPlayer[playerId],
                game.ScoresByPlayer[playerId],
                game.BandCombo,
                game.EnergyPercent,
                game.SongStartsAtUtc,
                game.LastSequenceByPlayer[playerId] + 1)));
    }

    private static PlayerControllerView RhythmController(
        VoiceChoonGameState game,
        Guid playerId,
        PlayerChart chart)
    {
        var notes = chart.Notes;
        return new PlayerControllerView(
            PlayerControllerKind.Rhythm,
            SubmitVoiceInputAction.ActionKind,
            true,
            string.Empty,
            GameJson.From(new RhythmControllerConfiguration(
                game.SongStartsAtUtc ?? throw new InvalidOperationException("VoiceChoon has no song start."),
                game.SongDurationSeconds,
                notes.Select(note => PlaybackNote(note, chart, game.SampleAssetIdsByPlayer[playerId])).ToArray(),
                game.LastSequenceByPlayer[playerId] + 1,
                2,
                DifficultySettings.For(game.Difficulty).GoodWindowMilliseconds / 1000d,
                DifficultySettings.For(game.Difficulty).GreatWindowMilliseconds / 1000d,
                DifficultySettings.For(game.Difficulty).PerfectWindowMilliseconds / 1000d)));
    }

    private static RhythmControllerNote PlaybackNote(
        RhythmNote note,
        PlayerChart chart,
        IReadOnlyDictionary<string, Guid> sampleAssets)
    {
        var familyPrefix = note.InstrumentFamily switch
        {
            VoiceChoonInstrumentFamily.Piano => "piano-",
            VoiceChoonInstrumentFamily.Bell => "bell-",
            VoiceChoonInstrumentFamily.Organ => "organ-",
            VoiceChoonInstrumentFamily.Guitar => "guitar-",
            VoiceChoonInstrumentFamily.Strings => "strings-",
            VoiceChoonInstrumentFamily.Brass => "brass-",
            VoiceChoonInstrumentFamily.Woodwind => "woodwind-",
            _ => null
        };
        var rolePromptKeys = familyPrefix is not null
            ? chart.RecordingPrompts.Where(prompt => prompt.Key.StartsWith(familyPrefix, StringComparison.Ordinal))
                .Select(prompt => prompt.Key).ToHashSet(StringComparer.Ordinal)
            : note.SourceRole == VoiceChoonTrackRole.Other &&
                             note.PlaybackStyle == RecordingStyle.Sustained
            ? chart.RecordingPrompts.Where(prompt => prompt.Key.StartsWith("legato-", StringComparison.Ordinal))
                .Select(prompt => prompt.Key).ToHashSet(StringComparer.Ordinal)
            : InstrumentSoundGuide.For(note.SourceRole).Select(prompt => prompt.Key)
                .ToHashSet(StringComparer.Ordinal);
        var rolePrompts = chart.RecordingPrompts
            .Where(prompt => rolePromptKeys.Contains(prompt.Key))
            .ToArray();
        var prompts = rolePrompts.Length > 0 ? rolePrompts : chart.RecordingPrompts.ToArray();
        var selectedPrompt = note.SourceRole is VoiceChoonTrackRole.Drums or VoiceChoonTrackRole.PercussionFx
            ? prompts[note.Lane % prompts.Length]
            : prompts.MinBy(prompt => Math.Abs(note.TargetMidiNote - prompt.RootMidiNote))!;
        var sample = new RecordedSample(selectedPrompt.Key, selectedPrompt.RootMidiNote, selectedPrompt.Style, 1);
        var plan = PitchShiftPlanner.Plan(note.TargetMidiNote, note.DurationSeconds, [sample]);
        sampleAssets.TryGetValue(selectedPrompt.Key, out var sampleAssetId);
        return new RhythmControllerNote(
            note.Id,
            note.Lane,
            note.StartTimeSeconds,
            note.DurationSeconds,
            note.Type.ToString(),
            sampleAssetId == Guid.Empty ? null : sampleAssetId,
            plan.PlaybackRate,
            plan.Loop,
            plan.LoopStartSeconds,
            plan.LoopEndSeconds,
            $"{selectedPrompt.Example} · {note.SourceTrack}",
            selectedPrompt.Style.ToString());
    }

    private static PlayerControllerView RecordingController(
        PlayerChart chart,
        IReadOnlyDictionary<string, Guid> sampleAssetIds) => new(
        PlayerControllerKind.Recording,
        ConfirmVoiceRecordingsAction.ActionKind,
        true,
        "Send my sound pack",
        GameJson.From(new RecordingControllerConfiguration(
            chart.RecordingPrompts.Select(prompt => new RecordingPromptConfiguration(
                prompt.Key,
                prompt.Label,
                prompt.Example,
                prompt.Style.ToString(),
                prompt.RootMidiNote,
                prompt.Guidance,
                sampleAssetIds.TryGetValue(prompt.Key, out var assetId) ? assetId : null)).ToArray(),
            "/api/voicechoon/samples",
            6,
            2 * 1024 * 1024)));

    private static PlayerControllerView WaitingController() => new(
        PlayerControllerKind.Waiting,
        string.Empty,
        false,
        string.Empty,
        GameJson.Empty);

    private static GamePresentationEntry[] Entries(VoiceChoonGameState game) => game.Participants
        .Select(player =>
        {
            var result = game.Results.SingleOrDefault(item => item.PlayerId == player.PlayerId);
            var chart = game.Charts.Single(item => item.PlayerIndex == player.PlayerIndex);
            return new GamePresentationEntry(
                player.PlayerId,
                player.DisplayName,
                result is null ? chart.InstrumentName : $"{result.AccuracyPercent}% · {result.Score:N0} pts",
                result?.Rank,
                result?.Score ?? 0);
        }).ToArray();

    private static string PlayerInstructions(
        GameModuleState current,
        VoiceChoonGameState game,
        bool recordingReady)
    {
        var song = VoiceChoonSongCatalog.GetDefinition(game.SongKey);
        return current.Phase switch
        {
            BriefingPhase => song.BriefingMessage,
            RecordingPhase when recordingReady => "Sounds locked. Waiting for the rest of the band.",
            RecordingPhase => song.RecordingMessage,
            ControllerReadyPhase => "Hands ready. The performance is about to begin.",
            CountdownPhase => "Hands ready. The performance is about to begin.",
            PlayingPhase => "Hit each lane as its note reaches the line.",
            ResultsPhase => "The band survived. Results are on the main screen.",
            _ => "VoiceChoon complete."
        };
    }

    private static string PhaseMessage(GameModuleState current, VoiceChoonGameState game) => current.Phase switch
    {
        BriefingPhase => "Meet your extremely human orchestra",
        RecordingPhase => $"{game.RecordingReadyPlayerIds.Count}/{game.Participants.Count(player => !player.IsBot)} sound kits ready",
        ControllerReadyPhase => "Preparing the band",
        CountdownPhase => "Performance starts in…",
        PlayingPhase => "The band is live",
        ResultsPhase => "Final band performance",
        _ => "VoiceChoon complete"
    };

    private static int ActivityCount(GameModuleState current, VoiceChoonGameState game) => current.Phase switch
    {
        RecordingPhase => game.RecordingReadyPlayerIds.Count,
        ControllerReadyPhase => game.ControllerReadyPlayerIds.Count,
        PlayingPhase => game.JudgementsByPlayer.Values.Count(items => items.Count > 0),
        _ => 0
    };

    private static SubmitVoiceInputAction ReadInput(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("sequence", out var sequenceElement) ||
            !sequenceElement.TryGetInt64(out var sequence) || sequence < 1 ||
            !payload.TryGetProperty("input", out var inputElement) ||
            inputElement.ValueKind != JsonValueKind.String ||
            !TryReadLane(inputElement.GetString(), out var lane))
        {
            throw new GameRuleViolationException("invalid-voice-input", "A valid sequenced VoiceChoon lane is required.");
        }
        var clientTimestamp = DateTimeOffset.UnixEpoch;
        if (payload.TryGetProperty("clientTimestamp", out var timestampElement) &&
            (timestampElement.ValueKind != JsonValueKind.String ||
             !timestampElement.TryGetDateTimeOffset(out clientTimestamp)))
        {
            throw new GameRuleViolationException("invalid-client-time", "The diagnostic client timestamp is invalid.");
        }
        var released = payload.TryGetProperty("released", out var releasedElement) &&
            releasedElement.ValueKind is JsonValueKind.True;
        return new SubmitVoiceInputAction(sequence, lane, clientTimestamp, released);
    }

    private static RegisterVoiceSampleAction ReadRegisteredSample(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("promptKey", out var promptElement) ||
            promptElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(promptElement.GetString()) ||
            !payload.TryGetProperty("assetId", out var assetElement) ||
            assetElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(assetElement.GetString(), out var assetId) || assetId == Guid.Empty)
        {
            throw new GameRuleViolationException("invalid-sample", "Valid VoiceChoon sample details are required.");
        }
        return new RegisterVoiceSampleAction(promptElement.GetString()!, assetId);
    }

    private static bool TryReadLane(string? input, out int lane)
    {
        lane = -1;
        return input is { Length: 5 } &&
            input.StartsWith("Lane", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(input.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out lane) &&
            lane is >= 0 and <= 3;
    }

    private static Guid RequirePlayer(VoiceChoonGameState game, GameActionContext context)
    {
        if (!context.Actor.TryGetPlayerId(out var playerId) ||
            !game.Participants.Any(player => player.PlayerId == playerId))
        {
            throw new GameRuleViolationException("player-required", "A current VoiceChoon player is required.");
        }
        return playerId;
    }

    private static Guid BotPlayerId(GameInstanceId gameInstanceId, int botIndex)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"voicechoon:{gameInstanceId.Value:N}:moosik-bot:{botIndex}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void RequirePhase(GameModuleState current, string expected)
    {
        if (current.Phase != expected)
        {
            throw new GameRuleViolationException("wrong-phase", "That VoiceChoon action is not available now.");
        }
    }

    private static VoiceChoonGameState ReadState(GameModuleState state) =>
        state.Data.Deserialize<VoiceChoonGameState>()
        ?? throw new InvalidOperationException("VoiceChoon state could not be read.");

    private static VoiceChoonGameConfiguration ReadConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
            configuration.ValueKind == JsonValueKind.Object && !configuration.EnumerateObject().Any())
        {
            return new VoiceChoonGameConfiguration();
        }
        try
        {
            var value = configuration.Deserialize<VoiceChoonGameConfiguration>();
            if (value is null || !Enum.IsDefined(value.Difficulty) || !VoiceChoonSongCatalog.IsKnownKey(value.SongKey))
            {
                throw new JsonException();
            }
            return value;
        }
        catch (JsonException)
        {
            throw new GameRuleViolationException(
                "invalid-configuration", "VoiceChoon requires a supported song and Easy, Medium or Hard difficulty.");
        }
    }

    private static GameModuleState ModuleState(
        string phase,
        DateTimeOffset? deadline,
        bool complete,
        VoiceChoonGameState state) => new(SchemaVersion, phase, deadline, complete, GameJson.From(state));

    private static VoiceChoonFlowOptions ValidateFlowOptions(VoiceChoonFlowOptions? options)
    {
        var value = options ?? new VoiceChoonFlowOptions();
        value.Validate();
        return value;
    }

    private sealed record DifficultySettings(
        ChartGenerationOptions ChartOptions,
        int GoodWindowMilliseconds,
        int GreatWindowMilliseconds,
        int PerfectWindowMilliseconds)
    {
        public static DifficultySettings For(VoiceChoonDifficulty difficulty) => difficulty switch
        {
            VoiceChoonDifficulty.Easy => new(
                new ChartGenerationOptions
                {
                    MaximumPressesPerSecond = 2,
                    MaximumSimultaneousPads = 1,
                    MinimumLaneGapSeconds = 0.4,
                    RapidRunGapSeconds = 0.65
                },
                300,
                180,
                90),
            VoiceChoonDifficulty.Medium => new(
                new ChartGenerationOptions
                {
                    MaximumPressesPerSecond = 3,
                    MaximumSimultaneousPads = 1,
                    MinimumLaneGapSeconds = 0.25,
                    RapidRunGapSeconds = 0.45
                },
                250,
                140,
                70),
            VoiceChoonDifficulty.Hard => new(new ChartGenerationOptions(), 200, 120, 60),
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
        };
    }
}
