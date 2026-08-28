namespace Quizizzo.Domain.Drawings;

public sealed class DrawingAssetMetadata
{
    private DrawingAssetMetadata()
    {
    }

    private DrawingAssetMetadata(
        Guid id,
        Guid submissionId,
        Guid partyId,
        Guid gameInstanceId,
        Guid playerId,
        string roundId,
        int frameNumber,
        string storageKey,
        string contentType,
        long length,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Id = id;
        SubmissionId = submissionId;
        PartyId = partyId;
        GameInstanceId = gameInstanceId;
        PlayerId = playerId;
        RoundId = roundId;
        FrameNumber = frameNumber;
        StorageKey = storageKey;
        ContentType = contentType;
        Length = length;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid PartyId { get; private set; }
    public Guid GameInstanceId { get; private set; }
    public Guid PlayerId { get; private set; }
    public string RoundId { get; private set; } = string.Empty;
    public int FrameNumber { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Length { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public static DrawingAssetMetadata Create(
        Guid submissionId,
        Guid partyId,
        Guid gameInstanceId,
        Guid playerId,
        string roundId,
        int frameNumber,
        string storageKey,
        string contentType,
        long length,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (submissionId == Guid.Empty || partyId == Guid.Empty || gameInstanceId == Guid.Empty ||
            playerId == Guid.Empty)
        {
            throw new ArgumentException("Drawing asset ownership identifiers are required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(roundId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (roundId.Length > 128 || frameNumber is < 1 or > 12 || length <= 0 ||
            expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Drawing asset metadata is outside the supported bounds.");
        }
        return new DrawingAssetMetadata(
            Guid.NewGuid(), submissionId, partyId, gameInstanceId, playerId, roundId.Trim(),
            frameNumber, storageKey, contentType, length, createdAtUtc, expiresAtUtc);
    }
}
