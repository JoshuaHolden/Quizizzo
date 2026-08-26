namespace Quizizzo.Domain.Players;

public readonly record struct PlayerName
{
    private PlayerName(string value) => Value = value;

    public string Value { get; }

    public static PlayerName Parse(string value)
    {
        if (!TryCreate(value, out var playerName, out var error))
        {
            throw new ArgumentException(error, nameof(value));
        }

        return playerName;
    }

    public static bool TryCreate(string? value, out PlayerName playerName, out string? error)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            playerName = default;
            error = "Enter a player name.";
            return false;
        }

        if (normalized.Length > QuizizzoLimits.PlayerNameLength)
        {
            playerName = default;
            error = $"Player names can contain at most {QuizizzoLimits.PlayerNameLength} characters.";
            return false;
        }

        if (normalized.Any(char.IsControl))
        {
            playerName = default;
            error = "Player names cannot contain control characters.";
            return false;
        }

        playerName = new PlayerName(normalized);
        error = null;
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}
