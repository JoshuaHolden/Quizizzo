using System.Security.Cryptography;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Parties;

namespace Quizizzo.Infrastructure.Parties;

public sealed class CryptographicRoomCodeGenerator : IRoomCodeGenerator
{
    public RoomCode Generate()
    {
        Span<char> characters = stackalloc char[RoomCode.Length];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = RoomCode.Alphabet[RandomNumberGenerator.GetInt32(RoomCode.Alphabet.Length)];
        }

        return RoomCode.Parse(new string(characters));
    }
}
