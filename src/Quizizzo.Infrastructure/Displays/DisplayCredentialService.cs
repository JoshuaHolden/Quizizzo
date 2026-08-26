using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Parties;

namespace Quizizzo.Infrastructure.Displays;

public sealed class DisplayCredentialService : IDisplayCredentialService
{
    public string GenerateSessionToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public string HashSessionToken(string sessionToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken)));
    }

    public string GeneratePairingCode()
    {
        Span<char> characters = stackalloc char[8];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = RoomCode.Alphabet[RandomNumberGenerator.GetInt32(RoomCode.Alphabet.Length)];
        }

        return new string(characters);
    }
}
