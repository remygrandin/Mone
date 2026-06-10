using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Messaging;
using Mone.Messaging.Messages;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Npgsql;

namespace Mone.Api.Services;

/// <summary>
/// Console-side sink for probe results. Remote (and co-located) executors no longer write Postgres
/// directly — they publish to NATS, and this hosted service consumes <c>probes.results.&gt;</c> and
/// persists each result. Forwarded spool items can re-deliver, so the insert is idempotent on the
/// composite PK (Timestamp, TargetId, ProbeId): duplicates are ignored.
/// </summary>
public sealed class ProbeResultPersistenceService(
    INatsJSContext jetStream,
    IServiceScopeFactory scopeFactory,
    ILogger<ProbeResultPersistenceService> logger) : BackgroundService
{
    private const string Durable = "probe-persister";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ProbeResultPersistenceService starting, creating durable consumer {Durable} on {Stream}",
            Durable, MoneStreams.ProbeResults.StreamName);

        var consumerConfig = new ConsumerConfig
        {
            Name = Durable,
            DurableName = Durable,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = MoneStreams.ProbeResults.SubjectPrefix
        };

        var consumer = await jetStream.CreateOrUpdateConsumerAsync(
            MoneStreams.ProbeResults.StreamName, consumerConfig, stoppingToken);

        logger.LogInformation("ProbeResultPersistenceService consumer ready, beginning message loop");

        await foreach (var msg in consumer.ConsumeAsync<ProbeResultMessage>(cancellationToken: stoppingToken))
        {
            if (msg.Data is null)
            {
                await msg.AckAsync(cancellationToken: stoppingToken);
                continue;
            }

            try
            {
                await PersistAsync(msg.Data, stoppingToken);
                await msg.AckAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist probe result for {TargetId}/{ProbeId}; will redeliver",
                    msg.Data.TargetId, msg.Data.ProbeId);
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }

    private async Task PersistAsync(ProbeResultMessage message, CancellationToken ct)
    {
        if (!Guid.TryParse(message.TargetId, out var targetGuid))
        {
            logger.LogWarning("Discarding probe result with non-GUID TargetId {TargetId}", message.TargetId);
            return;
        }

        var result = message.Result;
        var entity = new ProbeResultEntity
        {
            Timestamp = result.Timestamp,
            TargetId = targetGuid,
            ProbeId = message.ProbeId,
            Status = result.Status,
            Summary = result.Summary,
            DurationMs = result.Duration.TotalMilliseconds,
            MetadataJson = result.Metadata is not null ? JsonSerializer.Serialize(result.Metadata) : null
        };

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
        db.ProbeResults.Add(entity);

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogDebug("Persisted probe result for {TargetId}/{ProbeId} at {Timestamp}",
                message.TargetId, message.ProbeId, result.Timestamp);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Idempotent: a forwarded spool item re-delivered an already-persisted result. Safe to drop.
            logger.LogDebug("Duplicate probe result for {TargetId}/{ProbeId} at {Timestamp} ignored",
                message.TargetId, message.ProbeId, result.Timestamp);
        }
    }
}
