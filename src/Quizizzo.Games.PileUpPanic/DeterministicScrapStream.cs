namespace Quizizzo.Games.PileUpPanic;

public sealed record ScrapStreamState(
    ulong RandomState,
    IReadOnlyList<int> RecentClusterIndices,
    IReadOnlyList<int> MaterialOrder,
    int MaterialCursor);

public sealed record GeneratedScrap(string ClusterKey, string Material);

public sealed class DeterministicScrapSequence
{
    private const int RecentWindow = 4;
    private ulong randomState;
    private readonly Queue<int> recent;
    private int[] materialOrder;
    private int materialCursor;

    public DeterministicScrapSequence(ulong seed)
        : this(new ScrapStreamState(
            seed == 0 ? 0xA0761D6478BD642FUL : seed,
            [],
            Enumerable.Range(0, MaterialPalette.All.Count).ToArray(),
            MaterialPalette.All.Count))
    {
    }

    public DeterministicScrapSequence(ScrapStreamState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.RecentClusterIndices.Any(index => index < 0 || index >= ScrapClusterCatalogue.All.Count) ||
            state.MaterialOrder.Count != MaterialPalette.All.Count ||
            state.MaterialOrder.Distinct().Count() != MaterialPalette.All.Count ||
            state.MaterialOrder.Any(index => index < 0 || index >= MaterialPalette.All.Count) ||
            state.MaterialCursor < 0 || state.MaterialCursor > MaterialPalette.All.Count)
        {
            throw new InvalidDataException("The restored scrap sequence is invalid.");
        }
        randomState = state.RandomState == 0 ? 0xA0761D6478BD642FUL : state.RandomState;
        recent = new Queue<int>(state.RecentClusterIndices.TakeLast(RecentWindow));
        materialOrder = state.MaterialOrder.ToArray();
        materialCursor = state.MaterialCursor;
    }

    public GeneratedScrap Next()
    {
        var available = Enumerable.Range(0, ScrapClusterCatalogue.All.Count)
            .Where(index => !recent.Contains(index))
            .ToArray();
        if (available.Length == 0)
        {
            recent.Clear();
            available = Enumerable.Range(0, ScrapClusterCatalogue.All.Count).ToArray();
        }

        var clusterIndex = available[NextInt(available.Length)];
        recent.Enqueue(clusterIndex);
        while (recent.Count > RecentWindow)
        {
            recent.Dequeue();
        }

        if (materialCursor >= materialOrder.Length)
        {
            Shuffle(materialOrder);
            materialCursor = 0;
        }
        var material = MaterialPalette.All[materialOrder[materialCursor++]];
        return new GeneratedScrap(ScrapClusterCatalogue.All[clusterIndex].Key, material);
    }

    public ScrapStreamState Capture() => new(
        randomState,
        recent.ToArray(),
        materialOrder.ToArray(),
        materialCursor);

    public int NextInt(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);
        return (int)(NextUInt64() % (uint)exclusiveMaximum);
    }

    private ulong NextUInt64()
    {
        randomState += 0x9E3779B97F4A7C15UL;
        var value = randomState;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private void Shuffle(int[] values)
    {
        for (var index = values.Length - 1; index > 0; index--)
        {
            var other = NextInt(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }
}
