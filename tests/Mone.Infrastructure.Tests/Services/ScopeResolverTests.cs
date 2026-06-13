using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;
using Mone.Infrastructure.Tests.Fixtures;
using Xunit;

namespace Mone.Infrastructure.Tests.Services;

[Collection("Infrastructure")]
public class ScopeResolverTests
{
    private readonly InfrastructureFixture _fixture;

    public ScopeResolverTests(InfrastructureFixture fixture) => _fixture = fixture;

    private static HostEntity NewHost(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Address = "10.0.0.1",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static GroupEntity NewGroup(string name, Guid? parent = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ParentGroupId = parent,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task HostGroupClosure_IncludesDirectAndAncestorGroups()
    {
        await using var db = _fixture.CreateDbContext();
        var grandparent = NewGroup("sr-gp");
        var parent = NewGroup("sr-parent", grandparent.Id);
        var child = NewGroup("sr-child", parent.Id);
        var unrelated = NewGroup("sr-unrelated");
        var host = NewHost("sr-h1");

        db.HostGroups.AddRange(grandparent, parent, child, unrelated);
        db.Hosts.Add(host);
        db.HostGroupMemberships.Add(new GroupMembershipEntity { GroupId = child.Id, HostId = host.Id });
        await db.SaveChangesAsync();

        var resolver = new ScopeResolver(db);
        var closure = await resolver.GetHostGroupClosureAsync(host.Id);

        Assert.Contains(child.Id, closure);
        Assert.Contains(parent.Id, closure);
        Assert.Contains(grandparent.Id, closure);
        Assert.DoesNotContain(unrelated.Id, closure);
    }

    [Fact]
    public async Task HostTagIds_ReturnsOnlyTagsCarriedByHost()
    {
        await using var db = _fixture.CreateDbContext();
        var host = NewHost("sr-tagged");
        var t1 = new TagEntity { Id = Guid.NewGuid(), Name = "sr-prod" };
        var t2 = new TagEntity { Id = Guid.NewGuid(), Name = "sr-edge" };
        var other = new TagEntity { Id = Guid.NewGuid(), Name = "sr-other" };

        db.Hosts.Add(host);
        db.Tags.AddRange(t1, t2, other);
        db.HostTags.AddRange(
            new HostTagEntity { HostId = host.Id, TagId = t1.Id },
            new HostTagEntity { HostId = host.Id, TagId = t2.Id });
        await db.SaveChangesAsync();

        var resolver = new ScopeResolver(db);
        var tags = await resolver.GetHostTagIdsAsync(host.Id);

        Assert.Contains(t1.Id, tags);
        Assert.Contains(t2.Id, tags);
        Assert.DoesNotContain(other.Id, tags);
    }

    [Fact]
    public async Task GroupAncestorsAndSelf_WalksUpButNotDown()
    {
        await using var db = _fixture.CreateDbContext();
        var parent = NewGroup("sr-anc-parent");
        var middle = NewGroup("sr-anc-middle", parent.Id);
        var leaf = NewGroup("sr-anc-leaf", middle.Id);
        db.HostGroups.AddRange(parent, middle, leaf);
        await db.SaveChangesAsync();

        var resolver = new ScopeResolver(db);
        var ancestors = await resolver.GetGroupAncestorsAndSelfAsync(middle.Id);

        Assert.Contains(middle.Id, ancestors);
        Assert.Contains(parent.Id, ancestors);
        Assert.DoesNotContain(leaf.Id, ancestors); // children do not cover their parent
    }

    [Fact]
    public async Task GroupSubtree_IncludesSelfAndDescendants()
    {
        await using var db = _fixture.CreateDbContext();
        var root = NewGroup("sr-sub-root");
        var a = NewGroup("sr-sub-a", root.Id);
        var b = NewGroup("sr-sub-b", root.Id);
        var aChild = NewGroup("sr-sub-a-child", a.Id);
        var outside = NewGroup("sr-sub-outside");
        db.HostGroups.AddRange(root, a, b, aChild, outside);
        await db.SaveChangesAsync();

        var resolver = new ScopeResolver(db);
        var subtree = await resolver.GetGroupSubtreeAsync(root.Id);

        Assert.Contains(root.Id, subtree);
        Assert.Contains(a.Id, subtree);
        Assert.Contains(b.Id, subtree);
        Assert.Contains(aChild.Id, subtree);
        Assert.DoesNotContain(outside.Id, subtree);
    }

    [Fact]
    public async Task HostsInGroupSubtree_CollectsHostsAcrossDescendants()
    {
        await using var db = _fixture.CreateDbContext();
        var root = NewGroup("sr-hsub-root");
        var child = NewGroup("sr-hsub-child", root.Id);
        var hostRoot = NewHost("sr-hsub-h-root");
        var hostChild = NewHost("sr-hsub-h-child");
        var hostOutside = NewHost("sr-hsub-h-out");

        db.HostGroups.AddRange(root, child);
        db.Hosts.AddRange(hostRoot, hostChild, hostOutside);
        db.HostGroupMemberships.AddRange(
            new GroupMembershipEntity { GroupId = root.Id, HostId = hostRoot.Id },
            new GroupMembershipEntity { GroupId = child.Id, HostId = hostChild.Id });
        await db.SaveChangesAsync();

        var resolver = new ScopeResolver(db);
        var hosts = await resolver.GetHostsInGroupSubtreeAsync(root.Id);

        Assert.Contains(hostRoot.Id, hosts);
        Assert.Contains(hostChild.Id, hosts);
        Assert.DoesNotContain(hostOutside.Id, hosts);
    }
}
