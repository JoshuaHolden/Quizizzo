namespace Quizizzo.Application.Abstractions;

public interface IPlayerCredentialService
{
    string GenerateSessionToken();
    string HashSessionToken(string sessionToken);
}
