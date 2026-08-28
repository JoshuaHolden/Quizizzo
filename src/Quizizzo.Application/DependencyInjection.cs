using Microsoft.Extensions.DependencyInjection;
using Quizizzo.Application.Displays;
using Quizizzo.Application.Parties;
using Quizizzo.Application.Players;
using Quizizzo.Application.Games;

namespace Quizizzo.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddQuizizzoApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PartyMutationCoordinator>();
        services.AddScoped<PartyService>();
        services.AddScoped<DisplaySessionService>();
        services.AddScoped<PlayerService>();
        services.AddScoped<PartyGameService>();
        return services;
    }
}
