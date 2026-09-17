using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace Movies.Api.Sdk.Consumer;

public class AuthTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _tokenUrl;
    private string _cachedToken = string.Empty;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public AuthTokenProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        var identityApiUrl = configuration["IDENTITY_API_URL"] ?? "https://localhost:5003";
        _tokenUrl = $"{identityApiUrl.TrimEnd('/')}/token";
    }

    public async Task<string> GetTokenAsync()
    {
        if (!string.IsNullOrEmpty(_cachedToken))
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(_cachedToken);
            if (jwt.ValidTo > DateTime.UtcNow)
            {
                return _cachedToken;
            }
        }

        await Lock.WaitAsync();
        var response = await _httpClient.PostAsJsonAsync(_tokenUrl, new
        {
            userid = "d8566de3-b1a6-4a9b-b842-8e3887a82e41",
            email = "tewe-diary@hammayo.co.uk",
            customClaims = new Dictionary<string, object>
            {
                { "admin", true },
                { "trusted_member", true }
            }
        });
        var newToken = await response.Content.ReadAsStringAsync();
        _cachedToken = newToken;
        Lock.Release();
        return newToken;
    }
}


