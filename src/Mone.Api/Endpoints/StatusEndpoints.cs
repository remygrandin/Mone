using Microsoft.EntityFrameworkCore;
using Mone.Api.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;

using Mone.Api.Authorization;
using Mone.Contracts.Models;
using Mone.Infrastructure.Services;
using Mone.Messaging.Messages;
using NATS.Client.JetStream;

namespace Mone.Api.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hosts/{hostId:guid}/status")
            .WithTags("Status")
            .RequireAuthorization()
            .RequirePermission(PermissionResource.Monitoring);

        group.MapGet("/latest", async (Guid hostId, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var checkerIds = await db.StatusHistory
                .Where(s => s.TargetId == hostId)
                .Select(s => s.CheckerId)
                .Distinct()
                .ToListAsync();

            var latest = new List<StatusResponse>();
            foreach (var checkerId in checkerIds)
            {
                var entry = await db.StatusHistory
                    .Where(s => s.TargetId == hostId && s.CheckerId == checkerId)
                    .OrderByDescending(s => s.Timestamp)
                    .Select(s => new StatusResponse(s.Timestamp, s.TargetId, s.CheckerId, s.PreviousStatus, s.CurrentStatus))
                    .FirstAsync();
                latest.Add(entry);
            }

            return Results.Ok(latest);
        })
        .WithName("GetLatestStatus")
        .WithSummary("Get the latest status per checker for a host. Returns 404 if the host does not exist.")
        .Produces<IEnumerable<StatusResponse>>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/history", async (Guid hostId, UtcQueryTime? from, UtcQueryTime? to, int? limit, MoneDbContext db) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            IQueryable<Infrastructure.Data.Entities.StatusHistoryEntity> query = db.StatusHistory
                .Where(s => s.TargetId == hostId);

            if (from.HasValue) query = query.Where(s => s.Timestamp >= from.Value.Utc);
            if (to.HasValue) query = query.Where(s => s.Timestamp <= to.Value.Utc);

            var history = await query
                .OrderByDescending(s => s.Timestamp)
                .Take(Math.Clamp(limit ?? 1000, 1, 5000))
                .Select(s => new StatusResponse(s.Timestamp, s.TargetId, s.CheckerId, s.PreviousStatus, s.CurrentStatus))
                .ToListAsync();

            return Results.Ok(history);
        })
        .WithName("GetStatusHistory")
        .WithSummary("Get status-transition history for a host within an optional time range. Newest-first, bounded by the limit query param (default 1000, max 5000 rows). Returns 404 if the host does not exist.")
        .Produces<IEnumerable<StatusResponse>>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/rollup", async (Guid hostId, MoneDbContext db) =>
        {
            var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == hostId);
            if (host is null)
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var checkerIds = await db.StatusHistory
                .Where(s => s.TargetId == hostId)
                .Select(s => s.CheckerId)
                .Distinct()
                .ToListAsync();

            var latestStatuses = new List<MonitoringStatus>();
            foreach (var checkerId in checkerIds)
            {
                var current = await db.StatusHistory
                    .Where(s => s.TargetId == hostId && s.CheckerId == checkerId)
                    .OrderByDescending(s => s.Timestamp)
                    .Select(s => s.CurrentStatus)
                    .FirstAsync();
                latestStatuses.Add(current);
            }

            var rollup = HostStatusRollup.ComputeResult(latestStatuses, host.ErrorPolicyThreshold);

            return Results.Ok(new HostStatusRollupResponse(
                hostId,
                rollup.Status,
                host.ErrorPolicyThreshold,
                rollup.CheckerCount,
                rollup.ErrorCount,
                rollup.WarningCount));
        })
        .WithName("GetStatusRollup")
        .WithSummary("Get the server-authoritative N-of-M rollup of a host's latest per-checker statuses, including the error threshold and the checker/error/warning counts that explain the verdict. Returns 404 if the host does not exist.")
        .Produces<HostStatusRollupResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/force", async (
            Guid hostId,
            ForceStatusRequest request,
            MoneDbContext db,
            InheritanceResolver resolver,
            INatsJSContext jetStream,
            ILogger<Program> logger) =>
        {
            var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == hostId);
            if (host is null)
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            if (!Enum.IsDefined(request.Status))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = new[] { "Invalid target status." }
                });

            var checkers = await resolver.GetEffectiveCheckerAssignmentsAsync(hostId);
            var checker = checkers.FirstOrDefault(c => c.AssignmentId == request.AssignmentId);
            if (checker is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["assignmentId"] = new[] { $"No active assignment with ID '{request.AssignmentId}' on this host." }
                });

            var assignmentIdStr = request.AssignmentId.ToString();
            var previousStatus = await db.StatusHistory
                .Where(s => s.TargetId == hostId && s.CheckerId == assignmentIdStr)
                .OrderByDescending(s => s.Timestamp)
                .Select(s => (MonitoringStatus?)s.CurrentStatus)
                .FirstOrDefaultAsync() ?? MonitoringStatus.Unknown;

            var now = DateTimeOffset.UtcNow;
            var change = new StatusChange(
                hostId.ToString(),
                previousStatus,
                request.Status,
                new ProbeResult(request.Status, "Manually forced via Force Status", now, TimeSpan.Zero),
                now);

            try
            {
                await jetStream.PublishAsync(
                    $"status.changes.{checker.CheckerPluginId}.{hostId}",
                    new StatusChangeMessage(checker.CheckerPluginId, change));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish forced status change to NATS for {AssignmentId}/{HostId}",
                    request.AssignmentId, hostId);
            }

            try
            {
                db.StatusHistory.Add(new StatusHistoryEntity
                {
                    Timestamp = now,
                    TargetId = hostId,
                    CheckerId = assignmentIdStr,
                    PreviousStatus = previousStatus,
                    CurrentStatus = request.Status
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist forced status change for {AssignmentId}/{HostId}",
                    request.AssignmentId, hostId);
            }

            logger.LogInformation("Force-status injected for {AssignmentId} on host {HostId}: {Status}",
                request.AssignmentId, hostId, request.Status);

            return Results.Accepted(value: new ForceStatusResponse("forced", checker.CheckerPluginId, hostId));
        })
        .WithName("ForceStatus")
        .WithSummary("Force a one-shot status for a checker on a host: publishes a real StatusChange and persists a StatusHistory row. 202 on publish, 404 if host missing, 400 if status invalid or checker not assigned.")
        .Produces<ForceStatusResponse>(StatusCodes.Status202Accepted)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
