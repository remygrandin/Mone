using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;
using Xunit;

namespace Mone.CheckerEngine.Tests;

public class StreamCheckerServiceTests : IDisposable
{
    private readonly MoneDbContext _db;
    private readonly ILogger _logger = NullLogger.Instance;

    public StreamCheckerServiceTests()
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
    public async Task MergedConfig_GlobalBaseWithAssignmentOverride()
    {
        var pluginId = "test-checker";

        _db.PluginGlobalConfigs.Add(new PluginGlobalConfigEntity
        {
            Id = Guid.NewGuid(),
            PluginId = pluginId,
            ConfigJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["api_url"] = "https://global.example.com",
                ["timeout"] = "60",
                ["shared_token"] = "global-token"
            }),
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var assignmentConfig = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["api_url"] = "https://override.example.com",
            ["threshold"] = "90"
        });

        var result = await ConfigMerger.BuildMergedConfigAsync(
            _db, pluginId, assignmentConfig, _logger, CancellationToken.None);

        Assert.Equal("https://override.example.com", result["api_url"]);
        Assert.Equal("60", result["timeout"]);
        Assert.Equal("global-token", result["shared_token"]);
        Assert.Equal("90", result["threshold"]);
    }

    [Fact]
    public async Task MergedConfig_NoGlobalConfig_ReturnsAssignmentOnly()
    {
        var assignmentConfig = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["key1"] = "val1"
        });

        var result = await ConfigMerger.BuildMergedConfigAsync(
            _db, "checker-no-global", assignmentConfig, _logger, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("val1", result["key1"]);
    }

    [Fact]
    public async Task MergedConfig_NoAssignmentConfig_ReturnsGlobalOnly()
    {
        var pluginId = "checker-global-only";

        _db.PluginGlobalConfigs.Add(new PluginGlobalConfigEntity
        {
            Id = Guid.NewGuid(),
            PluginId = pluginId,
            ConfigJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["host"] = "global-host"
            }),
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await ConfigMerger.BuildMergedConfigAsync(
            _db, pluginId, null, _logger, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("global-host", result["host"]);
    }

    [Fact]
    public async Task MergedConfig_BothNull_ReturnsEmpty()
    {
        var result = await ConfigMerger.BuildMergedConfigAsync(
            _db, "checker-empty", null, _logger, CancellationToken.None);

        Assert.Empty(result);
    }
}
