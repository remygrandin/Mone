using Microsoft.EntityFrameworkCore;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;

namespace Mone.Api.Endpoints;

public static class EffectiveAssignmentEndpoints
{
    public static void MapEffectiveAssignmentEndpoints(this WebApplication app)
    {
        app.MapGet("/api/hosts/{hostId:guid}/effective-assignments", async (Guid hostId, MoneDbContext db, InheritanceResolver resolver) =>
        {
            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                return Results.NotFound();

            var probes = await resolver.GetEffectiveProbeAssignmentsAsync(hostId);
            var checkers = await resolver.GetEffectiveCheckerAssignmentsAsync(hostId);

            return Results.Ok(new { probes, checkers });
        }).RequireAuthorization();
    }
}
