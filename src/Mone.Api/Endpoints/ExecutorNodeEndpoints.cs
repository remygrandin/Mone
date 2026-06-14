using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;

using Mone.Api.Authorization;

namespace Mone.Api.Endpoints;

public static class ExecutorNodeEndpoints
{
    private const int HeartbeatIntervalSeconds = 30;
    private const int StaleAfterSeconds = 90;
    private const int OfflineAfterSeconds = 300;

    public static void MapExecutorNodeEndpoints(this WebApplication app)
    {
        // Node-facing routes (unattended services): gated by optional shared secret, not user auth.
        var nodes = app.MapGroup("/api/executor-nodes")
            .WithTags("Executor Nodes");

        nodes.MapPost("/register", async (
            RegisterExecutorNodeRequest request,
            HttpContext http,
            MoneDbContext db,
            IConfiguration config) =>
        {
            if (!IsNodeTokenValid(http, config))
                return Results.Problem("Invalid node token.", statusCode: StatusCodes.Status401Unauthorized);

            var now = DateTimeOffset.UtcNow;
            // Explicit Mone:Node:Address wins; otherwise advertise the source IP the API observed
            // the register call coming from. For genuinely remote executors that's their real
            // address; for co-located compose containers it's the bridge IP.
            var resolvedAddress = ResolveAddress(request.Address, http);
            var node = await db.ExecutorNodes.FirstOrDefaultAsync(n => n.Id == request.Id);
            if (node is null)
            {
                node = new ExecutorNodeEntity
                {
                    Id = request.Id,
                    Name = request.Name,
                    Hostname = request.Hostname,
                    Address = resolvedAddress,
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
                node.Address = resolvedAddress;
                node.Role = (ExecutorRole)request.Role;
                node.Version = request.Version;
                node.LastHeartbeatAt = now;
            }

            await db.SaveChangesAsync();
            return Results.Ok(new ExecutorNodeRegistrationResponse(node.Id));
        })
        .WithName("RegisterExecutorNode")
        .WithSummary("Register or update an executor node. Authenticated by the optional shared node token.")
        .Produces<ExecutorNodeRegistrationResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        nodes.MapPost("/{id:guid}/heartbeat", async (
            Guid id,
            ExecutorNodeHeartbeatRequest request,
            HttpContext http,
            MoneDbContext db,
            IConfiguration config) =>
        {
            if (!IsNodeTokenValid(http, config))
                return Results.Problem("Invalid node token.", statusCode: StatusCodes.Status401Unauthorized);

            var node = await db.ExecutorNodes.FirstOrDefaultAsync(n => n.Id == id);
            if (node is null) return Results.Problem("Executor node not found.", statusCode: StatusCodes.Status404NotFound);

            node.LastHeartbeatAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.Version))
                node.Version = request.Version;
            // Backfill the observed source IP for nodes that registered before address capture, or
            // before the API could see them. An explicitly-advertised address is never overwritten.
            if (string.IsNullOrWhiteSpace(node.Address))
                node.Address = ObservedIp(http);

            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("ExecutorNodeHeartbeat")
        .WithSummary("Record an executor node heartbeat, refreshing its last-seen time and version.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // Config pull for remote executors: returns fully-resolved probe specs (inheritance +
        // override + global config merge done here) so the executor needs neither the resolver
        // nor a database. Returns assignments that are unbound (run everywhere) or bound to {id}.
        nodes.MapGet("/{id:guid}/probe-assignments", async (
            Guid id,
            HttpContext http,
            MoneDbContext db,
            InheritanceResolver resolver,
            ILoggerFactory loggerFactory,
            IConfiguration config) =>
        {
            if (!IsNodeTokenValid(http, config))
                return Results.Problem("Invalid node token.", statusCode: StatusCodes.Status401Unauthorized);

            var logger = loggerFactory.CreateLogger("ExecutorNodeProbeAssignments");

            var nameByAssignment = await db.ProbeAssignments
                .AsNoTracking()
                .Select(a => new { a.Id, a.NameSnakeCase })
                .ToDictionaryAsync(a => a.Id, a => a.NameSnakeCase);

            var hosts = await db.Hosts.AsNoTracking().ToListAsync();
            var specs = new List<ProbeSpec>();

            foreach (var host in hosts)
            {
                var effective = await resolver.GetEffectiveProbeAssignmentsAsync(host.Id);
                foreach (var a in effective)
                {
                    if (!a.Enabled)
                        continue;
                    if (a.ExecutorNodeId is not null && a.ExecutorNodeId != id)
                        continue;

                    // Passive probes are included intentionally: the executor's scheduler skips them
                    // (results arrive via webhook, not the schedule), but the webhook handler needs
                    // their merged config (e.g. webhook_secret) from this same cached snapshot.
                    var merged = await ConfigMerger.BuildMergedConfigAsync(
                        db, a.ProbePluginId, a.ConfigJson, logger, http.RequestAborted);

                    nameByAssignment.TryGetValue(a.AssignmentId, out var nameSnake);

                    specs.Add(new ProbeSpec(
                        host.Id, host.Address, a.AssignmentId, a.ProbePluginId, a.ScheduleCron,
                        merged, a.TargetAddressOverride, nameSnake, a.ExecutorNodeId, a.Enabled));
                }
            }

            return Results.Ok(specs);
        })
        .WithName("GetExecutorNodeProbeAssignments")
        .WithSummary("Return fully-resolved probe specs an executor node should run (inheritance, override, and global config already merged).")
        .Produces<IEnumerable<ProbeSpec>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Browser-facing routes: require user auth.
        var admin = app.MapGroup("/api/executor-nodes")
            .WithTags("Executor Nodes")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.ExecutorNodes);

        admin.MapGet("/", async (MoneDbContext db) =>
        {
            var now = DateTimeOffset.UtcNow;
            var entities = await db.ExecutorNodes.AsNoTracking().ToListAsync();
            var response = entities
                .OrderBy(n => n.Name)
                .Select(n => ToResponse(n, now))
                .ToList();
            return Results.Ok(response);
        })
        .WithName("ListExecutorNodes")
        .WithSummary("List all registered executor nodes with computed health status.")
        .Produces<IEnumerable<ExecutorNodeResponse>>();

        admin.MapPut("/{id:guid}", async (Guid id, RenameExecutorNodeRequest request, MoneDbContext db) =>
        {
            var node = await db.ExecutorNodes.FirstOrDefaultAsync(n => n.Id == id);
            if (node is null) return Results.Problem("Executor node not found.", statusCode: StatusCodes.Status404NotFound);

            node.Name = request.Name;
            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(node, DateTimeOffset.UtcNow));
        })
        .WithName("RenameExecutorNode")
        .WithSummary("Rename an executor node. Returns 404 if the node does not exist.")
        .Produces<ExecutorNodeResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapDelete("/{id:guid}", async (Guid id, MoneDbContext db) =>
        {
            var node = await db.ExecutorNodes.FirstOrDefaultAsync(n => n.Id == id);
            if (node is null) return Results.Problem("Executor node not found.", statusCode: StatusCodes.Status404NotFound);

            db.ExecutorNodes.Remove(node);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("DeleteExecutorNode")
        .WithSummary("Delete an executor node by id. Returns 404 if the node does not exist.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // Explicit advertised address wins; otherwise the IP the connection arrived from.
    private static string? ResolveAddress(string? explicitAddress, HttpContext http) =>
        !string.IsNullOrWhiteSpace(explicitAddress) ? explicitAddress : ObservedIp(http);

    private static string? ObservedIp(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip is null) return null;
        // Normalize IPv4-mapped IPv6 (e.g. ::ffff:172.18.0.3) down to plain IPv4 for display.
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return ip.ToString();
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
