namespace Quizizzo.Domain.Voice;

public sealed class VoiceChoonSong
{
    public const int MaximumDisplayNameLength = 80;

    private VoiceChoonSong() { }

    private VoiceChoonSong(Guid id, string key, string displayName, string fileName, byte[] midiData,
        int minimumPlayers, int maximumPlayers, double durationSeconds, int trackCount,
        DateTimeOffset createdAtUtc, string createdByUserId)
    {
        Id = id;
        Key = key;
        DisplayName = NormalizeDisplayName(displayName);
        FileName = fileName;
        MidiData = midiData;
        MinimumPlayers = minimumPlayers;
        MaximumPlayers = maximumPlayers;
        DurationSeconds = durationSeconds;
        TrackCount = trackCount;
        CreatedAtUtc = createdAtUtc;
        CreatedByUserId = createdByUserId;
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public byte[] MidiData { get; private set; } = [];
    public int MinimumPlayers { get; private set; }
    public int MaximumPlayers { get; private set; }
    public double DurationSeconds { get; private set; }
    public int TrackCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;

    public static VoiceChoonSong Create(string key, string displayName, string fileName, byte[] midiData,
        int minimumPlayers, int maximumPlayers, double durationSeconds, int trackCount,
        DateTimeOffset createdAtUtc, string createdByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);
        ArgumentNullException.ThrowIfNull(midiData);
        if (midiData.Length is < 14 or > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(midiData));
        if (minimumPlayers is < 2 or > 8 || maximumPlayers < minimumPlayers || maximumPlayers > 8)
            throw new ArgumentOutOfRangeException(nameof(minimumPlayers));
        if (durationSeconds <= 0 || trackCount <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        return new(Guid.NewGuid(), key, displayName, fileName, midiData, minimumPlayers, maximumPlayers,
            durationSeconds, trackCount, createdAtUtc, createdByUserId);
    }

    public void Rename(string displayName) => DisplayName = NormalizeDisplayName(displayName);

    private static string NormalizeDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var normalized = displayName.Trim();
        if (normalized.Length > MaximumDisplayNameLength)
            throw new ArgumentOutOfRangeException(nameof(displayName),
                $"Song names can be at most {MaximumDisplayNameLength} characters.");
        return normalized;
    }
}
