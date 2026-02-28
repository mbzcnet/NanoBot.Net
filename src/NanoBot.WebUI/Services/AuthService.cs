using Microsoft.Extensions.Configuration;

namespace NanoBot.WebUI.Services;

public class AuthService : IAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IConfiguration configuration, ILogger<AuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task<bool> ValidateTokenAsync(string token)
    {
        var configuredToken = _configuration["WebUI:Auth:Token"];
        
        if (string.IsNullOrEmpty(configuredToken))
        {
            _logger.LogWarning("No token configured in WebUI:Auth:Token");
            return Task.FromResult(false);
        }

        return Task.FromResult(token == configuredToken);
    }

    public Task<bool> ValidatePasswordAsync(string password)
    {
        var configuredPassword = _configuration["WebUI:Auth:Password"];
        
        if (string.IsNullOrEmpty(configuredPassword))
        {
            _logger.LogWarning("No password configured in WebUI:Auth:Password");
            return Task.FromResult(false);
        }

        return Task.FromResult(password == configuredPassword);
    }

    public bool IsLocalhost(string remoteIpAddress)
    {
        return remoteIpAddress == "127.0.0.1" || 
               remoteIpAddress == "::1" || 
               remoteIpAddress == "localhost";
    }
}
