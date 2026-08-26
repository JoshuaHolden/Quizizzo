using System.Text.Json;
using Quizizzo.GameContracts;

namespace Quizizzo.GameEngine;

internal static class GameStateValidator
{
    public static void Validate(GameModuleState state, string moduleKey)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion < 1)
        {
            throw new InvalidOperationException($"Game module '{moduleKey}' returned an invalid schema version.");
        }

        if (string.IsNullOrWhiteSpace(state.Phase))
        {
            throw new InvalidOperationException($"Game module '{moduleKey}' returned an empty phase.");
        }

        if (state.Data.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"Game module '{moduleKey}' returned undefined state data.");
        }

        if (state.PhaseEndsAtUtc is { Offset: var offset } && offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Game module '{moduleKey}' returned a non-UTC deadline.");
        }

        if (state.IsComplete && state.PhaseEndsAtUtc.HasValue)
        {
            throw new InvalidOperationException($"Completed game module '{moduleKey}' cannot retain a deadline.");
        }
    }

    public static void ValidateTransition(
        GameTransition transition,
        GameRuntimeSnapshot current,
        IGameAction action)
    {
        ArgumentNullException.ThrowIfNull(transition);
        Validate(transition.State, current.GameKey);

        foreach (var award in transition.ScoreAwards)
        {
            if (!current.Participants.Any(player => player.PlayerId == award.PlayerId))
            {
                throw new InvalidOperationException(
                    $"Game module '{current.GameKey}' awarded points to a non-participant.");
            }
            if (award.Points == 0 || string.IsNullOrWhiteSpace(award.Reason))
            {
                throw new InvalidOperationException(
                    $"Game module '{current.GameKey}' returned an invalid score award.");
            }
        }

        foreach (var gameEvent in transition.Events)
        {
            if (string.IsNullOrWhiteSpace(gameEvent.Kind) || gameEvent.Data.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidOperationException(
                    $"Game module '{current.GameKey}' returned an invalid semantic event.");
            }
        }

        if (action is DeadlineElapsedAction elapsed &&
            transition.State.Phase == current.ModuleState.Phase &&
            transition.State.PhaseEndsAtUtc == elapsed.ScheduledForUtc)
        {
            throw new InvalidOperationException(
                $"Game module '{current.GameKey}' did not advance or clear its elapsed deadline.");
        }
    }
}
