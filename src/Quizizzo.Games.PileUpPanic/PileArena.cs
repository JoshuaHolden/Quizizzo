namespace Quizizzo.Games.PileUpPanic;

public sealed record ActiveScrap(string ClusterKey, string Material, int X, int Y, int Rotation)
{
    public IReadOnlyList<GridPoint> OccupiedCells() => ScrapClusterCatalogue.Get(ClusterKey)
        .CellsAt(Rotation)
        .Select(cell => new GridPoint(cell.X + X, cell.Y + Y))
        .ToArray();
}

public enum ChaosAbility
{
    SendJunk,
    ScrambleQueue,
    Shield
}

public sealed record LockOutcome(
    int CircuitsCompleted,
    int ViewsAwarded,
    int ChargeAwarded,
    bool AbilityEarned,
    int JunkApplied,
    bool Overloaded);

public sealed record PileArenaState(
    Guid PlayerId,
    string DisplayName,
    IReadOnlyList<ArenaCell> Grid,
    ActiveScrap? Active,
    IReadOnlyList<GeneratedScrap> Upcoming,
    int Views,
    int CircuitsCompleted,
    int ChaosCharge,
    ChaosAbility? AvailableAbility,
    bool Shielded,
    bool IsOverloaded,
    int QueuedJunk,
    ScrapStreamState ScrapSequence,
    IReadOnlyList<ChaosAbility> AbilityDeck);

public sealed class PileArena
{
    private readonly DeterministicScrapSequence scrapStream;
    private readonly Queue<GeneratedScrap> upcoming = new();
    private readonly Queue<ChaosAbility> abilityDeck = new();

    public PileArena(Guid playerId, string displayName, ulong seed)
    {
        PlayerId = playerId;
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("A display name is required.", nameof(displayName))
            : displayName;
        Grid = new ArenaGrid();
        scrapStream = new DeterministicScrapSequence(seed);
        RefillAbilityDeck();
        RefillUpcoming();
        SpawnNext();
    }

    public PileArena(PileArenaState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.PlayerId == Guid.Empty || string.IsNullOrWhiteSpace(state.DisplayName))
        {
            throw new InvalidDataException("A restored arena requires a player ID and display name.");
        }
        if (state.Upcoming.Count != 2 || state.Grid.Count > PileUpOptions.TotalRows * PileUpOptions.Columns)
        {
            throw new InvalidDataException("The restored arena queue or grid is invalid.");
        }

