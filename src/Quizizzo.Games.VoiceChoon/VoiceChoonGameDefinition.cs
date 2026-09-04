using Quizizzo.GameContracts;

namespace Quizizzo.Games.VoiceChoon;

public static class VoiceChoonGameDefinition
{
    public const string GameKey = "voicechoon";
    public const int MinimumPlayers = 1;
    public const int NormalMinimumPlayers = 3;
    public const int MaximumPlayers = 8;

    public static GameDescriptor Descriptor { get; } = new(
        GameKey,
        "VoiceChoon",
        MinimumPlayers,
        MaximumPlayers,
        "Perform a complete song together using your own ridiculous recorded sounds.",
        "Co-op rhythm · 3–8 players");
}
