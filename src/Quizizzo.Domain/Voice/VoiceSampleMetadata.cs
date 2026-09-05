namespace Quizizzo.Domain.Voice;

public sealed class VoiceSampleMetadata
{
    private VoiceSampleMetadata()
    {
    }

    private VoiceSampleMetadata(
        Guid id,
        Guid submissionId,
        Guid partyId,
        Guid gameInstanceId,
        Guid playerId,
        string promptKey,
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
        PromptKey = promptKey;
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
    public string PromptKey { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Length { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public bool IsRetainedForReplay { get; private set; }

    public void RetainForReplay()
    {
        IsRetainedForReplay = true;
        ExpiresAtUtc = DateTimeOffset.MaxValue;
    }

    public static VoiceSampleMetadata Create(
        Guid submissionId,
        Guid partyId,
        Guid gameInstanceId,
        Guid playerId,
        string promptKey,
        string storageKey,
        string contentType,
        long length,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (submissionId == Guid.Empty || partyId == Guid.Empty || gameInstanceId == Guid.Empty ||
            playerId == Guid.Empty)
        {
            throw new ArgumentException("Voice sample ownership identifiers are required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(promptKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (promptKey.Length > 128 || length <= 0 || expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Voice sample metadata is outside the supported bounds.");
        }
        return new VoiceSampleMetadata(
            Guid.NewGuid(), submissionId, partyId, gameInstanceId, playerId, promptKey.Trim(),
            storageKey, contentType, length, createdAtUtc, expiresAtUtc);
    }
}
