using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using Numeris.Services.Api;

namespace Numeris.Services.Auth;

public sealed class GoogleOAuthClient
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HttpClient Http = new();
    private readonly HttpClient _http;

    public GoogleOAuthClient(HttpClient? httpClient = null) => _http = httpClient ?? Http;

    public static string BuildAuthUrl(string clientId, string redirectUri, string state, IReadOnlyList<string> scopes)
    {
        var scope = string.Join(" ", scopes.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.Ordinal));
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new InvalidOperationException("Google OAuth scope list is empty");
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("https://accounts.google.com/o/oauth2/v2/auth");
        sb.Append("?client_id=").Append(HttpUtility.UrlEncode(clientId));
        sb.Append("&redirect_uri=").Append(HttpUtility.UrlEncode(redirectUri));
        sb.Append("&response_type=code");
        sb.Append("&scope=").Append(HttpUtility.UrlEncode(scope));
        sb.Append("&access_type=offline&prompt=consent");
        sb.Append("&state=").Append(HttpUtility.UrlEncode(state));
        return sb.ToString();
    }

    public async Task<OAuthTokens> ExchangeCodeAsync(string clientId, string clientSecret, string code, string redirectUri)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        });
        using var response = await _http.PostAsync(TokenEndpoint, form).ConfigureAwait(false);
        return await ParseTokenResponseAsync(response).ConfigureAwait(false);
    }

    public async Task<string> RefreshAccessTokenAsync(string clientId, string clientSecret, string refreshToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        });
        using var response = await _http.PostAsync(TokenEndpoint, form).ConfigureAwait(false);
        var tokens = await ParseTokenResponseAsync(response).ConfigureAwait(false);
        return tokens.AccessToken;
    }

    private static async Task<OAuthTokens> ParseTokenResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("Google OAuth", "token", response.StatusCode, ApiErrorMessage.FromBody(body));
        }

        var parsed = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse Google token response");
        if (string.IsNullOrWhiteSpace(parsed.AccessToken))
        {
            throw new InvalidOperationException("Google token response did not contain an access token");
        }
        return new OAuthTokens
        {
            AccessToken = parsed.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(parsed.RefreshToken) ? null : parsed.RefreshToken,
        };
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }
}

public sealed class OAuthTokens
{
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
}
