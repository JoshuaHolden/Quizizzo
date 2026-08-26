namespace Quizizzo.Application.Parties;

public sealed class PartyNotFoundException : Exception
{
    public PartyNotFoundException() : base("The party was not found.")
    {
    }
}
