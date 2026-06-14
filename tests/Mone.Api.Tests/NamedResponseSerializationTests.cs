using System.Text.Json;
using Mone.Api.Models;
using Mone.Infrastructure.Services;
using Xunit;

namespace Mone.Api.Tests;

/// <summary>
/// Wire-shape guard for the S05 named action-response DTOs. Each record replaced an
/// anonymous success body; serialized with Web (camelCase) defaults it must reproduce
/// the original lowercase JSON keys. This is the negative guard against PascalCase drift.
/// </summary>
public class NamedResponseSerializationTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static string[] RootKeys(object value)
    {
        var json = JsonSerializer.Serialize(value, WebOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
    }

    [Fact]
    public void EffectiveAssignmentsResponse_uses_lowercase_keys()
    {
        var keys = RootKeys(new EffectiveAssignmentsResponse(
            Array.Empty<EffectiveProbeAssignment>(),
            Array.Empty<EffectiveCheckerAssignment>()));

        Assert.Equal(new[] { "probes", "checkers" }, keys);
    }

    [Fact]
    public void HostOverridesResponse_uses_lowercase_keys()
    {
        var keys = RootKeys(new HostOverridesResponse(
            Array.Empty<OverrideResponse>(),
            Array.Empty<OverrideResponse>()));

        Assert.Equal(new[] { "probes", "checkers" }, keys);
    }

    [Fact]
    public void ExecutorNodeRegistrationResponse_uses_lowercase_key()
    {
        var keys = RootKeys(new ExecutorNodeRegistrationResponse(Guid.NewGuid()));

        Assert.Equal(new[] { "id" }, keys);
    }

    [Fact]
    public void RepositorySyncAllResponse_uses_lowercase_key()
    {
        var keys = RootKeys(new RepositorySyncAllResponse(3));

        Assert.Equal(new[] { "synced" }, keys);
    }

    [Fact]
    public void ProbeTriggerResponse_uses_lowercase_keys()
    {
        var keys = RootKeys(new ProbeTriggerResponse("triggered", "http-probe", Guid.NewGuid()));

        Assert.Equal(new[] { "status", "probePluginId", "hostId" }, keys);
    }
}
