namespace Quizizzo.IntegrationTests;

public sealed class DeploymentConfigurationTests
{
    [Fact]
    public void Compose_is_loopback_only_and_keeps_PostgreSQL_private()
    {
        var compose = ReadRepositoryFile("compose.yaml");

        Assert.Contains("name: quizizzo", compose, StringComparison.Ordinal);
        Assert.Contains(
            "127.0.0.1:${QUIZIZZO_HTTP_PORT:-8081}:8080",
            compose,
            StringComparison.Ordinal);
        Assert.DoesNotContain("- \"5432:", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("- \"6379:", compose, StringComparison.Ordinal);
        Assert.Contains("quizizzo-private", compose, StringComparison.Ordinal);
        Assert.Contains("QUIZIZZO_ALLOWED_HOSTS", ReadRepositoryFile(".env.example"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SignalR_uses_a_private_password_protected_Redis_backplane()
    {
        var compose = ReadRepositoryFile("compose.yaml");
        var project = ReadRepositoryFile("src/Quizizzo.Web/Quizizzo.Web.csproj");
        var program = ReadRepositoryFile("src/Quizizzo.Web/Program.cs");

        Assert.Contains("redis:8.2.8-alpine", compose, StringComparison.Ordinal);
        Assert.Contains("QUIZIZZO_REDIS_PASSWORD", compose, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__Redis", compose, StringComparison.Ordinal);
        Assert.Contains("condition: service_healthy", compose, StringComparison.Ordinal);
        Assert.Contains("Microsoft.AspNetCore.SignalR.StackExchangeRedis", project,
            StringComparison.Ordinal);
        Assert.Contains("AddStackExchangeRedis", program, StringComparison.Ordinal);
        Assert.Contains("Quizizzo.SignalR", program, StringComparison.Ordinal);
        Assert.Contains("redis-backplane", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_preserves_Data_Protection_keys_and_runs_as_non_root()
    {
        var compose = ReadRepositoryFile("compose.yaml");
        var dockerfile = ReadRepositoryFile("Dockerfile");

        Assert.Contains("quizizzo-data-protection:/app/data-protection", compose,
            StringComparison.Ordinal);
        Assert.Contains("DataProtection__KeyPath: /app/data-protection", compose,
            StringComparison.Ordinal);
        Assert.Contains("USER app", dockerfile, StringComparison.Ordinal);
        Assert.Contains("libgssapi-krb5-2", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrations_are_an_explicit_one_shot_service_on_the_private_network()
    {
        var compose = ReadRepositoryFile("compose.yaml");
        var program = ReadRepositoryFile("src/Quizizzo.Web/Program.cs");

        Assert.Contains("migrate:", compose, StringComparison.Ordinal);
        Assert.Contains("profiles: [\"tools\"]", compose, StringComparison.Ordinal);
        Assert.Contains("command: [\"--migrate=true\"]", compose, StringComparison.Ordinal);
        Assert.Contains("condition: service_healthy", compose, StringComparison.Ordinal);
        Assert.Contains("await database.Database.MigrateAsync();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCreated", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_requires_an_explicit_PostgreSQL_password()
    {
        var compose = ReadRepositoryFile("compose.yaml");

        Assert.Contains(
            "${QUIZIZZO_POSTGRES_PASSWORD:?Set QUIZIZZO_POSTGRES_PASSWORD in .env}",
            compose,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Container_context_excludes_secrets_and_generated_runtime_data()
    {
        var dockerIgnore = ReadRepositoryFile(".dockerignore");

        Assert.Contains(".env", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("node_modules", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("data-protection", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("assets", dockerIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public void Container_restores_only_the_production_project_graph()
    {
        var dockerfile = ReadRepositoryFile("Dockerfile");

        Assert.Contains(
            "dotnet restore src/Quizizzo.Web/Quizizzo.Web.csproj",
            dockerfile,
            StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet restore Quizizzo.sln", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_logs_only_errors_and_container_logs_are_bounded()
    {
        var compose = ReadRepositoryFile("compose.yaml");
        var program = ReadRepositoryFile("src/Quizizzo.Web/Program.cs");

        Assert.Contains("builder.Environment.IsProduction()", program, StringComparison.Ordinal);
        Assert.Contains("level >= LogLevel.Error", program, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(compose, "Logging__LogLevel__Default: Error"));
        Assert.Equal(2,
            CountOccurrences(compose, "Logging__LogLevel__Microsoft.AspNetCore: Error"));
        Assert.Equal(4, CountOccurrences(compose, "driver: json-file"));
        Assert.Equal(4, CountOccurrences(compose, "max-size: \"10m\""));
        Assert.Equal(4, CountOccurrences(compose, "max-file: \"3\""));
    }

    [Fact]
    public void Production_database_pool_leaves_capacity_for_backup_and_migration_operations()
    {
        var compose = ReadRepositoryFile("compose.yaml");
        var infrastructure = ReadRepositoryFile(
            "src/Quizizzo.Infrastructure/DependencyInjection.cs");

        Assert.Equal(2, CountOccurrences(compose, "Maximum Pool Size=32"));
        Assert.Equal(2, CountOccurrences(compose, "Connection Idle Lifetime=60"));
        Assert.Contains("MaximumDatabasePoolSize = 32", infrastructure, StringComparison.Ordinal);
        Assert.Contains("MaximumIdleConnectionLifetimeSeconds = 60", infrastructure,
            StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string search) =>
        value.Split(search, StringSplitOptions.None).Length - 1;

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Quizizzo.sln")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new DirectoryNotFoundException("The Quizizzo repository root was not found.");
        }
        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}
