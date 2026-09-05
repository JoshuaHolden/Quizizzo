namespace Quizizzo.Domain.Voice;

public sealed class VoiceChoonReplay
{
    public const int MaximumSnapshotCharacters = 2_000_000;

    private VoiceChoonReplay() { }

    private VoiceChoonReplay(
        Guid id,
        string shareCode,
        Guid partyId,
        Guid gameInstanceId,
        string hostUserId,
        string title,
        string snapshotJson,
        Guid[] sampleAssetIds,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ShareCode = shareCode;
        PartyId = partyId;
        GameInstanceId = gameInstanceId;
        HostUserId = hostUserId;
        Title = title;
        SnapshotJson = snapshotJson;
        SampleAssetIds = sampleAssetIds;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string ShareCode { get; private set; } = string.Empty;
    public Guid PartyId { get; private set; }
    public Guid GameInstanceId { get; private set; }
    public string HostUserId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string SnapshotJson { get; private set; } = string.Empty;
    public Guid[] SampleAssetIds { get; private set; } = [];
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static VoiceChoonReplay Create(
        string shareCode,
        Guid partyId,
        Guid gameInstanceId,
        string hostUserId,
        string title,
        string snapshotJson,
        IEnumerable<Guid> sampleAssetIds,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        if (partyId == Guid.Empty || gameInstanceId == Guid.Empty ||
            shareCode.Length is < 16 or > 64 || title.Length > 160 ||
            snapshotJson.Length > MaximumSnapshotCharacters)
        {
            throw new ArgumentException("VoiceChoon replay data is outside the supported bounds.");
        }

        var samples = sampleAssetIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (samples.Length is 0 or > 64)
        {
            throw new ArgumentException("A replay must reference a bounded set of voice samples.");
        }

        return new VoiceChoonReplay(
            Guid.NewGuid(), shareCode, partyId, gameInstanceId, hostUserId.Trim(), title.Trim(),
            snapshotJson, samples, createdAtUtc);
    }
}
