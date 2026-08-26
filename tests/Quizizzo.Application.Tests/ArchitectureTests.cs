namespace Quizizzo.Application.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Application_references_only_inward_Quizizzo_layers()
    {
        var references = typeof(Quizizzo.Application.Parties.PartyService).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("Quizizzo.", StringComparison.Ordinal) == true)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(references, reference =>
            Assert.Contains(reference, new[] { "Quizizzo.Domain", "Quizizzo.GameContracts" }));
    }
}
