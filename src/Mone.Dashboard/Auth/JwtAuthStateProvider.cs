using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Mone.Dashboard.Services;

namespace Mone.Dashboard.Auth;

public sealed class JwtAuthStateProvider : AuthenticationStateProvider
{
    private readonly AuthService _authService;

    public JwtAuthStateProvider(AuthService authService)
    {
        _authService = authService;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _authService.GetTokenAsync();
        if (string.IsNullOrEmpty(token))
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        var principal = AuthService.ParseToken(token);
        return new AuthenticationState(principal);
    }

    public void NotifyAuthStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
