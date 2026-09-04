using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Quizizzo.Application;
using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Displays;
using Quizizzo.Application.Players;
using Quizizzo.Infrastructure;
using Quizizzo.GameEngine;
using Quizizzo.GameContracts;
using Quizizzo.Games.Estimate;
using Quizizzo.Games.AniMates;
using Quizizzo.Games.MajorityRules;
using Quizizzo.Games.PileUpPanic;
using Quizizzo.Games.VoiceChoon;
using Quizizzo.Games.SlopMachine;
using Quizizzo.Games.Bullshit;
using Quizizzo.Web.Components;
using Quizizzo.Web.Components.Account;
using Quizizzo.Infrastructure.Health;
using Quizizzo.Infrastructure.Identity;
using Quizizzo.Infrastructure.Drawings;
using Quizizzo.Infrastructure.Games;
using Quizizzo.Infrastructure.Voice;
using Quizizzo.Web.Endpoints;
using Quizizzo.Web.Presentation;
using Quizizzo.Web.Realtime;
using Quizizzo.Web.Games;
using Quizizzo.Web.Security;
using Quizizzo.Domain.Voice;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
if (builder.Environment.IsProduction())
{
    builder.Logging.AddFilter((_, level) => level >= LogLevel.Error);
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
var signalR = builder.Services.AddSignalR();
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    signalR.AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("Quizizzo.SignalR");
    });
}

