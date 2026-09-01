using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Quizizzo.Web.Endpoints;

namespace Quizizzo.IntegrationTests;

public sealed class HostDisplayFlowTests
{
    [Fact]
    public async Task Host_launcher_resumes_the_single_active_party_and_opens_its_display()
    {
        await using var factory = new RecoveryWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Add(
            RecoveryWebApplicationFactory.HostHeader,
            RecoveryWebApplicationFactory.HostUserId);
        using var response = await client.GetAsync("/host");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/display", response.Headers.Location?.OriginalString);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith($"{HostDisplayEndpoints.DisplayCookieName}=", StringComparison.Ordinal));
        var newDisplay = Assert.Single(
            factory.State.Displays,
            display => display.Id != factory.State.Display.Id);
        Assert.Equal(factory.State.Party.Id, newDisplay.PartyId);

        using var repeatedResponse = await client.GetAsync("/host");

        Assert.Equal(HttpStatusCode.Redirect, repeatedResponse.StatusCode);
        Assert.Equal("/display", repeatedResponse.Headers.Location?.OriginalString);
        Assert.Equal(2, factory.State.Displays.Count);
    }
}
