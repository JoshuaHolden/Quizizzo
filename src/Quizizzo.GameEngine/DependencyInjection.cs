using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Quizizzo.GameEngine;

public static class DependencyInjection
{
    public static IServiceCollection AddQuizizzoGameEngine(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IGameStateStore, InMemoryGameStateStore>();
        services.TryAddSingleton<GameModuleCatalog>();
        services.TryAddSingleton<GameRuntimeManager>();
        return services;
    }
}
