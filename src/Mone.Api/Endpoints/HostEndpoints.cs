using Microsoft.EntityFrameworkCore;
using Mone.Api.Authorization;
using Mone.Api.Models;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class HostEndpoints
{
    public static void MapHostEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts")
            .WithTags("Hosts")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Hosts);

        group.MapGet("/", async (string? tags, MoneDbContext db) =>
        {
            IQueryable<HostEntity> query = db.Hosts
                .Include(h => h.HostTags)
                .ThenInclude(ht => ht.Tag);

            if (!string.IsNullOrWhiteSpace(tags))
            {
                var tagNames = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var tagName in tagNames)
                {
                    query = query.Where(h => h.HostTags.Any(ht => ht.Tag.Name == tagName));
                }
            }

            var hosts = await query.OrderBy(h => h.Name).ToListAsync();
            return Results.Ok(hosts.Select(ToResponse));
        })
        .WithName("ListHosts")
        .WithSummary("List all hosts, optionally filtered by comma-separated tag names.")
        .Produces<IEnumerable<HostResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var host = await db.Hosts
                .Include(h => h.HostTags).ThenInclude(ht => ht.Tag)
                .Include(h => h.ProbeAssignments)
                .Include(h => h.CheckerAssignments)
                .FirstOrDefaultAsync(h => h.Id == id);

            return host is null
                ? Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(ToResponse(host));
        })
        .WithName("GetHost")
        .WithSummary("Get a single host by id, including its tags and assignments.")
        .Produces<HostResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateHostRequest request, MoneDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Name"] = new[] { "Name is required." } });

            if (await db.Hosts.AnyAsync(h => h.Name == request.Name))
                return Results.Problem($"Host '{request.Name}' already exists.", statusCode: StatusCodes.Status409Conflict);

            var now = DateTimeOffset.UtcNow;
            var host = new HostEntity
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Address = request.Address,
                Enabled = request.Enabled,
                CreatedAt = now,
                UpdatedAt = now
            };

            if (request.TagIds is { Length: > 0 })
            {
                foreach (var tagId in request.TagIds)
                    host.HostTags.Add(new HostTagEntity { HostId = host.Id, TagId = tagId });
            }

            db.Hosts.Add(host);
            await db.SaveChangesAsync();

            await db.Entry(host).Collection(h => h.HostTags).LoadAsync();
            foreach (var ht in host.HostTags)
                await db.Entry(ht).Reference(x => x.Tag).LoadAsync();

            return Results.Created($"/api/hosts/{host.Id}", ToResponse(host));
        })
        .WithName("CreateHost")
        .WithSummary("Create a new host. Returns 409 if a host with the same name already exists.")
        .Produces<HostResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", async (Guid id, UpdateHostRequest request, MoneDbContext db) =>
        {
            var host = await db.Hosts
                .Include(h => h.HostTags).ThenInclude(ht => ht.Tag)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (host is null) return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            if (request.Name != host.Name && await db.Hosts.AnyAsync(h => h.Name == request.Name && h.Id != id))
                return Results.Problem($"Host '{request.Name}' already exists.", statusCode: StatusCodes.Status409Conflict);

            host.Name = request.Name;
            host.Address = request.Address;
            host.Enabled = request.Enabled;
            host.UpdatedAt = DateTimeOffset.UtcNow;

            if (request.TagIds is not null)
            {
                host.HostTags.Clear();
                foreach (var tagId in request.TagIds)
                    host.HostTags.Add(new HostTagEntity { HostId = host.Id, TagId = tagId });
            }

            await db.SaveChangesAsync();

            foreach (var ht in host.HostTags)
                await db.Entry(ht).Reference(x => x.Tag).LoadAsync();

            return Results.Ok(ToResponse(host));
        })
        .WithName("UpdateHost")
        .WithSummary("Update an existing host. Returns 404 if not found, 409 on a name collision.")
        .Produces<HostResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var host = await db.Hosts.FindAsync(id);
            if (host is null) return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            db.Hosts.Remove(host);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("DeleteHost")
        .WithSummary("Delete a host by id. Returns 404 if the host does not exist.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/error-policy", async (Guid id, MoneDbContext db) =>
        {
            var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == id);
            return host is null
                ? Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(new HostErrorPolicyResponse(host.Id, host.ErrorPolicyThreshold));
        })
        .WithName("GetHostErrorPolicy")
        .WithSummary("Get a host's N-of-M error-policy threshold. Returns 404 if the host does not exist.")
        .Produces<HostErrorPolicyResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/error-policy", async (Guid id, UpdateHostErrorPolicyRequest request, MoneDbContext db) =>
        {
            var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == id);
            if (host is null) return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            if (request.ErrorThreshold < 1)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ErrorThreshold"] = new[] { "ErrorThreshold must be at least 1." }
                });

            host.ErrorPolicyThreshold = request.ErrorThreshold;
            host.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new HostErrorPolicyResponse(host.Id, host.ErrorPolicyThreshold));
        })
        .WithName("UpdateHostErrorPolicy")
        .WithSummary("Set a host's N-of-M error-policy threshold (N). Returns 404 if the host does not exist, 400 if ErrorThreshold is below 1.")
        .Produces<HostErrorPolicyResponse>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static HostResponse ToResponse(HostEntity host) => new(
        host.Id,
        host.Name,
        host.Address,
        host.Enabled,
        host.CreatedAt,
        host.UpdatedAt,
        host.HostTags.Select(ht => new TagResponse(ht.Tag.Id, ht.Tag.Name, 0)).ToArray());
}