builder.Services.AddCascadingAuthenticationState();
var adminEmails = (builder.Configuration["Admin:Emails"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
builder.Services.AddAuthorizationBuilder().AddPolicy("Admin", policy => policy
    .RequireAuthenticatedUser()
    .RequireAssertion(context => context.User.Identity?.Name is { } name && adminEmails.Contains(name)));
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
if (builder.Configuration["DataProtection:KeyPath"] is { Length: > 0 } keyPath)
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
        .SetApplicationName("Quizizzo");
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddQuizizzoApplication();
builder.Services.AddOptions<GameRuntimeOptions>()
    .Bind(builder.Configuration.GetSection(GameRuntimeOptions.SectionName))
    .Validate(
        options => options.CommandQueueCapacity is >= GameRuntimeOptions.MinimumQueueCapacity and
            <= GameRuntimeOptions.MaximumQueueCapacity,
        "Game command queue capacity is outside the supported range.")
    .Validate(
        options => options.MaximumProcessedCommands is >= GameRuntimeOptions.MinimumProcessedCommands and
            <= GameRuntimeOptions.MaximumProcessedCommandLimit,
        "Processed game command capacity is outside the supported range.")
    .ValidateOnStart();
builder.Services.AddOptions<DrawingAssetStoreOptions>()
    .Bind(builder.Configuration.GetSection(DrawingAssetStoreOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "Drawing asset root path is required.")
    .Validate(
        options => options.MaximumAssetBytes is >= DrawingAssetStoreOptions.MinimumAssetBytes and
            <= DrawingAssetStoreOptions.MaximumConfiguredAssetBytes,
        "Drawing asset size limit is outside the supported range.")
    .Validate(
        options => options.RetentionPeriod >= TimeSpan.FromMinutes(1) &&
            options.RetentionPeriod <= TimeSpan.FromDays(30),
        "Drawing asset retention must be between one minute and 30 days.")
    .Validate(
        options => options.CleanupInterval >= TimeSpan.FromMinutes(1) &&
            options.CleanupInterval <= TimeSpan.FromDays(1),
        "Drawing asset cleanup interval must be between one minute and one day.")
    .ValidateOnStart();
builder.Services.AddOptions<VoiceSampleStoreOptions>()
    .Bind(builder.Configuration.GetSection(VoiceSampleStoreOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "Voice sample root path is required.")
    .Validate(
        options => options.MaximumAssetBytes is >= VoiceSampleStoreOptions.MinimumAssetBytes and
            <= VoiceSampleStoreOptions.MaximumConfiguredAssetBytes,
        "Voice sample size limit is outside the supported range.")
    .Validate(
        options => options.RetentionPeriod >= TimeSpan.FromMinutes(1) &&
            options.RetentionPeriod <= TimeSpan.FromDays(30),
        "Voice sample retention must be between one minute and 30 days.")
    .Validate(
        options => options.CleanupInterval >= TimeSpan.FromMinutes(1) &&
            options.CleanupInterval <= TimeSpan.FromDays(1),
        "Voice sample cleanup interval must be between one minute and one day.")
    .ValidateOnStart();
builder.Services.AddOptions<GameStateStoreOptions>()
    .Bind(builder.Configuration.GetSection(GameStateStoreOptions.SectionName))
    .Validate(
        options => options.CompletedSnapshotRetention >= TimeSpan.FromDays(1) &&
            options.CompletedSnapshotRetention <= TimeSpan.FromDays(365),
        "Completed game snapshot retention must be between one and 365 days.")
    .Validate(
        options => options.OrphanSnapshotRetention >= TimeSpan.FromHours(1) &&
            options.OrphanSnapshotRetention <= TimeSpan.FromDays(30),
        "Orphan game snapshot retention must be between one hour and 30 days.")
    .Validate(
        options => options.CleanupInterval >= TimeSpan.FromMinutes(10) &&
            options.CleanupInterval <= TimeSpan.FromDays(1),
        "Game snapshot cleanup interval must be between ten minutes and one day.")
    .ValidateOnStart();
builder.Services.AddQuizizzoInfrastructure(connectionString);
builder.Services.AddQuizizzoGameEngine();
builder.Services.AddSingleton<IGameModule, EstimateGameModule>();
builder.Services.AddSingleton<IGameModule, AniMatesGameModule>();
builder.Services.AddSingleton<IGameModule, MajorityRulesGameModule>();
builder.Services.AddSingleton<IGameModule, BullshitGameModule>();
builder.Services.AddSingleton<IGameModule, SlopMachineGameModule>();
builder.Services.AddSingleton<IGameModule, PileUpPanicGameModule>();
builder.Services.AddSingleton<IGameModule, VoiceChoonGameModule>();
builder.Services.AddSingleton<IPartyGameRuntime, GameRuntimeGateway>();
builder.Services.AddSingleton<IGameRuntimeObserver, GameRuntimeRealtimeObserver>();
builder.Services.AddSingleton<QrCodeService>();
builder.Services.AddSingleton<DisplayRealtimeStateLoader>();
builder.Services.AddSingleton<PlayerRealtimeStateLoader>();
builder.Services.AddSingleton<HostPartyRealtimeService>();
builder.Services.AddOptions<RealtimePresenceOptions>()
    .Bind(builder.Configuration.GetSection(RealtimePresenceOptions.SectionName))
    .Validate(
        options => options.PlayerDisconnectGracePeriod >= TimeSpan.FromMilliseconds(10) &&
            options.PlayerDisconnectGracePeriod <= TimeSpan.FromMinutes(5),
        "Player disconnect grace period must be between ten milliseconds and five minutes.")
    .ValidateOnStart();
builder.Services.AddSingleton<PartyConnectionRegistry>();
builder.Services.AddSingleton<PlayerReactionLimiter>();
builder.Services.AddHostedService<VoiceChoonSongCatalogLoader>();
builder.Services.AddSingleton<IPartyRealtimeNotifier, SignalRPartyRealtimeNotifier>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("player-join", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RequestPartitionKey.RemoteAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("drawing-submit", context =>
        RateLimitPartition.GetConcurrencyLimiter(
            RequestPartitionKey.RemoteAddress(context),
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 4,
                QueueLimit = 20,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
    options.AddPolicy("drawing-assets", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RequestPartitionKey.RemoteAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("voice-sample-submit", context =>
        RateLimitPartition.GetConcurrencyLimiter(
            RequestPartitionKey.RemoteAddress(context),
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 2,
                QueueLimit = 8,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
    options.AddPolicy("voice-samples", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RequestPartitionKey.RemoteAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddHealthChecks()
    .AddCheck("application", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DrawingAssetStoreHealthCheck>("drawing-assets", tags: ["ready"])
    .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"]);
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddHealthChecks()
        .AddCheck(
            "redis-backplane",
            new RedisBackplaneHealthCheck(redisConnectionString),
            tags: ["ready"]);
}

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

if (args.Contains("--migrate=true", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Quizizzo.DatabaseMigration");
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    DatabaseMigrationLog.Applying(logger);
    await database.Database.MigrateAsync();
    DatabaseMigrationLog.Completed(logger);
    return;
}

app.UseForwardedHeaders();

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
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers.Append("Referrer-Policy", "same-origin");
        context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(self), geolocation=()");
        if (HttpMethods.IsGet(context.Request.Method) &&
            (context.Request.Path.StartsWithSegments("/media/audio", StringComparison.OrdinalIgnoreCase) ||
             context.Request.Path.StartsWithSegments(
                 "/media/games/slop-machine", StringComparison.OrdinalIgnoreCase)) &&
            context.Response.StatusCode == StatusCodes.Status200OK)
        {
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            context.Response.Headers.Append("CDN-Cache-Control", "public, max-age=31536000");
        }
        return Task.CompletedTask;
    });
    await next(context);
});
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.Equals("/display"))
    {
        const string cookieName = HostDisplayEndpoints.DisplayCookieName;
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
                Secure = !app.Environment.IsDevelopment() || context.Request.IsHttps
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
app.MapHostDisplayEndpoints();
app.MapDrawingAssetEndpoints();
app.MapVoiceSampleEndpoints();
app.MapHub<PartyHub>("/hubs/party");
app.MapHealthChecks("/health/live", new() { Predicate = check => check.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program;

internal static partial class DatabaseMigrationLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Applying Quizizzo database migrations.")]
    public static partial void Applying(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Quizizzo database migrations completed.")]
    public static partial void Completed(ILogger logger);
}
