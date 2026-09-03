using System.Net;
using System.Text;
using System.Text.Json;
using ClickBaitThumbnailGenerator;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ClickBaitThumbnailGenerator.Tests;

public sealed class ScenarioTests
{
    [Fact]
    public void NormalizationRemovesCasePunctuationAccentsAndExtraSpace()
    {
        Assert.Equal("a chef s cafe disaster 42", ScenarioUtilities.Normalize("  A CHEF'S café—disaster #42!  "));
    }

    [Fact]
    public void NearDuplicateDetectionUsesNormalizedTokenSimilarity()
    {
        Assert.True(ScenarioUtilities.IsNearDuplicate(
            "A terrified chef watches a giant bean rise from a saucepan",
            "A terrified chef watches the giant bean rise from the saucepan"));
        Assert.False(ScenarioUtilities.IsNearDuplicate("A submarine in a bathtub", "A wedding cake chases the photographer"));
    }

    [Fact]
    public void FilenamesAreDeterministicAndPathSafe()
    {
        Assert.Equal("cb-000123.webp", ScenarioUtilities.Filename("cb-000123"));
        Assert.Throws<ArgumentException>(() => ScenarioUtilities.Filename("../secret"));
    }
}

public sealed class ImageProcessingTests
{
    [Fact]
    public void CenterCropIsExactlySixteenByNine()
    {
        var crop = ImageProcessor.CalculateCenteredCrop(1536, 1024, 512, 288);
        Assert.Equal(new Rectangle(0, 80, 1536, 864), crop);
    }

    [Fact]
    public async Task ProcessorWritesExact512By288Webp()
    {
        using var temporary = new TemporaryDirectory();
        using var source = new Image<Rgba32>(1536, 1024, Color.CornflowerBlue);
        await using var stream = new MemoryStream();
        await source.SaveAsync(stream, new PngEncoder());
        var storage = new StorageOptions
        {
            DatabasePath = Path.Combine(temporary.Path, "data.db"),
            GeneratedPath = Path.Combine(temporary.Path, "generated"),
            TemporaryPath = Path.Combine(temporary.Path, "tmp")
        };
        var processor = new ImageProcessor(new ProcessingOptions(), storage, new FixedTextChecker(TextDetectionResult.CheckUnavailable));

        var result = await processor.ProcessAsync("cb-000001", stream.ToArray(), [], CancellationToken.None);

        Assert.Equal("cb-000001.webp", result.FinalFilename);
        var output = Path.Combine(storage.GeneratedPath, result.FinalFilename);
        var info = await Image.IdentifyAsync(output);
        Assert.NotNull(info);
        Assert.Equal(512, info.Width);
        Assert.Equal(288, info.Height);
        Assert.Equal("Webp", info.Metadata.DecodedImageFormat?.Name);
        Assert.Equal(TextDetectionResult.CheckUnavailable, result.TextDetectionResult);
    }

    [Fact]
    public void PerceptualHashDistanceCountsChangedBits()
    {
        Assert.Equal(0, ImageProcessor.HammingDistance("0000000000000000", "0000000000000000"));
        Assert.Equal(64, ImageProcessor.HammingDistance("0000000000000000", "FFFFFFFFFFFFFFFF"));
    }

    private sealed class FixedTextChecker(TextDetectionResult result) : ITextChecker
    {
        public Task<TextDetectionResult> CheckAsync(Image image, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}

public sealed class RetryPolicyTests
{
    [Fact]
    public async Task RetriesTransientFailuresAndHonoursRetryAfter()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var policy = new RetryPolicy(3, (delay, _) => { delays.Add(delay); return Task.CompletedTask; }, () => 0);

        var result = await policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            if (attempts == 1) throw new OpenAiRequestException("limited", 429, TimeSpan.FromSeconds(7), transient: true);
            if (attempts == 2) throw new HttpRequestException("network");
            return Task.FromResult(42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(2)], delays);
    }
}

