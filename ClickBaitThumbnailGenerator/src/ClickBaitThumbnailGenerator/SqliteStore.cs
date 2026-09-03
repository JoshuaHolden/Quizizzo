using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ClickBaitThumbnailGenerator;

public sealed partial class SqliteStore(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath),
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        ForeignKeys = true
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (parent is not null) Directory.CreateDirectory(parent);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS SchemaInfo (Version INTEGER NOT NULL);
            INSERT INTO SchemaInfo (Version) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM SchemaInfo);
            CREATE TABLE IF NOT EXISTS Scenarios (
                Id TEXT PRIMARY KEY,
                Scene TEXT NOT NULL,
                NormalizedScene TEXT NOT NULL UNIQUE,
                Category TEXT NOT NULL,
                Composition TEXT NOT NULL,
                VisualStyle TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ImageJobs (
                ScenarioId TEXT PRIMARY KEY REFERENCES Scenarios(Id) ON DELETE CASCADE,
                Model TEXT NULL,
                FullPrompt TEXT NULL,
                GeneratedAtUtc TEXT NULL,
                AttemptCount INTEGER NOT NULL DEFAULT 0,
                ApiRequestId TEXT NULL,
                Status TEXT NOT NULL,
                ReviewStatus TEXT NOT NULL,
                FailureReason TEXT NULL,
                SourceWidth INTEGER NULL,
                SourceHeight INTEGER NULL,
                FinalFilename TEXT NULL,
                Sha256 TEXT NULL,
                PerceptualHash TEXT NULL,
                TextDetectionResult TEXT NULL,
                EstimatedCost TEXT NOT NULL DEFAULT '0',
                LeaseId TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ImageJobs_Status ON ImageJobs(Status, UpdatedAtUtc);
            CREATE INDEX IF NOT EXISTS IX_ImageJobs_ReviewStatus ON ImageJobs(ReviewStatus);
            """, cancellationToken).ConfigureAwait(false);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT Version FROM SchemaInfo LIMIT 1;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (version == 1)
        {
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS ImageTitleJobs (
                    ScenarioId TEXT PRIMARY KEY REFERENCES ImageJobs(ScenarioId) ON DELETE CASCADE,
                    Status TEXT NOT NULL,
                    AttemptCount INTEGER NOT NULL DEFAULT 0,
                    Model TEXT NULL,
                    TitlesJson TEXT NULL,
                    ApiRequestId TEXT NULL,
                    FailureReason TEXT NULL,
                    GeneratedAtUtc TEXT NULL,
                    LeaseId TEXT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ImageTitleJobs_Status ON ImageTitleJobs(Status, UpdatedAtUtc);
                UPDATE SchemaInfo SET Version=2;
                """, cancellationToken).ConfigureAwait(false);
            version = 2;
        }
        if (version != 2) throw new InvalidOperationException($"Unsupported database schema version {version}.");
    }

    public async Task<int> RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ImageJobs SET Status = 'Pending', LeaseId = NULL,
                FailureReason = 'Recovered after interrupted generation.', UpdatedAtUtc = $now
            WHERE Status = 'Generating';
            """;
        command.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> NextScenarioNumberAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(CAST(SUBSTR(Id, 4) AS INTEGER)), 0) + 1 FROM Scenarios;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<Scenario>> ListScenariosAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Scenario>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Scene, NormalizedScene, Category, Composition, VisualStyle, CreatedAtUtc FROM Scenarios ORDER BY Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadScenario(reader));
        return result;
    }

    public async Task<int> InsertScenariosAsync(IEnumerable<Scenario> scenarios, CancellationToken cancellationToken = default)
    {
        var inserted = 0;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var scenario in scenarios)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO Scenarios(Id, Scene, NormalizedScene, Category, Composition, VisualStyle, CreatedAtUtc)
                VALUES($id, $scene, $normalized, $category, $composition, $style, $created);
                """;
            command.Parameters.AddWithValue("$id", scenario.Id);
            command.Parameters.AddWithValue("$scene", scenario.Scene);
            command.Parameters.AddWithValue("$normalized", scenario.NormalizedScene);
            command.Parameters.AddWithValue("$category", scenario.Category);
            command.Parameters.AddWithValue("$composition", scenario.Composition);
            command.Parameters.AddWithValue("$style", scenario.VisualStyle);
            command.Parameters.AddWithValue("$created", Format(scenario.CreatedAtUtc));
            inserted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    public async Task<int> EnsurePendingJobsAsync(int? count, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO ImageJobs(ScenarioId, Status, ReviewStatus, UpdatedAtUtc)
            SELECT Id, 'Pending', 'Pending', $now FROM Scenarios
            WHERE NOT EXISTS (SELECT 1 FROM ImageJobs j WHERE j.ScenarioId = Scenarios.Id)
            ORDER BY Id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$limit", count ?? int.MaxValue);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImageJob?> TryLeaseNextJobAsync(CancellationToken cancellationToken = default)
    {
        var lease = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE ImageJobs SET Status = 'Generating', LeaseId = $lease,
                    AttemptCount = AttemptCount + 1, FailureReason = NULL, UpdatedAtUtc = $now
                WHERE ScenarioId = (SELECT ScenarioId FROM ImageJobs WHERE Status = 'Pending' ORDER BY UpdatedAtUtc, ScenarioId LIMIT 1)
                  AND Status = 'Pending';
                """;
            command.Parameters.AddWithValue("$lease", lease);
            command.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0) return null;
        }

        await using var select = connection.CreateCommand();
        select.CommandText = JobSelect + " WHERE j.LeaseId = $lease LIMIT 1;";
        select.Parameters.AddWithValue("$lease", lease);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadJob(reader) : null;
    }

    public async Task CompleteJobAsync(
        string scenarioId, string model, string prompt, GeneratedImage generated, ProcessedImage processed,
        decimal estimatedCost, CancellationToken cancellationToken = default)
    {
        var status = processed.DuplicateSuspected ? JobStatus.DuplicateSuspected : JobStatus.NeedsReview;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ImageJobs SET Model=$model, FullPrompt=$prompt, GeneratedAtUtc=$generated,
                ApiRequestId=$requestId, Status=$status, ReviewStatus='Pending', FailureReason=NULL,
                SourceWidth=$sourceWidth, SourceHeight=$sourceHeight, FinalFilename=$filename,
                Sha256=$sha, PerceptualHash=$hash, TextDetectionResult=$text,
                EstimatedCost=$cost, LeaseId=NULL, UpdatedAtUtc=$updated
            WHERE ScenarioId=$id AND Status='Generating';
            """;
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$prompt", prompt);
        command.Parameters.AddWithValue("$generated", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$requestId", (object?)generated.RequestId ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$sourceWidth", processed.SourceWidth);
        command.Parameters.AddWithValue("$sourceHeight", processed.SourceHeight);
        command.Parameters.AddWithValue("$filename", processed.FinalFilename);
        command.Parameters.AddWithValue("$sha", processed.Sha256);
        command.Parameters.AddWithValue("$hash", processed.PerceptualHash);
        command.Parameters.AddWithValue("$text", processed.TextDetectionResult.ToString());
        command.Parameters.AddWithValue("$cost", estimatedCost.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updated", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", scenarioId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException($"Image job {scenarioId} no longer has an active lease.");
    }

    public async Task FailJobAsync(string scenarioId, string reason, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ImageJobs SET Status='Failed', FailureReason=$reason, LeaseId=NULL, UpdatedAtUtc=$updated
            WHERE ScenarioId=$id;
            """;
        command.Parameters.AddWithValue("$reason", Truncate(reason, 1000));
        command.Parameters.AddWithValue("$updated", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", scenarioId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ResetFailedAsync(CancellationToken cancellationToken = default) =>
        await ResetWhereAsync("Status='Failed'", cancellationToken).ConfigureAwait(false);

    public async Task<int> ResetJobAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        var reset = await ResetWhereAsync("ScenarioId=$id", cancellationToken, scenarioId).ConfigureAwait(false);
        await DeleteTitleJobAsync(scenarioId, cancellationToken).ConfigureAwait(false);
        return reset;
    }

    public async Task SetReviewAsync(string scenarioId, ReviewStatus reviewStatus, JobStatus? forcedStatus = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = forcedStatus is null
            ? "UPDATE ImageJobs SET ReviewStatus=$review, UpdatedAtUtc=$now WHERE ScenarioId=$id;"
            : "UPDATE ImageJobs SET ReviewStatus=$review, Status=$status, UpdatedAtUtc=$now WHERE ScenarioId=$id;";
        command.Parameters.AddWithValue("$review", reviewStatus.ToString());
        if (forcedStatus is not null) command.Parameters.AddWithValue("$status", forcedStatus.Value.ToString());
        command.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", scenarioId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateScenarioAndResetAsync(string scenarioId, string scene, CancellationToken cancellationToken = default)
    {
        var normalized = ScenarioUtilities.Normalize(scene);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = "UPDATE Scenarios SET Scene=$scene, NormalizedScene=$normalized WHERE Id=$id;";
            update.Parameters.AddWithValue("$scene", scene.Trim());
            update.Parameters.AddWithValue("$normalized", normalized);
            update.Parameters.AddWithValue("$id", scenarioId);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var reset = connection.CreateCommand())
        {
            reset.Transaction = (SqliteTransaction)transaction;
            ConfigureReset(reset, "ScenarioId=$id", scenarioId);
            await reset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var titles = connection.CreateCommand())
        {
            titles.Transaction = (SqliteTransaction)transaction;
            titles.CommandText = "DELETE FROM ImageTitleJobs WHERE ScenarioId=$id;";
            titles.Parameters.AddWithValue("$id", scenarioId);
            await titles.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ImageJob>> GetJobsAsync(string? status = null, string? category = null, string? failure = null, CancellationToken cancellationToken = default)
    {
        var jobs = new List<ImageJob>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            conditions.Add("(j.Status=$status OR j.ReviewStatus=$status)");
            command.Parameters.AddWithValue("$status", status);
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            conditions.Add("s.Category=$category");
            command.Parameters.AddWithValue("$category", category);
        }
        if (!string.IsNullOrWhiteSpace(failure))
        {
            conditions.Add("j.FailureReason LIKE $failure");
            command.Parameters.AddWithValue("$failure", $"%{failure}%");
        }
        command.CommandText = JobSelect + (conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions)) + " ORDER BY j.ScenarioId;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) jobs.Add(ReadJob(reader));
        return jobs;
    }

    public async Task<ImageJob?> GetJobAsync(string scenarioId, CancellationToken cancellationToken = default) =>
        (await GetJobsByIdAsync(scenarioId, cancellationToken).ConfigureAwait(false)).SingleOrDefault();

    public async Task<IReadOnlyList<ImageJob>> GetApprovedJobsAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await GetJobsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return jobs.Where(x => x.ReviewStatus == ReviewStatus.Approved && x.FinalFilename is not null).ToArray();
    }

    public async Task<IReadOnlyList<string>> GetExistingHashesAsync(CancellationToken cancellationToken = default)
    {
        var hashes = new List<string>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PerceptualHash FROM ImageJobs WHERE PerceptualHash IS NOT NULL;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) hashes.Add(reader.GetString(0));
        return hashes;
    }

    public async Task<JobStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await GetJobsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new JobStatistics(
            jobs.Count,
            jobs.Count(x => x.Status == JobStatus.Pending),
            jobs.Count(x => x.Status == JobStatus.Generating),
            jobs.Count(x => x.Status == JobStatus.Generated),
            jobs.Count(x => x.Status == JobStatus.NeedsReview),
            jobs.Count(x => x.Status == JobStatus.Failed),
            jobs.Count(x => x.Status == JobStatus.DuplicateSuspected),
            jobs.Count(x => x.ReviewStatus == ReviewStatus.Approved),
            jobs.Count(x => x.ReviewStatus == ReviewStatus.Rejected),
            jobs.Sum(x => x.EstimatedCost));
    }

    private async Task<IReadOnlyList<ImageJob>> GetJobsByIdAsync(string id, CancellationToken cancellationToken)
    {
        var jobs = new List<ImageJob>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = JobSelect + " WHERE j.ScenarioId=$id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) jobs.Add(ReadJob(reader));
        return jobs;
    }

    private async Task<int> ResetWhereAsync(string where, CancellationToken cancellationToken, string? id = null)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        ConfigureReset(command, where, id);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ConfigureReset(SqliteCommand command, string where, string? id)
    {
        command.CommandText = $"""
            UPDATE ImageJobs SET Model=NULL, FullPrompt=NULL, GeneratedAtUtc=NULL, ApiRequestId=NULL,
                Status='Pending', ReviewStatus='Pending', FailureReason=NULL, SourceWidth=NULL, SourceHeight=NULL,
                FinalFilename=NULL, Sha256=NULL, PerceptualHash=NULL, TextDetectionResult=NULL,
                EstimatedCost='0', LeaseId=NULL, UpdatedAtUtc=$now WHERE {where};
            """;
        command.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        if (id is not null) command.Parameters.AddWithValue("$id", id);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA busy_timeout=10000;", cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Scenario ReadScenario(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
        DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static ImageJob ReadJob(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        NullableString(reader, 4), NullableString(reader, 5), NullableDate(reader, 6), reader.GetInt32(7), NullableString(reader, 8),
        Enum.Parse<JobStatus>(reader.GetString(9)), Enum.Parse<ReviewStatus>(reader.GetString(10)), NullableString(reader, 11),
        NullableInt(reader, 12), NullableInt(reader, 13), NullableString(reader, 14), NullableString(reader, 15), NullableString(reader, 16),
        reader.IsDBNull(17) ? null : Enum.Parse<TextDetectionResult>(reader.GetString(17)),
        decimal.Parse(reader.GetString(18), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(19), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        ParseTitles(NullableString(reader, 20)));

    private static string? NullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static int? NullableInt(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static DateTimeOffset? NullableDate(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
    private static string[] ParseTitles(string? json) => json is null
        ? []
        : JsonSerializer.Deserialize<string[]>(json) ?? [];

    private const string JobSelect = """
        SELECT j.ScenarioId, s.Scene, s.Category, s.VisualStyle, j.Model, j.FullPrompt, j.GeneratedAtUtc,
               j.AttemptCount, j.ApiRequestId, j.Status, j.ReviewStatus, j.FailureReason, j.SourceWidth,
               j.SourceHeight, j.FinalFilename, j.Sha256, j.PerceptualHash, j.TextDetectionResult,
               j.EstimatedCost, j.UpdatedAtUtc, t.TitlesJson
        FROM ImageJobs j JOIN Scenarios s ON s.Id=j.ScenarioId
        LEFT JOIN ImageTitleJobs t ON t.ScenarioId=j.ScenarioId
        """;
}
