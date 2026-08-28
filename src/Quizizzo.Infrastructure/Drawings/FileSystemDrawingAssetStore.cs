using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Quizizzo.Application.Abstractions;

namespace Quizizzo.Infrastructure.Drawings;

public sealed partial class FileSystemDrawingAssetStore : IDrawingAssetStore
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly string rootPath;
    private readonly int maximumAssetBytes;
    private readonly TimeSpan retentionPeriod;
    private readonly TimeProvider timeProvider;

    public FileSystemDrawingAssetStore(
        IOptions<DrawingAssetStoreOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.RootPath))
        {
            throw new InvalidOperationException("DrawingAssets:RootPath is required.");
        }
        if (value.MaximumAssetBytes is < DrawingAssetStoreOptions.MinimumAssetBytes or
            > DrawingAssetStoreOptions.MaximumConfiguredAssetBytes)
        {
            throw new InvalidOperationException(
                $"DrawingAssets:MaximumAssetBytes must be from {DrawingAssetStoreOptions.MinimumAssetBytes} " +
                $"to {DrawingAssetStoreOptions.MaximumConfiguredAssetBytes}.");
        }
        if (value.RetentionPeriod <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("DrawingAssets:RetentionPeriod must be positive.");
        }

        rootPath = Path.GetFullPath(value.RootPath);
        maximumAssetBytes = value.MaximumAssetBytes;
        retentionPeriod = value.RetentionPeriod;
        this.timeProvider = timeProvider;
    }

    public async Task<DrawingAssetReference> SaveAsync(
        DrawingAssetUpload asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Content.IsEmpty)
        {
            throw new ArgumentException("A drawing asset cannot be empty.", nameof(asset));
        }
        if (asset.Content.Length > maximumAssetBytes)
        {
            throw new ArgumentException(
                $"The drawing asset exceeds the {maximumAssetBytes}-byte limit.", nameof(asset));
        }

        var extension = asset.ContentType.ToLowerInvariant() switch
        {
            "image/webp" => ".webp",
            "image/png" => ".png",
            _ => throw new ArgumentException("Only WebP and PNG drawing assets are supported.", nameof(asset))
        };
        if (!HasExpectedSignature(asset.Content.Span, extension))
        {
            throw new ArgumentException("The drawing bytes do not match the declared image type.", nameof(asset));
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
                await stream.WriteAsync(asset.Content, cancellationToken);
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
        return new DrawingAssetReference(
            key,
            asset.ContentType.ToLowerInvariant(),
            asset.Content.Length,
            createdAtUtc,
            createdAtUtc.Add(retentionPeriod));
    }

    public async Task<DrawingAssetContent?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
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
            throw new InvalidDataException("The stored drawing asset exceeds the configured limit.");
        }
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".webp" => "image/webp",
            ".png" => "image/png",
            _ => throw new InvalidOperationException("The stored drawing asset type is invalid.")
        };
        return new DrawingAssetContent(bytes, contentType);
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
            var directoryInfo = new DirectoryInfo(directory);
            if (!ShardDirectoryPattern().IsMatch(directoryInfo.Name) ||
                directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = $"{directoryInfo.Name}/{Path.GetFileName(path)}";
                if (!AssetKeyPattern().IsMatch(key))
                {
                    continue;
                }

                var file = new FileInfo(path);
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    file.LastWriteTimeUtc > expiresBeforeUtc.UtcDateTime)
                {
                    continue;
                }

                try
                {
                    file.Delete();
                    deleted += 1;
                }
                catch (FileNotFoundException)
                {
                }
                catch (IOException)
                {
                    // An active reader can temporarily hold the file on Windows; retry next sweep.
                }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    directoryInfo.Delete();
                }
            }
            catch (IOException)
            {
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

    private static bool HasExpectedSignature(ReadOnlySpan<byte> content, string extension) => extension switch
    {
        ".png" => content.StartsWith(PngSignature),
        ".webp" => content.Length >= 12 &&
            content[..4].SequenceEqual("RIFF"u8) &&
            content.Slice(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };

    private string ResolveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !AssetKeyPattern().IsMatch(key))
        {
            throw new ArgumentException("The drawing asset key is invalid.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(rootPath, key.Replace('/', Path.DirectorySeparatorChar)));
        var requiredPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        if (!path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The drawing asset key is outside the configured store.", nameof(key));
        }
        return path;
    }

    [GeneratedRegex("^[0-9a-f]{2}/[0-9a-f]{32}\\.(webp|png)$", RegexOptions.CultureInvariant)]
    private static partial Regex AssetKeyPattern();

    [GeneratedRegex("^[0-9a-f]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShardDirectoryPattern();
}
