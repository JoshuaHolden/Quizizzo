using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Quizizzo.Application.Abstractions;

namespace Quizizzo.Infrastructure.Voice;

public sealed partial class FileSystemVoiceSampleStore : IVoiceSampleStore
{
    private readonly string rootPath;
    private readonly int maximumAssetBytes;
    private readonly TimeSpan retentionPeriod;
    private readonly TimeProvider timeProvider;

    public FileSystemVoiceSampleStore(IOptions<VoiceSampleStoreOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.RootPath))
        {
            throw new InvalidOperationException("VoiceSamples:RootPath is required.");
        }
        if (value.MaximumAssetBytes is < VoiceSampleStoreOptions.MinimumAssetBytes or
            > VoiceSampleStoreOptions.MaximumConfiguredAssetBytes)
        {
            throw new InvalidOperationException("VoiceSamples:MaximumAssetBytes is outside the supported bounds.");
        }
        if (value.RetentionPeriod <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("VoiceSamples:RetentionPeriod must be positive.");
        }
        rootPath = Path.GetFullPath(value.RootPath);
        maximumAssetBytes = value.MaximumAssetBytes;
        retentionPeriod = value.RetentionPeriod;
        this.timeProvider = timeProvider;
    }

    public async Task<VoiceSampleReference> SaveAsync(
        VoiceSampleUpload sample,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.Content.IsEmpty || sample.Content.Length > maximumAssetBytes)
        {
            throw new ArgumentException("The voice sample is empty or too large.", nameof(sample));
        }
        var extension = ExtensionFor(sample.ContentType);
        if (!HasExpectedSignature(sample.Content.Span, extension))
        {
            throw new ArgumentException("The voice sample bytes do not match the declared audio type.", nameof(sample));
        }

        var identifier = Guid.NewGuid().ToString("N");
        var key = $"{identifier[..2]}/{identifier}{extension}";
        var path = ResolveKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{identifier}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(sample.Content, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path);
            File.SetLastWriteTimeUtc(path, timeProvider.GetUtcNow().UtcDateTime);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        return new VoiceSampleReference(
            key,
            sample.ContentType.ToLowerInvariant(),
            sample.Content.Length,
            createdAtUtc,
            createdAtUtc.Add(retentionPeriod));
    }

    public async Task<VoiceSampleContent?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolveKey(key);
        if (!File.Exists(path))
        {
            return null;
        }
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumAssetBytes)
        {
            throw new InvalidDataException("The stored voice sample exceeds the configured limit.");
        }
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return new VoiceSampleContent(bytes, ContentTypeFor(Path.GetExtension(path)));
    }

    public Task<int> DeleteExpiredAsync(
        DateTimeOffset expiresBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult(0);
        }
        var deleted = 0;
        foreach (var directory in Directory.EnumerateDirectories(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new DirectoryInfo(directory);
            if (!ShardPattern().IsMatch(info.Name) || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = $"{info.Name}/{Path.GetFileName(path)}";
                var file = new FileInfo(path);
                if (!KeyPattern().IsMatch(key) || file.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    file.LastWriteTimeUtc > expiresBeforeUtc.UtcDateTime)
                {
                    continue;
                }
                try
                {
                    file.Delete();
                    deleted++;
                }
                catch (FileNotFoundException)
                {
                }
                catch (IOException)
                {
                }
            }
        }
        return Task.FromResult(deleted);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveKey(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private string ResolveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !KeyPattern().IsMatch(key))
        {
            throw new ArgumentException("The voice sample key is invalid.", nameof(key));
        }
        var path = Path.GetFullPath(Path.Combine(rootPath, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("The voice sample key escapes its storage root.", nameof(key));
        }
        return path;
    }

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "audio/webm" => ".webm",
        "audio/ogg" => ".ogg",
        "audio/wav" or "audio/wave" or "audio/x-wav" => ".wav",
        "audio/mp4" or "audio/m4a" => ".m4a",
        _ => throw new ArgumentException("Only WebM, Ogg, WAV, and M4A voice samples are supported.", nameof(contentType))
    };

    private static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".webm" => "audio/webm",
        ".ogg" => "audio/ogg",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        _ => throw new InvalidOperationException("The stored voice sample type is invalid.")
    };

    private static bool HasExpectedSignature(ReadOnlySpan<byte> content, string extension) => extension switch
    {
        ".webm" => content.Length >= 4 && content[..4].SequenceEqual((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]),
        ".ogg" => content.Length >= 4 && content[..4].SequenceEqual("OggS"u8),
        ".wav" => content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) &&
            content.Slice(8, 4).SequenceEqual("WAVE"u8),
        ".m4a" => content.Length >= 12 && content.Slice(4, 4).SequenceEqual("ftyp"u8),
        _ => false
    };

    [GeneratedRegex("^[0-9a-f]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShardPattern();

    [GeneratedRegex("^[0-9a-f]{2}/[0-9a-f]{32}\\.(webm|ogg|wav|m4a)$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}