        PlayerId = state.PlayerId;
        DisplayName = state.DisplayName;
        Grid = new ArenaGrid();
        foreach (var cell in state.Grid)
        {
            if (Grid[cell.X, cell.Y] is not null)
            {
                throw new InvalidDataException("The restored arena contains duplicate cells.");
            }
            Grid[cell.X, cell.Y] = cell.Material;
        }
        scrapStream = new DeterministicScrapSequence(state.ScrapSequence);
        foreach (var item in state.Upcoming)
        {
            ValidateGeneratedScrap(item);
            upcoming.Enqueue(item);
        }
        if (state.AvailableAbility is { } availableAbility && !Enum.IsDefined(availableAbility))
        {
            throw new InvalidDataException("The restored available ability is invalid.");
        }
        foreach (var ability in state.AbilityDeck)
        {
            if (!Enum.IsDefined(ability))
            {
                throw new InvalidDataException("The restored ability deck is invalid.");
            }
            abilityDeck.Enqueue(ability);
        }
        if (state.Active is { } active)
        {
            _ = ScrapClusterCatalogue.Get(active.ClusterKey);
            if (!MaterialPalette.All.Contains(active.Material, StringComparer.Ordinal) ||
                !Grid.CanOccupy(active.OccupiedCells()))
            {
                throw new InvalidDataException("The restored active scrap cluster is invalid.");
            }
        }
        if (state.Views < 0 || state.CircuitsCompleted < 0 || state.ChaosCharge is < 0 or > 99 ||
            state.QueuedJunk < 0 || (state.IsOverloaded && state.Active is not null))
        {
            throw new InvalidDataException("The restored arena counters or status are invalid.");
        }
        Active = state.Active;
        Views = state.Views;
        CircuitsCompleted = state.CircuitsCompleted;
        ChaosCharge = state.ChaosCharge;
        AvailableAbility = state.AvailableAbility;
        Shielded = state.Shielded;
        IsOverloaded = state.IsOverloaded;
        QueuedJunk = state.QueuedJunk;
    }

    public Guid PlayerId { get; }
    public string DisplayName { get; }
    public ArenaGrid Grid { get; }
    public ActiveScrap? Active { get; private set; }
    public IReadOnlyList<GeneratedScrap> Upcoming => upcoming.ToArray();
    public int Views { get; private set; }
    public int CircuitsCompleted { get; private set; }
    public int ChaosCharge { get; private set; }
    public ChaosAbility? AvailableAbility { get; private set; }
    public bool Shielded { get; private set; }
    public bool IsOverloaded { get; private set; }
    public int QueuedJunk { get; private set; }

    public bool TryMove(int deltaX, int deltaY)
    {
        if (Active is not { } active || IsOverloaded)
        {
            return false;
        }
        var moved = active with { X = active.X + deltaX, Y = active.Y + deltaY };
        if (!Grid.CanOccupy(moved.OccupiedCells()))
        {
            return false;
        }
        Active = moved;
        return true;
    }

    public bool CanMove(int deltaX, int deltaY) => Active is { } active &&
        !IsOverloaded &&
        Grid.CanOccupy((active with { X = active.X + deltaX, Y = active.Y + deltaY }).OccupiedCells());

    public bool TryRotateClockwise()
    {
        if (Active is not { } active || IsOverloaded)
        {
            return false;
        }
        var rotated = ScrapPhysics.TryRotateClockwise(Grid, active);
        if (rotated is null)
        {
            return false;
        }
        Active = rotated;
        return true;
    }

    public int InstantDrop()
    {
        var distance = 0;
        while (TryMove(0, 1))
        {
            distance++;
        }
        return distance;
    }

    public LockOutcome LockActive()
    {
        if (Active is not { } active || IsOverloaded)
        {
            throw new InvalidOperationException("There is no active scrap cluster to lock.");
        }
        Grid.Place(active.OccupiedCells(), active.Material);
        Active = null;
        var completed = Grid.CompleteAndCollapseCircuits();
        var views = completed == 0 ? 5 : completed * completed * 100;
        var charge = ChargeFor(completed);
        Views += views;
        CircuitsCompleted += completed;
        var abilityBefore = AvailableAbility;
        AddCharge(charge);

        var junkApplied = ApplyOneQueuedJunk();
        var overloaded = IsOverloaded || Grid.HasHiddenCells() || !SpawnNext();
        if (overloaded)
        {
            ForceOverload();
        }
        return new LockOutcome(
            completed,
            views,
            charge,
            abilityBefore is null && AvailableAbility is not null,
            junkApplied,
            overloaded);
    }

    public int QueueJunk(int count, int maximumQueued)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var before = QueuedJunk;
        QueuedJunk = Math.Min(maximumQueued, QueuedJunk + count);
        return QueuedJunk - before;
    }

    public bool ConsumeShield()
    {
        if (!Shielded)
        {
            return false;
        }
        Shielded = false;
        return true;
    }

    public void ActivateShield() => Shielded = true;

    public void ScrambleUpcoming()
    {
        upcoming.Clear();
        RefillUpcoming();
    }

    public ChaosAbility TakeAbility()
    {
        var ability = AvailableAbility
            ?? throw new InvalidOperationException("No chaos ability is available.");
        AvailableAbility = null;
        return ability;
    }

    public void GrantAbilityForTesting(ChaosAbility ability)
    {
        AvailableAbility = ability;
        ChaosCharge = 0;
    }

    public void AddSoftDropView() => Views++;

    public void AddInstantDropViews(int distance) => Views += Math.Max(0, distance * 2);

    public void ForceOverload()
    {
        IsOverloaded = true;
        Active = null;
    }

    public PileArenaState CaptureState() => new(
        PlayerId,
        DisplayName,
        Grid.OccupiedCells(),
        Active,
        upcoming.ToArray(),
        Views,
        CircuitsCompleted,
        ChaosCharge,
        AvailableAbility,
        Shielded,
        IsOverloaded,
        QueuedJunk,
        scrapStream.Capture(),
        abilityDeck.ToArray());

    private bool SpawnNext()
    {
        RefillUpcoming();
        var next = upcoming.Dequeue();
        RefillUpcoming();
        var definition = ScrapClusterCatalogue.Get(next.ClusterKey);
        var width = definition.CellsAt(0).Max(cell => cell.X) + 1;
        var candidate = new ActiveScrap(next.ClusterKey, next.Material, (PileUpOptions.Columns - width) / 2, 0, 0);
        if (!Grid.CanOccupy(candidate.OccupiedCells()))
        {
            return false;
        }
        Active = candidate;
        return true;
    }

    private void RefillUpcoming()
    {
        while (upcoming.Count < 2)
        {
            upcoming.Enqueue(scrapStream.Next());
        }
    }

    private static void ValidateGeneratedScrap(GeneratedScrap item)
    {
        _ = ScrapClusterCatalogue.Get(item.ClusterKey);
        if (!MaterialPalette.All.Contains(item.Material, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The restored scrap material is invalid.");
        }
    }

    private int ApplyOneQueuedJunk()
    {
        if (QueuedJunk == 0)
        {
            return 0;
        }
        QueuedJunk--;
        if (!Grid.AddJunkCircuit(scrapStream.NextInt(PileUpOptions.Columns)))
        {
            IsOverloaded = true;
        }
        return 1;
    }

    private void AddCharge(int amount)
    {
        if (amount <= 0 || AvailableAbility is not null)
        {
            return;
        }
        ChaosCharge = Math.Min(100, ChaosCharge + amount);
        if (ChaosCharge < 100)
        {
            return;
        }
        ChaosCharge = 0;
        if (abilityDeck.Count == 0)
        {
            RefillAbilityDeck();
        }
        AvailableAbility = abilityDeck.Dequeue();
    }

    private void RefillAbilityDeck()
    {
        var abilities = Enum.GetValues<ChaosAbility>().ToList();
        while (abilities.Count > 0)
        {
            var index = scrapStream.NextInt(abilities.Count);
            abilityDeck.Enqueue(abilities[index]);
            abilities.RemoveAt(index);
        }
    }

    private static int ChargeFor(int circuits) => circuits switch
    {
        <= 0 => 0,
        1 => 34,
        2 => 65,
        3 => 90,
        _ => 100
    };
}
