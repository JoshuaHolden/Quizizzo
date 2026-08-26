using Quizizzo.GameContracts;
using Quizizzo.GameEngine;

namespace Quizizzo.GameEngine.Tests;

public sealed class GameModuleCatalogTests
{
    [Fact]
    public void Registered_modules_are_discoverable_by_case_insensitive_key()
    {
        var module = new TestGameModule();
        var catalog = new GameModuleCatalog([module]);

        Assert.Same(module, catalog.GetRequired("TEST-GAME"));
        Assert.Equal(module.Descriptor, Assert.Single(catalog.List()));
    }

    [Fact]
    public void Duplicate_module_keys_fail_at_composition_time()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new GameModuleCatalog([new TestGameModule(), new TestGameModule()]));

        Assert.Contains("More than one", exception.Message);
    }

    [Fact]
    public void Game_contracts_do_not_reference_engine_transport_or_persistence_projects()
    {
        var forbidden = new[]
        {
            "Quizizzo.GameEngine",
            "Quizizzo.Web",
            "Quizizzo.Infrastructure"
        };
        var references = typeof(IGameModule).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();

        Assert.DoesNotContain(references, reference =>
            forbidden.Contains(reference, StringComparer.Ordinal));
    }
}
