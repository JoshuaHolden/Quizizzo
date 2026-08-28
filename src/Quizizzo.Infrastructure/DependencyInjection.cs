using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quizizzo.Application.Abstractions;
using Quizizzo.Infrastructure.Displays;
using Quizizzo.Infrastructure.Drawings;
using Quizizzo.Infrastructure.Identity;
using Quizizzo.Infrastructure.Games;
using Quizizzo.GameEngine;
using Quizizzo.Infrastructure.Parties;
using Quizizzo.Infrastructure.Players;

namespace Quizizzo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddQuizizzoInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));
        services.AddScoped<IPartyRepository, PartyRepository>();
        services.AddScoped<IDisplaySessionRepository, DisplaySessionRepository>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<IDrawingAssetMetadataRepository, DrawingAssetMetadataRepository>();
        services.AddSingleton<IGameStateStore, PostgreSqlGameStateStore>();
        services.AddSingleton<IRoomCodeGenerator, CryptographicRoomCodeGenerator>();
        services.AddSingleton<IDisplayCredentialService, DisplayCredentialService>();
        services.AddSingleton<IPlayerCredentialService, PlayerCredentialService>();
        services.AddSingleton<ICharacterGenerator, RandomCharacterGenerator>();
        services.AddSingleton<IDrawingAssetStore, FileSystemDrawingAssetStore>();
        services.AddHostedService<DrawingAssetCleanupService>();
        services.AddHostedService<GameSnapshotCleanupService>();
        return services;
    }
}
