namespace Quizizzo.Domain.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_has_no_project_layer_dependencies()
    {
        var referencedLayers = typeof(Quizizzo.Domain.Class1).Assembly
            .GetReferencedAssemblies()
            .Where(reference => reference.Name?.StartsWith("Quizizzo.", StringComparison.Ordinal) == true)
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Empty(referencedLayers);
    }
}
