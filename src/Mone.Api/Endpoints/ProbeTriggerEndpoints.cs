using Mone.Api.Models;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Services;
using Mone.Messaging;
using Mone.Messaging.Messages;
using Microsoft.EntityFrameworkCore;
using NATS.Client.JetStream;

using Mone.Api.Authorization;

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
                return Results.Problem("Host not found.", statusCode: StatusCodes.Status404NotFound);

            var plugin = pluginEngine.Registry.Get(request.ProbePluginId);
            if (plugin is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["probePluginId"] = new[] { $"Plugin '{request.ProbePluginId}' is not loaded." }
                });

            if (plugin.Metadata.ProbeMode == ProbeMode.Passive)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["probePluginId"] = new[] { "Passive probes cannot be triggered manually." }
                });

            var effectiveAssignments = await resolver.GetEffectiveProbeAssignmentsAsync(hostId);
            var assignment = effectiveAssignments.FirstOrDefault(a => a.ProbePluginId == request.ProbePluginId);

            if (assignment is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["probePluginId"] = new[] { $"No active assignment for '{request.ProbePluginId}' on this host." }
                });

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

            return Results.Accepted(value: new ProbeTriggerResponse("triggered", request.ProbePluginId, hostId));
        })
        .WithTags("Probes")
        .WithName("TriggerProbe")
        .WithSummary("Manually trigger an active probe on a host. Returns 202 on publish, 404 if the host is missing, or a validation problem if the plugin is not loaded, passive, or not assigned.")
        .Produces<ProbeTriggerResponse>(StatusCodes.Status202Accepted)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization().RequirePermission(PermissionResource.Assignments);
    }
}
