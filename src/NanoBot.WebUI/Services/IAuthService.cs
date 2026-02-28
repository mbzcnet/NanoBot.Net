namespace NanoBot.WebUI.Services;

public interface IAuthService
{
    Task<bool> ValidateTokenAsync(string token);
    Task<bool> ValidatePasswordAsync(string password);
    bool IsLocalhost(string remoteIpAddress);
}
