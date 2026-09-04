using System.Reflection;

namespace Quizizzo.Games.VoiceChoon;

public static class VoiceChoonSongCatalog
{
    public const string DefaultSongKey = "coop-showdown";
    public const string WubquakeSongKey = "wubquake";
    public const string GreensleevesSongKey = "greensleeves";
    public const string DefaultSongName = "quizizzo_coop_showdown.mid";
    private const string WubquakeSongName = "quizizzo_wubquake.mid";

    private static readonly IReadOnlyList<VoiceChoonSongDefinition> Definitions =
    [
        new(
            DefaultSongKey,
            "Co-op Showdown",
            DefaultSongName,
            "Meet your extremely human orchestra.",
            "Record the example sounds clearly: short hits should be punchy, sustained sounds should be steady.",
            "Quizizzo.Games.VoiceChoon.Assets.quizizzo_coop_showdown.mid"),
        new(
            WubquakeSongKey,
            "Wubquake",
            WubquakeSongName,
            "Build a ridiculous bass-heavy dubstep band.",
            "For bass and chord prompts, make a low, steady buzz or vowel. For drums, use short punchy mouth hits. For leads and stabs, use bright, sharp syllables.",
            "Quizizzo.Games.VoiceChoon.Assets.quizizzo_wubquake.mid"),
        new(
            GreensleevesSongKey,
            "Greensleeves",
            "gs.mid",
            "Turn Greensleeves into a tiny live mouth-noise ensemble.",
            "This song only uses melody, chords, bass, and light percussion. Record a clear bright lead, a steady held vowel for chords, a low rounded bass sound, and short light rhythmic clicks.",
            "Quizizzo.Games.VoiceChoon.Assets.gs.mid")
    ];

    public static IReadOnlyList<VoiceChoonSongDefinition> Available => Definitions;

    public static RawMidiSong LoadDefaultSong()
        => Load(DefaultSongKey);

    public static RawMidiSong Load(string songKey)
    {
        var definition = GetDefinition(songKey);
        using var stream = typeof(VoiceChoonSongCatalog).Assembly
            .GetManifestResourceStream(definition.ResourceName)
            ?? throw new InvalidOperationException($"Embedded MIDI resource '{definition.ResourceName}' was not found.");
        return MidiParser.Parse(stream, definition.FileName);
    }

    public static VoiceChoonSongDefinition GetDefinition(string songKey) =>
        Definitions.FirstOrDefault(item => string.Equals(item.Key, songKey, StringComparison.OrdinalIgnoreCase))
        ?? Definitions[0];

    public static bool IsKnownKey(string songKey) =>
        Definitions.Any(item => string.Equals(item.Key, songKey, StringComparison.OrdinalIgnoreCase));

    public static RawMidiSong Load(Stream stream, string sourceName) =>
        MidiParser.Parse(stream, sourceName);
}

public sealed record VoiceChoonSongDefinition(
    string Key,
    string DisplayName,
    string FileName,
    string BriefingMessage,
    string RecordingMessage,
    string ResourceName);
