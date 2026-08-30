using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Quizizzo.Web.Endpoints;

namespace Quizizzo.IntegrationTests;

public sealed partial class HostDisplayFlowTests
{
    [Fact]
    public async Task Host_can_present_on_this_device_without_manual_pairing()
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
        var partyPath = $"/host/party/{factory.State.Party.Id.Value}";
        var page = await client.GetStringAsync(partyPath);
        var tokenMatch = AntiforgeryTokenRegex().Match(page);

        Assert.True(tokenMatch.Success);
        Assert.Contains("Present on this device", page, StringComparison.Ordinal);
        Assert.Contains("Connect another screen", page, StringComparison.Ordinal);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value)
        });
        using var response = await client.PostAsync($"{partyPath}/present", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/display", response.Headers.Location?.OriginalString);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith($"{HostDisplayEndpoints.DisplayCookieName}=", StringComparison.Ordinal));
        var newDisplay = Assert.Single(
            factory.State.Displays,
            display => display.Id != factory.State.Display.Id);
        Assert.Equal(factory.State.Party.Id, newDisplay.PartyId);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenRegex();
}
