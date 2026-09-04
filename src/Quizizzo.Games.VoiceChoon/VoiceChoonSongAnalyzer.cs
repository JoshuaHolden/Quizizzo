using System.Text;

namespace Quizizzo.Games.VoiceChoon;

public sealed record VoiceChoonSongAnalysis(
    string SuggestedKey, double DurationSeconds, int TrackCount, int NoteCount,
    int MinimumPlayers, int MaximumPlayers, IReadOnlyList<string> TrackNames);

public static class VoiceChoonSongAnalyzer
{
    public const int MaximumMidiBytes = 1024 * 1024;
    public const int MaximumNotes = 100_000;
    public const double MaximumDurationSeconds = 20 * 60;

    public static VoiceChoonSongAnalysis Analyze(ReadOnlyMemory<byte> data, string fileName, string displayName)
    {
        if (data.Length is < 14 or > MaximumMidiBytes) throw new InvalidDataException("MIDI files must be between 14 bytes and 1 MB.");
        if (!Path.GetExtension(fileName).Equals(".mid", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(fileName).Equals(".midi", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Upload a .mid or .midi file.");
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        RawMidiSong song;
        try { song = MidiParser.Parse(stream, Path.GetFileName(fileName)); }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException)
        { throw new InvalidDataException("The file is not a supported playable Standard MIDI file.", exception); }
        var notes = song.Tracks.Sum(track => track.Notes.Count);
        if (song.Tracks.Count < 2) throw new InvalidDataException("VoiceChoon songs need at least two playable tracks.");
        if (notes > MaximumNotes || song.DurationSeconds > MaximumDurationSeconds)
            throw new InvalidDataException("That MIDI is too long or contains too many notes.");
        var maximum = Math.Clamp(song.Tracks.Count, 2, VoiceChoonGameDefinition.MaximumPlayers);
        var minimum = song.Tracks.Count <= 4 && notes / Math.Max(1, song.DurationSeconds) <= 16 ? 2 : 3;
        minimum = Math.Min(minimum, maximum);
        return new(Slug(displayName), song.DurationSeconds, song.Tracks.Count, notes, minimum, maximum,
            song.Tracks.Select(track => track.Name).ToArray());
    }

    public static string Slug(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character)) builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        return builder.ToString().Trim('-') is { Length: > 0 } slug ? slug[..Math.Min(48, slug.Length)] : "uploaded-song";
    }
}
