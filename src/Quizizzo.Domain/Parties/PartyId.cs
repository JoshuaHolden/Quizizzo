namespace Quizizzo.Domain.Parties;

public readonly record struct PartyId(Guid Value)
{
    public static PartyId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
