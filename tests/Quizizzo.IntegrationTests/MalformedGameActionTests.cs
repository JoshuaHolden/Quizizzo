using Microsoft.Extensions.DependencyInjection;
using Quizizzo.Application.Games;
using Quizizzo.GameContracts;
using Quizizzo.Games.Estimate;

namespace Quizizzo.IntegrationTests;

public sealed class MalformedGameActionTests
{
    [Fact]
    public async Task Malformed_transport_payload_is_rejected_and_idempotent_in_the_engine()
    {
        using var factory = new RecoveryWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var games = scope.ServiceProvider.GetRequiredService<PartyGameService>();
        await games.StartAsync(
            factory.State.Party.Id.Value,
            RecoveryWebApplicationFactory.HostUserId,
            EstimateGameModule.GameKey);
        var commandId = Guid.NewGuid();

        var first = await games.ExecutePlayerActionAsync(
            factory.State.Player.Id.Value,
            commandId,
            SubmitEstimateAction.ActionKind,
            GameJson.From(new { value = "not-a-number" }));
        var retry = await games.ExecutePlayerActionAsync(
            factory.State.Player.Id.Value,
            commandId,
            SubmitEstimateAction.ActionKind,
            GameJson.From(new { value = "not-a-number" }));

        Assert.False(first.Applied);
        Assert.Equal("invalid-estimate", first.ErrorCode);
        Assert.True(retry.IsDuplicate);
        Assert.Equal(first.Phase, retry.Phase);
    }
}
