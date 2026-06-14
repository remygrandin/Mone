using Microsoft.AspNetCore.Mvc;
using Mone.Api.Models;
using Mone.Api.Services;

namespace Mone.Api.Endpoints;

public static class LdapAuthEndpoints
{
    public static IEndpointRouteBuilder MapLdapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth/ldap").WithTags("Auth");

        group.MapPost("/login", async (
            [FromBody] LdapLoginRequest request,
            LdapAuthService ldapAuthService,
            ExternalAuthProvisioningService provisioningService,
            JwtTokenService jwtTokenService,
            ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["credentials"] = ["Username and password are required"]
                });

            LdapAuthResult ldapResult;
            try
            {
                ldapResult = await ldapAuthService.AuthenticateAsync(request.Username, request.Password);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LDAP authentication error for Username={Username}", request.Username);
                return Results.Problem("Authentication service unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!ldapResult.Success || string.IsNullOrEmpty(ldapResult.Email))
            {
                return Results.Problem("Invalid credentials", statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await provisioningService.FindOrCreateExternalUserAsync(
                ldapResult.Email,
                "ldap",
                ldapResult.DistinguishedName ?? request.Username,
                ldapResult.DisplayName);

            var token = jwtTokenService.GenerateToken(user);
            return Results.Ok(token);
        })
        .WithName("LdapLogin")
        .WithSummary("Authenticate against the configured LDAP directory and receive a JWT token. Returns 401 on invalid credentials.")
        .Produces<TokenResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .AllowAnonymous();

        return routes;
    }
}
