using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.ProbeExecutor.Jobs;
using Xunit;

namespace Mone.ProbeExecutor.Tests;

public class ProbeExecutionJobTests : IDisposable
{
    private readonly MoneDbContext _db;

    public ProbeExecutionJobTests()
    {
        var options = new DbContextOptionsBuilder<MoneDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _db = new MoneDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task BuildMergedConfig_GlobalBaseWithAssignmentOverride()
    {
        var pluginId = "test-probe";
        var assignmentId = Guid.NewGuid();
        var hostId = await CreateHostAsync();

        _db.PluginGlobalConfigs.Add(new PluginGlobalConfigEntity
        {
            Id = Guid.NewGuid(),
            PluginId = pluginId,
            ConfigJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["host"] = "global-host",
                ["port"] = "443",
                ["shared-key"] = "global-secret"
            }),
            UpdatedAt = DateTime.UtcNow
        });

        _db.ProbeAssignments.Add(new ProbeAssignmentEntity
        {
            Id = assignmentId,
            HostId = hostId,
            ProbePluginId = pluginId,
            Name = $"probe-{assignmentId:N}",
            NameSnakeCase = $"probe_{assignmentId:N}",
            ScheduleCron = "0/30 * * * * ?",
            ConfigJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["host"] = "assignment-host",
                ["timeout"] = "30"
            })
        });
        await _db.SaveChangesAsync();

        var job = CreateJob();
        var result = await job.BuildMergedConfigAsync(pluginId, assignmentId.ToString(), CancellationToken.None);

        Assert.Equal("assignment-host", result["host"]);
        Assert.Equal("443", result["port"]);
        Assert.Equal("global-secret", result["shared-key"]);
        Assert.Equal("30", result["timeout"]);
    }

    [Fact]
    public async Task BuildMergedConfig_NoGlobalConfig_FallsBackToAssignmentOnly()
    {
        var pluginId = "probe-no-global";
        var assignmentId = Guid.NewGuid();
        var hostId = await CreateHostAsync();

        _db.ProbeAssignments.Add(new ProbeAssignmentEntity
        {
            Id = assignmentId,
            HostId = hostId,
            ProbePluginId = pluginId,
            Name = $"probe-{assignmentId:N}",
            NameSnakeCase = $"probe_{assignmentId:N}",
            ScheduleCron = "0/30 * * * * ?",
            ConfigJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["key1"] = "val1"
            })
        });
        await _db.SaveChangesAsync();

        var job = CreateJob();
        var result = await job.BuildMergedConfigAsync(pluginId, assignmentId.ToString(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("val1", result["key1"]);
    }

    [Fact]
    public async Task BuildMergedConfig_EmptyGlobalAndAssignment_ReturnsEmptyDict()
    {
        var pluginId = "probe-empty";
        var assignmentId = Guid.NewGuid();
        var hostId = await CreateHostAsync();

        _db.ProbeAssignments.Add(new ProbeAssignmentEntity
        {
            Id = assignmentId,
            HostId = hostId,
            ProbePluginId = pluginId,
            Name = $"probe-{assignmentId:N}",
            NameSnakeCase = $"probe_{assignmentId:N}",
            ScheduleCron = "0/30 * * * * ?",
            ConfigJson = null
        });
        await _db.SaveChangesAsync();

        var job = CreateJob();
        var result = await job.BuildMergedConfigAsync(pluginId, assignmentId.ToString(), CancellationToken.None);

        Assert.Empty(result);
    }

    private async Task<Guid> CreateHostAsync()
    {
        var host = new HostEntity
        {
            Id = Guid.NewGuid(),
            Name = $"test-host-{Guid.NewGuid():N}",
            Address = "127.0.0.1"
        };
        _db.Hosts.Add(host);
        await _db.SaveChangesAsync();
        return host.Id;
    }

    private ProbeExecutionJob CreateJob()
    {
        return new ProbeExecutionJob(
            null!,
            _db,
            null!,
            NullLogger<ProbeExecutionJob>.Instance);
    }
}
