using Microsoft.EntityFrameworkCore;

namespace Mone.ProbeExecutor.Data;

/// <summary>
/// Owns all access to the local SQLite spool/config cache behind a single write lock. SQLite is a
/// single-writer store and this executor has several concurrent callers (scheduled jobs, the
/// webhook, the forwarder), so every operation is serialized. The DB is created on first use.
/// </summary>
public sealed class SpoolStore : IDisposable
{
    private readonly DbContextOptions<SpoolDbContext> _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<SpoolStore> _logger;
    private bool _initialized;

    public SpoolStore(IConfiguration configuration, ILogger<SpoolStore> logger)
    {
        _logger = logger;
        var path = configuration["Mone:Node:SpoolPath"] ?? "/app/data/spool.db";
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _options = new DbContextOptionsBuilder<SpoolDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
    }

    private async Task<SpoolDbContext> OpenAsync(CancellationToken ct)
    {
        var db = new SpoolDbContext(_options);
        if (!_initialized)
        {
            await db.Database.EnsureCreatedAsync(ct);
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
            _initialized = true;
        }
        return db;
    }

    public async Task SaveSnapshotAsync(string json, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.ConfigSnapshots.FirstOrDefaultAsync(x => x.Id == 1, ct);
            if (row is null)
            {
                db.ConfigSnapshots.Add(new ConfigSnapshotRow { Id = 1, Json = json, FetchedAt = DateTimeOffset.UtcNow });
            }
            else
            {
                row.Json = json;
                row.FetchedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<string?> LoadSnapshotAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await OpenAsync(ct);
            var row = await db.ConfigSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
            return row?.Json;
        }
        finally { _lock.Release(); }
    }

    public async Task EnqueueResultAsync(string subject, string payloadJson, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await OpenAsync(ct);
            db.ResultSpool.Add(new ResultSpoolRow
            {
                Subject = subject,
                PayloadJson = payloadJson,
                EnqueuedAt = DateTimeOffset.UtcNow,
                Attempts = 0
            });
            await db.SaveChangesAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<ResultSpoolRow>> PeekBatchAsync(int batchSize, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await OpenAsync(ct);
            return await db.ResultSpool.AsNoTracking()
                .OrderBy(x => x.Id)
                .Take(batchSize)
                .ToListAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteResultAsync(long id, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await OpenAsync(ct);
            await db.ResultSpool.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task IncrementAttemptsAsync(long id, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await OpenAsync(ct);
            await db.ResultSpool.Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Attempts, r => r.Attempts + 1), ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await OpenAsync(ct);
            return await db.ResultSpool.CountAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public void Dispose() => _lock.Dispose();
}
