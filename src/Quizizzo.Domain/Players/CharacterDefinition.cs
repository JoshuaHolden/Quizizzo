namespace Quizizzo.Domain.Players;

public enum CharacterBodyType
{
    Bean,
    Blob,
    Round,
    Square
}

public enum CharacterEyes
{
    Bright,
    Sleepy,
    Starry,
    Googly
}

public enum CharacterMouth
{
    Smile,
    Grin,
    Surprised,
    Tongue
}

public enum CharacterAccessory
{
    None,
    Crown,
    BowTie,
    PartyHat,
    Glasses
}

public sealed class CharacterDefinition
{
    private CharacterDefinition()
    {
    }

    public CharacterDefinition(
        CharacterBodyType bodyType,
        string primaryColour,
        CharacterEyes eyes,
        CharacterMouth mouth,
        CharacterAccessory accessory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryColour);
        BodyType = bodyType;
        PrimaryColour = primaryColour;
        Eyes = eyes;
        Mouth = mouth;
        Accessory = accessory;
    }

    public CharacterBodyType BodyType { get; private set; }
    public string PrimaryColour { get; private set; } = string.Empty;
    public CharacterEyes Eyes { get; private set; }
    public CharacterMouth Mouth { get; private set; }
    public CharacterAccessory Accessory { get; private set; }
}
