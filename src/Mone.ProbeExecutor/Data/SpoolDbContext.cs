using Microsoft.EntityFrameworkCore;

namespace Mone.ProbeExecutor.Data;

/// <summary>
/// Local SQLite store for a (possibly remote) probe executor: the last-known config snapshot and a
/// store-and-forward spool of results that could not be published while NATS was unreachable. This
/// is a disposable cache — it uses <c>EnsureCreated</c>, not EF migrations.
/// </summary>
public sealed class SpoolDbContext(DbContextOptions<SpoolDbContext> options) : DbContext(options)
{
    public DbSet<ConfigSnapshotRow> ConfigSnapshots => Set<ConfigSnapshotRow>();
    public DbSet<ResultSpoolRow> ResultSpool => Set<ResultSpoolRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConfigSnapshotRow>(e =>
        {
            e.ToTable("config_snapshot");
            e.HasKey(x => x.Id);
            e.Property(x => x.Json).IsRequired();
        });

        modelBuilder.Entity<ResultSpoolRow>(e =>
        {
            e.ToTable("result_spool");
            e.HasKey(x => x.Id);
            e.Property(x => x.Subject).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => x.EnqueuedAt);
        });
    }
}

/// <summary>Single-row (Id == 1) cache of the most recent probe-spec snapshot fetched from the API.</summary>
public sealed class ConfigSnapshotRow
{
    public int Id { get; set; }
    public required string Json { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>A probe result buffered locally because publishing to NATS failed.</summary>
public sealed class ResultSpoolRow
{
    public long Id { get; set; }
    public required string Subject { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public int Attempts { get; set; }
}
