using Mone.Api.Models;

namespace Mone.Api.Endpoints;

public static class AuthProviderEndpoints
{
    public static IEndpointRouteBuilder MapAuthProviderEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/auth/providers", (IConfiguration config, ILogger<Program> logger) =>
        {
            var providers = new List<AuthProviderResponse>();

            if (config.GetValue<bool>("Oidc:Enabled"))
                providers.Add(new AuthProviderResponse("oidc", "SSO", "/api/auth/oidc/login"));

            if (config.GetValue<bool>("Ldap:Enabled"))
                providers.Add(new AuthProviderResponse("ldap", "LDAP", "/api/auth/ldap/login"));

            logger.LogInformation("Auth providers requested: {EnabledProviders}",
                providers.Count > 0 ? string.Join(", ", providers.Select(p => p.Name)) : "none");

            return Results.Ok(providers);
        })
        .WithName("GetAuthProviders")
        .WithTags("Auth")
        .Produces<List<AuthProviderResponse>>()
        .AllowAnonymous();

        return routes;
    }
}
