namespace Quizizzo.Application.Players;

public sealed class PlayerSessionNotFoundException : Exception
{
    public PlayerSessionNotFoundException() : base("No valid player session was found.")
    {
    }
}
