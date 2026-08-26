using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Quizizzo.Application;
using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Displays;
using Quizizzo.Application.Players;
using Quizizzo.Infrastructure;
using Quizizzo.GameEngine;
using Quizizzo.GameContracts;
using Quizizzo.Games.Estimate;
using Quizizzo.Web.Components;
using Quizizzo.Web.Components.Account;
using Quizizzo.Infrastructure.Health;
using Quizizzo.Infrastructure.Identity;
using Quizizzo.Web.Endpoints;
using Quizizzo.Web.Presentation;
using Quizizzo.Web.Realtime;
using Quizizzo.Web.Games;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddQuizizzoApplication();
builder.Services.AddQuizizzoInfrastructure(connectionString);
builder.Services.AddQuizizzoGameEngine();
builder.Services.AddSingleton<IGameModule, EstimateGameModule>();
builder.Services.AddSingleton<IPartyGameRuntime, GameRuntimeGateway>();
builder.Services.AddSingleton<IGameRuntimeObserver, GameRuntimeRealtimeObserver>();
builder.Services.AddSingleton<QrCodeService>();
builder.Services.Configure<RealtimePresenceOptions>(
    builder.Configuration.GetSection(RealtimePresenceOptions.SectionName));
builder.Services.AddSingleton<PartyConnectionRegistry>();
builder.Services.AddSingleton<IPartyRealtimeNotifier, SignalRPartyRealtimeNotifier>();
builder.Services.AddRateLimiter(options =>
    options.AddPolicy("player-join", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            })));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddHealthChecks()
    .AddCheck("application", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"]);

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.Equals("/display"))
    {
        const string cookieName = "quizizzo.display";
        context.Request.Cookies.TryGetValue(cookieName, out var sessionToken);
        var displaySessions = context.RequestServices.GetRequiredService<DisplaySessionService>();
        var restored = await displaySessions.RestoreOrCreateAsync(sessionToken, context.RequestAborted);
        context.Items["Quizizzo.DisplaySession"] = restored.View;

        if (restored.IsNew)
        {
            context.Response.Cookies.Append(cookieName, restored.SessionToken, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = TimeSpan.FromDays(30),
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps
            });
        }
    }

    await next(context);
});

app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.Equals("/play"))
    {
        context.Request.Cookies.TryGetValue(PlayerSessionEndpoints.PlayerCookieName, out var sessionToken);
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            try
            {
                var players = context.RequestServices.GetRequiredService<PlayerService>();
                context.Items[PlayerSessionEndpoints.PlayerContextItem] =
                    await players.ReconnectAsync(sessionToken, context.RequestAborted);
            }
            catch (Exception exception) when (exception is PlayerSessionNotFoundException or InvalidOperationException)
            {
                context.Response.Cookies.Delete(PlayerSessionEndpoints.PlayerCookieName);
            }
        }
    }

    await next(context);
});

app.UseAntiforgery();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();
app.MapPlayerSessionEndpoints();
app.MapHub<PartyHub>("/hubs/party");
app.MapHealthChecks("/health/live", new() { Predicate = check => check.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program;
