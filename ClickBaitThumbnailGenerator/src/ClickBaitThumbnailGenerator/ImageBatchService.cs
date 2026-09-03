namespace ClickBaitThumbnailGenerator;

using Microsoft.Extensions.Logging;

public sealed class ImageBatchService(
    SqliteStore store,
    IOpenAiClient openAiClient,
    IRetryPolicy retryPolicy,
    IPromptBuilder promptBuilder,
    ImageProcessor processor,
    OpenAiOptions openAiOptions,
    ProcessingOptions processingOptions,
    StorageOptions storageOptions,
    ILogger<ImageBatchService> logger)
{
    private static readonly Action<ILogger, int, Exception?> RecoveredJobs = LoggerMessage.Define<int>(
        LogLevel.Information, new EventId(1001, nameof(RecoveredJobs)), "Recovered {RecoveredJobCount} interrupted image jobs");
    private static readonly Action<ILogger, string, Exception?> GenerationFailed = LoggerMessage.Define<string>(
        LogLevel.Warning, new EventId(1002, nameof(GenerationFailed)), "Image generation failed for scenario {ScenarioId}");

    public async Task GenerateAsync(int? count, int concurrency, CancellationToken cancellationToken)
    {
        if (count is <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (concurrency is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(concurrency));
        var recovered = await store.RecoverInterruptedJobsAsync(cancellationToken).ConfigureAwait(false);
        if (recovered > 0) RecoveredJobs(logger, recovered, null);
        var queued = await store.EnsurePendingJobsAsync(count, cancellationToken).ConfigureAwait(false);
        if (queued == 0 && (await store.GetStatisticsAsync(cancellationToken).ConfigureAwait(false)).Pending == 0)
        {
            Console.WriteLine("No pending scenarios. Create scenarios or retry failed jobs first.");
            return;
        }

        var initial = await store.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        var target = count ?? initial.Pending;
        var completed = 0;
        var claimed = 0;
        using var limit = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        var workers = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            while (!linked.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref claimed) > target) return;
                var job = await store.TryLeaseNextJobAsync(linked.Token).ConfigureAwait(false);
                if (job is null) return;
                try
                {
                    var scenario = new Scenario(job.ScenarioId, job.Scene, ScenarioUtilities.Normalize(job.Scene), job.Category, string.Empty, job.VisualStyle, DateTimeOffset.UtcNow);
                    var prompt = promptBuilder.Build(scenario);
                    var generated = await retryPolicy.ExecuteAsync(token => openAiClient.GenerateImageAsync(prompt, token), linked.Token).ConfigureAwait(false);
                    if (processingOptions.KeepOriginalFiles)
                    {
                        var originals = Path.Combine(storageOptions.GeneratedPath, "originals");
                        Directory.CreateDirectory(originals);
                        await File.WriteAllBytesAsync(Path.Combine(originals, $"{job.ScenarioId}.source"), generated.Bytes, linked.Token).ConfigureAwait(false);
                    }
                    var hashes = await store.GetExistingHashesAsync(linked.Token).ConfigureAwait(false);
                    var processed = await processor.ProcessAsync(job.ScenarioId, generated.Bytes, hashes, linked.Token).ConfigureAwait(false);
                    await store.CompleteJobAsync(job.ScenarioId, openAiOptions.ImageModel, prompt, generated, processed, openAiOptions.EstimatedCostPerImageUsd, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    await store.FailJobAsync(job.ScenarioId, "Generation cancelled; retry or resume to continue.", CancellationToken.None).ConfigureAwait(false);
                    return;
                }
                catch (Exception exception)
                {
                    GenerationFailed(logger, job.ScenarioId, exception);
                    await store.FailJobAsync(job.ScenarioId, exception.Message, linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    var stats = await store.GetStatisticsAsync(CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"Generated {Math.Min(done, target)}/{target} | Approved {stats.Approved} | Failed {stats.Failed} | Estimated spend ${stats.EstimatedSpend:0.00}");
                    if (done >= target) limit.Cancel();
                }
            }
        }, linked.Token)).ToArray();

        try { await Task.WhenAll(workers).ConfigureAwait(false); }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
    }
}
