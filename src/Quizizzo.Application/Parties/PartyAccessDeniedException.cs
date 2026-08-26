namespace Quizizzo.Application.Parties;

public sealed class PartyAccessDeniedException : Exception
{
    public PartyAccessDeniedException() : base("The current host does not own this party.")
    {
    }
}
