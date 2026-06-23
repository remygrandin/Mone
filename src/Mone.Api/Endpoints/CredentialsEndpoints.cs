using Microsoft.EntityFrameworkCore;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class CredentialsEndpoints
{
    public record CredentialsResponse(Guid Id, string Name, string Username, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    public record CreateCredentialsRequest(string Name, string Username, string Password);
    public record UpdateCredentialsRequest(string? Name, string? Username, string? Password);

    public static void MapCredentialsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/credentials")
            .WithTags("Credentials")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Plugins);

        group.MapGet("/", ListCredentials)
            .WithName("ListCredentials")
            .WithSummary("List all stored credentials.")
            .Produces<IEnumerable<CredentialsResponse>>();

        group.MapPost("/", CreateCredentials)
            .WithName("CreateCredentials")
            .WithSummary("Create a new credentials entry.")
            .Produces<CredentialsResponse>(StatusCodes.Status201Created);

        group.MapGet("/{id}", GetCredentials)
            .WithName("GetCredentials")
            .WithSummary("Get a specific credentials entry.")
            .Produces<CredentialsResponse>();

        group.MapPut("/{id}", UpdateCredentials)
            .WithName("UpdateCredentials")
            .WithSummary("Update a credentials entry.")
            .Produces<CredentialsResponse>();

        group.MapDelete("/{id}", DeleteCredentials)
            .WithName("DeleteCredentials")
            .WithSummary("Delete a credentials entry.")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> ListCredentials(MoneDbContext db)
    {
        var credentials = await db.Credentials
            .OrderBy(c => c.Name)
            .Select(c => new CredentialsResponse(c.Id, c.Name, c.Username, c.CreatedAt, c.UpdatedAt))
            .ToListAsync();

        return Results.Ok(credentials);
    }

    private static async Task<IResult> CreateCredentials(CreateCredentialsRequest request, MoneDbContext db)
    {
        var existing = await db.Credentials.FirstOrDefaultAsync(c => c.Name == request.Name);
        if (existing is not null)
            return Results.BadRequest(new { error = $"Credentials with name '{request.Name}' already exists" });

        var now = DateTimeOffset.UtcNow;
        var credentials = new CredentialsEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Username = request.Username,
            Password = request.Password,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Credentials.Add(credentials);
        await db.SaveChangesAsync();

        return Results.Created($"/api/credentials/{credentials.Id}",
            new CredentialsResponse(credentials.Id, credentials.Name, credentials.Username, credentials.CreatedAt, credentials.UpdatedAt));
    }

    private static async Task<IResult> GetCredentials(Guid id, MoneDbContext db)
    {
        var credentials = await db.Credentials.FirstOrDefaultAsync(c => c.Id == id);
        if (credentials is null)
            return Results.NotFound();

        return Results.Ok(new CredentialsResponse(credentials.Id, credentials.Name, credentials.Username, credentials.CreatedAt, credentials.UpdatedAt));
    }

    private static async Task<IResult> UpdateCredentials(Guid id, UpdateCredentialsRequest request, MoneDbContext db)
    {
        var credentials = await db.Credentials.FirstOrDefaultAsync(c => c.Id == id);
        if (credentials is null)
            return Results.NotFound();

        if (!string.IsNullOrEmpty(request.Name) && request.Name != credentials.Name)
        {
            var existing = await db.Credentials.FirstOrDefaultAsync(c => c.Name == request.Name);
            if (existing is not null)
                return Results.BadRequest(new { error = $"Credentials with name '{request.Name}' already exists" });

            credentials.Name = request.Name;
        }

        if (!string.IsNullOrEmpty(request.Username))
            credentials.Username = request.Username;

        if (!string.IsNullOrEmpty(request.Password))
            credentials.Password = request.Password;

        credentials.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        return Results.Ok(new CredentialsResponse(credentials.Id, credentials.Name, credentials.Username, credentials.CreatedAt, credentials.UpdatedAt));
    }

    private static async Task<IResult> DeleteCredentials(Guid id, MoneDbContext db)
    {
        var credentials = await db.Credentials.FirstOrDefaultAsync(c => c.Id == id);
        if (credentials is null)
            return Results.NotFound();

        db.Credentials.Remove(credentials);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }
}
