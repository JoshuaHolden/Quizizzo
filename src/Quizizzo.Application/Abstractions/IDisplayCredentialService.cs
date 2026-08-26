namespace Quizizzo.Application.Abstractions;

public interface IDisplayCredentialService
{
    string GenerateSessionToken();
    string HashSessionToken(string sessionToken);
    string GeneratePairingCode();
}
