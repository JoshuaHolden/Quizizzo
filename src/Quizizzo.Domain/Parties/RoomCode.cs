namespace Quizizzo.Domain.Parties;

public readonly record struct RoomCode
{
    public const int Length = 4;
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private RoomCode(string value) => Value = value;

    public string Value { get; }

    public static RoomCode Parse(string value)
    {
        if (!TryCreate(value, out var roomCode))
        {
            throw new ArgumentException("Room codes must contain four unambiguous letters or digits.", nameof(value));
        }

        return roomCode;
    }

    public static bool TryCreate(string? value, out RoomCode roomCode)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (normalized is null ||
            normalized.Length != Length ||
            normalized.Any(character => !Alphabet.Contains(character)))
        {
            roomCode = default;
            return false;
        }

        roomCode = new RoomCode(normalized);
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}