public sealed class PersistenceAndExportTests
{
    [Fact]
    public async Task InterruptedGeneratingJobIsRecoveredToPending()
    {
        using var temporary = new TemporaryDirectory();
        var store = await CreateStoreAsync(temporary);
        await InsertScenarioAsync(store, "cb-000001");
        await store.EnsurePendingJobsAsync(null);
        Assert.NotNull(await store.TryLeaseNextJobAsync());

        Assert.Equal(1, await store.RecoverInterruptedJobsAsync());
        var job = Assert.Single(await store.GetJobsAsync());
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Contains("Recovered", job.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportContainsOnlyApprovedImagesAndManifestOmitsPrompts()
    {
        using var temporary = new TemporaryDirectory();
        var storage = CreateStorage(temporary);
        Directory.CreateDirectory(storage.GeneratedPath);
        var store = new SqliteStore(storage.DatabasePath);
        await store.InitializeAsync();
        await InsertScenarioAsync(store, "cb-000001");
        await InsertScenarioAsync(store, "cb-000002");
        await store.EnsurePendingJobsAsync(null);
        var first = await store.TryLeaseNextJobAsync();
        Assert.NotNull(first);
        await File.WriteAllBytesAsync(Path.Combine(storage.GeneratedPath, "cb-000001.webp"), [1, 2, 3]);
        await store.CompleteJobAsync(first.ScenarioId, "image-model", "private prompt", new GeneratedImage([1], "req_1"),
            new ProcessedImage(1536, 1024, "cb-000001.webp", "AABB", "0000000000000000", TextDetectionResult.NoTextDetected, false), 0.01m);
        await store.SetReviewAsync(first.ScenarioId, ReviewStatus.Approved);
        await store.EnsurePendingTitleJobsAsync(null);
        var titleJob = await store.TryLeaseNextTitleJobAsync();
        Assert.NotNull(titleJob);
        await store.CompleteTitleJobAsync(first.ScenarioId, "vision-model", new GeneratedTitles([
            "I Invented Liquid Rainbows",
            "Never Put a Ladder in Paint"
        ], "req_title"));
        var second = await store.TryLeaseNextJobAsync();
        Assert.NotNull(second);
        await store.FailJobAsync(second.ScenarioId, "failed");
        var exporter = new ExportService(store, new ProcessingOptions(), storage);
        var output = Path.Combine(temporary.Path, "export");

        Assert.Equal(1, await exporter.ExportAsync(output, includeProvenance: false, CancellationToken.None));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "thumbnails.json")));
        Assert.Single(manifest.RootElement.EnumerateArray());
        var json = manifest.RootElement.GetRawText();
        Assert.Contains("cb-000001", json, StringComparison.Ordinal);
        Assert.Contains("\"aiTitles\"", json, StringComparison.Ordinal);
        Assert.Contains("I Invented Liquid Rainbows", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private prompt", json, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output, "provenance.json")));
    }

    [Fact]
    public async Task InterruptedTitleJobIsRecoveredAndCompletedTitlesJoinImage()
    {
        using var temporary = new TemporaryDirectory();
        var store = await CreateStoreAsync(temporary);
        await InsertScenarioAsync(store, "cb-000001");
        await store.EnsurePendingJobsAsync(null);
        var imageJob = await store.TryLeaseNextJobAsync();
        Assert.NotNull(imageJob);
        await store.CompleteJobAsync(imageJob.ScenarioId, "image-model", "prompt", new GeneratedImage([1], "request"),
            new ProcessedImage(1536, 1024, "cb-000001.webp", "AABB", "0000000000000000", TextDetectionResult.NoTextDetected, false), 0.01m);
        Assert.Equal(1, await store.EnsurePendingTitleJobsAsync(null));
        Assert.NotNull(await store.TryLeaseNextTitleJobAsync());

        Assert.Equal(1, await store.RecoverInterruptedTitleJobsAsync());
        Assert.Equal(TitleJobStatus.Pending, Assert.Single(await store.GetTitleJobsAsync()).Status);
        var recovered = await store.TryLeaseNextTitleJobAsync();
        Assert.NotNull(recovered);
        await store.CompleteTitleJobAsync(recovered.ScenarioId, "vision-model", new GeneratedTitles(["The Floor Became Lava", "I Broke Gravity Again"], "req_titles"));

        var joined = Assert.Single(await store.GetJobsAsync());
        Assert.Equal(["The Floor Became Lava", "I Broke Gravity Again"], joined.AiTitles);
    }

    private static async Task<SqliteStore> CreateStoreAsync(TemporaryDirectory temporary)
    {
        var store = new SqliteStore(Path.Combine(temporary.Path, "state.db"));
        await store.InitializeAsync();
        return store;
    }

    private static StorageOptions CreateStorage(TemporaryDirectory temporary) => new()
    {
        DatabasePath = Path.Combine(temporary.Path, "state.db"),
        GeneratedPath = Path.Combine(temporary.Path, "generated"),
        TemporaryPath = Path.Combine(temporary.Path, "tmp")
    };

    private static Task<int> InsertScenarioAsync(SqliteStore store, string id) => store.InsertScenariosAsync([
        new Scenario(id, $"A peculiar visual scenario for {id}", $"a peculiar visual scenario for {id}", "test", "test", "photographic", DateTimeOffset.UtcNow)
    ]);
}

