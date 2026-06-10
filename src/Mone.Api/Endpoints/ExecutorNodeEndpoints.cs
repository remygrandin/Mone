using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

namespace Mone.Api.Endpoints;

public static class ExecutorNodeEndpoints
{
    private const int HeartbeatIntervalSeconds = 30;
    private const int StaleAfterSeconds = 90;
    private const int OfflineAfterSeconds = 300;

    public static void MapExecutorNodeEndpoints(this WebApplication app)
    {
        // Node-facing routes (unattended services): gated by optional shared secret, not user auth.
        var nodes = app.MapGroup("/api/executor-nodes");

        nodes.MapPost("/register", async (
            RegisterExecutorNodeRequest request,
            HttpContext http,
            MoneDbContext db,
            IConfiguration config) =>
        {
            if (!IsNodeTokenValid(http, config))
                return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;
            var node = await db.ExecutorNodes.FirstOrDefaultAsync(n => n.Id == request.Id);
            if (node is null)
            {
                node = new ExecutorNodeEntity
                {
                    Id = request.Id,
                    Name = request.Name,
                    Hostname = request.Hostname,
                    Address = request.Address,
                    Role = (ExecutorRole)request.Role,
                    Version = request.Version,
                    RegisteredAt = now,
                    LastHeartbeatAt = now
                };
                db.ExecutorNodes.Add(node);
            }
            else
            {
                node.Name = request.Name;
                node.Hostname = request.Hostname;
                node.Address = request.Address;
                node.Role = (ExecutorRole)request.Role;
                node.Version = request.Version;
                node.LastHeartbeatAt = now;
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { id = node.Id });
        });

        nodes.MapPost("/{id:guid}/heartbeat", async (
            Guid id,
            ExecutorNodeHeartbeatRequest request,
            HttpContext http,
            MoneDbContext db,
            IConfiguration config) =>
        {
            if (!IsNodeTokenValid(http, config))
                return Results.Unauthorized();

            var node = await db.ExecutorNodes.FirstOrDefaultAsync(n => n.Id == id);
            if (node is null) return Results.NotFound();

            node.LastHeartbeatAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.Version))
                node.Version = request.Version;

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Browser-facing routes: require user auth.
        var admin = app.MapGroup("/api/executor-nodes").RequireAuthorization();

        admin.MapGet("/", async (MoneDbContext db) =>
        {
            var now = DateTimeOffset.UtcNow;
            var entities = await db.ExecutorNodes.AsNoTracking().ToListAsync();
            var response = entities
                .OrderBy(n => n.Name)
                .Select(n => ToResponse(n, now))
                .ToList();
            return Results.Ok(response);
        });

        admin.MapPut("/{id:guid}", async (Guid id, RenameExecutorNodeRequest request, MoneDbContext db) =>
        {
            var node = await db.ExecutorNodes.FirstOrDefaultAsync(n => n.Id == id);
            if (node is null) return Results.NotFound();

            node.Name = request.Name;
            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(node, DateTimeOffset.UtcNow));
        });

        admin.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var node = await db.ExecutorNodes.FirstOrDefaultAsync(n => n.Id == id);
            if (node is null) return Results.NotFound();

            db.ExecutorNodes.Remove(node);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static bool IsNodeTokenValid(HttpContext http, IConfiguration config)
    {
        var expected = config["Mone:Node:Token"];
        if (string.IsNullOrEmpty(expected))
            return true; // No token configured → open (documented in deploy guide).

        var provided = http.Request.Headers["X-Node-Token"].FirstOrDefault();
        return string.Equals(provided, expected, StringComparison.Ordinal);
    }

    private static ExecutorNodeResponse ToResponse(ExecutorNodeEntity n, DateTimeOffset now)
    {
        var roles = new List<string>();
        if (n.Role.HasFlag(ExecutorRole.Probe)) roles.Add(nameof(ExecutorRole.Probe));
        if (n.Role.HasFlag(ExecutorRole.Checker)) roles.Add(nameof(ExecutorRole.Checker));

        return new ExecutorNodeResponse(
            n.Id, n.Name, n.Hostname, n.Address, roles.ToArray(), n.Version,
            n.LastHeartbeatAt, n.RegisteredAt,
            ComputeHealth(n.LastHeartbeatAt, now), HeartbeatIntervalSeconds);
    }

    private static string ComputeHealth(DateTimeOffset? lastHeartbeat, DateTimeOffset now)
    {
        if (lastHeartbeat is null) return "Offline";
        var age = (now - lastHeartbeat.Value).TotalSeconds;
        if (age <= StaleAfterSeconds) return "Online";
        if (age <= OfflineAfterSeconds) return "Stale";
        return "Offline";
    }
}
