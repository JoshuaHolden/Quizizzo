using Quizizzo.Games.VoiceChoon;

namespace Quizizzo.GameEngine.Tests;

public sealed class VoiceChoonPipelineTests
{
    [Fact]
    public void Descriptor_defines_the_supported_cooperative_party_size()
    {
        Assert.Equal("voicechoon", VoiceChoonGameDefinition.Descriptor.Key);
        Assert.Equal("VoiceChoon", VoiceChoonGameDefinition.Descriptor.DisplayName);
        Assert.Equal(1, VoiceChoonGameDefinition.Descriptor.MinimumPlayers);
        Assert.Equal(3, VoiceChoonGameDefinition.NormalMinimumPlayers);
        Assert.Equal(8, VoiceChoonGameDefinition.Descriptor.MaximumPlayers);
    }

    [Fact]
    public void Default_song_parses_all_roles_sections_and_two_minute_timeline()
    {
        var song = VoiceChoonSongCatalog.LoadDefaultSong();

        Assert.Equal(480, song.TicksPerQuarterNote);
        Assert.Equal(8, song.Tracks.Count);
        Assert.InRange(song.DurationSeconds, 119, 121);
        Assert.Equal(
            Enum.GetValues<VoiceChoonTrackRole>().Where(role => role != VoiceChoonTrackRole.Other),
            song.Tracks.Select(track => track.Role).OrderBy(role => role));
        Assert.Equal(
            ["INTRO", "VERSE A", "CALL RESPONSE", "CHORUS 1", "BREAKDOWN", "BUILD", "FINAL CHORUS", "OUTRO"],
            song.Sections.Select(section => section.Name));
    }