[Collection("Environment")]
public sealed class OpenAiClientTests
{
    [Fact]
    public async Task ParsesStructuredScenarioResponseAndSendsJsonSchema()
    {
        var responseJson = """
            {"output":[{"type":"message","content":[{"type":"output_text","text":"{\"scenarios\":[{\"scene\":\"A giant bean surprises a chef\",\"category\":\"cooking\",\"composition\":\"reaction\",\"visualStyle\":\"photographic\"}]}"}]}]}
            """;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseJson, Encoding.UTF8, "application/json") });
        var old = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key-not-real");
        try
        {
            var client = new OpenAiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test/v1/") }, new OpenAiOptions());
            var scenarios = await client.GenerateScenariosAsync(1, CancellationToken.None);
            var scenario = Assert.Single(scenarios);
            Assert.Equal("A giant bean surprises a chef", scenario.Scene);
            Assert.Contains("\"json_schema\"", handler.LastBody, StringComparison.Ordinal);
            Assert.DoesNotContain("test-key-not-real", handler.LastBody, StringComparison.Ordinal);
        }
        finally { Environment.SetEnvironmentVariable("OPENAI_API_KEY", old); }
    }

    [Fact]
    public async Task ParsesBase64ImageResponseAndRequestId()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"b64_json\":\"AQID\"}]}", Encoding.UTF8, "application/json") };
            response.Headers.Add("x-request-id", "req_test");
            return response;
        });
        var old = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key-not-real");
        try
        {
            var client = new OpenAiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test/v1/") }, new OpenAiOptions());
            var image = await client.GenerateImageAsync("safe prompt", CancellationToken.None);
            Assert.Equal([1, 2, 3], image.Bytes);
            Assert.Equal("req_test", image.RequestId);
        }
        finally { Environment.SetEnvironmentVariable("OPENAI_API_KEY", old); }
    }

    [Fact]
    public async Task ParsesVisionDistractorTitlesAndSendsWebpImageInput()
    {
        var responseJson = """
            {"output":[{"type":"message","content":[{"type":"output_text","text":"{\"titles\":[\"I Invented Liquid Rainbows\",\"Never Put a Ladder in Paint\"]}"}]}]}
            """;
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseJson, Encoding.UTF8, "application/json") };
            response.Headers.Add("x-request-id", "req_titles");
            return response;
        });
        var old = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key-not-real");
        try
        {
            var client = new OpenAiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test/v1/") }, new OpenAiOptions());
            var titles = await client.GenerateDistractorTitlesAsync([1, 2, 3], CancellationToken.None);
            Assert.Equal(["I Invented Liquid Rainbows", "Never Put a Ladder in Paint"], titles.Titles);
            Assert.Equal("req_titles", titles.RequestId);
            Assert.Contains("data:image/webp;base64,AQID", handler.LastBody, StringComparison.Ordinal);
            Assert.Contains("\"detail\":\"low\"", handler.LastBody, StringComparison.Ordinal);
            Assert.Contains("\"max_output_tokens\":1000", handler.LastBody, StringComparison.Ordinal);
            Assert.Contains("\"reasoning\":{\"effort\":\"low\"}", handler.LastBody, StringComparison.Ordinal);
            Assert.Contains("thumbnail_distractor_titles", handler.LastBody, StringComparison.Ordinal);
            Assert.DoesNotContain("test-key-not-real", handler.LastBody, StringComparison.Ordinal);
        }
        finally { Environment.SetEnvironmentVariable("OPENAI_API_KEY", old); }
    }

    [Fact]
    public async Task ReportsIncompleteVisionResponseReason()
    {
        var responseJson = """
            {"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[{"type":"reasoning"}]}
            """;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseJson, Encoding.UTF8, "application/json") });
        var old = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key-not-real");
        try
        {
            var client = new OpenAiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test/v1/") }, new OpenAiOptions());
            var exception = await Assert.ThrowsAsync<OpenAiRequestException>(() => client.GenerateDistractorTitlesAsync([1, 2, 3], CancellationToken.None));
            Assert.Contains("max_output_tokens", exception.Message, StringComparison.Ordinal);
            Assert.True(exception.IsTransient);
        }
        finally { Environment.SetEnvironmentVariable("OPENAI_API_KEY", old); }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string LastBody { get; private set; } = string.Empty;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}

[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentTestGroup;

public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clickbait-tests", Guid.NewGuid().ToString("N"));
    public string Path { get; }
    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
