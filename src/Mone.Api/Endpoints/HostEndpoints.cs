using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class HostEndpoints
{
    public static void MapHostEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts").RequireAuthorization();

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
        });

        group.MapGet("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var host = await db.Hosts
                .Include(h => h.HostTags).ThenInclude(ht => ht.Tag)
                .Include(h => h.ProbeAssignments)
                .Include(h => h.CheckerAssignments)
                .FirstOrDefaultAsync(h => h.Id == id);

            return host is null ? Results.NotFound() : Results.Ok(ToResponse(host));
        });

        group.MapPost("/", async (CreateHostRequest request, MoneDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("Name is required.");

            if (await db.Hosts.AnyAsync(h => h.Name == request.Name))
                return Results.Conflict($"Host '{request.Name}' already exists.");

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
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateHostRequest request, MoneDbContext db) =>
        {
            var host = await db.Hosts
                .Include(h => h.HostTags).ThenInclude(ht => ht.Tag)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (host is null) return Results.NotFound();

            if (request.Name != host.Name && await db.Hosts.AnyAsync(h => h.Name == request.Name && h.Id != id))
                return Results.Conflict($"Host '{request.Name}' already exists.");

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
        });

        group.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var host = await db.Hosts.FindAsync(id);
            if (host is null) return Results.NotFound();

            db.Hosts.Remove(host);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
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
