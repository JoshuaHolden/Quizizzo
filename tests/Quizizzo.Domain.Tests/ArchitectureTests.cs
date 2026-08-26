namespace Quizizzo.Domain.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_has_no_project_layer_dependencies()
    {
        var referencedLayers = typeof(Quizizzo.Domain.Parties.Party).Assembly
            .GetReferencedAssemblies()
            .Where(reference => reference.Name?.StartsWith("Quizizzo.", StringComparison.Ordinal) == true)
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Empty(referencedLayers);
    }
}
