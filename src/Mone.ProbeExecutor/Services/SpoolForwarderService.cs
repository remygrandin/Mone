using System.Text.Json;
using Mone.Messaging.Messages;
using Mone.ProbeExecutor.Data;
using NATS.Client.JetStream;

namespace Mone.ProbeExecutor.Services;

/// <summary>
/// Drains the local result spool to NATS. Each row is deleted only after a confirmed publish
/// (store-and-forward), so a result is never lost and is removed locally only once the console has
/// it. A failed publish stops the batch — NATS is still down — and is retried on the next tick.
/// </summary>
public sealed class SpoolForwarderService(
    INatsJSContext jetStream,
    SpoolStore spoolStore,
    ILogger<SpoolForwarderService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Spool drain failed; will retry on next tick");
            }
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        var batch = await spoolStore.PeekBatchAsync(BatchSize, ct);
        if (batch.Count == 0)
            return;

        logger.LogInformation("Draining {Count} spooled probe result(s) to NATS", batch.Count);

        var forwarded = 0;
        foreach (var row in batch)
        {
            ProbeResultMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<ProbeResultMessage>(row.PayloadJson);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Spool row {Id} has corrupt payload; deleting it", row.Id);
                await spoolStore.DeleteResultAsync(row.Id, ct);
                continue;
            }

            if (message is null)
            {
                await spoolStore.DeleteResultAsync(row.Id, ct);
                continue;
            }

            try
            {
                var ack = await jetStream.PublishAsync(row.Subject, message, cancellationToken: ct);
                ack.EnsureSuccess();
            }
            catch (Exception ex)
            {
                // NATS still unreachable: leave this row (and the rest of the batch) for the next tick.
                await spoolStore.IncrementAttemptsAsync(row.Id, ct);
                logger.LogWarning(ex, "Could not forward spooled result {Id}; stopping drain until next tick", row.Id);
                break;
            }

            await spoolStore.DeleteResultAsync(row.Id, ct);
            forwarded++;
        }

        if (forwarded > 0)
            logger.LogInformation("Forwarded {Forwarded} spooled result(s); {Remaining} remain",
                forwarded, await spoolStore.CountAsync(ct));
    }
}
