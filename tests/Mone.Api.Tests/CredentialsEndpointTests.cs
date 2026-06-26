using System.Net;
using System.Net.Http.Json;
using Mone.Api.Endpoints;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

[Collection("Api")]
public class CredentialsEndpointTests
{
    private readonly ApiFixture _fixture;

    public CredentialsEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateCredentials_Returns201_WithValidResponse()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"cred_create_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var request = new CredentialsEndpoints.CreateCredentialsRequest("TestCreds", "testuser", "testpass123");

        var response = await client.PostAsJsonAsync("/api/credentials", request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CredentialsEndpoints.CredentialsResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("TestCreds", created.Name);
        Assert.Equal("testuser", created.Username);
    }

    [Fact]
    public async Task CreateCredentials_Duplicate_ReturnsBadRequest()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"cred_dup_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var request = new CredentialsEndpoints.CreateCredentialsRequest("DupTest", "user1", "pass1");
        await client.PostAsJsonAsync("/api/credentials", request, cancellationToken: TestContext.Current.CancellationToken);

        var request2 = new CredentialsEndpoints.CreateCredentialsRequest("DupTest", "user2", "pass2");
        var response = await client.PostAsJsonAsync("/api/credentials", request2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListCredentials_ReturnsCreatedCredentials()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"cred_list_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var request = new CredentialsEndpoints.CreateCredentialsRequest("ListTest", "listuser", "listpass");
        var createResp = await client.PostAsJsonAsync("/api/credentials", request, cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResp.Content.ReadFromJsonAsync<CredentialsEndpoints.CredentialsResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/api/credentials", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(body);
        Assert.Contains("ListTest", body);
    }

    [Fact]
    public async Task GetCredentialsById_ReturnsCredentials()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"cred_getid_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var createResp = await client.PostAsJsonAsync("/api/credentials",
            new CredentialsEndpoints.CreateCredentialsRequest("GetTest", "getuser", "getpass"), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResp.Content.ReadFromJsonAsync<CredentialsEndpoints.CredentialsResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.GetAsync($"/api/credentials/{created!.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cred = await response.Content.ReadFromJsonAsync<CredentialsEndpoints.CredentialsResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(created.Id, cred!.Id);
        Assert.Equal("GetTest", cred.Name);
    }

    [Fact]
    public async Task GetCredentialsById_NonExistent_Returns404()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"cred_404_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var response = await client.GetAsync($"/api/credentials/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_UpdatesSuccessfully()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"cred_update_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var createResp = await client.PostAsJsonAsync("/api/credentials",
            new CredentialsEndpoints.CreateCredentialsRequest("UpdateTest", "olduser", "oldpass"), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResp.Content.ReadFromJsonAsync<CredentialsEndpoints.CredentialsResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var updateReq = new CredentialsEndpoints.UpdateCredentialsRequest(null, "newuser", "newpass");
        var response = await client.PutAsJsonAsync($"/api/credentials/{created!.Id}", updateReq, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CredentialsEndpoints.CredentialsResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("newuser", updated!.Username);
    }

    [Fact]
    public async Task DeleteCredentials_RemovesSuccessfully()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"cred_del_{Guid.NewGuid():N}@test.com", "ValidPass1!");

        var createResp = await client.PostAsJsonAsync("/api/credentials",
            new CredentialsEndpoints.CreateCredentialsRequest("DelTest", "deluser", "delpass"), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResp.Content.ReadFromJsonAsync<CredentialsEndpoints.CredentialsResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.DeleteAsync($"/api/credentials/{created!.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResp = await client.GetAsync($"/api/credentials/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }
}
