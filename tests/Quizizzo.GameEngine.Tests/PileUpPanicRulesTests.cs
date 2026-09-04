using System.Text.Json;
using Quizizzo.Games.PileUpPanic;

namespace Quizizzo.GameEngine.Tests;

public sealed class PileUpPanicRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Playable_catalogue_excludes_the_most_obtuse_legacy_clusters()
    {
        Assert.Equal(13, ScrapClusterCatalogue.All.Count);
        Assert.Equal(11, ScrapClusterCatalogue.Playable.Count);
        Assert.DoesNotContain(ScrapClusterCatalogue.Playable, cluster => cluster.Key == "flag-post");
        Assert.DoesNotContain(ScrapClusterCatalogue.Playable, cluster => cluster.Key == "split-anvil");
        Assert.Contains(ScrapClusterCatalogue.Playable, cluster => cluster.Cells.Count == 2);
        Assert.True(ScrapClusterCatalogue.Playable.Count(cluster => cluster.Cells.Count == 3) >= 3);
        Assert.All(ScrapClusterCatalogue.Playable, cluster =>
            Assert.Equal(cluster.Cells.Count, cluster.Cells.Distinct().Count()));
    }

    [Fact]
    public void Scrap_stream_is_deterministic_recoverable_and_avoids_recent_repeats()
    {
        var first = new DeterministicScrapSequence(42);
        var sequence = Enumerable.Range(0, 24).Select(_ => first.Next()).ToArray();
        var second = new DeterministicScrapSequence(42);

        Assert.Equal(sequence, Enumerable.Range(0, 24).Select(_ => second.Next()).ToArray());
        Assert.All(Enumerable.Range(4, sequence.Length - 4), index =>
            Assert.DoesNotContain(sequence[index].ClusterKey, sequence[(index - 4)..index].Select(item => item.ClusterKey)));
        Assert.All(sequence, item =>
            Assert.Contains(ScrapClusterCatalogue.Playable, cluster => cluster.Key == item.ClusterKey));

        var recoverable = new DeterministicScrapSequence(91);
        _ = Enumerable.Range(0, 7).Select(_ => recoverable.Next()).ToArray();
        var restored = new DeterministicScrapSequence(recoverable.Capture());
        Assert.Equal(recoverable.Next(), restored.Next());

        var legacy = new DeterministicScrapSequence(new ScrapStreamState(
            42,
            [10, 11],
            Enumerable.Range(0, MaterialPalette.All.Count).ToArray(),
            MaterialPalette.All.Count));
        Assert.Contains(ScrapClusterCatalogue.Playable,
            cluster => cluster.Key == legacy.Next().ClusterKey);
    }

    [Fact]
    public void Material_order_is_shuffled_independently_from_cluster_identity()
    {
        var sequence = new DeterministicScrapSequence(17);
        var scraps = Enumerable.Range(0, MaterialPalette.All.Count).Select(_ => sequence.Next()).ToArray();

        Assert.Equal(MaterialPalette.All.Count, scraps.Select(item => item.Material).Distinct().Count());
        Assert.NotEqual(scraps.Select(item => item.ClusterKey), scraps.Select(item => item.Material));
    }

    [Fact]
    public void Rotation_uses_bounded_generic_correction_near_wall_and_rejects_blocked_result()
    {
        var clear = new ArenaGrid();
        var nearWall = new ActiveScrap("long-hook", "copper", 7, 3, 0);
        var corrected = ScrapPhysics.TryRotateClockwise(clear, nearWall);

        Assert.NotNull(corrected);
        Assert.True(corrected.X < nearWall.X);
        Assert.True(clear.CanOccupy(corrected.OccupiedCells()));

        var blocked = new ArenaGrid();
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < PileUpOptions.Columns; column++)
            {
                blocked[column, row] = "junk";
            }
        }
        Assert.Null(ScrapPhysics.TryRotateClockwise(blocked, nearWall));
    }

    [Fact]
    public void Collision_instant_drop_and_lock_are_server_owned()
    {
        var arena = new PileArena(Guid.NewGuid(), "Ada", 5);
        var activeCells = arena.Active!.OccupiedCells();
        Assert.False(arena.TryMove(-PileUpOptions.Columns, 0));

        var distance = arena.InstantDrop();
        var outcome = arena.LockActive();

        Assert.True(distance > 0);
        Assert.False(outcome.Overloaded);
        Assert.True(arena.Grid.OccupiedCells().Count >= activeCells.Count);
        Assert.NotNull(arena.Active);
        Assert.Equal(2, arena.Upcoming.Count);
    }

    [Fact]
    public void Stash_swaps_once_per_drop_and_restores_availability_after_lock()
    {
        var arena = new PileArena(Guid.NewGuid(), "Ada", 5);
        var original = arena.Active!;

        Assert.True(arena.StashActive());
        Assert.False(arena.StashAvailable);
        Assert.Equal(original.ClusterKey, arena.Stashed!.ClusterKey);
        Assert.False(arena.StashActive());

        arena.InstantDrop();
        arena.LockActive();

        Assert.True(arena.StashAvailable);
        Assert.NotNull(arena.Active);
    }

    [Fact]
    public void Grounded_cluster_observes_server_lock_delay_and_restores_mid_delay()
    {
        var options = new PileUpOptions
        {
            InitialFallInterval = TimeSpan.FromMilliseconds(50),
            MinimumFallInterval = TimeSpan.FromMilliseconds(50),
            LockDelay = TimeSpan.FromMilliseconds(200),
            SimulationStep = TimeSpan.FromMilliseconds(50)
        };
        var fixture = new MatchFixture(2, options);
        _ = fixture.Match.Arenas[fixture.PlayerIds[0]].InstantDrop();

        fixture.Match.AdvanceSimulation(Now.AddMilliseconds(50));
        var restored = PileUpMatch.Restore(fixture.Match.CaptureState());
        fixture.Match.AdvanceSimulation(Now.AddMilliseconds(200));
        restored.AdvanceSimulation(Now.AddMilliseconds(200));
        Assert.Empty(fixture.Match.Arenas[fixture.PlayerIds[0]].Grid.OccupiedCells());
        Assert.Empty(restored.Arenas[fixture.PlayerIds[0]].Grid.OccupiedCells());

        fixture.Match.AdvanceSimulation(Now.AddMilliseconds(250));
        restored.AdvanceSimulation(Now.AddMilliseconds(250));
        Assert.NotEmpty(fixture.Match.Arenas[fixture.PlayerIds[0]].Grid.OccupiedCells());
        Assert.Equal(
            JsonSerializer.Serialize(fixture.Match.CaptureState()),
            JsonSerializer.Serialize(restored.CaptureState()));
    }

    [Fact]
    public void Fall_speed_starts_slow_and_accelerates_per_shared_circuit()
    {
        var options = new PileUpOptions();

        Assert.Equal(TimeSpan.FromMilliseconds(1100), options.FallIntervalFor(0));
        Assert.Equal(TimeSpan.FromMilliseconds(1050), options.FallIntervalFor(1));
        Assert.Equal(TimeSpan.FromMilliseconds(850), options.FallIntervalFor(5));
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.FallIntervalFor(20));
    }

    [Fact]
    public void One_players_completed_circuit_accelerates_every_arena_together()
    {
        var options = new PileUpOptions
        {
            InitialFallInterval = TimeSpan.FromMilliseconds(1000),
            MinimumFallInterval = TimeSpan.FromMilliseconds(200),
            SpeedUpBy = TimeSpan.FromMilliseconds(100),
            SimulationStep = TimeSpan.FromMilliseconds(50)
        };
        var fixture = new MatchFixture(2, options);
        var state = fixture.Match.CaptureState();
        var players = state.Players.ToArray();
        players[0] = players[0] with
        {
            Arena = players[0].Arena with { CircuitsCompleted = 1 }
        };
        var accelerated = PileUpMatch.Restore(state with { Players = players });
        var startingRows = accelerated.Arenas.Values
            .ToDictionary(arena => arena.PlayerId, arena => arena.Active!.Y);

        accelerated.AdvanceSimulation(Now.AddMilliseconds(900));

        Assert.All(accelerated.Arenas.Values, arena =>
            Assert.Equal(startingRows[arena.PlayerId] + 1, arena.Active!.Y));
    }

    [Fact]
    public void Completed_circuit_is_removed_and_cells_above_collapse()
    {
        var grid = new ArenaGrid();
        var bottom = PileUpOptions.TotalRows - 1;
        for (var column = 0; column < PileUpOptions.Columns; column++)
        {
            grid[column, bottom] = "copper";
        }
        grid[3, bottom - 1] = "aqua";

        Assert.Equal(1, grid.CompleteAndCollapseCircuits());
        Assert.Equal("aqua", grid[3, bottom]);
        Assert.Null(grid[3, bottom - 1]);
    }

    [Fact]
    public void Multiple_circuits_complete_simultaneously_and_collapse_once()
    {
        var grid = new ArenaGrid();
        var bottom = PileUpOptions.TotalRows - 1;
        for (var row = bottom - 1; row <= bottom; row++)
        {
            for (var column = 0; column < PileUpOptions.Columns; column++)
            {
                grid[column, row] = "violet";
            }
        }
        grid[8, bottom - 2] = "mint";

        Assert.Equal(2, grid.CompleteAndCollapseCircuits());
        Assert.Equal("mint", grid[8, bottom]);
        Assert.Single(grid.OccupiedCells());
    }

    [Fact]
    public void Repeated_circuit_completions_fill_charge_and_deal_only_one_ability()
    {
        var arena = new PileArena(Guid.NewGuid(), "Circuit", 71);
        for (var completion = 0; completion < 3; completion++)
        {
            arena.InstantDrop();
            var activeBottomColumns = arena.Active!.OccupiedCells()
                .Where(cell => cell.Y == PileUpOptions.TotalRows - 1)
                .Select(cell => cell.X)
                .ToHashSet();
            for (var column = 0; column < PileUpOptions.Columns; column++)
            {
                if (!activeBottomColumns.Contains(column) &&
                    arena.Grid[column, PileUpOptions.TotalRows - 1] is null)
                {
                    arena.Grid[column, PileUpOptions.TotalRows - 1] = "copper";
                }
            }

            Assert.True(arena.LockActive().CircuitsCompleted >= 1);
        }

        Assert.NotNull(arena.AvailableAbility);
        Assert.Equal(0, arena.ChaosCharge);
    }

    [Fact]
    public void Hidden_spawn_cells_signal_overload()
    {
        var grid = new ArenaGrid();
        grid[4, PileUpOptions.HiddenRows - 1] = "coral";

        Assert.True(grid.HasHiddenCells());
    }

    [Fact]
    public void Junk_circuit_has_one_open_cell_and_pushes_stack_up()
    {
        var grid = new ArenaGrid();
        var bottom = PileUpOptions.TotalRows - 1;
        grid[0, bottom] = "copper";

        Assert.True(grid.AddJunkCircuit(4));
        Assert.Equal("copper", grid[0, bottom - 1]);
        Assert.Null(grid[4, bottom]);
        Assert.Equal(PileUpOptions.Columns - 1,
            Enumerable.Range(0, PileUpOptions.Columns).Count(column => grid[column, bottom] == "junk"));
    }

    [Fact]
    public void Junk_queue_and_short_window_caps_prevent_attack_cascades()
    {
        var standalone = new PileArena(Guid.NewGuid(), "Bounded", 19);
        standalone.QueueJunk(20, 4);
        Assert.Equal(4, standalone.QueuedJunk);

        var fixture = new MatchFixture(4, new PileUpOptions
        {
            MaximumQueuedJunk = 4,
            MaximumJunkPerWindow = 2
        });
        var target = fixture.PlayerIds[3];
        foreach (var source in fixture.PlayerIds.Take(3))
        {
            fixture.Match.Arenas[source].GrantAbilityForTesting(ChaosAbility.SendJunk);
            fixture.Input(source, PileInputType.ActivateAbility, target);
        }

        Assert.Equal(2, fixture.Match.Arenas[target].QueuedJunk);
    }

    [Fact]
    public void Shield_consumes_one_attack_without_applying_junk()
    {
        var fixture = new MatchFixture(2);
        var source = fixture.PlayerIds[0];
        var target = fixture.PlayerIds[1];
        fixture.Match.Arenas[target].GrantAbilityForTesting(ChaosAbility.Shield);
        fixture.Input(target, PileInputType.ActivateAbility);
        fixture.Match.Arenas[source].GrantAbilityForTesting(ChaosAbility.SendJunk);

        Assert.Equal(InputResultKind.Applied,
            fixture.Input(source, PileInputType.ActivateAbility, target).Kind);
        Assert.False(fixture.Match.Arenas[target].Shielded);
        Assert.Equal(0, fixture.Match.Arenas[target].QueuedJunk);
        Assert.Contains(fixture.Match.Events, item => item.Kind == "AbilityBlocked" && item.TargetPlayerId == target);
    }

    [Fact]
    public void Junk_is_announced_then_applied_only_after_the_target_locks()
    {
        var fixture = new MatchFixture(2);
        var source = fixture.PlayerIds[0];
        var target = fixture.PlayerIds[1];
        fixture.Match.Arenas[source].GrantAbilityForTesting(ChaosAbility.SendJunk);

        fixture.Input(source, PileInputType.ActivateAbility, target);
        Assert.Equal(1, fixture.Match.Arenas[target].QueuedJunk);
        Assert.Contains(fixture.Match.Events, item =>
            item.Kind == "IncomingJunk" && item.TargetPlayerId == target);
        Assert.DoesNotContain(fixture.Match.Events, item => item.Kind == "JunkApplied");

        fixture.Input(target, PileInputType.InstantDrop);
        Assert.Equal(0, fixture.Match.Arenas[target].QueuedJunk);
        Assert.Contains(fixture.Match.Events, item =>
            item.Kind == "JunkApplied" && item.PlayerId == target);
    }

    [Fact]
    public void Scramble_replaces_only_upcoming_clusters()
    {
        var fixture = new MatchFixture(2);
        var source = fixture.PlayerIds[0];
        var target = fixture.PlayerIds[1];
        var active = fixture.Match.Arenas[target].Active;
        var before = fixture.Match.Arenas[target].Upcoming.ToArray();
        fixture.Match.Arenas[source].GrantAbilityForTesting(ChaosAbility.ScrambleQueue);

        fixture.Input(source, PileInputType.ActivateAbility, target);

        Assert.Equal(active, fixture.Match.Arenas[target].Active);
        Assert.NotEqual(before, fixture.Match.Arenas[target].Upcoming);
    }

    [Fact]
    public void Offensive_ability_rejects_self_and_overloaded_targets()
    {
        var fixture = new MatchFixture(3);
        var source = fixture.PlayerIds[0];
        fixture.Match.Arenas[source].GrantAbilityForTesting(ChaosAbility.SendJunk);

        Assert.Equal(InputResultKind.Ignored,
            fixture.Input(source, PileInputType.ActivateAbility, source).Kind);
        Assert.NotNull(fixture.Match.Arenas[source].AvailableAbility);

        var overloaded = fixture.PlayerIds[1];
        fixture.Match.Arenas[overloaded].ForceOverload();
        Assert.Equal(InputResultKind.Ignored,
            fixture.Input(source, PileInputType.ActivateAbility, overloaded).Kind);
    }

    [Fact]
    public void Duplicate_stale_and_rate_exceeding_inputs_are_rejected()
    {
        var options = new PileUpOptions { InputLimitPerSecond = 2 };
        var fixture = new MatchFixture(2, options);
        var player = fixture.PlayerIds[0];

        Assert.Equal(InputResultKind.Applied, fixture.Input(player, PileInputType.MoveLeft, sequence: 5).Kind);
        Assert.Equal("duplicate-input", fixture.Input(player, PileInputType.MoveLeft, sequence: 5).Code);
        Assert.Equal("stale-input", fixture.Input(player, PileInputType.MoveLeft, sequence: 4).Code);
        Assert.Equal(InputResultKind.Applied, fixture.Input(player, PileInputType.MoveRight, sequence: 6).Kind);
        Assert.Equal("input-rate-exceeded", fixture.Input(player, PileInputType.RotateClockwise, sequence: 7).Code);
    }

    [Fact]
    public void Disconnect_rejects_input_reconnect_restores_control_and_grace_expiry_forfeits()
    {
        var options = new PileUpOptions { DisconnectGracePeriod = TimeSpan.FromSeconds(2) };
        var fixture = new MatchFixture(2, options);
        var player = fixture.PlayerIds[0];
        fixture.Match.SetConnection(player, false, Now);
        Assert.Equal("player-disconnected", fixture.Input(player, PileInputType.MoveLeft).Code);

        fixture.Match.SetConnection(player, true, Now.AddSeconds(1));
        Assert.NotEqual(InputResultKind.Rejected, fixture.Input(player, PileInputType.MoveLeft).Kind);
        fixture.Match.SetConnection(player, false, Now.AddSeconds(1));
        fixture.Match.AdvanceSimulation(Now.AddSeconds(4));

        Assert.True(fixture.Match.Arenas[player].IsOverloaded);
        Assert.True(fixture.Match.IsRoundComplete);
    }

    [Fact]
    public void Last_operational_arena_wins_and_round_does_not_timeout()
    {
        var elimination = new MatchFixture(2);
        elimination.Match.Arenas[elimination.PlayerIds[1]].ForceOverload();
        elimination.Match.AdvanceSimulation(Now.AddMilliseconds(50));
        Assert.Equal(elimination.PlayerIds[0], elimination.Match.RoundWinnerId);

        var ongoing = new MatchFixture(2, new PileUpOptions { RoundDuration = TimeSpan.FromSeconds(1) });
        ongoing.Match.AdvanceSimulation(Now.AddSeconds(1));
        Assert.False(ongoing.Match.IsRoundComplete);
        Assert.Null(ongoing.Match.RoundWinnerId);
    }

    [Theory]
    [InlineData(2, "side-by-side")]
    [InlineData(3, "one-over-two")]
    [InlineData(4, "two-by-two")]
    public void Two_to_four_player_snapshots_select_readable_layout(int count, string layout)
    {
        var fixture = new MatchFixture(count);
        var snapshot = fixture.Match.CreateSnapshot();

        Assert.Equal(count, snapshot.Arenas.Count);
        Assert.Equal(layout, snapshot.Layout);
        Assert.All(snapshot.Arenas, arena =>
        {
            Assert.NotNull(arena.Active);
            Assert.Equal(2, arena.Upcoming.Count);
            Assert.NotEqual(arena.PlayerId, arena.TargetPlayerId);
        });
    }

    [Fact]
    public void Four_player_deterministic_input_simulation_produces_complete_authoritative_snapshot()
    {
        var fixture = new MatchFixture(4);
        foreach (var player in fixture.PlayerIds)
        {
            Assert.Equal(InputResultKind.Applied, fixture.Input(player, PileInputType.InstantDrop).Kind);
        }
        fixture.Match.AdvanceSimulation(Now.AddSeconds(1));
        var snapshot = fixture.Match.CreateSnapshot();

        Assert.Equal(4, snapshot.OperationalPlayers);
        Assert.All(snapshot.Arenas, arena =>
        {
            Assert.NotEmpty(arena.Grid);
            Assert.True(arena.Views > 0);
            Assert.Equal(0, arena.LastInputSequence);
        });
    }

    [Fact]
    public void Match_state_round_trips_through_json_without_losing_authoritative_state()
    {
        var fixture = new MatchFixture(3);
        var first = fixture.PlayerIds[0];
        var second = fixture.PlayerIds[1];
        fixture.Match.Arenas[first].GrantAbilityForTesting(ChaosAbility.SendJunk);
        fixture.Input(first, PileInputType.ActivateAbility, second);
        fixture.Input(second, PileInputType.InstantDrop);
        fixture.Match.SetConnection(fixture.PlayerIds[2], false, Now.AddMilliseconds(100));
        fixture.Match.AdvanceSimulation(Now.AddSeconds(1));

        var serialized = JsonSerializer.Serialize(fixture.Match.CaptureState());
        var restoredState = JsonSerializer.Deserialize<PileUpMatchState>(serialized);
        var restored = PileUpMatch.Restore(Assert.IsType<PileUpMatchState>(restoredState));

        Assert.Equal(serialized, JsonSerializer.Serialize(restored.CaptureState()));
        Assert.Equal(
            JsonSerializer.Serialize(fixture.Match.CreateSnapshot()),
            JsonSerializer.Serialize(restored.CreateSnapshot()));
        Assert.Empty(restored.Events);
        Assert.Equal("duplicate-input", restored.ApplyInput(
            first,
            new PileInputCommand(restored.MatchId, 0, PileInputType.MoveLeft, null, Now),
            Now.AddSeconds(1)).Code);
    }

    [Fact]
    public void Restored_match_continues_with_the_same_deterministic_scrap_stream()
    {
        var fixture = new MatchFixture(2);
        fixture.Input(fixture.PlayerIds[0], PileInputType.InstantDrop);
        var restored = PileUpMatch.Restore(fixture.Match.CaptureState());
        var nextAt = Now.AddSeconds(1);

        _ = fixture.Match.ApplyInput(
            fixture.PlayerIds[0],
            new PileInputCommand(fixture.Match.MatchId, 1, PileInputType.InstantDrop, null, nextAt),
            nextAt);
        _ = restored.ApplyInput(
            fixture.PlayerIds[0],
            new PileInputCommand(restored.MatchId, 1, PileInputType.InstantDrop, null, nextAt),
            nextAt);

        Assert.Equal(
            JsonSerializer.Serialize(fixture.Match.CaptureState()),
            JsonSerializer.Serialize(restored.CaptureState()));
    }

    private sealed class MatchFixture
    {
        private readonly Dictionary<Guid, long> sequences;

        public MatchFixture(int count, PileUpOptions? options = null)
        {
            PlayerIds = Enumerable.Range(0, count)
                .Select(index => Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"))
                .ToArray();
            sequences = PlayerIds.ToDictionary(player => player, _ => -1L);
            Match = new PileUpMatch(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                PlayerIds.Select((player, index) => new PileMatchParticipant(player, $"Player {index + 1}")).ToArray(),
                12345,
                Now,
                options);
        }

        public PileUpMatch Match { get; }
        public Guid[] PlayerIds { get; }

        public PileInputResult Input(
            Guid player,
            PileInputType type,
            Guid? target = null,
            long? sequence = null)
        {
            var next = sequence ?? ++sequences[player];
            sequences[player] = Math.Max(sequences[player], next);
            return Match.ApplyInput(
                player,
                new PileInputCommand(Match.MatchId, next, type, target, Now),
                Now);
        }
    }
}
