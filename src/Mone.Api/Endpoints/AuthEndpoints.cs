using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Mone.Api.Models;
using Mone.Api.Services;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (
            [FromBody] RegisterRequest request,
            UserManager<UserEntity> userManager,
            ILogger<Program> logger) =>
        {
            var user = new UserEntity { UserName = request.Email, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                logger.LogWarning("User registration failed for {Email}: {Errors}",
                    request.Email, string.Join("; ", result.Errors.Select(e => e.Description)));
                return Results.ValidationProblem(
                    result.Errors.GroupBy(e => e.Code).ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.Description).ToArray()));
            }

            logger.LogInformation("User registered: {UserId}, Email={Email}", user.Id, user.Email);
            return Results.Created($"/api/auth/me", new UserResponse(user.Id, user.Email!, "System"));
        })
        .WithName("Register")
        .WithSummary("Register a new local user account with an email and password.")
        .Produces<UserResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            UserManager<UserEntity> userManager,
            JwtTokenService jwtTokenService,
            ILogger<Program> logger) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            {
                logger.LogWarning("Login failed for {Email}", request.Email);
                return Results.Problem("Invalid credentials", statusCode: StatusCodes.Status401Unauthorized);
            }

            var token = jwtTokenService.GenerateToken(user);
            logger.LogInformation("User logged in: {UserId}", user.Id);
            return Results.Ok(token);
        })
        .WithName("Login")
        .WithSummary("Authenticate with email and password and receive a JWT token. Returns 401 on invalid credentials.")
        .Produces<TokenResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", async (
            ClaimsPrincipal principal,
            UserManager<UserEntity> userManager) =>
        {
            var user = await userManager.FindByIdAsync(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new UserResponse(user.Id, user.Email!, user.ThemePreference ?? "System"));
        })
        .WithName("Me")
        .WithSummary("Get the currently authenticated user's id, email, and stored theme preference (defaults to System).")
        .RequireAuthorization()
        .Produces<UserResponse>();

        group.MapPut("/me/theme", async (
            [FromBody] UpdateThemeRequest request,
            UserManager<UserEntity> userManager,
            ClaimsPrincipal principal,
            ILogger<Program> logger) =>
        {
            if (request.Theme is not ("Light" or "Dark" or "System"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Theme"] = new[] { "Theme must be Light, Dark, or System." }
                });
            }

            var user = await userManager.FindByIdAsync(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            user.ThemePreference = request.Theme;
            await userManager.UpdateAsync(user);
            logger.LogInformation("Theme preference updated: {UserId}, Theme={Theme}", user.Id, request.Theme);
            return Results.NoContent();
        })
        .WithName("UpdateTheme")
        .WithSummary("Persist the authenticated user's Light/Dark/System theme preference.")
        .RequireAuthorization()
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem();

        return routes;
    }
}
