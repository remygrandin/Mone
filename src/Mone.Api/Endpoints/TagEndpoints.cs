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
            .WithTags("Tags")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Tags);

        group.MapGet("/", async (MoneDbContext db) =>
        {
            var tags = await db.Tags
                .OrderBy(t => t.Name)
                .Select(t => new TagResponse(t.Id, t.Name, t.HostTags.Count))
                .ToListAsync();

            return Results.Ok(tags);
        })
        .WithName("ListTags")
        .WithSummary("List all tags with their host usage counts.")
        .Produces<IEnumerable<TagResponse>>();

        group.MapPost("/", async (CreateTagRequest request, MoneDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Name"] = new[] { "Name is required." } });

            if (await db.Tags.AnyAsync(t => t.Name == request.Name))
                return Results.Problem($"Tag '{request.Name}' already exists.", statusCode: StatusCodes.Status409Conflict);

            var tag = new TagEntity
            {
                Id = Guid.NewGuid(),
                Name = request.Name
            };

            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            return Results.Created($"/api/tags/{tag.Id}", new TagResponse(tag.Id, tag.Name, 0));
        })
        .WithName("CreateTag")
        .WithSummary("Create a new tag. Returns 409 if a tag with the same name already exists.")
        .Produces<TagResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var tag = await db.Tags.FindAsync(id);
            if (tag is null) return Results.Problem("Tag not found.", statusCode: StatusCodes.Status404NotFound);

            db.Tags.Remove(tag);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("DeleteTag")
        .WithSummary("Delete a tag by id. Returns 404 if the tag does not exist.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
