namespace Quizizzo.Games.VoiceChoon;

public sealed class InstrumentAssignmentService
{
    public static IReadOnlyList<InstrumentAssignment> Assign(
        RawMidiSong song,
        int playerCount,
        Func<VoiceChoonTrackRole, IReadOnlyList<SoundRecordingPrompt>>? promptFactory = null)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (playerCount is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), "VoiceChoon requires one to eight players.");
        }

        var assigned = Enumerable.Range(0, playerCount)
            .Select(_ => new List<RawMidiTrack>())
            .ToArray();
        foreach (var track in song.Tracks
                     .OrderBy(track => Priority(track.Role))
                     .ThenBy(track => track.Index))
        {
            var preferred = PreferredOwner(playerCount, track.Role);
            var owner = preferred >= 0 && preferred < playerCount
                ? preferred
                : Enumerable.Range(0, playerCount)
                    .MinBy(index => assigned[index].Sum(item => item.Notes.Count));
            assigned[owner].Add(track);
        }

        return assigned.Select((tracks, playerIndex) =>
        {
            var prompts = tracks.SelectMany(track => (promptFactory ?? InstrumentSoundGuide.For)(track.Role))
                .DistinctBy(prompt => prompt.Key);
            return new InstrumentAssignment(
                playerIndex,
                tracks.ToArray(),
                (playerCount <= 2 ? prompts : prompts.Take(4)).ToArray());
        }).ToArray();
    }

    internal static int Priority(VoiceChoonTrackRole role) => role switch
    {
        VoiceChoonTrackRole.LeadA => 0,
        VoiceChoonTrackRole.LeadB => 1,
        VoiceChoonTrackRole.Drums => 2,
        VoiceChoonTrackRole.Bass => 3,
        VoiceChoonTrackRole.Chords => 4,
        VoiceChoonTrackRole.PercussionFx => 5,
        VoiceChoonTrackRole.Arp => 6,
        VoiceChoonTrackRole.VocalStabs => 7,
        _ => 8
    };

    private static int PreferredOwner(int players, VoiceChoonTrackRole role) => (players, role) switch
    {
        (2, VoiceChoonTrackRole.LeadA or VoiceChoonTrackRole.Chords) => 0,
        (2, _) => 1,
        (3, VoiceChoonTrackRole.Drums or VoiceChoonTrackRole.PercussionFx) => 0,
        (3, VoiceChoonTrackRole.Bass or VoiceChoonTrackRole.Chords) => 1,
        (3, _) => 2,
        (4, VoiceChoonTrackRole.Drums) => 0,
        (4, VoiceChoonTrackRole.Bass) => 1,
        (4, VoiceChoonTrackRole.Chords or VoiceChoonTrackRole.Arp) => 2,
        (4, _) => 3,
        (5, VoiceChoonTrackRole.Drums) => 0,
        (5, VoiceChoonTrackRole.Bass) => 1,
        (5, VoiceChoonTrackRole.Chords) => 2,
        (5, VoiceChoonTrackRole.LeadA) => 3,
        (5, _) => 4,
        (6, VoiceChoonTrackRole.Drums) => 0,
        (6, VoiceChoonTrackRole.Bass) => 1,
        (6, VoiceChoonTrackRole.Chords) => 2,
        (6, VoiceChoonTrackRole.LeadA) => 3,
        (6, VoiceChoonTrackRole.LeadB) => 4,
        (6, _) => 5,
        (7, VoiceChoonTrackRole.Drums) => 0,
        (7, VoiceChoonTrackRole.Bass) => 1,
        (7, VoiceChoonTrackRole.Chords) => 2,
        (7, VoiceChoonTrackRole.LeadA) => 3,
        (7, VoiceChoonTrackRole.LeadB) => 4,
        (7, VoiceChoonTrackRole.Arp) => 6,
        (7, _) => 5,
        (8, VoiceChoonTrackRole.Drums) => 0,
        (8, VoiceChoonTrackRole.Bass) => 1,
        (8, VoiceChoonTrackRole.Chords) => 2,
        (8, VoiceChoonTrackRole.LeadA) => 3,
        (8, VoiceChoonTrackRole.LeadB) => 4,
        (8, VoiceChoonTrackRole.PercussionFx) => 5,
        (8, VoiceChoonTrackRole.Arp) => 6,
        (8, VoiceChoonTrackRole.VocalStabs) => 7,
        _ => -1
    };
}

public static class InstrumentSoundGuide
{
    public static IReadOnlyList<SoundRecordingPrompt> For(VoiceChoonTrackRole role) => role switch
    {
        VoiceChoonTrackRole.Drums =>
        [
            new("kick", "Kick", "BOOM", RecordingStyle.Percussion, 36, "A short, low thump."),
            new("snare", "Snare", "KAH", RecordingStyle.Percussion, 38, "A sharp crack."),
            new("hat", "Hi-hat", "TSH", RecordingStyle.Percussion, 42, "A short hiss."),
            new("drum-fx", "Drum FX", "PAH", RecordingStyle.Percussion, 49, "A loud pop or cymbal-like burst.")
        ],
        VoiceChoonTrackRole.Bass => MelodicPair("bass", "BWAAAA", "MMMM", RecordingStyle.Sustained, 43, 55,
            "Use a low, steady voice with a clean middle that can loop."),
        VoiceChoonTrackRole.Chords => MelodicPair("chords", "AAAAH", "OOOOH", RecordingStyle.Sustained, 55, 67,
            "Hold a stable vowel; avoid vibrato so stacked chords stay clear."),
        VoiceChoonTrackRole.LeadA => MelodicPair("lead-a", "BEEP", "WEEEE", RecordingStyle.OneShot, 60, 72,
            "Make a bright, clean sound with a quick start."),
        VoiceChoonTrackRole.LeadB => MelodicPair("lead-b", "WAAH", "NEEOOW", RecordingStyle.OneShot, 60, 72,
            "Give the response voice a different character from Lead A."),
        VoiceChoonTrackRole.PercussionFx =>
        [
            new("perc-pop", "Pop", "POP", RecordingStyle.Percussion, 60, "A dry lip pop or tongue click."),
            new("perc-tchk", "Click", "TCHK", RecordingStyle.Percussion, 62, "A crisp, very short consonant sound.")
        ],
        VoiceChoonTrackRole.Arp => MelodicPair("arp", "TING", "DING", RecordingStyle.OneShot, 64, 76,
            "Keep it tiny and percussive so fast notes remain distinct."),
        VoiceChoonTrackRole.VocalStabs => MelodicPair("stabs", "HEY", "BAH", RecordingStyle.OneShot, 60, 67,
            "Shout a short syllable and stop cleanly."),
        _ => MelodicPair("voice", "BOOP", "WAAH", RecordingStyle.OneShot, 60, 72,
            "Use two contrasting short sounds; VoiceChoon will choose the nearest root.")
    };

    private static SoundRecordingPrompt[] MelodicPair(
        string key,
        string low,
        string high,
        RecordingStyle style,
        int lowRoot,
        int highRoot,
        string guidance) =>
    [
        new($"{key}-low", "Low sound", low, style, lowRoot, guidance),
        new($"{key}-high", "High sound", high, style, highRoot, guidance)
    ];
}
