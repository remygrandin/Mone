using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Mone.Contracts.Models;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Xunit;

namespace Mone.Api.Tests;

/// <summary>
/// End-to-end checks that <c>RequirePermissionFilter</c> enforces scope and level over HTTP:
/// out-of-scope writes are 403, View does not imply Manage, and the self-permissions endpoint is
/// reachable by any authenticated user.
/// </summary>
[Collection("Api")]
public class AuthorizationEndpointTests
{
    private readonly ApiFixture _fixture;

    public AuthorizationEndpointTests(ApiFixture fixture) => _fixture = fixture;

    private static HostEntity NewHost() => new()
    {
        Id = Guid.NewGuid(),
        Name = $"authz-{Guid.NewGuid():N}",
        Address = "10.0.0.1",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private async Task<Guid> SeedRoleAsync(PermissionResource resource, PermissionLevel level)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
        var role = new RoleEntity
        {
            Id = Guid.NewGuid(), Name = $"authz-role-{Guid.NewGuid():N}", IsSystem = false,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        role.Permissions.Add(new RolePermissionEntity { RoleId = role.Id, Resource = resource, Level = level });
        db.MoneRoles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private async Task RunInDbAsync(Func<MoneDbContext, Task> action)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
        await action(db);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task NoPermission_GetHosts_Returns403()
    {
        var (client, _) = await _fixture.CreatePlainClientAsync($"authz-none-{Guid.NewGuid():N}@test.com");

        var response = await client.GetAsync("/api/hosts", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task SelfPermissions_ReachableByAnyAuthenticatedUser()
    {
        var (client, _) = await _fixture.CreatePlainClientAsync($"authz-self-{Guid.NewGuid():N}@test.com");

        var response = await client.GetAsync("/api/auth/me/permissions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var caps = await response.Content.ReadFromJsonAsync<MyPermissionsResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(caps);
        Assert.Empty(caps!.Capabilities); // zero grants
        client.Dispose();
    }

    [Fact]
    public async Task GroupScopedUser_CanReadInScopeHost_ButNotOutOfScope()
    {
        var (client, userId) = await _fixture.CreatePlainClientAsync($"authz-scope-{Guid.NewGuid():N}@test.com");
        var roleId = await SeedRoleAsync(PermissionResource.Hosts, PermissionLevel.Manage);

        var group = new GroupEntity { Id = Guid.NewGuid(), Name = $"authz-g-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var hostIn = NewHost();
        var hostOut = NewHost();
        await RunInDbAsync(db =>
        {
            db.HostGroups.Add(group);
            db.Hosts.AddRange(hostIn, hostOut);
            db.HostGroupMemberships.Add(new GroupMembershipEntity { GroupId = group.Id, HostId = hostIn.Id });
            db.UserRoleAssignments.Add(new UserRoleAssignmentEntity
            {
                Id = Guid.NewGuid(), UserId = userId, RoleId = roleId,
                ScopeType = ScopeType.Group, ScopeId = group.Id, CreatedAt = DateTimeOffset.UtcNow
            });
            return Task.CompletedTask;
        });

        var inResp = await client.GetAsync($"/api/hosts/{hostIn.Id}", TestContext.Current.CancellationToken);
        var outResp = await client.GetAsync($"/api/hosts/{hostOut.Id}", TestContext.Current.CancellationToken);
        var listResp = await client.GetAsync("/api/hosts", TestContext.Current.CancellationToken); // collection => Global target, scoped grant ignored

        Assert.Equal(HttpStatusCode.OK, inResp.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, outResp.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listResp.StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task ViewLevel_AllowsRead_DeniesWrite()
    {
        var (client, userId) = await _fixture.CreatePlainClientAsync($"authz-view-{Guid.NewGuid():N}@test.com");
        var roleId = await SeedRoleAsync(PermissionResource.Hosts, PermissionLevel.View);

        var host = NewHost();
        await RunInDbAsync(db =>
        {
            db.Hosts.Add(host);
            db.UserRoleAssignments.Add(new UserRoleAssignmentEntity
            {
                Id = Guid.NewGuid(), UserId = userId, RoleId = roleId,
                ScopeType = ScopeType.Global, ScopeId = null, CreatedAt = DateTimeOffset.UtcNow
            });
            return Task.CompletedTask;
        });

        var getResp = await client.GetAsync($"/api/hosts/{host.Id}", TestContext.Current.CancellationToken);
        var putResp = await client.PutAsJsonAsync($"/api/hosts/{host.Id}",
            new UpdateHostRequest(host.Name, "10.0.0.2", true), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, putResp.StatusCode);
        client.Dispose();
    }
}
