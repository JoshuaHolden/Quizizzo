namespace Quizizzo.Games.PileUpPanic;

public readonly record struct GridPoint(int X, int Y);

public sealed record ScrapClusterDefinition(string Key, IReadOnlyList<GridPoint> Cells)
{
    public IReadOnlyList<GridPoint> CellsAt(int clockwiseTurns)
    {
        var turns = ((clockwiseTurns % 4) + 4) % 4;
        var transformed = Cells.Select(cell => turns switch
        {
            0 => cell,
            1 => new GridPoint(-cell.Y, cell.X),
            2 => new GridPoint(-cell.X, -cell.Y),
            _ => new GridPoint(cell.Y, -cell.X)
        }).ToArray();
        var minimumX = transformed.Min(cell => cell.X);
        var minimumY = transformed.Min(cell => cell.Y);
        return transformed
            .Select(cell => new GridPoint(cell.X - minimumX, cell.Y - minimumY))
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToArray();
    }
}

public static class ScrapClusterCatalogue
{
    public static IReadOnlyList<ScrapClusterDefinition> All { get; } =
    [
        Define("bolt", (0, 0), (1, 0)),
        Define("corner-chip", (0, 0), (0, 1), (1, 1)),
        Define("short-rail", (0, 0), (1, 0), (2, 0)),
        Define("offset-trio", (0, 0), (1, 0), (1, 1)),
        Define("crooked-fork", (0, 0), (1, 0), (2, 0), (1, 1)),
        Define("step-plate", (1, 0), (2, 0), (0, 1), (1, 1)),
        Define("long-hook", (0, 0), (0, 1), (0, 2), (1, 2)),
        Define("block-plate", (0, 0), (1, 0), (0, 1), (1, 1)),
        Define("wide-cup", (0, 0), (2, 0), (0, 1), (1, 1), (2, 1)),
        Define("stair-stack", (0, 0), (0, 1), (1, 1), (1, 2), (2, 2)),
        Define("flag-post", (0, 0), (1, 0), (0, 1), (0, 2), (0, 3)),
        Define("split-anvil", (0, 0), (2, 0), (0, 1), (1, 1), (1, 2))
    ];

    public static IReadOnlyList<ScrapClusterDefinition> Playable { get; } = All
        .Where(cluster => cluster.Key is not ("flag-post" or "split-anvil"))
        .ToArray();

    public static ScrapClusterDefinition Get(string key) =>
        All.Single(cluster => string.Equals(cluster.Key, key, StringComparison.Ordinal));

    private static ScrapClusterDefinition Define(string key, params (int X, int Y)[] cells) =>
        new(key, cells.Select(cell => new GridPoint(cell.X, cell.Y)).ToArray());
}

public static class MaterialPalette
{
    public static IReadOnlyList<string> All { get; } =
    ["copper", "aqua", "lemon", "violet", "coral", "mint", "sky", "sand"];
}
