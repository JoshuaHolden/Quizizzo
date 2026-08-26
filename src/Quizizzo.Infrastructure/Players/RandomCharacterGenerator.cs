using System.Security.Cryptography;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Players;

namespace Quizizzo.Infrastructure.Players;

public sealed class RandomCharacterGenerator : ICharacterGenerator
{
    private static readonly string[] Colours =
    [
        "#FF4D6D", "#FF9F1C", "#FFD60A", "#2EC4B6",
        "#00B4D8", "#4361EE", "#8338EC", "#F15BB5"
    ];

    public CharacterDefinition Generate() => new(
        Pick<CharacterBodyType>(),
        Colours[RandomNumberGenerator.GetInt32(Colours.Length)],
        Pick<CharacterEyes>(),
        Pick<CharacterMouth>(),
        Pick<CharacterAccessory>());

    private static T Pick<T>() where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return values[RandomNumberGenerator.GetInt32(values.Length)];
    }
}
