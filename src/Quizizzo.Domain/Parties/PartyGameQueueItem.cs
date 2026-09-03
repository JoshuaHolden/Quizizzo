namespace Quizizzo.Domain.Parties;

public sealed record PartyGameQueueItem
{
    public const int MaximumGameKeyLength = 64;
    public const int MaximumConfigurationLength = 4096;

    public PartyGameQueueItem(Guid queueItemId, string gameKey, string configurationJson)
    {
        if (queueItemId == Guid.Empty)
        {
            throw new ArgumentException("A queue item ID is required.", nameof(queueItemId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(gameKey);
        var normalizedGameKey = gameKey.Trim();
        if (normalizedGameKey.Length > MaximumGameKeyLength)
        {
            throw new ArgumentOutOfRangeException(nameof(gameKey));
        }

        var normalizedConfiguration = string.IsNullOrWhiteSpace(configurationJson)
            ? "{}"
            : configurationJson.Trim();
        if (normalizedConfiguration.Length > MaximumConfigurationLength)
        {
            throw new ArgumentOutOfRangeException(nameof(configurationJson));
        }

        QueueItemId = queueItemId;
        GameKey = normalizedGameKey;
        ConfigurationJson = normalizedConfiguration;
    }

    public Guid QueueItemId { get; }
    public string GameKey { get; }
    public string ConfigurationJson { get; }
}
