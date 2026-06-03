using Microsoft.Extensions.Logging.Abstractions;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;
using Mone.Infrastructure.Tests.Fixtures;
using Xunit;

namespace Mone.Infrastructure.Tests.Services;

[Collection("Infrastructure")]
public class InheritanceResolverTests
{
    private readonly InfrastructureFixture _fixture;

    public InheritanceResolverTests(InfrastructureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DirectOnly_ReturnsDirectAssignments()
    {
        await using var db = _fixture.CreateDbContext();
        var host = new HostEntity { Id = Guid.NewGuid(), Name = "h1", Address = "10.0.0.1", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Hosts.Add(host);
        db.ProbeAssignments.Add(new ProbeAssignmentEntity { Id = Guid.NewGuid(), HostId = host.Id, ProbePluginId = "ping", Name = "ping-direct", NameSnakeCase = "ping_direct", ScheduleCron = "*/5 * * * *" });
        db.CheckerAssignments.Add(new CheckerAssignmentEntity { Id = Guid.NewGuid(), HostId = host.Id, CheckerPluginId = "http" });
        await db.SaveChangesAsync();

        var resolver = new InheritanceResolver(db, NullLogger<InheritanceResolver>.Instance);

        var probes = await resolver.GetEffectiveProbeAssignmentsAsync(host.Id);
        var checkers = await resolver.GetEffectiveCheckerAssignmentsAsync(host.Id);

        Assert.Single(probes);
        Assert.Equal(AssignmentSourceType.Direct, probes[0].SourceType);
        Assert.Null(probes[0].SourceGroupId);
        Assert.Single(checkers);
        Assert.Equal(AssignmentSourceType.Direct, checkers[0].SourceType);
    }

    [Fact]
    public async Task HostInGroup_InheritsGroupAssignment()
    {
        await using var db = _fixture.CreateDbContext();
        var host = new HostEntity { Id = Guid.NewGuid(), Name = "h2", Address = "10.0.0.2", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var group = new GroupEntity { Id = Guid.NewGuid(), Name = "g1", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Hosts.Add(host);
        db.HostGroups.Add(group);
        db.HostGroupMemberships.Add(new GroupMembershipEntity { GroupId = group.Id, HostId = host.Id });
        db.ProbeAssignments.Add(new ProbeAssignmentEntity { Id = Guid.NewGuid(), GroupId = group.Id, ProbePluginId = "ping", Name = "ping-group", NameSnakeCase = "ping_group", ScheduleCron = "*/10 * * * *" });
        await db.SaveChangesAsync();

        var resolver = new InheritanceResolver(db, NullLogger<InheritanceResolver>.Instance);
        var probes = await resolver.GetEffectiveProbeAssignmentsAsync(host.Id);

        Assert.Single(probes);
        Assert.Equal(AssignmentSourceType.Inherited, probes[0].SourceType);
        Assert.Equal(group.Id, probes[0].SourceGroupId);
    }

    [Fact]
    public async Task NestedGroups_InheritsFromParentChain()
    {
        await using var db = _fixture.CreateDbContext();
        var host = new HostEntity { Id = Guid.NewGuid(), Name = "h3", Address = "10.0.0.3", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var childGroup = new GroupEntity { Id = Guid.NewGuid(), Name = "child", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var parentGroup = new GroupEntity { Id = Guid.NewGuid(), Name = "parent", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        childGroup.ParentGroupId = parentGroup.Id;
        db.Hosts.Add(host);
        db.HostGroups.AddRange(parentGroup, childGroup);
        db.HostGroupMemberships.Add(new GroupMembershipEntity { GroupId = childGroup.Id, HostId = host.Id });
        db.ProbeAssignments.Add(new ProbeAssignmentEntity { Id = Guid.NewGuid(), GroupId = childGroup.Id, ProbePluginId = "ping-child", Name = "ping-child", NameSnakeCase = "ping_child", ScheduleCron = "*/5 * * * *" });
        db.ProbeAssignments.Add(new ProbeAssignmentEntity { Id = Guid.NewGuid(), GroupId = parentGroup.Id, ProbePluginId = "ping-parent", Name = "ping-parent", NameSnakeCase = "ping_parent", ScheduleCron = "*/15 * * * *" });
        await db.SaveChangesAsync();

        var resolver = new InheritanceResolver(db, NullLogger<InheritanceResolver>.Instance);
        var probes = await resolver.GetEffectiveProbeAssignmentsAsync(host.Id);

        Assert.Equal(2, probes.Count);
        Assert.All(probes, p => Assert.Equal(AssignmentSourceType.Inherited, p.SourceType));
        Assert.Contains(probes, p => p.ProbePluginId == "ping-child" && p.SourceGroupId == childGroup.Id);
        Assert.Contains(probes, p => p.ProbePluginId == "ping-parent" && p.SourceGroupId == parentGroup.Id);
    }

    [Fact]
    public async Task NoAssignments_ReturnsEmpty()
    {
        await using var db = _fixture.CreateDbContext();
        var host = new HostEntity { Id = Guid.NewGuid(), Name = "h4", Address = "10.0.0.4", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();

        var resolver = new InheritanceResolver(db, NullLogger<InheritanceResolver>.Instance);

        var probes = await resolver.GetEffectiveProbeAssignmentsAsync(host.Id);
        var checkers = await resolver.GetEffectiveCheckerAssignmentsAsync(host.Id);

        Assert.Empty(probes);
        Assert.Empty(checkers);
    }
}
