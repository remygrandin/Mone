using Microsoft.EntityFrameworkCore;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Tests.Fixtures;
using Xunit;

namespace Mone.Infrastructure.Tests;

[Collection("Infrastructure")]
public class DatabaseSchemaTests(InfrastructureFixture fixture)
{
    [Fact]
    public async Task Migrations_CreateAllConfigTables()
    {
        await using var db = fixture.CreateDbContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'public'
            ORDER BY table_name;
            """;

        var tables = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));

        Assert.Contains("hosts", tables);
        Assert.Contains("tags", tables);
        Assert.Contains("host_tags", tables);
        Assert.Contains("probe_assignments", tables);
        Assert.Contains("checker_assignments", tables);
        Assert.Contains("probe_results", tables);
        Assert.Contains("status_history", tables);
    }

    [Theory]
    [InlineData("probe_results")]
    [InlineData("status_history")]
    public async Task TimescaleDB_HypertablesExist(string tableName)
    {
        await using var db = fixture.CreateDbContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT count(*) FROM timescaledb_information.hypertables
            WHERE hypertable_name = @table;
            """;
        var param = cmd.CreateParameter();
        param.ParameterName = "table";
        param.Value = tableName;
        cmd.Parameters.Add(param);

        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ProbeResults_InsertAndQuery()
    {
        await using var db = fixture.CreateDbContext();
        var targetId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;

        db.ProbeResults.Add(new ProbeResultEntity
        {
            Timestamp = timestamp,
            TargetId = targetId,
            ProbeId = "test-probe-schema",
            Status = MonitoringStatus.Healthy,
            Summary = "Schema test OK",
            DurationMs = 10.0
        });
        await db.SaveChangesAsync();

        var result = await db.ProbeResults
            .Where(r => r.TargetId == targetId && r.ProbeId == "test-probe-schema")
            .SingleAsync();

        Assert.Equal(MonitoringStatus.Healthy, result.Status);
        Assert.Equal("Schema test OK", result.Summary);
        Assert.Equal(10.0, result.DurationMs);
    }
}
