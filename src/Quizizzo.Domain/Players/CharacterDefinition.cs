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
    Tongue,
    Sad,
    Straight,
    TeethLower
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
public enum CharacterBodySize { Thin, Normal, Thick }
public enum CharacterSkinTone { Tint1 = 1, Tint3 = 3, Tint5 = 5, Tint7 = 7 }
public enum CharacterHairColour { Brown, Black, Blonde, Red }
public enum CharacterHairStyle { Style1 = 1, Style2, Style3, Style4, Style5, Style6, Style7, Style8 }
public enum CharacterEyeColour { Black, Blue, Brown, Green, Pine }
public enum CharacterEyeSize { Large, Small }
public enum CharacterFaceShape { Oval, Round, Wide }
public enum CharacterNoseShape { Nose1 = 1, Nose2, Nose3 }
public enum CharacterBrowShape { Brow1 = 1, Brow2, Brow3 }
public enum CharacterShirtColour { Navy, Blue, Green, Red }
public enum CharacterTrouserColour { Navy, Blue, Green, Tan }
public enum CharacterTrouserLength { FullLength, Cropped, Shorts }
public enum CharacterShoeColour { Brown, Black, Blue, Red }
public enum CharacterShoeStyle { Style1 = 1, Style2, Style3, Style4, Style5 }
public enum CharacterShirtStyle { Default, Style1, Style2, Style3, Style4, Style5, Style6, Style7, Style8 }
public enum CharacterTrouserStyle { Style1 = 1, Style2, Style3, Style4 }

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
        HairStyle = CharacterHairStyle.Style1;
        EyeColour = CharacterEyeColour.Blue;
        EyeSize = CharacterEyeSize.Large;
        FaceShape = CharacterFaceShape.Round;
        NoseShape = CharacterNoseShape.Nose1;
        BrowShape = CharacterBrowShape.Brow1;
        ShoeStyle = CharacterShoeStyle.Style1;
        ShirtStyle = Presentation == CharacterPresentation.Woman
            ? CharacterShirtStyle.Style4
            : CharacterShirtStyle.Style1;
        TrouserStyle = CharacterTrouserStyle.Style1;
        BodySize = CharacterBodySize.Normal;
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
        CharacterShoeColour shoeColour,
        CharacterHairStyle hairStyle = CharacterHairStyle.Style1,
        CharacterEyeColour eyeColour = CharacterEyeColour.Blue,
        CharacterEyeSize eyeSize = CharacterEyeSize.Large,
        CharacterFaceShape faceShape = CharacterFaceShape.Round,
        CharacterNoseShape noseShape = CharacterNoseShape.Nose1,
        CharacterMouth mouth = CharacterMouth.Smile,
        CharacterBrowShape browShape = CharacterBrowShape.Brow1,
        CharacterShoeStyle shoeStyle = CharacterShoeStyle.Style1,
        CharacterShirtStyle shirtStyle = CharacterShirtStyle.Default,
        CharacterTrouserStyle trouserStyle = CharacterTrouserStyle.Style1,
        CharacterBodySize bodySize = CharacterBodySize.Normal)
    {
        EnsureDefined(presentation);
        EnsureDefined(skinTone);
        EnsureDefined(hairColour);
        EnsureDefined(shirtColour);
        EnsureDefined(trouserColour);
        EnsureDefined(trouserLength);
        EnsureDefined(shoeColour);
        EnsureDefined(hairStyle);
        EnsureDefined(eyeColour);
        EnsureDefined(eyeSize);
        EnsureDefined(faceShape);
        EnsureDefined(noseShape);
        EnsureDefined(mouth);
        EnsureDefined(browShape);
        EnsureDefined(shoeStyle);
        EnsureDefined(shirtStyle);
        EnsureDefined(trouserStyle);
        EnsureDefined(bodySize);
        if (presentation == CharacterPresentation.Woman && hairStyle > CharacterHairStyle.Style6)
        {
            throw new ArgumentOutOfRangeException(nameof(hairStyle));
        }
        var resolvedShirtStyle = shirtStyle == CharacterShirtStyle.Default
            ? presentation == CharacterPresentation.Woman
                ? CharacterShirtStyle.Style4
                : CharacterShirtStyle.Style1
            : shirtStyle;
        var feminineShirt = resolvedShirtStyle is CharacterShirtStyle.Style4 or CharacterShirtStyle.Style8;
        if ((presentation == CharacterPresentation.Woman) != feminineShirt)
        {
            throw new ArgumentOutOfRangeException(nameof(shirtStyle));
        }
        Presentation = presentation;
        SkinTone = skinTone;
        HairColour = hairColour;
        ShirtColour = shirtColour;
        TrouserColour = trouserColour;
        TrouserLength = trouserLength;
        ShoeColour = shoeColour;
        HairStyle = hairStyle;
        EyeColour = eyeColour;
        EyeSize = eyeSize;
        FaceShape = faceShape;
        NoseShape = noseShape;
        BrowShape = browShape;
        ShoeStyle = shoeStyle;
        ShirtStyle = resolvedShirtStyle;
        TrouserStyle = trouserStyle;
        BodySize = bodySize;
        BodyType = presentation == CharacterPresentation.Woman ? CharacterBodyType.Blob : CharacterBodyType.Bean;
        PrimaryColour = shirtColour switch
        {
            CharacterShirtColour.Blue => "#3498db",
            CharacterShirtColour.Green => "#62bb46",
            CharacterShirtColour.Red => "#e65345",
            _ => "#34495e",
        };
        Eyes = CharacterEyes.Bright;
        Mouth = mouth;
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
    public CharacterHairStyle HairStyle { get; private set; } = CharacterHairStyle.Style1;
    public CharacterEyeColour EyeColour { get; private set; } = CharacterEyeColour.Blue;
    public CharacterEyeSize EyeSize { get; private set; } = CharacterEyeSize.Large;
    public CharacterFaceShape FaceShape { get; private set; } = CharacterFaceShape.Round;
    public CharacterNoseShape NoseShape { get; private set; } = CharacterNoseShape.Nose1;
    public CharacterBrowShape BrowShape { get; private set; } = CharacterBrowShape.Brow1;
    public CharacterShoeStyle ShoeStyle { get; private set; } = CharacterShoeStyle.Style1;
    public CharacterShirtStyle ShirtStyle { get; private set; } = CharacterShirtStyle.Default;
    public CharacterTrouserStyle TrouserStyle { get; private set; } = CharacterTrouserStyle.Style1;
    public CharacterBodySize BodySize { get; private set; } = CharacterBodySize.Normal;
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
