using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Quizizzo.Application.Abstractions;

namespace Quizizzo.Infrastructure.Players;

public sealed class PlayerCredentialService : IPlayerCredentialService
{
    public string GenerateSessionToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public string HashSessionToken(string sessionToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken)));
    }
}
