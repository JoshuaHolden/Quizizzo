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

public enum CharacterPresentation { Man, Woman }
public enum CharacterSkinTone { Tint1 = 1, Tint3 = 3, Tint5 = 5, Tint7 = 7 }
public enum CharacterHairColour { Brown, Black, Blonde, Red }
public enum CharacterShirtColour { Navy, Blue, Green, Red }
public enum CharacterTrouserColour { Navy, Blue, Green, Tan }
public enum CharacterTrouserLength { FullLength, Cropped, Shorts }
public enum CharacterShoeColour { Brown, Black, Blue, Red }

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
        Presentation = bodyType is CharacterBodyType.Blob or CharacterBodyType.Square
            ? CharacterPresentation.Woman
            : CharacterPresentation.Man;
        SkinTone = bodyType switch
        {
            CharacterBodyType.Blob => CharacterSkinTone.Tint3,
            CharacterBodyType.Round => CharacterSkinTone.Tint5,
            CharacterBodyType.Square => CharacterSkinTone.Tint7,
            _ => CharacterSkinTone.Tint1,
        };
        HairColour = eyes switch
        {
            CharacterEyes.Googly => CharacterHairColour.Black,
            CharacterEyes.Bright => CharacterHairColour.Blonde,
            CharacterEyes.Starry => CharacterHairColour.Red,
            _ => CharacterHairColour.Brown,
        };
        ShirtColour = CharacterShirtColour.Navy;
        TrouserColour = CharacterTrouserColour.Navy;
        TrouserLength = CharacterTrouserLength.FullLength;
        ShoeColour = CharacterShoeColour.Brown;
    }

    public CharacterDefinition(
        CharacterPresentation presentation,
        CharacterSkinTone skinTone,
        CharacterHairColour hairColour,
        CharacterShirtColour shirtColour,
        CharacterTrouserColour trouserColour,
        CharacterTrouserLength trouserLength,
        CharacterShoeColour shoeColour)
    {
        EnsureDefined(presentation);
        EnsureDefined(skinTone);
        EnsureDefined(hairColour);
        EnsureDefined(shirtColour);
        EnsureDefined(trouserColour);
        EnsureDefined(trouserLength);
        EnsureDefined(shoeColour);
        Presentation = presentation;
        SkinTone = skinTone;
        HairColour = hairColour;
        ShirtColour = shirtColour;
        TrouserColour = trouserColour;
        TrouserLength = trouserLength;
        ShoeColour = shoeColour;
        BodyType = presentation == CharacterPresentation.Woman ? CharacterBodyType.Blob : CharacterBodyType.Bean;
        PrimaryColour = shirtColour switch
        {
            CharacterShirtColour.Blue => "#3498db",
            CharacterShirtColour.Green => "#62bb46",
            CharacterShirtColour.Red => "#e65345",
            _ => "#34495e",
        };
        Eyes = CharacterEyes.Bright;
        Mouth = CharacterMouth.Smile;
        Accessory = CharacterAccessory.None;
    }

    public CharacterBodyType BodyType { get; private set; }
    public string PrimaryColour { get; private set; } = string.Empty;
    public CharacterEyes Eyes { get; private set; }
    public CharacterMouth Mouth { get; private set; }
    public CharacterAccessory Accessory { get; private set; }
    public CharacterPresentation Presentation { get; private set; }
    public CharacterSkinTone SkinTone { get; private set; }
    public CharacterHairColour HairColour { get; private set; }
    public CharacterShirtColour ShirtColour { get; private set; }
    public CharacterTrouserColour TrouserColour { get; private set; }
    public CharacterTrouserLength TrouserLength { get; private set; }
    public CharacterShoeColour ShoeColour { get; private set; }

    private static void EnsureDefined<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
