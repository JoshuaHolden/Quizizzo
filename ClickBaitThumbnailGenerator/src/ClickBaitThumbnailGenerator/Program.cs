using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClickBaitThumbnailGenerator;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.local.json", optional: true)
                .AddEnvironmentVariables("CLICKBAIT_")
                .Build();
            var options = configuration.Get<AppOptions>() ?? throw new ConfigurationException(["appsettings.json could not be loaded."]);
            options.Validate();
            var services = await AppServices.CreateAsync(options).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            var root = BuildCommandLine(services, options, cancellation.Token);
            return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled. Completed jobs remain safe; use the matching images or titles resume command to continue.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static RootCommand BuildCommandLine(AppServices services, AppOptions options, CancellationToken cancellationToken)
    {
        var root = new RootCommand("Generate, process, review, and export original clickbait thumbnail assets.");

        var scenarios = new Command("scenarios", "Manage image scenario descriptions.");
        var scenarioGenerate = new Command("generate", "Generate varied scenarios using the configured text model.");
        var scenarioCount = new Option<int>("--count") { Description = "Number of accepted scenarios to add.", DefaultValueFactory = _ => options.Generation.DefaultScenarioCount };
        scenarioGenerate.Options.Add(scenarioCount);
        scenarioGenerate.SetAction(async parseResult =>
        {
            var added = await services.Scenarios.GenerateAsync(parseResult.GetValue(scenarioCount), cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Added {added} scenario(s).");
        });
        var scenarioImport = new Command("import", "Import scenario records from a JSON array.");
        var importFile = new Option<FileInfo>("--file") { Description = "JSON file to import.", Required = true };
        scenarioImport.Options.Add(importFile);
        scenarioImport.SetAction(async parseResult =>
        {
            var file = parseResult.GetValue(importFile) ?? throw new ArgumentException("--file is required.");
            var added = await services.Scenarios.ImportAsync(file.FullName, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Imported {added} new scenario(s); duplicates were skipped.");
        });
        var scenarioList = new Command("list", "Write all scenarios as JSON.");
        scenarioList.SetAction(async _ => await services.Scenarios.WriteListAsync(Console.Out, cancellationToken).ConfigureAwait(false));
        scenarios.Subcommands.Add(scenarioGenerate);
        scenarios.Subcommands.Add(scenarioImport);
        scenarios.Subcommands.Add(scenarioList);

        var images = new Command("images", "Manage image-generation jobs.");
        var imageGenerate = new Command("generate", "Generate images for scenarios that do not yet have jobs.");
        var imageCount = new Option<int?>("--count") { Description = "Maximum jobs to process." };
        var imageAll = new Option<bool>("--all") { Description = "Process every available scenario." };
        var concurrency = new Option<int>("--concurrency") { Description = "Maximum simultaneous API calls.", DefaultValueFactory = _ => options.OpenAI.Concurrency };
        imageGenerate.Options.Add(imageCount);
        imageGenerate.Options.Add(imageAll);
        imageGenerate.Options.Add(concurrency);
        imageGenerate.SetAction(async parseResult =>
        {
            var all = parseResult.GetValue(imageAll);
            var count = parseResult.GetValue(imageCount);
            if (all && count is not null) throw new ArgumentException("Use either --all or --count, not both.");
            if (!all && count is null) throw new ArgumentException("Specify --count or --all.");
            await services.Images.GenerateAsync(all ? null : count, parseResult.GetValue(concurrency), cancellationToken).ConfigureAwait(false);
        });
        var resume = new Command("resume", "Recover interrupted jobs and process all pending work.");
        resume.SetAction(async _ => await services.Images.GenerateAsync(null, options.OpenAI.Concurrency, cancellationToken).ConfigureAwait(false));
        var retry = new Command("retry-failed", "Reset failed jobs and retry them.");
        retry.SetAction(async _ =>
        {
            var count = await services.Store.ResetFailedAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Queued {count} failed job(s).");
            if (count > 0) await services.Images.GenerateAsync(count, options.OpenAI.Concurrency, cancellationToken).ConfigureAwait(false);
        });
        var stats = new Command("stats", "Show persistent job counts and estimated spend.");
        stats.SetAction(async _ => PrintStatistics(await services.Store.GetStatisticsAsync(cancellationToken).ConfigureAwait(false)));
        images.Subcommands.Add(imageGenerate);
        images.Subcommands.Add(resume);
        images.Subcommands.Add(retry);
        images.Subcommands.Add(stats);

        var titles = new Command("titles", "Generate AI vision-based distractor titles for processed images.");
        var titleGenerate = new Command("generate", "Generate exactly two distractor titles per image.");
        var titleCount = new Option<int?>("--count") { Description = "Maximum images to title." };
        var titleAll = new Option<bool>("--all") { Description = "Process every generated image without titles." };
        var titleConcurrency = new Option<int>("--concurrency") { Description = "Maximum simultaneous vision requests.", DefaultValueFactory = _ => options.OpenAI.Concurrency };
        titleGenerate.Options.Add(titleCount);
        titleGenerate.Options.Add(titleAll);
        titleGenerate.Options.Add(titleConcurrency);
        titleGenerate.SetAction(async parseResult =>
        {
            var all = parseResult.GetValue(titleAll);
            var count = parseResult.GetValue(titleCount);
            if (all && count is not null) throw new ArgumentException("Use either --all or --count, not both.");
            if (!all && count is null) throw new ArgumentException("Specify --count or --all.");
            await services.Titles.GenerateAsync(all ? null : count, parseResult.GetValue(titleConcurrency), cancellationToken).ConfigureAwait(false);
        });
        var titleResume = new Command("resume", "Recover interrupted title jobs and process all pending work.");
        titleResume.SetAction(async _ => await services.Titles.GenerateAsync(null, options.OpenAI.Concurrency, cancellationToken).ConfigureAwait(false));
        var titleRetry = new Command("retry-failed", "Reset and retry failed title jobs.");
        titleRetry.SetAction(async _ =>
        {
            var count = await services.Store.ResetFailedTitleJobsAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Queued {count} failed title job(s).");
            if (count > 0) await services.Titles.GenerateAsync(count, options.OpenAI.Concurrency, cancellationToken).ConfigureAwait(false);
        });
        var titleStats = new Command("stats", "Show distractor-title job counts.");
        titleStats.SetAction(async _ => PrintTitleStatistics(await services.Store.GetTitleStatisticsAsync(cancellationToken).ConfigureAwait(false)));
        titles.Subcommands.Add(titleGenerate);
        titles.Subcommands.Add(titleResume);
        titles.Subcommands.Add(titleRetry);
        titles.Subcommands.Add(titleStats);

        var review = new Command("review", "Start the localhost-only review gallery.");
        var port = new Option<int>("--port") { Description = "Local HTTP port.", DefaultValueFactory = _ => 5099 };
        var noOpen = new Option<bool>("--no-open") { Description = "Do not open the browser automatically." };
        review.Options.Add(port);
        review.Options.Add(noOpen);
        review.SetAction(async parseResult => await services.Review.RunAsync(parseResult.GetValue(port), !parseResult.GetValue(noOpen), cancellationToken).ConfigureAwait(false));

        var export = new Command("export", "Export approved WebP assets and a game-facing manifest.");
        var output = new Option<DirectoryInfo>("--output") { Description = "Export directory.", DefaultValueFactory = _ => new DirectoryInfo("export") };
        var provenance = new Option<bool>("--provenance") { Description = "Also export a private provenance report containing prompts." };
        export.Options.Add(output);
        export.Options.Add(provenance);
        export.SetAction(async parseResult =>
        {
            var directory = parseResult.GetValue(output) ?? new DirectoryInfo("export");
            var count = await services.Exporter.ExportAsync(directory.FullName, parseResult.GetValue(provenance), cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Exported {count} approved thumbnail(s) to {directory.FullName}.");
        });

        root.Subcommands.Add(scenarios);
        root.Subcommands.Add(images);
        root.Subcommands.Add(titles);
        root.Subcommands.Add(review);
        root.Subcommands.Add(export);
        return root;
    }

    private static void PrintStatistics(JobStatistics stats)
    {
        Console.WriteLine($"Total: {stats.Total}");
        Console.WriteLine($"Pending: {stats.Pending} | Generating: {stats.Generating} | Needs review: {stats.NeedsReview}");
        Console.WriteLine($"Approved: {stats.Approved} | Rejected: {stats.Rejected} | Failed: {stats.Failed} | Duplicate suspected: {stats.DuplicateSuspected}");
        Console.WriteLine($"Estimated generation spend: ${stats.EstimatedSpend:0.00} USD (configured estimate; verify current API pricing separately)");
    }

    private static void PrintTitleStatistics(TitleStatistics stats)
    {
        Console.WriteLine($"Total: {stats.Total}");
        Console.WriteLine($"Pending: {stats.Pending} | Generating: {stats.Generating} | Generated: {stats.Generated} | Failed: {stats.Failed}");
    }

    private sealed class AppServices(ServiceProvider provider) : IDisposable
    {
        public required SqliteStore Store { get; init; }
        public required ScenarioService Scenarios { get; init; }
        public required ImageBatchService Images { get; init; }
        public required TitleBatchService Titles { get; init; }
        public required ReviewServer Review { get; init; }
        public required ExportService Exporter { get; init; }

        public static async Task<AppServices> CreateAsync(AppOptions options)
        {
            var registrations = new ServiceCollection();
            registrations.AddLogging(builder => builder.AddSimpleConsole(console =>
            {
                console.SingleLine = true;
                console.TimestampFormat = "HH:mm:ss ";
            }));
            registrations.AddSingleton(options.OpenAI);
            registrations.AddSingleton(options.Processing);
            registrations.AddSingleton(options.Generation);
            registrations.AddSingleton(options.Storage);
            registrations.AddSingleton(_ => new SqliteStore(options.Storage.DatabasePath));
            registrations.AddSingleton<IRetryPolicy>(_ => new RetryPolicy(options.OpenAI.MaximumRetries));
            registrations.AddSingleton<IPromptBuilder, PromptBuilder>();
            registrations.AddSingleton<ITextChecker>(_ => new TesseractTextChecker(options.Storage.TemporaryPath));
            registrations.AddSingleton<ImageProcessor>();
            registrations.AddSingleton<ScenarioService>();
            registrations.AddSingleton<ImageBatchService>();
            registrations.AddSingleton<TitleBatchService>();
            registrations.AddSingleton<ReviewServer>();
            registrations.AddSingleton<ExportService>();
            registrations.AddHttpClient<IOpenAiClient, OpenAiClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.openai.com/v1/");
                client.Timeout = Timeout.InfiniteTimeSpan;
            });
            var provider = registrations.BuildServiceProvider(validateScopes: true);
            var store = provider.GetRequiredService<SqliteStore>();
            await store.InitializeAsync().ConfigureAwait(false);
            return new AppServices(provider)
            {
                Store = store,
                Scenarios = provider.GetRequiredService<ScenarioService>(),
                Images = provider.GetRequiredService<ImageBatchService>(),
                Titles = provider.GetRequiredService<TitleBatchService>(),
                Review = provider.GetRequiredService<ReviewServer>(),
                Exporter = provider.GetRequiredService<ExportService>()
            };
        }

        public void Dispose() => provider.Dispose();
    }
}
