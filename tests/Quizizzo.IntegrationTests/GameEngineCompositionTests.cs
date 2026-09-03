using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quizizzo.GameEngine;
using Quizizzo.Games.Estimate;
using Quizizzo.Games.AniMates;
using Quizizzo.Games.MajorityRules;
using Quizizzo.Games.Bullshit;
using Quizizzo.Games.PileUpPanic;
using Quizizzo.Infrastructure.Games;

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
        Assert.IsType<PostgreSqlGameStateStore>(stateStore);
        var games = firstRuntime.ListGames();
        Assert.Contains(games, game => game.Key == EstimateGameModule.GameKey && game.DisplayName == "Estimate");
        Assert.Contains(games, game => game.Key == AniMatesGameModule.GameKey && game.DisplayName == "AniMates");
        Assert.Contains(games, game => game.Key == MajorityRulesGameModule.GameKey && game.DisplayName == "Majority Rules");
        Assert.Contains(games, game => game.Key == BullshitGameModule.GameKey && game.DisplayName == "Bullshit");
        Assert.Contains(games, game => game.Key == PileUpPanicGameModule.GameKey && game.DisplayName == "Pile-Up Panic");
    }
}
