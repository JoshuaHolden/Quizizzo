using Quizizzo.Application.Abstractions;
using Quizizzo.Games.VoiceChoon;

namespace Quizizzo.Web.Games;

public sealed partial class VoiceChoonSongCatalogLoader(
    IServiceScopeFactory scopeFactory,
    ILogger<VoiceChoonSongCatalogLoader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var songs = await scope.ServiceProvider.GetRequiredService<IVoiceChoonSongRepository>()
                    .ListAsync(stoppingToken);
                foreach (var song in songs)
                {
                    VoiceChoonSongCatalog.RegisterUploaded(new VoiceChoonSongDefinition(
                        song.Key, song.DisplayName, song.MinimumPlayers, song.FileName,
                        $"Turn {song.DisplayName} into a ridiculous mouth-noise performance.",
                        "Record clear short hits and steady vowels suited to your assigned tracks.",
                        UploadedSongId: song.Id, MaximumPlayers: song.MaximumPlayers), song.MidiData);
                }
                Loaded(logger, songs.Count);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LoadFailed(logger, exception);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    [LoggerMessage(EventId = 1801, Level = LogLevel.Information,
        Message = "Loaded {SongCount} uploaded VoiceChoon songs.")]
    private static partial void Loaded(ILogger logger, int songCount);

    [LoggerMessage(EventId = 1802, Level = LogLevel.Error,
        Message = "Uploaded VoiceChoon songs could not be loaded; retrying.")]
    private static partial void LoadFailed(ILogger logger, Exception exception);
}
