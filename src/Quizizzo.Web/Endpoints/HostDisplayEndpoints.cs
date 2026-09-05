using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Quizizzo.Application.Displays;
using Quizizzo.Application.Parties;
using Quizizzo.Infrastructure.Identity;
using Quizizzo.Web.Realtime;

namespace Quizizzo.Web.Endpoints;

public static class HostDisplayEndpoints
{
    public const string DisplayCookieName = "quizizzo.display";

    public static IEndpointRouteBuilder MapHostDisplayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/host", LaunchAsync)
            .RequireRateLimiting("guest-host");
        return endpoints;
    }

    private static async Task<IResult> LaunchAsync(
        HttpContext context,
        PartyService parties,
        DisplaySessionService displays,
        IPartyRealtimeNotifier notifier,
        IWebHostEnvironment environment,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn)
    {
        var hostUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (hostUserId is null)
        {
            var guest = new ApplicationUser
            {
                UserName = $"guest-{Guid.NewGuid():N}",
                EmailConfirmed = true
            };
            var created = await users.CreateAsync(guest);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException("Quizizzo could not start a guest host session.");
            }

            await signIn.SignInAsync(guest, isPersistent: false);
            hostUserId = guest.Id;
        }
        var party = await parties.GetActiveAsync(hostUserId, context.RequestAborted);
        if (party is null)
        {
            try
            {
                party = await parties.CreateAsync(hostUserId, context.RequestAborted);
            }
            catch (InvalidOperationException)
            {
                party = await parties.GetActiveAsync(hostUserId, context.RequestAborted);
                if (party is null)
                {
                    throw;
                }
            }
        }

        return await PairCurrentBrowserAsync(
            party.PartyId,
            hostUserId,
            context,
            displays,
            notifier,
            environment);
    }

    private static async Task<IResult> PairCurrentBrowserAsync(
        Guid partyId,
        string hostUserId,
        HttpContext context,
        DisplaySessionService displays,
        IPartyRealtimeNotifier notifier,
        IWebHostEnvironment environment)
    {
        context.Request.Cookies.TryGetValue(DisplayCookieName, out var sessionToken);
        var restored = await displays.RestoreOrCreateAsync(sessionToken, context.RequestAborted);
        if (restored.View.PartyId == partyId)
        {
            return Results.Redirect("/display");
        }
        if (restored.View.PartyId is { } existingPartyId && existingPartyId != partyId)
        {
            restored = await displays.CreateAsync(context.RequestAborted);
        }

        var paired = await displays.PairAsync(
            restored.View.PairingCode,
            partyId,
            hostUserId,
            context.RequestAborted);

        if (restored.IsNew)
        {
            context.Response.Cookies.Append(DisplayCookieName, restored.SessionToken, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = TimeSpan.FromDays(30),
                SameSite = SameSiteMode.Lax,
                Secure = !environment.IsDevelopment() || context.Request.IsHttps
            });
        }

        await notifier.DisplaySessionChangedAsync(
            paired.DisplaySessionId,
            "DisplayPaired",
            context.RequestAborted);
        await notifier.PartyChangedAsync(partyId, "DisplayPaired", context.RequestAborted);
        return Results.Redirect("/display");
    }
}
