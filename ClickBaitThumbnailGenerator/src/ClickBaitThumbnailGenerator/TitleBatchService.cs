using Microsoft.Extensions.Logging;

namespace ClickBaitThumbnailGenerator;

public sealed class TitleBatchService(
    SqliteStore store,
    IOpenAiClient openAiClient,
    IRetryPolicy retryPolicy,
    OpenAiOptions openAiOptions,
    StorageOptions storageOptions,
    ILogger<TitleBatchService> logger)
{
    private static readonly Action<ILogger, int, Exception?> RecoveredJobs = LoggerMessage.Define<int>(
        LogLevel.Information, new EventId(2001, nameof(RecoveredJobs)), "Recovered {RecoveredJobCount} interrupted title jobs");
    private static readonly Action<ILogger, string, Exception?> GenerationFailed = LoggerMessage.Define<string>(
        LogLevel.Warning, new EventId(2002, nameof(GenerationFailed)), "Distractor-title generation failed for scenario {ScenarioId}");

    public async Task GenerateAsync(int? count, int concurrency, CancellationToken cancellationToken)
    {
        if (count is <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (concurrency is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(concurrency));
        var recovered = await store.RecoverInterruptedTitleJobsAsync(cancellationToken).ConfigureAwait(false);
        if (recovered > 0) RecoveredJobs(logger, recovered, null);
        await store.EnsurePendingTitleJobsAsync(count, cancellationToken).ConfigureAwait(false);
        var initial = await store.GetTitleStatisticsAsync(cancellationToken).ConfigureAwait(false);
        if (initial.Pending == 0)
        {
            Console.WriteLine("No images are waiting for AI distractor titles.");
            return;
        }

        var target = count is null ? initial.Pending : Math.Min(count.Value, initial.Pending);
        var completed = 0;
        var claimed = 0;
        using var limit = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        var workers = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            while (!linked.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref claimed) > target) return;
                var job = await store.TryLeaseNextTitleJobAsync(linked.Token).ConfigureAwait(false);
                if (job is null) return;
                try
                {
                    var imagePath = ResolveImagePath(job.FinalFilename);
                    var imageBytes = await File.ReadAllBytesAsync(imagePath, linked.Token).ConfigureAwait(false);
                    var generated = await retryPolicy.ExecuteAsync(
                        token => openAiClient.GenerateDistractorTitlesAsync(imageBytes, token), linked.Token).ConfigureAwait(false);
                    await store.CompleteTitleJobAsync(job.ScenarioId, openAiOptions.VisionModel, generated, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    await store.FailTitleJobAsync(job.ScenarioId, "Title generation cancelled; resume to continue.", CancellationToken.None).ConfigureAwait(false);
                    return;
                }
                catch (Exception exception)
                {
                    GenerationFailed(logger, job.ScenarioId, exception);
                    await store.FailTitleJobAsync(job.ScenarioId, exception.Message, linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    var stats = await store.GetTitleStatisticsAsync(CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"Titles {Math.Min(done, target)}/{target} | Generated {stats.Generated} | Failed {stats.Failed}");
                    if (done >= target) limit.Cancel();
                }
            }
        }, linked.Token)).ToArray();

        try { await Task.WhenAll(workers).ConfigureAwait(false); }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
    }

    private string ResolveImagePath(string filename)
    {
        var root = Path.GetFullPath(storageOptions.GeneratedPath) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(storageOptions.GeneratedPath, filename));
        if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path))
            throw new FileNotFoundException($"Generated image is missing for {filename}.", path);
        return path;
    }
}
