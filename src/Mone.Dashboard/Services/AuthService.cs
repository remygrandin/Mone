using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Blazored.LocalStorage;
using Mone.Dashboard.Models;

namespace Mone.Dashboard.Services;

public sealed class AuthService
{
    private const string TokenKey = "authToken";
    private const string ExpiresKey = "authTokenExpires";

    private readonly HttpClient _http;
    private readonly ILocalStorageService _localStorage;

    private string? _cachedToken;
    private DateTime? _cachedExpires;

    public AuthService(HttpClient http, ILocalStorageService localStorage)
    {
        _http = http;
        _localStorage = localStorage;
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(email, password));
        if (!response.IsSuccessStatusCode)
            return false;

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (token is null)
            return false;

        await _localStorage.SetItemAsStringAsync(TokenKey, token.Token);
        await _localStorage.SetItemAsStringAsync(ExpiresKey, token.Expires.ToString("O"));
        _cachedToken = token.Token;
        _cachedExpires = token.Expires;
        return true;
    }

    public async Task<List<AuthProviderInfo>> GetProvidersAsync()
    {
        try
        {
            var providers = await _http.GetFromJsonAsync<List<AuthProviderInfo>>("api/auth/providers");
            return providers ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task StoreTokenAsync(string token, DateTime expires)
    {
        await _localStorage.SetItemAsStringAsync(TokenKey, token);
        await _localStorage.SetItemAsStringAsync(ExpiresKey, expires.ToString("O"));
        _cachedToken = token;
        _cachedExpires = expires;
    }

    public async Task LogoutAsync()
    {
        _cachedToken = null;
        _cachedExpires = null;
        await _localStorage.RemoveItemAsync(TokenKey);
        await _localStorage.RemoveItemAsync(ExpiresKey);
    }

    public async Task<string?> GetTokenAsync()
    {
        if (_cachedToken is not null && _cachedExpires.HasValue)
        {
            if (_cachedExpires.Value > DateTime.UtcNow)
                return _cachedToken;

            await LogoutAsync();
            return null;
        }

        var token = await _localStorage.GetItemAsStringAsync(TokenKey);
        if (string.IsNullOrEmpty(token))
            return null;

        var expiresStr = await _localStorage.GetItemAsStringAsync(ExpiresKey);
        if (string.IsNullOrEmpty(expiresStr) ||
            !DateTime.TryParse(expiresStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expires))
        {
            await LogoutAsync();
            return null;
        }

        if (expires <= DateTime.UtcNow)
        {
            await LogoutAsync();
            return null;
        }

        _cachedToken = token;
        _cachedExpires = expires;
        return token;
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        return await GetTokenAsync() is not null;
    }

    public static ClaimsPrincipal ParseToken(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return new ClaimsPrincipal(new ClaimsIdentity());

        var payload = parts[1];
        var remainder = payload.Length % 4;
        var padded = remainder switch
        {
            2 => payload + "==",
            3 => payload + "=",
            _ => payload,
        };
        var base64 = padded.Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(base64);
        var json = JsonDocument.Parse(bytes);

        var claims = new List<Claim>();
        foreach (var prop in json.RootElement.EnumerateObject())
        {
            claims.Add(new Claim(prop.Name, prop.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "jwt");
        return new ClaimsPrincipal(identity);
    }
}
