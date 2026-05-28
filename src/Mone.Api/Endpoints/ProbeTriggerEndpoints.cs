using Mone.Api.Models;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;
using Mone.Messaging;
using Mone.Messaging.Messages;
using Microsoft.EntityFrameworkCore;
using NATS.Client.JetStream;

namespace Mone.Api.Endpoints;

public static class ProbeTriggerEndpoints
{
    public static void MapProbeTriggerEndpoints(this WebApplication app)
    {
        app.MapPost("/api/hosts/{hostId:guid}/trigger-probe", async (
            Guid hostId,
            TriggerProbeRequest request,
            MoneDbContext db,
            InheritanceResolver resolver,
            PluginEngine.PluginEngine pluginEngine,
            INatsJSContext jetStream,
            ILogger<Program> logger) =>
        {
            var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == hostId);
            if (host is null)
                return Results.NotFound(new { error = "Host not found." });

            var plugin = pluginEngine.Registry.Get(request.ProbePluginId);
            if (plugin is null)
                return Results.BadRequest(new { error = $"Plugin '{request.ProbePluginId}' is not loaded." });

            if (plugin.Metadata.ProbeMode == ProbeMode.Passive)
                return Results.BadRequest(new { error = "Passive probes cannot be triggered manually." });

            var effectiveAssignments = await resolver.GetEffectiveProbeAssignmentsAsync(hostId);
            var assignment = effectiveAssignments.FirstOrDefault(a => a.ProbePluginId == request.ProbePluginId);

            if (assignment is null)
                return Results.BadRequest(new { error = $"No active assignment for '{request.ProbePluginId}' on this host." });

            var message = new ProbeTriggerMessage(
                hostId,
                request.ProbePluginId,
                assignment.AssignmentId,
                assignment.TargetAddressOverride,
                host.Address);

            var subject = $"probe.trigger.{hostId}";
            await jetStream.PublishAsync(subject, message);

            logger.LogInformation("Manual probe trigger published for {ProbePluginId} on host {HostId}",
                request.ProbePluginId, hostId);

            return Results.Accepted(value: new { status = "triggered", probePluginId = request.ProbePluginId, hostId });
        }).RequireAuthorization();
    }
}
