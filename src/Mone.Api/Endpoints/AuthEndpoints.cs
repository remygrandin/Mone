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
            return Results.Created($"/api/auth/me", new UserResponse(user.Id, user.Email!));
        })
        .WithName("Register")
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
        .Produces<TokenResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var email = principal.FindFirstValue(ClaimTypes.Email)!;
            return Results.Ok(new UserResponse(userId, email));
        })
        .WithName("Me")
        .RequireAuthorization()
        .Produces<UserResponse>();

        return routes;
    }
}
