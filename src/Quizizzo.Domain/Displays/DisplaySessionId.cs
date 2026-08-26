namespace Quizizzo.Domain.Displays;

public readonly record struct DisplaySessionId(Guid Value)
{
    public static DisplaySessionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
