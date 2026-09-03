namespace Quizizzo.Games.PileUpPanic;

public static class ScrapPhysics
{
    private static readonly GridPoint[] RotationCorrections =
    [
        new(0, 0), new(-1, 0), new(1, 0), new(0, -1),
        new(-2, 0), new(2, 0), new(-1, -1), new(1, -1)
    ];

    public static ActiveScrap? TryRotateClockwise(ArenaGrid grid, ActiveScrap active)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(active);
        foreach (var correction in RotationCorrections)
        {
            var rotated = active with
            {
                Rotation = (active.Rotation + 1) % 4,
                X = active.X + correction.X,
                Y = active.Y + correction.Y
            };
            if (grid.CanOccupy(rotated.OccupiedCells()))
            {
                return rotated;
            }
        }
        return null;
    }
}
