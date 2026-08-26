using Quizizzo.GameContracts;

namespace Quizizzo.GameEngine;

public sealed class GameModuleCatalog
{
    private readonly IReadOnlyDictionary<string, IGameModule> modules;

    public GameModuleCatalog(IEnumerable<IGameModule> modules)
    {
        var discovered = new Dictionary<string, IGameModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            Validate(module.Descriptor);
            if (!discovered.TryAdd(module.Descriptor.Key, module))
            {
                throw new InvalidOperationException(
                    $"More than one game module is registered for key '{module.Descriptor.Key}'.");
            }
        }

        this.modules = discovered;
    }

    public IReadOnlyList<GameDescriptor> List() =>
        modules.Values.Select(module => module.Descriptor).OrderBy(item => item.DisplayName).ToArray();

    public IGameModule GetRequired(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return modules.TryGetValue(key.Trim(), out var module)
            ? module
            : throw new KeyNotFoundException($"No game module is registered for key '{key}'.");
    }

    private static void Validate(GameDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.DisplayName);
        if (descriptor.MinimumPlayers < 1 || descriptor.MaximumPlayers < descriptor.MinimumPlayers)
        {
            throw new InvalidOperationException(
                $"Game module '{descriptor.Key}' has invalid player limits.");
        }
    }
}
