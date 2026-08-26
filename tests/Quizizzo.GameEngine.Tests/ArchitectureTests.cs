namespace Quizizzo.GameEngine.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void GameEngine_does_not_reference_transport_or_UI_layers()
    {
        var forbidden = new[] { "Quizizzo.Web", "Quizizzo.Infrastructure" };
        var references = typeof(Quizizzo.GameEngine.GameRuntimeManager).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();

        Assert.DoesNotContain(references, reference => forbidden.Contains(reference, StringComparer.Ordinal));
    }
}
