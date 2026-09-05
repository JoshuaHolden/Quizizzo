using Quizizzo.Domain.Voice;

namespace Quizizzo.Domain.Tests;

public sealed class VoiceChoonSongTests
{
    [Fact]
    public void Rename_trims_and_updates_the_display_name()
    {
        var song = CreateSong();

        song.Rename("  Clair de Lune  ");

        Assert.Equal("Clair de Lune", song.DisplayName);
    }

    [Fact]
    public void Rename_rejects_blank_or_oversized_names()
    {
        var song = CreateSong();

        Assert.Throws<ArgumentException>(() => song.Rename("   "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            song.Rename(new string('x', VoiceChoonSong.MaximumDisplayNameLength + 1)));
    }

    private static VoiceChoonSong CreateSong() => VoiceChoonSong.Create(
        "clair-de-lune", "Original name", "clair.mid", new byte[14], 2, 2, 30, 2,
        new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero), "admin-user");
}
