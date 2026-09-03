namespace Quizizzo.Games.PileUpPanic;

public sealed record PileUpOptions
{
    public const int Columns = 9;
    public const int VisibleRows = 17;
    public const int HiddenRows = 3;
    public const int TotalRows = VisibleRows + HiddenRows;

    public int InputLimitPerSecond { get; init; } = 24;
    public TimeSpan HorizontalRepeat { get; init; } = TimeSpan.FromMilliseconds(110);
    public TimeSpan SoftDropRepeat { get; init; } = TimeSpan.FromMilliseconds(55);
    public TimeSpan InitialFallInterval { get; init; } = TimeSpan.FromMilliseconds(850);
    public TimeSpan MinimumFallInterval { get; init; } = TimeSpan.FromMilliseconds(180);
    public TimeSpan SpeedUpEvery { get; init; } = TimeSpan.FromSeconds(25);
    public TimeSpan SpeedUpBy { get; init; } = TimeSpan.FromMilliseconds(90);
    public TimeSpan LockDelay { get; init; } = TimeSpan.FromMilliseconds(450);
    public int MaximumQueuedJunk { get; init; } = 4;
    public int MaximumJunkPerWindow { get; init; } = 2;
    public TimeSpan JunkWindow { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan AbilityCooldown { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan DisconnectGracePeriod { get; init; } = TimeSpan.FromSeconds(12);
    public TimeSpan RoundDuration { get; init; } = TimeSpan.FromSeconds(150);
    public TimeSpan SimulationStep { get; init; } = TimeSpan.FromMilliseconds(50);

    public void Validate()
    {
        Positive(InputLimitPerSecond, nameof(InputLimitPerSecond));
        Positive(MaximumQueuedJunk, nameof(MaximumQueuedJunk));
        Positive(MaximumJunkPerWindow, nameof(MaximumJunkPerWindow));
        Positive(HorizontalRepeat, nameof(HorizontalRepeat));
        Positive(SoftDropRepeat, nameof(SoftDropRepeat));
        Positive(InitialFallInterval, nameof(InitialFallInterval));
        Positive(MinimumFallInterval, nameof(MinimumFallInterval));
        Positive(SpeedUpEvery, nameof(SpeedUpEvery));
        Positive(SpeedUpBy, nameof(SpeedUpBy));
        Positive(LockDelay, nameof(LockDelay));
        Positive(JunkWindow, nameof(JunkWindow));
        Positive(AbilityCooldown, nameof(AbilityCooldown));
        Positive(DisconnectGracePeriod, nameof(DisconnectGracePeriod));
        Positive(RoundDuration, nameof(RoundDuration));
        Positive(SimulationStep, nameof(SimulationStep));
        if (MinimumFallInterval > InitialFallInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumFallInterval),
                "The minimum fall interval cannot exceed the initial interval.");
        }
    }

    private static void Positive(int value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "The value must be positive.");
        }
    }

    private static void Positive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, "The duration must be positive.");
        }
    }
}
