using System.Reflection;

namespace Quizizzo.Games.VoiceChoon;

public static class VoiceChoonSongCatalog
{
    public const string DefaultSongName = "quizizzo_coop_showdown.mid";
    private const string DefaultSongResource =
        "Quizizzo.Games.VoiceChoon.Assets.quizizzo_coop_showdown.mid";

    public static RawMidiSong LoadDefaultSong()
    {
        using var stream = typeof(VoiceChoonSongCatalog).Assembly
            .GetManifestResourceStream(DefaultSongResource)
            ?? throw new InvalidOperationException($"Embedded MIDI resource '{DefaultSongResource}' was not found.");
        return MidiParser.Parse(stream, DefaultSongName);
    }

    public static RawMidiSong Load(Stream stream, string sourceName) =>
        MidiParser.Parse(stream, sourceName);
}
