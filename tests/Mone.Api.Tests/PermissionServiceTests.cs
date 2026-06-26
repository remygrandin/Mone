using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Mone.Api.Authorization;
using Mone.Api.Services;
using Mone.Api.Tests.Fixtures;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;
using Xunit;

namespace Mone.Api.Tests;

/// <summary>
/// Exercises <see cref="PermissionService.GetEffectiveLevelAsync"/> — the core authorization
/// decision — against a real database. Each test seeds its own user/role/scope so it is isolated
/// from other tests sharing the fixture database.
/// </summary>
[Collection("Api")]
public class PermissionServiceTests
{
    private readonly ApiFixture _fixture;

    public PermissionServiceTests(ApiFixture fixture) => _fixture = fixture;

    private async Task<string> CreateUserAsync(IServiceProvider sp)
    {
        var userManager = sp.GetRequiredService<UserManager<UserEntity>>();
        var email = $"perm-{Guid.NewGuid():N}@example.com";
        var user = new UserEntity { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, "Test1234!");
        Assert.True(result.Succeeded);
        return user.Id;
    }

    private static async Task<Guid> CreateRoleAsync(
        MoneDbContext db, PermissionResource resource, PermissionLevel level)
    {
        var role = new RoleEntity
        {
            Id = Guid.NewGuid(),
            Name = $"role-{Guid.NewGuid():N}",
            IsSystem = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        role.Permissions.Add(new RolePermissionEntity { RoleId = role.Id, Resource = resource, Level = level });
        db.MoneRoles.Add(role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return role.Id;
    }

    private static HostEntity NewHost() => new()
    {
        Id = Guid.NewGuid(),
        Name = $"host-{Guid.NewGuid():N}",
        Address = "10.0.0.1",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task GlobalManage_GrantsManageEverywhere()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MoneDbContext>();

        var userId = await CreateUserAsync(sp);
        var roleId = await CreateRoleAsync(db, PermissionResource.Hosts, PermissionLevel.Manage);
        db.UserRoleAssignments.Add(new UserRoleAssignmentEntity
        {
            Id = Guid.NewGuid(), UserId = userId, RoleId = roleId,
            ScopeType = ScopeType.Global, ScopeId = null, CreatedAt = DateTimeOffset.UtcNow
        });
        var host = NewHost();
        db.Hosts.Add(host);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var svc = new PermissionService(db, new ScopeResolver(db));
        var level = await svc.GetEffectiveLevelAsync(userId, PermissionResource.Hosts, ScopeTarget.Host(host.Id), TestContext.Current.CancellationToken);

        Assert.Equal(PermissionLevel.Manage, level);
    }

    [Fact]
    public async Task GroupScope_CoversHostInGroup_ButNotOutside()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MoneDbContext>();

        var userId = await CreateUserAsync(sp);
        var roleId = await CreateRoleAsync(db, PermissionResource.Hosts, PermissionLevel.Manage);

        var group = new GroupEntity { Id = Guid.NewGuid(), Name = $"g-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var hostIn = NewHost();
        var hostOut = NewHost();
        db.HostGroups.Add(group);
        db.Hosts.AddRange(hostIn, hostOut);
        db.HostGroupMemberships.Add(new GroupMembershipEntity { GroupId = group.Id, HostId = hostIn.Id });
        db.UserRoleAssignments.Add(new UserRoleAssignmentEntity
        {
            Id = Guid.NewGuid(), UserId = userId, RoleId = roleId,
            ScopeType = ScopeType.Group, ScopeId = group.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var svc = new PermissionService(db, new ScopeResolver(db));

        Assert.Equal(PermissionLevel.Manage,
            await svc.GetEffectiveLevelAsync(userId, PermissionResource.Hosts, ScopeTarget.Host(hostIn.Id), TestContext.Current.CancellationToken));
        Assert.Equal(PermissionLevel.None,
            await svc.GetEffectiveLevelAsync(userId, PermissionResource.Hosts, ScopeTarget.Host(hostOut.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TagScope_CoversTaggedHostOnly()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MoneDbContext>();

        var userId = await CreateUserAsync(sp);
        var roleId = await CreateRoleAsync(db, PermissionResource.Monitoring, PermissionLevel.View);

        var tag = new TagEntity { Id = Guid.NewGuid(), Name = $"t-{Guid.NewGuid():N}" };
        var tagged = NewHost();
        var untagged = NewHost();
        db.Tags.Add(tag);
        db.Hosts.AddRange(tagged, untagged);
        db.HostTags.Add(new HostTagEntity { HostId = tagged.Id, TagId = tag.Id });
        db.UserRoleAssignments.Add(new UserRoleAssignmentEntity
        {
            Id = Guid.NewGuid(), UserId = userId, RoleId = roleId,
            ScopeType = ScopeType.Tag, ScopeId = tag.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var svc = new PermissionService(db, new ScopeResolver(db));

        Assert.Equal(PermissionLevel.View,
            await svc.GetEffectiveLevelAsync(userId, PermissionResource.Monitoring, ScopeTarget.Host(tagged.Id), TestContext.Current.CancellationToken));
        Assert.Equal(PermissionLevel.None,
            await svc.GetEffectiveLevelAsync(userId, PermissionResource.Monitoring, ScopeTarget.Host(untagged.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ScopedGrant_DoesNotApplyToGlobalTarget()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MoneDbContext>();

        var userId = await CreateUserAsync(sp);
        var roleId = await CreateRoleAsync(db, PermissionResource.Hosts, PermissionLevel.Manage);
        var group = new GroupEntity { Id = Guid.NewGuid(), Name = $"g-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.HostGroups.Add(group);
        db.UserRoleAssignments.Add(new UserRoleAssignmentEntity
        {
            Id = Guid.NewGuid(), UserId = userId, RoleId = roleId,
            ScopeType = ScopeType.Group, ScopeId = group.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var svc = new PermissionService(db, new ScopeResolver(db));

        // A collection route (Global target) must ignore the group-scoped grant.
        Assert.Equal(PermissionLevel.None,
            await svc.GetEffectiveLevelAsync(userId, PermissionResource.Hosts, ScopeTarget.Global, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EffectiveLevel_IsMaxAcrossAssignments()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MoneDbContext>();

        var userId = await CreateUserAsync(sp);
        var viewRole = await CreateRoleAsync(db, PermissionResource.Hosts, PermissionLevel.View);
        var manageRole = await CreateRoleAsync(db, PermissionResource.Hosts, PermissionLevel.Manage);

        var group = new GroupEntity { Id = Guid.NewGuid(), Name = $"g-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var host = NewHost();
        db.HostGroups.Add(group);
        db.Hosts.Add(host);
        db.HostGroupMemberships.Add(new GroupMembershipEntity { GroupId = group.Id, HostId = host.Id });
        // Global View + Group Manage → Manage wins.
        db.UserRoleAssignments.AddRange(
            new UserRoleAssignmentEntity { Id = Guid.NewGuid(), UserId = userId, RoleId = viewRole, ScopeType = ScopeType.Global, ScopeId = null, CreatedAt = DateTimeOffset.UtcNow },
            new UserRoleAssignmentEntity { Id = Guid.NewGuid(), UserId = userId, RoleId = manageRole, ScopeType = ScopeType.Group, ScopeId = group.Id, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var svc = new PermissionService(db, new ScopeResolver(db));

        Assert.Equal(PermissionLevel.Manage,
            await svc.GetEffectiveLevelAsync(userId, PermissionResource.Hosts, ScopeTarget.Host(host.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Capabilities_ReportMaxPerResourceIgnoringScope()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MoneDbContext>();

        var userId = await CreateUserAsync(sp);
        var hostsRole = await CreateRoleAsync(db, PermissionResource.Hosts, PermissionLevel.View);
        var group = new GroupEntity { Id = Guid.NewGuid(), Name = $"g-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.HostGroups.Add(group);
        db.UserRoleAssignments.Add(new UserRoleAssignmentEntity
        {
            Id = Guid.NewGuid(), UserId = userId, RoleId = hostsRole,
            ScopeType = ScopeType.Group, ScopeId = group.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var svc = new PermissionService(db, new ScopeResolver(db));
        var caps = await svc.GetCapabilitiesAsync(userId, TestContext.Current.CancellationToken);

        // Even though the grant is scoped, the coarse capability map reports it (used for nav gating).
        Assert.True(caps.TryGetValue(PermissionResource.Hosts, out var level));
        Assert.Equal(PermissionLevel.View, level);
        Assert.False(caps.ContainsKey(PermissionResource.Plugins));
    }
}
