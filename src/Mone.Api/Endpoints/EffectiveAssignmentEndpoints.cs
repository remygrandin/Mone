using Microsoft.EntityFrameworkCore;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;

using Mone.Api.Authorization;
using Mone.Api.Models;
using Mone.Contracts.Models;

namespace Mone.Api.Endpoints;

public static class EffectiveAssignmentEndpoints
{
    public static void MapEffectiveAssignmentEndpoints(this WebApplication app)
    {
        app.MapGet("/api/hosts/{hostId:guid}/effective-assignments", async (Guid hostId, MoneDbContext db, InheritanceResolver resolver) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var probes = await resolver.GetEffectiveProbeAssignmentsAsync(hostId);
            var checkers = await resolver.GetEffectiveCheckerAssignmentsAsync(hostId);

            return Results.Ok(new EffectiveAssignmentsResponse(probes, checkers));
        })
        .WithTags("Assignments")
        .WithName("GetEffectiveAssignments")
        .WithSummary("Get the resolved effective probe and checker assignments for a host, including inherited group assignments and overrides. Returns 404 if the host does not exist.")
        .Produces<EffectiveAssignmentsResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization().RequirePermission(PermissionResource.Monitoring);
    }
}
