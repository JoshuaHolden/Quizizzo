using System.Text.Json;

namespace ClickBaitThumbnailGenerator;

public sealed class ExportService(SqliteStore store, ProcessingOptions processing, StorageOptions storage)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<int> ExportAsync(string output, bool includeProvenance, CancellationToken cancellationToken)
    {
        var approved = await store.GetApprovedJobsAsync(cancellationToken).ConfigureAwait(false);
        var root = Path.GetFullPath(output);
        var imageDirectory = Path.Combine(root, "images");
        Directory.CreateDirectory(imageDirectory);
        var manifest = new List<ExportManifestItem>();
        var provenance = new List<ProvenanceItem>();
        foreach (var job in approved)
        {
            if (job.AiTitles.Count != 2)
                throw new InvalidOperationException($"Approved image {job.ScenarioId} has no complete AI distractor titles. Run titles generate --all first.");
            var source = Path.GetFullPath(Path.Combine(storage.GeneratedPath, job.FinalFilename!));
            if (!File.Exists(source)) throw new FileNotFoundException($"Approved image is missing for {job.ScenarioId}.", source);
            var destination = Path.Combine(imageDirectory, job.FinalFilename!);
            await CopyAtomicAsync(source, destination, cancellationToken).ConfigureAwait(false);
            manifest.Add(new ExportManifestItem(
                job.ScenarioId,
                $"images/{job.FinalFilename}",
                job.Category,
                processing.OutputWidth,
                processing.OutputHeight,
                job.Sha256!,
                job.AiTitles));
            if (includeProvenance)
                provenance.Add(new ProvenanceItem(job.ScenarioId, job.Model!, job.GeneratedAtUtc, job.FullPrompt!, job.Sha256!, job.PerceptualHash!));
        }
        await WriteAtomicJsonAsync(Path.Combine(root, "thumbnails.json"), manifest, cancellationToken).ConfigureAwait(false);
        if (includeProvenance) await WriteAtomicJsonAsync(Path.Combine(root, "provenance.json"), provenance, cancellationToken).ConfigureAwait(false);
        return approved.Count;
    }

    private static async Task CopyAtomicAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using var input = File.OpenRead(source);
            await using var output = File.Create(temporary);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, true);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } }
    }

    private static async Task WriteAtomicJsonAsync<T>(string destination, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using var stream = File.Create(temporary);
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, true);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } }
    }
}
