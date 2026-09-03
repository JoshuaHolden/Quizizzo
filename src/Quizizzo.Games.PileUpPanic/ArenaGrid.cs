namespace Quizizzo.Games.PileUpPanic;

public sealed class ArenaGrid
{
    private readonly string?[,] cells;

    public ArenaGrid()
        : this(new string?[PileUpOptions.TotalRows, PileUpOptions.Columns])
    {
    }

    private ArenaGrid(string?[,] cells) => this.cells = cells;

    public string? this[int x, int y]
    {
        get => IsInside(x, y) ? cells[y, x] : null;
        set
        {
            if (!IsInside(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }
            cells[y, x] = value;
        }
    }

    public bool CanOccupy(IEnumerable<GridPoint> points) => points.All(point =>
        IsInside(point.X, point.Y) && cells[point.Y, point.X] is null);

    public void Place(IEnumerable<GridPoint> points, string material)
    {
        var placed = points.ToArray();
        if (string.IsNullOrWhiteSpace(material) || !CanOccupy(placed))
        {
            throw new InvalidOperationException("The scrap cluster cannot occupy those cells.");
        }
        foreach (var point in placed)
        {
            cells[point.Y, point.X] = material;
        }
    }

    public IReadOnlyList<int> CompleteCircuits() => Enumerable.Range(0, PileUpOptions.TotalRows)
        .Where(row => Enumerable.Range(0, PileUpOptions.Columns).All(column => cells[row, column] is not null))
        .ToArray();

    public int CompleteAndCollapseCircuits()
    {
        var complete = CompleteCircuits().ToHashSet();
        if (complete.Count == 0)
        {
            return 0;
        }

        var destination = PileUpOptions.TotalRows - 1;
        for (var source = PileUpOptions.TotalRows - 1; source >= 0; source--)
        {
            if (complete.Contains(source))
            {
                continue;
            }
            for (var column = 0; column < PileUpOptions.Columns; column++)
            {
                cells[destination, column] = cells[source, column];
            }
            destination--;
        }
        while (destination >= 0)
        {
            for (var column = 0; column < PileUpOptions.Columns; column++)
            {
                cells[destination, column] = null;
            }
            destination--;
        }
        return complete.Count;
    }

    public bool AddJunkCircuit(int openColumn, string material = "junk")
    {
        if (openColumn < 0 || openColumn >= PileUpOptions.Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(openColumn));
        }
        if (Enumerable.Range(0, PileUpOptions.Columns).Any(column => cells[0, column] is not null))
        {
            return false;
        }

        for (var row = 0; row < PileUpOptions.TotalRows - 1; row++)
        {
            for (var column = 0; column < PileUpOptions.Columns; column++)
            {
                cells[row, column] = cells[row + 1, column];
            }
        }
        for (var column = 0; column < PileUpOptions.Columns; column++)
        {
            cells[PileUpOptions.TotalRows - 1, column] = column == openColumn ? null : material;
        }
        return true;
    }

    public bool HasHiddenCells() => Enumerable.Range(0, PileUpOptions.HiddenRows)
        .Any(row => Enumerable.Range(0, PileUpOptions.Columns).Any(column => cells[row, column] is not null));

    public int StackHeight()
    {
        for (var row = 0; row < PileUpOptions.TotalRows; row++)
        {
            if (Enumerable.Range(0, PileUpOptions.Columns).Any(column => cells[row, column] is not null))
            {
                return PileUpOptions.TotalRows - row;
            }
        }
        return 0;
    }

    public IReadOnlyList<ArenaCell> OccupiedCells() =>
        (from row in Enumerable.Range(0, PileUpOptions.TotalRows)
         from column in Enumerable.Range(0, PileUpOptions.Columns)
         let material = cells[row, column]
         where material is not null
         select new ArenaCell(column, row, material)).ToArray();

    public ArenaGrid Clone()
    {
        var copy = new string?[PileUpOptions.TotalRows, PileUpOptions.Columns];
        Array.Copy(cells, copy, cells.Length);
        return new ArenaGrid(copy);
    }

    private static bool IsInside(int x, int y) =>
        x >= 0 && x < PileUpOptions.Columns && y >= 0 && y < PileUpOptions.TotalRows;
}

public sealed record ArenaCell(int X, int Y, string Material);
