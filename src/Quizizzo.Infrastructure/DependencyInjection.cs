using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quizizzo.Application.Abstractions;
using Quizizzo.Infrastructure.Displays;
using Quizizzo.Infrastructure.Identity;
using Quizizzo.Infrastructure.Parties;

namespace Quizizzo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddQuizizzoInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));
        services.AddScoped<IPartyRepository, PartyRepository>();
        services.AddScoped<IDisplaySessionRepository, DisplaySessionRepository>();
        services.AddSingleton<IRoomCodeGenerator, CryptographicRoomCodeGenerator>();
        services.AddSingleton<IDisplayCredentialService, DisplayCredentialService>();
        return services;
    }
}
