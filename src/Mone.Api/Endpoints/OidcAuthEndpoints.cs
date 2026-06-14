using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Mone.Api.Services;

namespace Mone.Api.Endpoints;

public static class OidcAuthEndpoints
{
    public static IEndpointRouteBuilder MapOidcAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth/oidc").WithTags("Auth");

        group.MapGet("/login", (HttpContext context) =>
        {
            var properties = new AuthenticationProperties
            {
                RedirectUri = "/api/auth/oidc/callback"
            };
            return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
        })
        .WithName("OidcLogin")
        .WithSummary("Begin the OIDC login flow by challenging the configured external identity provider.")
        .AllowAnonymous();

        group.MapGet("/callback", async (
            HttpContext context,
            ExternalAuthProvisioningService provisioningService,
            JwtTokenService jwtTokenService,
            ILogger<Program> logger) =>
        {
            var result = await context.AuthenticateAsync(OpenIdConnectDefaults.AuthenticationScheme);
            if (!result.Succeeded || result.Principal is null)
            {
                logger.LogWarning("OIDC callback failed: {Error}", result.Failure?.Message ?? "No principal");
                return Results.Problem("OIDC authentication failed", statusCode: StatusCodes.Status401Unauthorized);
            }

            var email = result.Principal.FindFirstValue(ClaimTypes.Email)
                        ?? result.Principal.FindFirstValue("email");
            var name = result.Principal.FindFirstValue(ClaimTypes.Name)
                       ?? result.Principal.FindFirstValue("name");
            var sub = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? result.Principal.FindFirstValue("sub");

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(sub))
            {
                logger.LogWarning("OIDC callback missing claims: email={Email}, sub={Sub}", email, sub);
                return Results.Problem("Missing required claims (email, sub)", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var user = await provisioningService.FindOrCreateExternalUserAsync(email, "oidc", sub, name);
                var token = jwtTokenService.GenerateToken(user);

                logger.LogInformation("OIDC callback success: UserId={UserId}, Email={Email}, Provider=oidc", user.Id, email);

                return Results.Redirect($"/login?token={Uri.EscapeDataString(token.Token)}&expires={Uri.EscapeDataString(token.Expires.ToString("O"))}");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OIDC provisioning failed for Email={Email}", email);
                return Results.Problem("User provisioning failed", statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithName("OidcCallback")
        .WithSummary("Handle the OIDC provider redirect, provision or match the user, and redirect to the app with a token.")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .AllowAnonymous();

        return routes;
    }
}
