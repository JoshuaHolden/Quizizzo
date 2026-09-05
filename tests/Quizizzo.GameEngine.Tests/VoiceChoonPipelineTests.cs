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
        Assert.Equal(2, VoiceChoonSongCatalog.GetDefinition(VoiceChoonSongCatalog.GreensleevesSongKey).MinimumPlayers);
        Assert.Equal(3, VoiceChoonSongCatalog.GetDefinition(VoiceChoonSongCatalog.DefaultSongKey).MinimumPlayers);
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

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Uploaded_generic_tracks_are_balanced_without_empty_players(int playerCount)
    {
        var tracks = Enumerable.Range(0, 4).Select(index => new RawMidiTrack(
            index,
            $"Meme track {index + 1}",
            VoiceChoonTrackRole.Other,
            false,
            [new RawMidiNote(index * 96, 96, index, .5, 60 + index, 100, index)])).ToArray();
        var song = new RawMidiSong("quizizzo_meme_meltdown_2to4players.mid", 96, 4, tracks, []);

        var assignments = InstrumentAssignmentService.Assign(song, playerCount);

        Assert.Equal(playerCount, assignments.Count);
        Assert.All(assignments, assignment => Assert.NotEmpty(assignment.Tracks));
        Assert.Equal(4, assignments.Sum(assignment => assignment.Tracks.Count));
        Assert.InRange(
            assignments.Max(assignment => assignment.Tracks.Count) -
            assignments.Min(assignment => assignment.Tracks.Count),
            0,
            1);
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
    public void Greensleeves_uses_only_the_four_instrument_families_present_in_the_midi()
    {
        var song = VoiceChoonSongCatalog.Load(VoiceChoonSongCatalog.GreensleevesSongKey);

        Assert.Equal("gs.mid", song.SourceName);
        Assert.Equal(
            [
                VoiceChoonTrackRole.LeadA,
                VoiceChoonTrackRole.Chords,
                VoiceChoonTrackRole.Bass,
                VoiceChoonTrackRole.Drums
            ],
            song.Tracks.Select(track => track.Role));

        var assignments = InstrumentAssignmentService.Assign(song, 4);
        Assert.Equal(4, assignments.Count);
        Assert.All(assignments, assignment => Assert.NotEmpty(assignment.RecordingPrompts));
        Assert.DoesNotContain(assignments.SelectMany(assignment => assignment.RecordingPrompts), prompt =>
            prompt.Key.StartsWith("arp-", StringComparison.Ordinal) ||
            prompt.Key.StartsWith("lead-b-", StringComparison.Ordinal) ||
            prompt.Key.StartsWith("stabs-", StringComparison.Ordinal));

        var duo = InstrumentAssignmentService.Assign(song, 2);
        Assert.Equal(2, duo.Count);
        Assert.All(duo, assignment => Assert.NotEmpty(assignment.Tracks));
        Assert.Equal(song.Tracks.Count, duo.Sum(assignment => assignment.Tracks.Count));
        Assert.Contains(duo[0].Tracks, track => track.Role == VoiceChoonTrackRole.LeadA);
        Assert.Contains(duo[1].Tracks, track => track.Role == VoiceChoonTrackRole.Drums);
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

    [Fact]
    public void Piano_tracks_request_bell_like_samples_and_decay_instead_of_looping()
    {
        var piano = new RawMidiTrack(0, "Clair de Lune Piano", VoiceChoonTrackRole.Other, false,
            [new RawMidiNote(0, 192, 0, 1.8, 72, 52, 0)], 0);
        var assignment = Assert.Single(InstrumentAssignmentService.Assign(
            new RawMidiSong("clair-de-lune.mid", 96, 1.8, [piano], []), 1));
        var note = Assert.Single(new ChartGenerator().Generate([assignment]).Single().PlaybackNotes);

        Assert.True(TrackArticulation.IsPiano(piano));
        Assert.All(assignment.RecordingPrompts,
            prompt => Assert.Equal(RecordingStyle.Piano, prompt.Style));
        Assert.Equal(["DOONG", "TING"], assignment.RecordingPrompts.Select(prompt => prompt.Example));
        Assert.Equal(RecordingStyle.Piano, note.PlaybackStyle);
        Assert.False(PitchShiftPlanner.Plan(note.TargetMidiNote, note.DurationSeconds,
            [new RecordedSample("piano", 67, note.PlaybackStyle, 1)]).Loop);
    }

    [Theory]
    [InlineData(10, VoiceChoonInstrumentFamily.Bell, RecordingStyle.Bell, "BONG")]
    [InlineData(19, VoiceChoonInstrumentFamily.Organ, RecordingStyle.SoftSustain, "VOOO")]
    [InlineData(25, VoiceChoonInstrumentFamily.Guitar, RecordingStyle.Plucked, "DWANG")]
    [InlineData(48, VoiceChoonInstrumentFamily.Strings, RecordingStyle.SoftSustain, "VAAAH")]
    [InlineData(57, VoiceChoonInstrumentFamily.Brass, RecordingStyle.Brass, "BRAAH")]
    [InlineData(73, VoiceChoonInstrumentFamily.Woodwind, RecordingStyle.Woodwind, "DOOO")]
    public void General_midi_families_receive_distinct_prompts_and_articulation(
        int program, VoiceChoonInstrumentFamily family, RecordingStyle style, string example)
    {
        var track = new RawMidiTrack(0, "Track", VoiceChoonTrackRole.Other, false,
            [new RawMidiNote(0, 96, 0, 1, 60, 90, 0)], program);

        Assert.Equal(family, TrackArticulation.FamilyFor(track));
        Assert.Equal(style, TrackArticulation.RecordingStyleFor(track));
        Assert.Equal(example, InstrumentSoundGuide.For(track)[0].Example);
    }

    [Theory]
    [InlineData(RecordingStyle.SoftSustain)]
    [InlineData(RecordingStyle.Brass)]
    [InlineData(RecordingStyle.Woodwind)]
    public void Sustaining_instrument_families_loop_long_notes(RecordingStyle style) =>
        Assert.True(PitchShiftPlanner.Plan(60, 1.5,
            [new RecordedSample("sample", 60, style, 1)]).Loop);

    [Fact]
    public void Short_generic_electronic_tracks_keep_one_shot_articulation()
    {
        var synth = new RawMidiTrack(0, "Track 1", VoiceChoonTrackRole.Other, false,
            Enumerable.Range(0, 8).Select(index =>
                new RawMidiNote(index * 24, 12, index * .2, .12, 60 + index, 110, 0)).ToArray(), 81);

        Assert.False(TrackArticulation.IsLegato(synth));
        Assert.All(InstrumentSoundGuide.For(synth),
            prompt => Assert.Equal(RecordingStyle.OneShot, prompt.Style));
    }

    [Fact]
    public void Uploaded_song_analysis_derives_a_bounded_player_range_and_safe_key()
    {
        using var stream = typeof(VoiceChoonSongCatalog).Assembly.GetManifestResourceStream(
            "Quizizzo.Games.VoiceChoon.Assets.gs.mid")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        var analysis = VoiceChoonSongAnalyzer.Analyze(memory.ToArray(), "my tune.mid", "My Lovely Tune!");

        Assert.Equal("my-lovely-tune", analysis.SuggestedKey);
        Assert.Equal(4, analysis.TrackCount);
        Assert.Equal(2, analysis.MinimumPlayers);
        Assert.Equal(4, analysis.MaximumPlayers);
        Assert.InRange(analysis.NoteCount, 1, VoiceChoonSongAnalyzer.MaximumNotes);
    }

    [Fact]
    public void Uploaded_catalog_entries_can_be_added_loaded_and_removed_without_replacing_built_ins()
    {
        using var stream = typeof(VoiceChoonSongCatalog).Assembly.GetManifestResourceStream(
            "Quizizzo.Games.VoiceChoon.Assets.gs.mid")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        const string key = "test-uploaded-greensleeves";
        var definition = new VoiceChoonSongDefinition(key, "Test upload", 2, "test.mid",
            "Briefing", "Recording", UploadedSongId: Guid.NewGuid(), MaximumPlayers: 4);
        try
        {
            VoiceChoonSongCatalog.RegisterUploaded(definition, memory.ToArray());
            Assert.True(VoiceChoonSongCatalog.IsKnownKey(key));
            Assert.Equal("test.mid", VoiceChoonSongCatalog.Load(key).SourceName);
            Assert.False(VoiceChoonSongCatalog.IsBuiltIn(key));
            var builtInCollision = definition with { Key = VoiceChoonSongCatalog.DefaultSongKey };
            Assert.Throws<InvalidOperationException>(() => VoiceChoonSongCatalog.RegisterUploaded(
                builtInCollision, memory.ToArray()));
        }
        finally
        {
            Assert.True(VoiceChoonSongCatalog.RemoveUploaded(key));
        }
        Assert.False(VoiceChoonSongCatalog.IsKnownKey(key));
    }

    [Fact]
    public void Uploaded_song_analysis_rejects_malformed_and_misleading_files()
    {
        Assert.Throws<InvalidDataException>(() =>
            VoiceChoonSongAnalyzer.Analyze("not midi data"u8.ToArray(), "fake.mid", "Fake"));
        Assert.Throws<InvalidDataException>(() =>
            VoiceChoonSongAnalyzer.Analyze(new byte[32], "fake.txt", "Fake"));
    }
}
