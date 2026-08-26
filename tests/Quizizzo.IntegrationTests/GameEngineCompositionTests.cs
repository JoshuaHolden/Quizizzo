using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quizizzo.GameEngine;
using Quizizzo.Games.Estimate;

namespace Quizizzo.IntegrationTests;

public sealed class GameEngineCompositionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public GameEngineCompositionTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Web_composition_registers_one_runtime_catalog_and_state_store()
    {
        var firstRuntime = factory.Services.GetRequiredService<GameRuntimeManager>();
        var secondRuntime = factory.Services.GetRequiredService<GameRuntimeManager>();
        var stateStore = factory.Services.GetRequiredService<IGameStateStore>();

        Assert.Same(firstRuntime, secondRuntime);
        Assert.IsType<InMemoryGameStateStore>(stateStore);
        var game = Assert.Single(firstRuntime.ListGames());
        Assert.Equal(EstimateGameModule.GameKey, game.Key);
        Assert.Equal("Estimate", game.DisplayName);
    }
}
