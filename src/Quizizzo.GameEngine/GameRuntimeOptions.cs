namespace Quizizzo.GameEngine;

public sealed class GameRuntimeOptions
{
    public const string SectionName = "GameRuntime";
    public const int MinimumQueueCapacity = 32;
    public const int MaximumQueueCapacity = 4096;
    public const int MinimumProcessedCommands = 256;
    public const int MaximumProcessedCommandLimit = 65_536;

    public int CommandQueueCapacity { get; set; } = 256;
    public int MaximumProcessedCommands { get; set; } = 4096;

    internal void Validate()
    {
        if (CommandQueueCapacity is < MinimumQueueCapacity or > MaximumQueueCapacity)
        {
            throw new InvalidOperationException(
                $"Game command queue capacity must be from {MinimumQueueCapacity} to {MaximumQueueCapacity}.");
        }
        if (MaximumProcessedCommands is < MinimumProcessedCommands or > MaximumProcessedCommandLimit)
        {
            throw new InvalidOperationException(
                $"Processed command capacity must be from {MinimumProcessedCommands} " +
                $"to {MaximumProcessedCommandLimit}.");
        }
    }
}
