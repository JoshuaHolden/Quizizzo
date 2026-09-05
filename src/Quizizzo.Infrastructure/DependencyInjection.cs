using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Quizizzo.Application.Abstractions;
using Quizizzo.Infrastructure.Displays;
using Quizizzo.Infrastructure.Drawings;
using Quizizzo.Infrastructure.Identity;
using Quizizzo.Infrastructure.Games;
using Quizizzo.GameEngine;
using Quizizzo.Infrastructure.Parties;
using Quizizzo.Infrastructure.Players;
using Quizizzo.Infrastructure.Voice;

namespace Quizizzo.Infrastructure;

public static class DependencyInjection
{
    internal const int MaximumDatabasePoolSize = 32;
    internal const int MaximumIdleConnectionLifetimeSeconds = 60;

    public static IServiceCollection AddQuizizzoInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        var connectionSettings = new NpgsqlConnectionStringBuilder(connectionString);
        connectionSettings.MaxPoolSize = Math.Min(
            connectionSettings.MaxPoolSize, MaximumDatabasePoolSize);
        connectionSettings.ConnectionIdleLifetime = Math.Min(
            connectionSettings.ConnectionIdleLifetime, MaximumIdleConnectionLifetimeSeconds);
        var boundedConnectionString = connectionSettings.ConnectionString;
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(boundedConnectionString, postgres =>
                postgres.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));
        services.AddScoped<IPartyRepository, PartyRepository>();
        services.AddScoped<IDisplaySessionRepository, DisplaySessionRepository>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<IDrawingAssetMetadataRepository, DrawingAssetMetadataRepository>();
        services.AddScoped<IVoiceSampleMetadataRepository, VoiceSampleMetadataRepository>();
        services.AddScoped<IVoiceChoonSongRepository, VoiceChoonSongRepository>();
        services.AddScoped<IVoiceChoonReplayRepository, VoiceChoonReplayRepository>();
        services.AddSingleton<IGameStateStore, PostgreSqlGameStateStore>();
        services.AddSingleton<IRoomCodeGenerator, CryptographicRoomCodeGenerator>();
        services.AddSingleton<IDisplayCredentialService, DisplayCredentialService>();
        services.AddSingleton<IPlayerCredentialService, PlayerCredentialService>();
        services.AddSingleton<ICharacterGenerator, RandomCharacterGenerator>();
        services.AddSingleton<IDrawingAssetStore, FileSystemDrawingAssetStore>();
        services.AddSingleton<IVoiceSampleStore, FileSystemVoiceSampleStore>();
        services.AddHostedService<DrawingAssetCleanupService>();
        services.AddHostedService<VoiceSampleCleanupService>();
        services.AddHostedService<GameSnapshotCleanupService>();
        return services;
    }
}
