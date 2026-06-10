using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;
using Xunit;

namespace Mone.ProbeExecutor.Tests;

public class ConfigMergerTests : IDisposable
{
    private readonly MoneDbContext _db;

    public ConfigMergerTests()
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
        await _db.SaveChangesAsync();

        var assignmentConfigJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["host"] = "assignment-host",
            ["timeout"] = "30"
        });

        var result = await ConfigMerger.BuildMergedConfigAsync(
            _db, pluginId, assignmentConfigJson, NullLogger.Instance, CancellationToken.None);

        Assert.Equal("assignment-host", result["host"]);
        Assert.Equal("443", result["port"]);
        Assert.Equal("global-secret", result["shared-key"]);
        Assert.Equal("30", result["timeout"]);
    }

    [Fact]
    public async Task BuildMergedConfig_NoGlobalConfig_FallsBackToAssignmentOnly()
    {
        var pluginId = "probe-no-global";

        var assignmentConfigJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["key1"] = "val1"
        });

        var result = await ConfigMerger.BuildMergedConfigAsync(
            _db, pluginId, assignmentConfigJson, NullLogger.Instance, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("val1", result["key1"]);
    }

    [Fact]
    public async Task BuildMergedConfig_EmptyGlobalAndAssignment_ReturnsEmptyDict()
    {
        var pluginId = "probe-empty";

        var result = await ConfigMerger.BuildMergedConfigAsync(
            _db, pluginId, null, NullLogger.Instance, CancellationToken.None);

        Assert.Empty(result);
    }
}
