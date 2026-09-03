using System.Text.Json;

namespace ClickBaitThumbnailGenerator;

public sealed class ScenarioService(SqliteStore store, IOpenAiClient openAiClient, IRetryPolicy retryPolicy, GenerationOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<int> GenerateAsync(int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var existing = (await store.ListScenariosAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var next = await store.NextScenarioNumberAsync(cancellationToken).ConfigureAwait(false);
        var inserted = 0;
        var emptyBatches = 0;
        while (inserted < count && emptyBatches < 5)
        {
            var requested = Math.Min(options.ScenarioBatchSize, count - inserted + Math.Min(10, count - inserted));
            var candidates = await retryPolicy.ExecuteAsync(
                token => openAiClient.GenerateScenariosAsync(requested, token), cancellationToken).ConfigureAwait(false);
            var accepted = new List<Scenario>();
            foreach (var candidate in candidates)
            {
                if (inserted + accepted.Count >= count || !IsValid(candidate)) break;
                var normalized = ScenarioUtilities.Normalize(candidate.Scene);
                if (existing.Any(x => x.NormalizedScene == normalized || ScenarioUtilities.IsNearDuplicate(x.Scene, candidate.Scene)) ||
                    accepted.Any(x => x.NormalizedScene == normalized || ScenarioUtilities.IsNearDuplicate(x.Scene, candidate.Scene))) continue;
                accepted.Add(new Scenario(
                    $"cb-{next++:D6}", candidate.Scene.Trim(), normalized, Slug(candidate.Category),
                    Slug(candidate.Composition), Slug(candidate.VisualStyle), DateTimeOffset.UtcNow));
            }

            var added = await store.InsertScenariosAsync(accepted, cancellationToken).ConfigureAwait(false);
            existing.AddRange(accepted.Take(added));
            inserted += added;
            emptyBatches = added == 0 ? emptyBatches + 1 : 0;
            Console.WriteLine($"Scenarios {inserted}/{count} accepted ({existing.Count} total)");
        }
        if (inserted < count) Console.WriteLine($"Stopped after repeated duplicate-only batches; generated {inserted} of {count} requested scenarios.");
        return inserted;
    }

    public async Task<int> ImportAsync(string file, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        var imports = await JsonSerializer.DeserializeAsync<List<ImportScenario>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The scenario file did not contain a JSON array.");
        var existing = (await store.ListScenariosAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var next = await store.NextScenarioNumberAsync(cancellationToken).ConfigureAwait(false);
        var accepted = new List<Scenario>();
        foreach (var item in imports)
        {
            var candidate = new ScenarioCandidate(item.Scene ?? string.Empty, item.Category ?? string.Empty, item.Composition ?? string.Empty, item.VisualStyle ?? string.Empty);
            if (!IsValid(candidate)) continue;
            var normalized = ScenarioUtilities.Normalize(candidate.Scene);
            if (existing.Concat(accepted).Any(x => x.NormalizedScene == normalized || ScenarioUtilities.IsNearDuplicate(x.Scene, candidate.Scene))) continue;
            var id = string.IsNullOrWhiteSpace(item.Id) ? $"cb-{next++:D6}" : item.Id;
            _ = ScenarioUtilities.Filename(id);
            accepted.Add(new Scenario(id, candidate.Scene.Trim(), normalized, Slug(candidate.Category), Slug(candidate.Composition), Slug(candidate.VisualStyle), item.CreatedAt ?? DateTimeOffset.UtcNow));
        }
        return await store.InsertScenariosAsync(accepted, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteListAsync(TextWriter writer, CancellationToken cancellationToken)
    {
        var scenarios = await store.ListScenariosAsync(cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(JsonSerializer.Serialize(scenarios.Select(x => new
        {
            id = x.Id,
            scene = x.Scene,
            category = x.Category,
            composition = x.Composition,
            visualStyle = x.VisualStyle,
            createdAt = x.CreatedAtUtc
        }), JsonOptions)).ConfigureAwait(false);
    }

    private static bool IsValid(ScenarioCandidate candidate) =>
        !string.IsNullOrWhiteSpace(candidate.Scene) && candidate.Scene.Trim().Length is >= 12 and <= 300 &&
        !string.IsNullOrWhiteSpace(candidate.Category) && !string.IsNullOrWhiteSpace(candidate.Composition) && !string.IsNullOrWhiteSpace(candidate.VisualStyle);

    private static string Slug(string value) => string.Join('-', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private sealed record ImportScenario(string? Id, string? Scene, string? Category, string? Composition, string? VisualStyle, DateTimeOffset? CreatedAt);
}
