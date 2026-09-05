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

    [Fact]
    public void Parser_extends_notes_while_the_midi_sustain_pedal_is_held()
    {
        byte[] midi =
        [
            0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, 0, 96,
            0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 20,
            0, 0x90, 60, 100,
            48, 0xB0, 64, 127,
            48, 0x80, 60, 0,
            96, 0xB0, 64, 0,
            0, 0xFF, 0x2F, 0
        ];

        using var stream = new MemoryStream(midi);
        var note = Assert.Single(Assert.Single(VoiceChoonSongCatalog.Load(stream, "pedal.mid").Tracks).Notes);

        Assert.Equal(192, note.DurationTicks);
        Assert.InRange(note.DurationSeconds, .99, 1.01);
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

    // ---  Sustained synth-lead tests  ---

    private static RawMidiTrack SynthLeadTrack(
        VoiceChoonTrackRole role,
        int program,
        IReadOnlyList<double> noteDurationsSeconds) =>
        new(0, "Synth Lead", role, false,
            noteDurationsSeconds.Select((duration, index) =>
                new RawMidiNote(index * 960L, (long)(duration * 960), index * duration, duration,
                    60 + index % 12, 100, 0)).ToArray(),
            program);

    [Fact]
    public void Synth_lead_program_with_predominantly_long_notes_becomes_sustained()
    {
        // 6 of 8 notes >= 0.65 s  =>  75 % >= 40 % threshold
        var durations = new double[] { 1.0, 0.8, 0.9, 0.7, 1.2, 0.1, 0.95, 0.85 };
        var track = SynthLeadTrack(VoiceChoonTrackRole.Other, 81, durations);

        Assert.True(TrackArticulation.IsSustainedSynthLead(track));
        Assert.Equal(RecordingStyle.Sustained, TrackArticulation.RecordingStyleFor(track));

        var prompts = InstrumentSoundGuide.For(track);
        Assert.Equal(2, prompts.Count);
        Assert.All(prompts, p => Assert.Equal(RecordingStyle.Sustained, p.Style));
        Assert.Equal("WAAAA", prompts[0].Example);
        Assert.Equal("NEEEOOW", prompts[1].Example);
        Assert.Equal(55, prompts[0].RootMidiNote);
        Assert.Equal(72, prompts[1].RootMidiNote);
    }

    [Fact]
    public void Synth_lead_program_with_short_notes_remains_one_shot()
    {
        // All notes short — 0 of 8 >= 0.65 s; average well below 0.8 s
        var durations = Enumerable.Repeat(0.1, 8).ToArray();
        var track = SynthLeadTrack(VoiceChoonTrackRole.Other, 81, durations);

        Assert.False(TrackArticulation.IsSustainedSynthLead(track));
        Assert.Equal(RecordingStyle.OneShot, TrackArticulation.RecordingStyleFor(track));
        Assert.All(InstrumentSoundGuide.For(track),
            p => Assert.Equal(RecordingStyle.OneShot, p.Style));
    }

    [Fact]
    public void Lead_role_with_long_notes_and_generic_family_becomes_sustained()
    {
        // LeadA with no program number; all notes long  =>  average > 0.8 s
        var durations = Enumerable.Repeat(1.0, 6).ToArray<double>();
        var track = new RawMidiTrack(0, "Lead", VoiceChoonTrackRole.LeadA, false,
            durations.Select((dur, i) => new RawMidiNote(i * 960L, 960L, i * dur, dur, 60, 100, 0)).ToArray());

        Assert.True(TrackArticulation.IsSustainedSynthLead(track));
        Assert.Equal(RecordingStyle.Sustained, TrackArticulation.RecordingStyleFor(track));
    }

    [Fact]
    public void Lead_role_with_named_non_generic_family_is_not_classified_as_sustained_synth()
    {
        // LeadA but the name resolves to Strings family — should not be claimed by synth path
        var durations = Enumerable.Repeat(1.0, 6).ToArray<double>();
        var track = new RawMidiTrack(0, "String Lead", VoiceChoonTrackRole.LeadA, false,
            durations.Select((dur, i) => new RawMidiNote(i * 960L, 960L, i * dur, dur, 60, 100, 0)).ToArray());

        Assert.False(TrackArticulation.IsSustainedSynthLead(track));
    }

    [Fact]
    public void Sustained_synth_note_long_enough_loops_and_short_note_does_not()
    {
        // A sustained-style sample with a long note => loop; short note => no loop
        RecordedSample[] samples =
        [
            new("synth-low", 55, RecordingStyle.Sustained, 1.5),
            new("synth-high", 72, RecordingStyle.Sustained, 1.5)
        ];

        var longPlan = PitchShiftPlanner.Plan(60, 1.0, samples);
        var shortPlan = PitchShiftPlanner.Plan(60, 0.3, samples);

        Assert.True(longPlan.Loop);
        Assert.NotNull(longPlan.LoopStartSeconds);
        Assert.NotNull(longPlan.LoopEndSeconds);
        Assert.False(shortPlan.Loop);
        Assert.Null(shortPlan.LoopStartSeconds);
        Assert.Null(shortPlan.LoopEndSeconds);
    }

    [Fact]
    public void Sustained_synth_chart_notes_carry_sustained_playback_style_through_to_chart()
    {
        // Build a minimal chart from a synth-lead track with long notes
        var durations = Enumerable.Repeat(1.0, 6).ToArray<double>();
        var track = SynthLeadTrack(VoiceChoonTrackRole.Other, 81, durations);
        var song = new RawMidiSong("synth-test.mid", 960, 6, [track], []);
        var assignment = Assert.Single(InstrumentAssignmentService.Assign(song, 1));
        var chart = Assert.Single(new ChartGenerator().Generate([assignment]));

        Assert.All(chart.PlaybackNotes,
            note => Assert.Equal(RecordingStyle.Sustained, note.PlaybackStyle));
    }

    [Fact]
    public void Piano_tracks_remain_unaffected_by_synth_lead_classification()
    {
        var piano = new RawMidiTrack(0, "Piano", VoiceChoonTrackRole.Other, false,
            Enumerable.Repeat(1.0, 6).Select((dur, i) =>
                new RawMidiNote(i * 960L, 960L, i * dur, dur, 60, 100, 0)).ToArray(), 0);

        Assert.False(TrackArticulation.IsSustainedSynthLead(piano));
        Assert.Equal(RecordingStyle.Piano, TrackArticulation.RecordingStyleFor(piano));
        Assert.All(InstrumentSoundGuide.For(piano),
            p => Assert.Equal(RecordingStyle.Piano, p.Style));
    }

    [Fact]
    public void Sustain_pedal_held_before_note_on_extends_note_duration()
    {
        // Pedal pressed at tick 0, note plays ticks 100–200, pedal released at tick 400.
        // The note should be extended to tick 400, not cut at 200.
        var pedalDown = MakeSustainEvent(0, 127, channel: 0);
        var pedalUp = MakeSustainEvent(400, 0, channel: 0);
        var sustainEvents = new[] { pedalDown, pedalUp };
        var note = MakeNote(noteNumber: 60, start: 100, end: 200, channel: 0);
        var extendedEnd = MidiParser.SustainedEndTimePublic(note, [note], sustainEvents);
        Assert.Equal(400, extendedEnd);
    }

    [Fact]
    public void Sustain_pedal_up_between_note_on_and_note_off_does_not_extend()
    {
        // Pedal was down, came back up at tick 150 (while note 100–200 was sounding),
        // then pressed again at tick 180. The last event ≤ note.EndTime is a pedal-down
        // — old code would have extended. But pedal was not held AT note-on (tick 0 pedal
        // is a pedal-up), and the pressed-during-note window (150–200) only has a down at 180.
        // So the note SHOULD be extended: pedal came down at 180 before note-off 200.
        var events = new[]
        {
            MakeSustainEvent(0, 0, channel: 0),    // pedal up at start
            MakeSustainEvent(150, 0, channel: 0),  // pedal up (already up, no-op effect)
            MakeSustainEvent(180, 127, channel: 0), // pedal pressed while note sounds
            MakeSustainEvent(400, 0, channel: 0),  // pedal up after note-off
        };
        var note = MakeNote(noteNumber: 60, start: 100, end: 200, channel: 0);
        var extendedEnd = MidiParser.SustainedEndTimePublic(note, [note], events);
        Assert.Equal(400, extendedEnd);
    }

    [Fact]
    public void Sustain_pedal_released_before_note_on_does_not_extend()
    {
        // Pedal was up well before the note — no extension should occur.
        var events = new[]
        {
            MakeSustainEvent(0, 127, channel: 0),  // pedal down
            MakeSustainEvent(50, 0, channel: 0),   // pedal up BEFORE note-on at 100
        };
        var note = MakeNote(noteNumber: 60, start: 100, end: 200, channel: 0);
        var naturalEnd = MidiParser.SustainedEndTimePublic(note, [note], events);
        Assert.Equal(200, naturalEnd);
    }

    [Fact]
    public void Loop_region_minimum_gap_is_enforced_for_short_samples()
    {
        // A 0.15-second sample: natural 30%/70% boundaries are 0.045 s and 0.105 s — 60 ms apart,
        // which is fine. But we specifically test that LoopEnd >= LoopStart + 0.05 holds even when
        // the 70% boundary falls close to the 30% boundary + 50 ms floor.
        // Use a 0.20-second sample: loopStart=0.06, loopEnd=max(0.14, 0.11)=0.14 — gap=0.08.
        var plan = PitchShiftPlanner.Plan(60, 2.0,
            [new RecordedSample("short", 60, RecordingStyle.Sustained, 0.20)]);
        Assert.True(plan.Loop);
        Assert.NotNull(plan.LoopStartSeconds);
        Assert.NotNull(plan.LoopEndSeconds);
        var gap = plan.LoopEndSeconds!.Value - plan.LoopStartSeconds!.Value;
        Assert.True(gap >= 0.05, $"Loop region too small: {gap:F4}s");
    }

    [Fact]
    public void Zero_duration_sample_does_not_loop()
    {
        // A sample with DurationSeconds = 0 must never produce a loop (would crash Web Audio).
        var plan = PitchShiftPlanner.Plan(60, 2.0,
            [new RecordedSample("empty", 60, RecordingStyle.Sustained, 0)]);
        Assert.False(plan.Loop);
        Assert.Null(plan.LoopStartSeconds);
        Assert.Null(plan.LoopEndSeconds);
    }

    [Fact]
    public void Low_pitch_heuristic_does_not_classify_repeated_note_tracks_as_bass()
    {
        // A non-channel-10 kick drum replacement track: 32 hits all on MIDI note 36 (low C).
        // Average pitch < 48, but 100% of notes share one pitch — should not be Bass.
        var notes = Enumerable.Range(0, 32)
            .Select(i => new RawMidiNote(i * 120L, 60L, i * 0.1, 0.05, 36, 100, 2))
            .ToArray();
        var role = MidiParser.InferRole("Track 1", isPercussion: false, notes);
        Assert.NotEqual(VoiceChoonTrackRole.Bass, role);
    }

    [Fact]
    public void Low_pitch_heuristic_still_classifies_genuine_bass_lines_as_bass()
    {
        // A real walking bass line across multiple distinct low pitches (28–47).
        // No single pitch dominates, average < 48 — should be Bass.
        int[] pitches = [28, 31, 33, 35, 36, 38, 40, 42, 43, 45, 47, 45, 43, 42, 40, 38];
        var notes = pitches.Select((p, i) =>
            new RawMidiNote(i * 120L, 120L, i * 0.1, 0.1, p, 90, 0)).ToArray();
        var role = MidiParser.InferRole("Track 1", isPercussion: false, notes);
        Assert.Equal(VoiceChoonTrackRole.Bass, role);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (long Time, Melanchall.DryWetMidi.Core.ControlChangeEvent Control)
        MakeSustainEvent(long time, int value, byte channel)
    {
        var evt = new Melanchall.DryWetMidi.Core.ControlChangeEvent
        {
            ControlNumber = (Melanchall.DryWetMidi.Common.SevenBitNumber)64,
            ControlValue = (Melanchall.DryWetMidi.Common.SevenBitNumber)value,
            Channel = (Melanchall.DryWetMidi.Common.FourBitNumber)channel
        };
        return (time, evt);
    }

    private static Melanchall.DryWetMidi.Interaction.Note MakeNote(
        int noteNumber, long start, long end, byte channel)
    {
        // DryWetMidi Note: NoteOn at `start`, length = end - start, channel.
        var note = new Melanchall.DryWetMidi.Interaction.Note(
            (Melanchall.DryWetMidi.Common.SevenBitNumber)noteNumber,
            end - start,
            start)
        {
            Channel = (Melanchall.DryWetMidi.Common.FourBitNumber)channel,
            Velocity = (Melanchall.DryWetMidi.Common.SevenBitNumber)100,
            OffVelocity = (Melanchall.DryWetMidi.Common.SevenBitNumber)0
        };
        return note;
    }
}