    [Fact]
    public void Parser_accepts_an_unrelated_standard_midi_stream()
    {
        byte[] midi =
        [
            0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, 0, 96,
            0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 23,
            0, 0xFF, 3, 7, 0x4D, 0x79, 0x73, 0x74, 0x65, 0x72, 0x79,
            0, 0x90, 60, 100,
            96, 0x80, 60, 64,
            0, 0xFF, 0x2F, 0
        ];

        using var stream = new MemoryStream(midi);
        var song = VoiceChoonSongCatalog.Load(stream, "future-song.mid");

        var track = Assert.Single(song.Tracks);
        Assert.Equal("Mystery", track.Name);
        Assert.Equal(VoiceChoonTrackRole.Other, track.Role);
        Assert.Equal(60, Assert.Single(track.Notes).MidiNote);
        Assert.InRange(song.DurationSeconds, 0.49, 0.51);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Assignments_cover_every_track_for_supported_party_sizes(int playerCount)
    {
        var song = VoiceChoonSongCatalog.LoadDefaultSong();
        var assignments = InstrumentAssignmentService.Assign(song, playerCount);

        Assert.Equal(playerCount, assignments.Count);
        Assert.All(assignments, assignment => Assert.NotEmpty(assignment.Tracks));
        Assert.Equal(song.Tracks.Count, assignments.SelectMany(assignment => assignment.Tracks).Distinct().Count());
        Assert.All(assignments, assignment => Assert.InRange(assignment.RecordingPrompts.Count, 1, 4));
    }

    [Fact]
    public void Three_player_assignment_matches_the_cooperative_score_design()
    {
        var assignments = InstrumentAssignmentService.Assign(VoiceChoonSongCatalog.LoadDefaultSong(), 3);

        Assert.Equal(
            [VoiceChoonTrackRole.Drums, VoiceChoonTrackRole.PercussionFx],
            assignments[0].Tracks.Select(track => track.Role).OrderBy(role => role));
        Assert.Equal(
            [VoiceChoonTrackRole.Bass, VoiceChoonTrackRole.Chords],
            assignments[1].Tracks.Select(track => track.Role).OrderBy(role => role));
        Assert.Equal(4, assignments[2].Tracks.Count);
    }

    [Fact]
    public void Solo_test_assignment_includes_every_track_and_sound_prompt()
    {
        var song = VoiceChoonSongCatalog.LoadDefaultSong();
        var assignment = Assert.Single(InstrumentAssignmentService.Assign(song, 1));

        Assert.Equal(song.Tracks.Count, assignment.Tracks.Count);
        Assert.Equal(18, assignment.RecordingPrompts.Count);
        Assert.Equal(assignment.RecordingPrompts.Count,
            assignment.RecordingPrompts.Select(prompt => prompt.Key).Distinct().Count());
    }

    [Fact]
    public void Generated_charts_retain_target_pitch_and_bound_lanes_chords_and_density()
    {
        var assignments = InstrumentAssignmentService.Assign(VoiceChoonSongCatalog.LoadDefaultSong(), 3);
        var charts = new ChartGenerator().Generate(assignments);

        Assert.Equal(3, charts.Count);
        Assert.All(charts, chart => Assert.True(chart.PlaybackNotes.Count >= chart.Notes.Count));
        Assert.All(charts.SelectMany(chart => chart.Notes), note =>
        {
            Assert.InRange(note.Lane, 0, 3);
            Assert.InRange(note.TargetMidiNote, 0, 127);
        });
        Assert.All(charts, chart =>
        {
            Assert.All(chart.Notes.GroupBy(note => note.StartTimeSeconds), simultaneous =>
                Assert.InRange(simultaneous.Count(), 1, 2));
            Assert.All(chart.Notes, note =>
                Assert.InRange(
                    chart.Notes.Count(other => other.StartTimeSeconds <= note.StartTimeSeconds &&
                        other.StartTimeSeconds > note.StartTimeSeconds - 1),
                    1,
                    5));
        });
        Assert.Contains(charts.SelectMany(chart => chart.Notes), note => note.Type == RhythmNoteType.Hold);
    }

    [Theory]
    [InlineData(2, 1, 0.4)]
    [InlineData(3, 1, 0.25)]
    [InlineData(5, 2, 0.08)]
    public void Difficulty_chart_limits_prevent_unplayable_same_lane_bursts(
        int maximumPressesPerSecond,
        int maximumSimultaneousPads,
        double minimumLaneGapSeconds)
    {
        var assignments = InstrumentAssignmentService.Assign(VoiceChoonSongCatalog.LoadDefaultSong(), 3);
        var charts = new ChartGenerator(new ChartGenerationOptions
        {
            MaximumPressesPerSecond = maximumPressesPerSecond,
            MaximumSimultaneousPads = maximumSimultaneousPads,
            MinimumLaneGapSeconds = minimumLaneGapSeconds
        }).Generate(assignments);

        Assert.All(charts, chart =>
        {
            Assert.All(chart.Notes.GroupBy(note => note.StartTimeSeconds), simultaneous =>
                Assert.InRange(simultaneous.Count(), 1, maximumSimultaneousPads));
            Assert.All(chart.Notes, note =>
                Assert.InRange(
                    chart.Notes.Count(other => other.StartTimeSeconds <= note.StartTimeSeconds &&
                        other.StartTimeSeconds > note.StartTimeSeconds - 1),
                    1,
                    maximumPressesPerSecond));
            Assert.All(chart.Notes.GroupBy(note => note.Lane), lane =>
            {
                var ordered = lane.OrderBy(note => note.StartTimeSeconds).ToArray();
                Assert.All(ordered.Zip(ordered.Skip(1)), pair =>
                    Assert.True(pair.Second.StartTimeSeconds - pair.First.StartTimeSeconds >=
                        minimumLaneGapSeconds - 0.0001));
            });
        });
    }

    [Fact]
    public void Rapid_same_lane_runs_become_single_hold_targets_when_enabled()
    {
        var assignments = InstrumentAssignmentService.Assign(VoiceChoonSongCatalog.LoadDefaultSong(), 3);
        var baseline = new ChartGenerator(new ChartGenerationOptions
        {
            MaximumPressesPerSecond = 3,
            MaximumSimultaneousPads = 1,
            MinimumLaneGapSeconds = 0.25
        }).Generate(assignments);
        var compressed = new ChartGenerator(new ChartGenerationOptions
        {
            MaximumPressesPerSecond = 3,
            MaximumSimultaneousPads = 1,
            MinimumLaneGapSeconds = 0.25,
            RapidRunGapSeconds = 0.45
        }).Generate(assignments);

        Assert.True(compressed.Sum(chart => chart.Notes.Count) < baseline.Sum(chart => chart.Notes.Count));
        Assert.True(compressed.Sum(chart => chart.Notes.Count(note => note.Type == RhythmNoteType.Hold)) >
                    baseline.Sum(chart => chart.Notes.Count(note => note.Type == RhythmNoteType.Hold)));
        Assert.All(compressed.SelectMany(chart => chart.Notes).Where(note => note.Type == RhythmNoteType.Hold),
            note => Assert.True(note.DurationSeconds >= 0.5));
    }

    [Theory]
    [InlineData(35, 0)]
    [InlineData(38, 1)]
    [InlineData(42, 2)]
    [InlineData(49, 3)]
    public void Drum_notes_map_semantically(int midiNote, int expectedLane) =>
        Assert.Equal(expectedLane, ChartGenerator.DrumLane(midiNote));

    [Fact]
    public void Pitch_plan_chooses_nearest_root_folds_extremes_and_loops_sustains()
    {
        RecordedSample[] samples =
        [
            new("low", 48, RecordingStyle.Sustained, 1),
            new("high", 72, RecordingStyle.Sustained, 1)
        ];

        var plan = PitchShiftPlanner.Plan(96, 2, samples);

        Assert.Equal(96, plan.RequestedMidiNote);
        Assert.Equal("high", plan.SampleKey);
        Assert.InRange(plan.SemitoneShift, -PitchShiftPlanner.MaximumSemitoneShift,
            PitchShiftPlanner.MaximumSemitoneShift);
        Assert.True(plan.Loop);
        Assert.Equal(0.3, plan.LoopStartSeconds);
        Assert.Equal(0.7, plan.LoopEndSeconds);
    }

    [Fact]
    public void Instrument_guides_request_distinct_noises_for_drums_and_two_roots_for_melody()
    {
        Assert.Equal(["BOOM", "KAH", "TSH", "PAH"],
            InstrumentSoundGuide.For(VoiceChoonTrackRole.Drums).Select(prompt => prompt.Example));
        Assert.Equal(2, InstrumentSoundGuide.For(VoiceChoonTrackRole.Bass).Count);
        Assert.All(InstrumentSoundGuide.For(VoiceChoonTrackRole.Chords), prompt =>
            Assert.Equal(RecordingStyle.Sustained, prompt.Style));
    }
}
