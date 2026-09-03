using System.Text.Json;
using Quizizzo.GameContracts;
using Quizizzo.Games.SlopMachine;

namespace Quizizzo.GameEngine.Tests;

public sealed class SlopMachineGameModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Embedded_manifest_contains_every_generated_thumbnail_and_machine_titles()
    {
        using var stream = typeof(SlopMachineGameModule).Assembly.GetManifestResourceStream(
            "Quizizzo.Games.SlopMachine.Assets.thumbnails.json");
        Assert.NotNull(stream);
        var thumbnails = JsonSerializer.Deserialize<SlopThumbnail[]>(stream, ManifestJsonOptions);

        Assert.NotNull(thumbnails);
        Assert.Equal(996, thumbnails.Length);
        Assert.All(thumbnails, thumbnail =>
        {
            Assert.EndsWith(".webp", thumbnail.ImageUrl, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, thumbnail.AiTitles.Count);
            Assert.All(thumbnail.AiTitles, title => Assert.False(string.IsNullOrWhiteSpace(title)));
        });
        SlopMachineGameModule.ValidateCatalogue(thumbnails);
    }

    [Fact]
    public void Intro_and_writing_advance_directly_into_voting_on_server_owned_deadlines()
    {
        var game = new Fixture(3);

        Assert.NotNull(game.State.PhaseEndsAtUtc);
        game.Deadline();
        Assert.Equal(SlopMachineGameModule.FreshIntroPhase, game.State.Phase);
        Assert.NotNull(game.State.PhaseEndsAtUtc);

        game.Deadline();
        Assert.Equal(SlopMachineGameModule.FreshWritingPhase, game.State.Phase);
        var playerView = game.PlayerView(game.PlayerIds[0]);
        Assert.Equal(PlayerControllerKind.Text, playerView.Controller.Kind);
        Assert.Equal(SubmitSlopTextAction.ActionKind, playerView.Controller.ActionKind);
        game.SubmitAllText();
        Assert.Equal(SlopMachineGameModule.FreshVotingPhase, game.State.Phase);
        Assert.NotNull(game.State.PhaseEndsAtUtc);
    }

    [Fact]
    public void Revised_default_deadlines_are_applied_to_intro_writing_voting_and_results()
    {
        var game = new Fixture(3, useDefaultDurations: true);
        Assert.Equal(TimeSpan.FromSeconds(6), game.State.PhaseEndsAtUtc - game.LastActionAt);

        game.Deadline();
        Assert.Equal(TimeSpan.FromSeconds(6), game.State.PhaseEndsAtUtc - game.LastActionAt);
        game.Deadline();
        Assert.Equal(TimeSpan.FromSeconds(40), game.State.PhaseEndsAtUtc - game.LastActionAt);
        game.SubmitAllText();
        Assert.Equal(TimeSpan.FromSeconds(15), game.State.PhaseEndsAtUtc - game.LastActionAt);
        game.VoteAll();
        Assert.Equal(TimeSpan.FromSeconds(6), game.State.PhaseEndsAtUtc - game.LastActionAt);
    }

    [Theory]
    [InlineData(SlopMachineGameModule.FreshVotingPhase)]
    [InlineData(SlopMachineGameModule.RouletteVotingPhase)]
    [InlineData(SlopMachineGameModule.TelephoneVotingPhase)]
    [InlineData(SlopMachineGameModule.CommentsVotingPhase)]
    [InlineData(SlopMachineGameModule.FinalVotingPhase)]
    public void Every_slop_voting_round_supplies_static_thumbnail_choices_not_animations(string phase)
    {
        var game = ReachVotingPhase(phase);

        Assert.Equal(phase, game.State.Phase);
        var voteView = game.PlayerView(game.PlayerIds[0]);
        Assert.Equal(PlayerControllerKind.Vote, voteView.Controller.Kind);
        var voteConfiguration = voteView.Controller.Configuration.Deserialize<VoteControllerConfiguration>();
        Assert.NotNull(voteConfiguration);
        Assert.NotEmpty(voteConfiguration.Options);
        Assert.All(voteConfiguration.Options, option =>
        {
            Assert.Null(option.FrameAssetIds);
            if (phase is SlopMachineGameModule.RouletteVotingPhase or
                SlopMachineGameModule.TelephoneVotingPhase or
                SlopMachineGameModule.CommentsVotingPhase)
            {
                Assert.False(string.IsNullOrWhiteSpace(option.ImageUrl));
            }
        });
    }

    [Fact]
    public void Fresh_slop_rejects_self_votes_and_awards_votes_plus_joint_viral_bonuses()
    {
        var game = new Fixture(3);
        game.Advance();
        game.Advance();
        game.SubmitAllText();
        var firstPlayer = game.PlayerIds[0];
        var own = game.StateData.Options.Single(option => option.AuthorId == firstPlayer);

        var selfVote = Assert.Throws<GameRuleViolationException>(() =>
            game.Apply(GameActor.Player(firstPlayer), new VoteForSlopAction(own.OptionId)));
        Assert.Equal("self-vote", selfVote.Code);

        foreach (var playerId in game.PlayerIds)
        {
            var option = game.StateData.Options.First(candidate => candidate.AuthorId != playerId);
            game.Apply(GameActor.Player(playerId), new VoteForSlopAction(option.OptionId));
        }

        Assert.Equal(SlopMachineGameModule.FreshResultsPhase, game.State.Phase);
        Assert.Equal(4000, game.LastTransition.ScoreAwards.Sum(award => award.Points));
        Assert.NotEmpty(game.StateData.Bonuses);
    }

    [Fact]
    public void Illegal_phase_duplicate_submission_and_duplicate_vote_are_rejected()
    {
        var game = new Fixture(3);
        var playerId = game.PlayerIds[0];

        var wrongPhase = Assert.Throws<GameRuleViolationException>(() =>
            game.Apply(GameActor.Player(playerId), new SubmitSlopTextAction("Too early")));
        Assert.Equal("wrong-phase", wrongPhase.Code);

        game.Advance();
        game.Advance();
        game.Apply(GameActor.Player(playerId), new SubmitSlopTextAction("First upload"));
        var duplicate = Assert.Throws<GameRuleViolationException>(() =>
            game.Apply(GameActor.Player(playerId), new SubmitSlopTextAction("Second upload")));
        Assert.Equal("already-submitted", duplicate.Code);

        game.SubmitAllText();
        var option = game.StateData.Options.First(candidate => candidate.AuthorId != playerId);
        game.Apply(GameActor.Player(playerId), new VoteForSlopAction(option.OptionId));
        var duplicateVote = Assert.Throws<GameRuleViolationException>(() =>
            game.Apply(GameActor.Player(playerId), new VoteForSlopAction(option.OptionId)));
        Assert.Equal("already-voted", duplicateVote.Code);
    }

    [Fact]
    public void Writing_timeout_skips_missing_uploads_without_blocking_the_game()
    {
        var game = new Fixture(3);
        game.Advance();
        game.Advance();

        game.Deadline();
        Assert.Equal(SlopMachineGameModule.FreshResultsPhase, game.State.Phase);
        Assert.Empty(game.StateData.Options);
        Assert.Empty(game.LastTransition.ScoreAwards);
    }

    [Fact]
    public void Roulette_allows_one_two_reel_respin_and_server_constructs_the_assigned_format()
    {
        var game = new Fixture(3);
        game.ReachRouletteSpinning();
        var playerId = game.PlayerIds[0];
        var before = game.StateData.Assignments[playerId];

        var invalidReel = Assert.Throws<GameRuleViolationException>(() =>
            game.Apply(GameActor.Player(playerId), new RespinSlopReelAction("not-a-reel")));
        Assert.Equal("invalid-reel", invalidReel.Code);

        game.Apply(GameActor.Player(playerId), new RespinSlopReelAction("format"));

        var after = game.StateData.Assignments[playerId];
        Assert.True(after.RespinUsed);
        Assert.NotEqual(before.Format, after.Format);
        var repeated = Assert.Throws<GameRuleViolationException>(() =>
            game.Apply(GameActor.Player(playerId), new RespinSlopReelAction("thumbnail")));
        Assert.Equal("respin-used", repeated.Code);

        game.Advance();
        var invalid = Assert.Throws<GameRuleViolationException>(() =>
            game.Apply(GameActor.Player(playerId), new SubmitSlopTextAction("Too short")));
        Assert.Equal("invalid-format", invalid.Code);

        var blankCount = game.StateData.Assignments[playerId].Format.Split("___").Length - 1;
        game.Apply(GameActor.Player(playerId), new SubmitSlopTextAction(
            "This forged complete title is ignored",
            Enumerable.Range(1, blankCount).Select(index => $"answer {index}").ToArray()));
        Assert.DoesNotContain("___", game.StateData.TextSubmissions[playerId], StringComparison.Ordinal);
        Assert.DoesNotContain("forged", game.StateData.TextSubmissions[playerId], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Telephone_uses_a_derangement_unique_decoys_and_objective_match_scoring()
    {
        var game = new Fixture(4);
        game.ReachTelephoneWriting();
        game.SubmitAllText();

        Assert.Equal(SlopMachineGameModule.TelephoneMatchingPhase, game.State.Phase);
        Assert.All(game.StateData.TelephoneMatches, pair =>
        {
            Assert.NotEqual(pair.Key, pair.Value.WriterId);
            Assert.Equal(4, pair.Value.OptionThumbnailIds.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(pair.Value.IntendedThumbnailId, pair.Value.OptionThumbnailIds);
        });

        foreach (var playerId in game.StateData.TelephoneMatches.Keys.ToArray())
        {
            var intended = game.StateData.TelephoneMatches[playerId].IntendedThumbnailId;
            game.Apply(GameActor.Player(playerId), new MatchTelephoneThumbnailAction(intended));
        }

        Assert.Equal(SlopMachineGameModule.TelephoneVotingPhase, game.State.Phase);
        Assert.Equal(8, game.LastTransition.ScoreAwards.Count);
        Assert.All(game.LastTransition.ScoreAwards, award => Assert.Equal(1500, award.Points));
    }

    [Fact]
    public void Slop_machine_rejects_two_players_and_supports_three_to_twelve()
    {
        Assert.Throws<GameRuleViolationException>(() => new Fixture(2));
        Assert.Equal(3, new Fixture(3).StateData.Participants.Count);
        Assert.Equal(12, new Fixture(12).StateData.Participants.Count);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(6, 6)]
    [InlineData(7, 4)]
    [InlineData(12, 4)]
    public void Voting_uses_all_entries_up_to_six_then_balanced_four_entry_heats(
        int playerCount,
        int expectedFirstHeatSize)
    {
        var game = new Fixture(playerCount);
        game.Advance();
        game.Advance();
        game.SubmitAllText();

        Assert.Equal(expectedFirstHeatSize, game.StateData.Options.Count);
        if (playerCount >= 7)
        {
            Assert.All(game.StateData.VoteHeats!, heat => Assert.InRange(heat.Count, 3, 4));
        }
        else
        {
            Assert.Single(game.StateData.VoteHeats!);
        }
        Assert.All(game.StateData.VotingOpportunities!, opportunity =>
            Assert.Equal(playerCount - 1, opportunity.Value));
    }

    [Fact]
    public void Final_always_shows_every_human_title_plus_two_machine_titles()
    {
        var game = new Fixture(12);

        game.ReachFinalVoting();

        Assert.Equal(14, game.StateData.Options.Count);
        Assert.Single(game.StateData.VoteHeats!);
    }

    [Fact]
    public void Roulette_uses_a_five_second_growth_overlay_instead_of_a_scoreboard()
    {
        var game = new Fixture(3);

        game.ReachRouletteGrowthReview();
        var display = game.DisplayView();

        Assert.Equal(SlopMachineGameModule.ScoreReview2Phase, game.State.Phase);
        Assert.False(display.ShowRoundRanking);
        Assert.Contains("GAINED", display.Prompt, StringComparison.Ordinal);
        Assert.Contains("NOW TRENDING AT #", display.PhaseMessage, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(5), game.State.PhaseEndsAtUtc - game.LastActionAt);
    }

    [Fact]
    public void Telephone_matcher_may_vote_for_the_pairing_they_matched()
    {
        var game = new Fixture(3);
        game.ReachTelephoneWriting();
        game.SubmitAllText();
        game.SubmitAllTelephoneMatches();
        var matcher = game.PlayerIds[0];
        var pairing = game.StateData.Options.Single(option => option.PartnerId == matcher);

        game.Apply(GameActor.Player(matcher), new VoteForSlopAction(pairing.OptionId));

        Assert.Equal(pairing.OptionId, game.StateData.Votes[matcher]);
    }

    [Fact]
    public void Telephone_popularity_points_go_only_to_the_title_writer()
    {
        var game = new Fixture(3);
        game.ReachTelephoneWriting();
        game.SubmitAllText();
        game.SubmitAllTelephoneMatches();
        var target = game.StateData.Options[0];
        var writer = Assert.IsType<Guid>(target.AuthorId);
        var matcher = Assert.IsType<Guid>(target.PartnerId);

        foreach (var playerId in game.PlayerIds)
        {
            var option = playerId == writer
                ? game.StateData.Options.First(candidate =>
                    candidate.AuthorId != playerId && candidate.AuthorId != matcher)
                : target;
            game.Apply(GameActor.Player(playerId), new VoteForSlopAction(option.OptionId));
        }

        Assert.Contains(game.LastTransition.ScoreAwards, award =>
            award.PlayerId == writer && award.Reason == "Telephone pairing votes");
        Assert.DoesNotContain(game.LastTransition.ScoreAwards, award =>
            award.PlayerId == matcher &&
            award.Reason is "Telephone pairing votes" or "Telephone Disaster Bonus");
    }

    [Fact]
    public void Comments_assign_returning_uploads_away_from_their_creators_when_possible()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var game = new Fixture(4);

            game.ReachCommentsWriting();

            Assert.Equal(SlopMachineGameModule.CommentsWritingPhase, game.State.Phase);
            Assert.All(game.StateData.Assignments, assignment =>
            {
                var sourceSubmissionId = Assert.IsType<Guid>(assignment.Value.SourceSubmissionId);
                var upload = Assert.Single(game.StateData.Uploads,
                    item => item.SubmissionId == sourceSubmissionId);
                Assert.NotEqual(assignment.Key, upload.AuthorId);
            });
        }
    }

    [Fact]
    public void Comments_reveal_keeps_video_title_comment_type_and_comment_together()
    {
        var game = new Fixture(4);
        game.ReachCommentsWriting();
        game.SubmitAllText();

        var display = game.DisplayView();

        Assert.Equal(SlopMachineGameModule.CommentsVotingPhase, game.State.Phase);
        Assert.Equal("comment-feed", display.Media!.Mode);
        Assert.All(display.Media.Items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Heading));
            Assert.False(string.IsNullOrWhiteSpace(item.Body));
            Assert.False(string.IsNullOrWhiteSpace(item.Badge));
        });
    }

    [Fact]
    public void Score_review_exposes_locked_round_delta_and_view_units()
    {
        var game = new Fixture(3);

        game.ReachFirstScoreReview();
        var display = game.DisplayView();

        Assert.Equal(SlopMachineGameModule.ScoreReview1Phase, game.State.Phase);
        Assert.True(display.ShowRoundRanking);
        Assert.Equal("views", display.ScoreUnit);
        Assert.All(display.Entries, entry =>
            Assert.Equal(game.StateData.EarnedViews.GetValueOrDefault(entry.PlayerId) -
                game.StateData.ScoreReviewStart.GetValueOrDefault(entry.PlayerId), entry.PointsAwarded));
    }

    [Fact]
    public void Complete_game_is_reconstructable_and_reaches_winner_celebration_then_completion()
    {
        var game = new Fixture(3);
        game.PlayCompleteGame();

        Assert.True(game.State.IsComplete);
        Assert.Equal(SlopMachineGameModule.CompletedPhase, game.State.Phase);
        Assert.Equal(game.PlayerIds.Length, game.StateData.EarnedViews.Count);
        Assert.Equal(game.StateData.UsedThumbnailIds.Count,
            game.StateData.UsedThumbnailIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(12)]
    public void Complete_game_supports_minimum_and_platform_maximum_player_counts(int playerCount)
    {
        var game = new Fixture(playerCount);

        game.PlayCompleteGame();

        Assert.True(game.State.IsComplete);
        Assert.Equal(playerCount, game.StateData.Participants.Count);
    }

    [Fact]
    public void Text_is_normalized_bounded_and_safely_returned_as_data()
    {
        var game = new Fixture(3);
        game.Advance();
        game.Advance();
        game.Apply(GameActor.Player(game.PlayerIds[0]),
            new SubmitSlopTextAction("  A   title <script>alert(1)</script>  "));

        Assert.Equal("A title <script>alert(1)</script>",
            game.StateData.TextSubmissions[game.PlayerIds[0]]);
        var tooLong = Assert.Throws<GameRuleViolationException>(() => game.Apply(
            GameActor.Player(game.PlayerIds[1]), new SubmitSlopTextAction(new string('x', 91))));
        Assert.Equal("invalid-text", tooLong.Code);
    }

    [Fact]
    public void Final_human_winner_receives_humanity_bonus()
    {
        var game = new Fixture(3);
        game.ReachFinalVoting();
        var target = game.StateData.Options.Single(option => option.AuthorId == game.PlayerIds[0]);
        foreach (var playerId in game.PlayerIds)
        {
            var selection = playerId == target.AuthorId
                ? game.StateData.Options.First(option => option.AuthorId.HasValue && option.AuthorId != playerId)
                : target;
            game.Apply(GameActor.Player(playerId), new VoteForSlopAction(selection.OptionId));
        }

        Assert.False(game.StateData.MachineWonFinal);
        Assert.Contains(game.LastTransition.ScoreAwards, award =>
            award.PlayerId == target.AuthorId && award.Points == 3000 &&
            award.Reason == "Humanity Bonus");
    }

    [Fact]
    public void Final_machine_winner_denies_humanity_bonus_and_identification_pays_two_thousand_views()
    {
        var game = new Fixture(3);
        game.ReachFinalVoting();
        var machine = game.StateData.Options.First(option => option.IsMachine);
        foreach (var playerId in game.PlayerIds)
        {
            game.Apply(GameActor.Player(playerId), new VoteForSlopAction(machine.OptionId));
        }

        Assert.True(game.StateData.MachineWonFinal);
        Assert.DoesNotContain(game.LastTransition.ScoreAwards, award => award.Reason == "Humanity Bonus");
        var machineOptions = game.StateData.Options.Where(option => option.IsMachine).ToArray();
        foreach (var playerId in game.PlayerIds)
        {
            game.Apply(GameActor.Player(playerId), new IdentifyMachineTitleAction(machineOptions[0].OptionId));
            game.Apply(GameActor.Player(playerId), new IdentifyMachineTitleAction(machineOptions[1].OptionId));
        }
        Assert.All(game.LastTransition.ScoreAwards, award => Assert.Equal(2000, award.Points));
    }

    [Fact]
    public void Tied_best_human_titles_each_receive_the_humanity_bonus()
    {
        var game = new Fixture(4);
        game.ReachFinalVoting();
        var first = game.StateData.Options.Single(option => option.AuthorId == game.PlayerIds[0]);
        var second = game.StateData.Options.Single(option => option.AuthorId == game.PlayerIds[1]);

        game.Apply(GameActor.Player(game.PlayerIds[0]), new VoteForSlopAction(second.OptionId));
        game.Apply(GameActor.Player(game.PlayerIds[1]), new VoteForSlopAction(first.OptionId));
        game.Apply(GameActor.Player(game.PlayerIds[2]), new VoteForSlopAction(first.OptionId));
        game.Apply(GameActor.Player(game.PlayerIds[3]), new VoteForSlopAction(second.OptionId));

        var humanityBonuses = game.LastTransition.ScoreAwards
            .Where(award => award.Reason == "Humanity Bonus").ToArray();
        Assert.Equal(2, humanityBonuses.Length);
        Assert.Contains(humanityBonuses, award => award.PlayerId == game.PlayerIds[0]);
        Assert.Contains(humanityBonuses, award => award.PlayerId == game.PlayerIds[1]);
    }

    [Fact]
    public void Machine_tied_with_a_human_is_not_a_machine_victory()
    {
        var game = new Fixture(4);
        game.ReachFinalVoting();
        var human = game.StateData.Options.Single(option => option.AuthorId == game.PlayerIds[0]);
        var machine = game.StateData.Options.First(option => option.IsMachine);

        game.Apply(GameActor.Player(game.PlayerIds[0]), new VoteForSlopAction(machine.OptionId));
        game.Apply(GameActor.Player(game.PlayerIds[1]), new VoteForSlopAction(human.OptionId));
        game.Apply(GameActor.Player(game.PlayerIds[2]), new VoteForSlopAction(machine.OptionId));
        game.Apply(GameActor.Player(game.PlayerIds[3]), new VoteForSlopAction(human.OptionId));

        Assert.False(game.StateData.MachineWonFinal);
        Assert.Contains(game.LastTransition.ScoreAwards, award =>
            award.PlayerId == human.AuthorId && award.Points == 3000 &&
            award.Reason == "Humanity Bonus");
    }

    [Fact]
    public void Catalogue_validation_rejects_duplicate_ids_and_missing_machine_titles()
    {
        var valid = Fixture.TestCatalogue();
        var duplicate = valid.ToArray();
        duplicate[1] = duplicate[1] with { Id = duplicate[0].Id };
        Assert.Throws<InvalidOperationException>(() => SlopMachineGameModule.ValidateCatalogue(duplicate));

        var missingTitles = valid.ToArray();
        missingTitles[0] = missingTitles[0] with { AiTitles = ["Only one"] };
        Assert.Throws<InvalidOperationException>(() => SlopMachineGameModule.ValidateCatalogue(missingTitles));
    }

    private static Fixture ReachVotingPhase(string phase)
    {
        var game = new Fixture(3);
        switch (phase)
        {
            case SlopMachineGameModule.FreshVotingPhase:
                game.Advance();
                game.Advance();
                game.SubmitAllText();
                break;
            case SlopMachineGameModule.RouletteVotingPhase:
                game.ReachRouletteSpinning();
                game.Advance();
                game.SubmitAllText();
                break;
            case SlopMachineGameModule.TelephoneVotingPhase:
                game.ReachTelephoneWriting();
                game.SubmitAllText();
                game.SubmitAllTelephoneMatches();
                break;
            case SlopMachineGameModule.CommentsVotingPhase:
                game.ReachCommentsWriting();
                game.SubmitAllText();
                break;
            case SlopMachineGameModule.FinalVotingPhase:
                game.ReachFinalVoting();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
        }
        return game;
    }

    private sealed class Fixture
    {
        private readonly SlopMachineGameModule module;
        private readonly Guid instanceId = Guid.NewGuid();
        private readonly Guid partyId = Guid.NewGuid();
        private DateTimeOffset now = Now;

        public Fixture(int playerCount, bool useDefaultDurations = false)
        {
            module = useDefaultDurations
                ? new SlopMachineGameModule(TestCatalogue())
                : new SlopMachineGameModule(
                    TestCatalogue(), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            PlayerIds = Enumerable.Range(0, playerCount).Select(_ => Guid.NewGuid()).ToArray();
            LastActionAt = now;
            State = module.Start(new GameStartContext(new GameInstanceId(instanceId), partyId, "host",
                PlayerIds.Select((id, index) => new GameParticipant(id, $"Player {index + 1}"))
                    .ToArray(), now));
        }

        public Guid[] PlayerIds { get; }
        public GameModuleState State { get; private set; }
        public GameTransition LastTransition { get; private set; } = default!;
        public DateTimeOffset LastActionAt { get; private set; }
        public SlopMachineState StateData => State.Data.Deserialize<SlopMachineState>()!;

        public void Apply(GameActor actor, IGameAction action)
        {
            LastActionAt = now;
            LastTransition = module.Apply(State,
                new GameActionContext(new GameInstanceId(instanceId), partyId, actor, now), action);
            State = LastTransition.State;
            now = now.AddMilliseconds(10);
        }

        public void Advance() => Apply(GameActor.Host("host"), new AdvanceSlopMachineAction());

        public void Deadline()
        {
            now = State.PhaseEndsAtUtc ?? now;
            Apply(GameActor.SystemActor, new DeadlineElapsedAction(now));
        }

        public void SubmitAllText()
        {
            foreach (var playerId in PlayerIds.Where(id => !StateData.TextSubmissions.ContainsKey(id)))
            {
                if (State.Phase == SlopMachineGameModule.RouletteWritingPhase)
                {
                    var assignment = StateData.Assignments[playerId];
                    var blankCount = assignment.Format.Split("___").Length - 1;
                    var values = Enumerable.Range(1, blankCount)
                        .Select(index => $"answer {PlayerIds.ToList().IndexOf(playerId) + 1}-{index}")
                        .ToArray();
                    Apply(GameActor.Player(playerId), new SubmitSlopTextAction(string.Empty, values));
                }
                else
                {
                    Apply(GameActor.Player(playerId), new SubmitSlopTextAction(
                        $"A funny upload from {PlayerIds.ToList().IndexOf(playerId) + 1}"));
                }
            }
        }

        public void VoteAll()
        {
            foreach (var playerId in PlayerIds)
            {
                var options = StateData.Options.Where(option =>
                    option.AuthorId != playerId).ToArray();
                if (options.Length > 0 && !StateData.Votes.ContainsKey(playerId))
                {
                    Apply(GameActor.Player(playerId), new VoteForSlopAction(options[0].OptionId));
                }
            }
            if (State.Phase.EndsWith("Voting", StringComparison.Ordinal))
            {
                Deadline();
            }
        }

        public void SubmitAllTelephoneMatches()
        {
            foreach (var playerId in StateData.TelephoneMatches.Keys.ToArray())
            {
                var match = StateData.TelephoneMatches[playerId];
                Apply(GameActor.Player(playerId), new MatchTelephoneThumbnailAction(match.IntendedThumbnailId));
            }
        }

        public void ReachRouletteSpinning()
        {
            PlayFresh();
            Advance();
            Advance();
        }

        public void ReachTelephoneWriting()
        {
            ReachRouletteGrowthReview();
            Advance();
            Advance();
        }

        public void ReachRouletteGrowthReview()
        {
            ReachRouletteSpinning();
            Advance();
            SubmitAllText();
            VoteAll();
            CompleteRemainingVoteHeats(SlopMachineGameModule.RouletteResultsPhase);
            Advance();
        }

        public void ReachCommentsWriting()
        {
            ReachTelephoneWriting();
            SubmitAllText();
            foreach (var playerId in StateData.TelephoneMatches.Keys.ToArray())
            {
                var match = StateData.TelephoneMatches[playerId];
                Apply(GameActor.Player(playerId), new MatchTelephoneThumbnailAction(match.IntendedThumbnailId));
            }
            VoteAll();
            CompleteRemainingVoteHeats(SlopMachineGameModule.TelephoneResultsPhase);
            Advance();
            Advance();
            Advance();
        }

        public void ReachFirstScoreReview() => PlayFresh();

        public DisplayGameViewPayload DisplayView() => module.CreateView(
                State, new GameViewContext(GameAudienceRole.Display, "display", null))
            .Data.Deserialize<DisplayGameViewPayload>()!;

        public PlayerGameViewPayload PlayerView(Guid playerId) => module.CreateView(
                State, new GameViewContext(GameAudienceRole.Player, playerId.ToString(), playerId))
            .Data.Deserialize<PlayerGameViewPayload>()!;

        public void PlayCompleteGame()
        {
            ReachFinalVoting();
            VoteAll();
            foreach (var playerId in PlayerIds)
            {
                foreach (var option in StateData.Options.Where(option => option.IsMachine).Take(2))
                {
                    Apply(GameActor.Player(playerId), new IdentifyMachineTitleAction(option.OptionId));
                }
            }
            Advance();
            Advance();
            Advance();
        }

        public void ReachFinalVoting()
        {
            ReachCommentsWriting();
            SubmitAllText();
            VoteAll();
            CompleteRemainingVoteHeats(SlopMachineGameModule.CommentsResultsPhase);
            Advance();
            Advance();
            Advance();
            SubmitAllText();
        }

        private void PlayFresh()
        {
            Advance();
            Advance();
            SubmitAllText();
            VoteAll();
            CompleteRemainingVoteHeats(SlopMachineGameModule.FreshResultsPhase);
            Advance();
            SubmitAllText();
            VoteAll();
            CompleteRemainingVoteHeats(SlopMachineGameModule.FreshResultsPhase);
            Advance();
        }

        private bool HasNextVoteHeat() => StateData.VoteHeats is { Count: > 0 } &&
            StateData.VoteHeat + 1 < StateData.VoteHeats.Count;

        private void CompleteRemainingVoteHeats(string resultPhase)
        {
            while (State.Phase == resultPhase && HasNextVoteHeat())
            {
                Advance();
                VoteAll();
            }
        }

        public static SlopThumbnail[] TestCatalogue() => Enumerable.Range(1, 160).Select(index =>
            new SlopThumbnail(
                $"test-{index:D3}",
                $"/media/games/slop-machine/thumbnails/test-{index:D3}.webp",
                $"Test thumbnail {index}",
                index % 3 == 0 ? "animals" : "chaos",
                index % 2 == 0 ? "close reaction" : "wide action",
                [$"Machine title {index} A", $"Machine title {index} B"])).ToArray();
    }
}
