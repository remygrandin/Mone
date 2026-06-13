using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

using Mone.Api.Authorization;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class TagEndpoints
{
    public static void MapTagEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tags")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Tags);

        group.MapGet("/", async (MoneDbContext db) =>
        {
            var tags = await db.Tags
                .OrderBy(t => t.Name)
                .Select(t => new TagResponse(t.Id, t.Name, t.HostTags.Count))
                .ToListAsync();

            return Results.Ok(tags);
        });

        group.MapPost("/", async (CreateTagRequest request, MoneDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("Name is required.");

            if (await db.Tags.AnyAsync(t => t.Name == request.Name))
                return Results.Conflict($"Tag '{request.Name}' already exists.");

            var tag = new TagEntity
            {
                Id = Guid.NewGuid(),
                Name = request.Name
            };

            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            return Results.Created($"/api/tags/{tag.Id}", new TagResponse(tag.Id, tag.Name, 0));
        });

        group.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var tag = await db.Tags.FindAsync(id);
            if (tag is null) return Results.NotFound();

            db.Tags.Remove(tag);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
