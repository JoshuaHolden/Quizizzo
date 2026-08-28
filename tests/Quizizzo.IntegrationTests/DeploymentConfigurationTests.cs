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
        Assert.Contains("quizizzo-private", compose, StringComparison.Ordinal);
        Assert.Contains("QUIZIZZO_ALLOWED_HOSTS", ReadRepositoryFile(".env.example"),
            StringComparison.Ordinal);
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
