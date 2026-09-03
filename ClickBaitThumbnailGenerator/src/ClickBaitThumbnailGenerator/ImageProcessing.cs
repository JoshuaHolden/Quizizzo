using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ClickBaitThumbnailGenerator;

public interface ITextChecker
{
    Task<TextDetectionResult> CheckAsync(Image image, CancellationToken cancellationToken);
}

public sealed class TesseractTextChecker(string temporaryPath) : ITextChecker
{
    private int? _available;

    public async Task<TextDetectionResult> CheckAsync(Image image, CancellationToken cancellationToken)
    {
        if (_available == 0) return TextDetectionResult.CheckUnavailable;
        Directory.CreateDirectory(temporaryPath);
        var input = Path.Combine(temporaryPath, $"ocr-{Guid.NewGuid():N}.png");
        try
        {
            await image.SaveAsync(input, new PngEncoder(), cancellationToken).ConfigureAwait(false);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "tesseract",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(input);
            process.StartInfo.ArgumentList.Add("stdout");
            process.StartInfo.ArgumentList.Add("--psm");
            process.StartInfo.ArgumentList.Add("11");
            try
            {
                if (!process.Start()) return TextDetectionResult.CheckUnavailable;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                _available = 0;
                return TextDetectionResult.CheckUnavailable;
            }
            _available = 1;
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            if (process.ExitCode != 0) return TextDetectionResult.CheckUnavailable;
            return output.Any(char.IsLetterOrDigit) ? TextDetectionResult.TextSuspected : TextDetectionResult.NoTextDetected;
        }
        finally
        {
            try { File.Delete(input); } catch (IOException) { }
        }
    }
}

public sealed class ImageProcessor(ProcessingOptions options, StorageOptions storage, ITextChecker textChecker)
{
    public async Task<ProcessedImage> ProcessAsync(
        string scenarioId,
        byte[] sourceBytes,
        IReadOnlyCollection<string> existingHashes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(storage.GeneratedPath);
        Directory.CreateDirectory(storage.TemporaryPath);
        Image<Rgba32> source;
        try
        {
            source = Image.Load<Rgba32>(sourceBytes);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or ArgumentException)
        {
            throw new InvalidDataException("The generated response was not a valid image.", exception);
        }
        using (source)
        {
            var sourceWidth = source.Width;
            var sourceHeight = source.Height;
            var crop = CalculateCenteredCrop(sourceWidth, sourceHeight, options.OutputWidth, options.OutputHeight);
            if (crop.Width < options.OutputWidth || crop.Height < options.OutputHeight)
                throw new InvalidDataException($"Source image {sourceWidth}x{sourceHeight} is too small; upscaling is disabled.");

            using var final = source.Clone(context => context.Crop(crop).Resize(options.OutputWidth, options.OutputHeight));
            final.Metadata.ExifProfile = null;
            final.Metadata.IccProfile = null;
            final.Metadata.XmpProfile = null;

            var hash = ComputeDifferenceHash(final);
            var duplicate = existingHashes.Any(existing => HammingDistance(hash, existing) <= options.DuplicateHashThreshold);
            var textResult = await textChecker.CheckAsync(final, cancellationToken).ConfigureAwait(false);
            var filename = ScenarioUtilities.Filename(scenarioId);
            var destination = Path.GetFullPath(Path.Combine(storage.GeneratedPath, filename));
            var generatedRoot = Path.GetFullPath(storage.GeneratedPath) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(generatedRoot, StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe output path.");

            var temporary = Path.Combine(storage.TemporaryPath, $"{scenarioId}-{Guid.NewGuid():N}.webp.tmp");
            try
            {
                await final.SaveAsync(temporary, new WebpEncoder { Quality = options.WebPQuality }, cancellationToken).ConfigureAwait(false);
                var bytes = await File.ReadAllBytesAsync(temporary, cancellationToken).ConfigureAwait(false);
                var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
                File.Move(temporary, destination, true);
                return new ProcessedImage(sourceWidth, sourceHeight, filename, sha, hash, textResult, duplicate);
            }
            finally
            {
                try { File.Delete(temporary); } catch (IOException) { }
            }
        }
    }

    public static Rectangle CalculateCenteredCrop(int sourceWidth, int sourceHeight, int outputWidth, int outputHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || outputWidth <= 0 || outputHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Image dimensions must be positive.");
        var targetRatio = (double)outputWidth / outputHeight;
        var sourceRatio = (double)sourceWidth / sourceHeight;
        int width;
        int height;
        if (sourceRatio > targetRatio)
        {
            height = sourceHeight;
            width = (int)Math.Floor(height * targetRatio);
        }
        else
        {
            width = sourceWidth;
            height = (int)Math.Floor(width / targetRatio);
        }
        return new Rectangle((sourceWidth - width) / 2, (sourceHeight - height) / 2, width, height);
    }

    public static string ComputeDifferenceHash(Image image)
    {
        using var small = image.CloneAs<Rgba32>();
        small.Mutate(context => context.Resize(9, 8).Grayscale());
        ulong hash = 0;
        small.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < 8; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < 8; x++)
                {
                    hash <<= 1;
                    if (row[x].R > row[x + 1].R) hash |= 1;
                }
            }
        });
        return hash.ToString("X16", CultureInfo.InvariantCulture);
    }

    public static int HammingDistance(string left, string right)
    {
        if (!ulong.TryParse(left, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var leftValue) ||
            !ulong.TryParse(right, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rightValue))
            throw new ArgumentException("Perceptual hashes must be 64-bit hexadecimal strings.");
        return BitOperations.PopCount(leftValue ^ rightValue);
    }
}
