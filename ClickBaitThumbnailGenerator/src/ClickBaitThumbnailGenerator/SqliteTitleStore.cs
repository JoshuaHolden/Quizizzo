using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ClickBaitThumbnailGenerator;

public sealed partial class SqliteStore
{
    public async Task<int> EnsurePendingTitleJobsAsync(int? count, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO ImageTitleJobs(ScenarioId, Status, UpdatedAtUtc)
            SELECT ScenarioId, 'Pending', $now FROM ImageJobs
            WHERE FinalFilename IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM ImageTitleJobs t WHERE t.ScenarioId=ImageJobs.ScenarioId)
            ORDER BY ScenarioId
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$limit", count ?? int.MaxValue);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RecoverInterruptedTitleJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ImageTitleJobs SET Status='Pending', LeaseId=NULL,
                FailureReason='Recovered after interrupted title generation.', UpdatedAtUtc=$now
            WHERE Status='Generating';
            """;
        command.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TitleJob?> TryLeaseNextTitleJobAsync(CancellationToken cancellationToken = default)
    {
        var lease = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE ImageTitleJobs SET Status='Generating', LeaseId=$lease,
                    AttemptCount=AttemptCount+1, FailureReason=NULL, UpdatedAtUtc=$now
                WHERE ScenarioId=(SELECT ScenarioId FROM ImageTitleJobs WHERE Status='Pending' ORDER BY UpdatedAtUtc, ScenarioId LIMIT 1)
                  AND Status='Pending';
                """;
            command.Parameters.AddWithValue("$lease", lease);
            command.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0) return null;
        }

        await using var select = connection.CreateCommand();
        select.CommandText = TitleSelect + " WHERE t.LeaseId=$lease LIMIT 1;";
        select.Parameters.AddWithValue("$lease", lease);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadTitleJob(reader) : null;
    }

    public async Task CompleteTitleJobAsync(
        string scenarioId,
        string model,
        GeneratedTitles generated,
        CancellationToken cancellationToken = default)
    {
        if (generated.Titles.Count != 2) throw new ArgumentException("Exactly two AI titles are required.", nameof(generated));
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ImageTitleJobs SET Status='Generated', Model=$model, TitlesJson=$titles,
                ApiRequestId=$requestId, FailureReason=NULL, GeneratedAtUtc=$generated,
                LeaseId=NULL, UpdatedAtUtc=$updated
            WHERE ScenarioId=$id AND Status='Generating';
            """;
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$titles", JsonSerializer.Serialize(generated.Titles));
        command.Parameters.AddWithValue("$requestId", (object?)generated.RequestId ?? DBNull.Value);
        command.Parameters.AddWithValue("$generated", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$updated", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", scenarioId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException($"Title job {scenarioId} no longer has an active lease.");
    }

    public async Task FailTitleJobAsync(string scenarioId, string reason, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ImageTitleJobs SET Status='Failed', FailureReason=$reason, LeaseId=NULL, UpdatedAtUtc=$updated
            WHERE ScenarioId=$id;
            """;
        command.Parameters.AddWithValue("$reason", Truncate(reason, 1000));
        command.Parameters.AddWithValue("$updated", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", scenarioId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ResetFailedTitleJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ImageTitleJobs SET Status='Pending', Model=NULL, TitlesJson=NULL, ApiRequestId=NULL,
                FailureReason=NULL, GeneratedAtUtc=NULL, LeaseId=NULL, UpdatedAtUtc=$now
            WHERE Status='Failed';
            """;
        command.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TitleJob>> GetTitleJobsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<TitleJob>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = TitleSelect + " ORDER BY t.ScenarioId;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadTitleJob(reader));
        return result;
    }

    public async Task<TitleStatistics> GetTitleStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await GetTitleJobsAsync(cancellationToken).ConfigureAwait(false);
        return new TitleStatistics(
            jobs.Count,
            jobs.Count(job => job.Status == TitleJobStatus.Pending),
            jobs.Count(job => job.Status == TitleJobStatus.Generating),
            jobs.Count(job => job.Status == TitleJobStatus.Generated),
            jobs.Count(job => job.Status == TitleJobStatus.Failed));
    }

    private async Task DeleteTitleJobAsync(string scenarioId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ImageTitleJobs WHERE ScenarioId=$id;";
        command.Parameters.AddWithValue("$id", scenarioId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TitleJob ReadTitleJob(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        Enum.Parse<TitleJobStatus>(reader.GetString(2)),
        reader.GetInt32(3),
        NullableString(reader, 4),
        ParseTitles(NullableString(reader, 5)),
        NullableString(reader, 6),
        NullableString(reader, 7),
        NullableDate(reader, 8),
        DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private const string TitleSelect = """
        SELECT t.ScenarioId, j.FinalFilename, t.Status, t.AttemptCount, t.Model, t.TitlesJson,
               t.ApiRequestId, t.FailureReason, t.GeneratedAtUtc, t.UpdatedAtUtc
        FROM ImageTitleJobs t JOIN ImageJobs j ON j.ScenarioId=t.ScenarioId
        """;
}
