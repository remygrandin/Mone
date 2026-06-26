using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Mone.Api.Models;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

/// <summary>
/// Cross-endpoint guard for the S02 error contract: every error response across the
/// hosts / host-groups / auth / plugins families must be RFC-9457 ProblemDetails
/// (application/problem+json) carrying the matching HTTP status code.
/// </summary>
[Collection("Api")]
public class ProblemDetailsConsistencyTests
{
    private readonly ApiFixture _fixture;

    public ProblemDetailsConsistencyTests(ApiFixture fixture) => _fixture = fixture;

    private static async Task AssertProblemDetailsAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal((int)expected, problem.Status);
    }

    [Fact]
    public async Task CreateHost_EmptyName_Returns400ProblemDetails()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"pd_host400_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync("/api/hosts", new CreateHostRequest("", "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);

        await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateHost_DuplicateName_Returns409ProblemDetails()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"pd_host409_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var name = $"pd-dup-{Guid.NewGuid():N}";
        var first = await client.PostAsJsonAsync("/api/hosts", new CreateHostRequest(name, "10.0.0.1"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var response = await client.PostAsJsonAsync("/api/hosts", new CreateHostRequest(name, "10.0.0.2"), cancellationToken: TestContext.Current.CancellationToken);

        await AssertProblemDetailsAsync(response, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateHostGroup_InvalidParent_Returns400ProblemDetails()
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"pd_hg400_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync("/api/host-groups",
            new CreateHostGroupRequest($"grp-{Guid.NewGuid():N}", null, Guid.NewGuid()), cancellationToken: TestContext.Current.CancellationToken);

        await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/hosts")]
    [InlineData("/api/host-groups")]
    [InlineData("/api/plugin-repos")]
    public async Task GetUnknownResource_Returns404ProblemDetails(string family)
    {
        using var client = await _fixture.CreateAuthenticatedClientAsync(
            $"pd_404_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);

        var response = await client.GetAsync($"{family}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Login_WrongCredentials_Returns401ProblemDetails()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"nobody_{Guid.NewGuid():N}@test.com", "WrongPass1!"), cancellationToken: TestContext.Current.CancellationToken);

        await AssertProblemDetailsAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListHosts_WithoutPermission_Returns403ProblemDetails()
    {
        var (client, _) = await _fixture.CreatePlainClientAsync($"pd_403_{Guid.NewGuid():N}@test.com", "ValidPass1!", TestContext.Current.CancellationToken);
        using var _client = client;

        var response = await client.GetAsync("/api/hosts", TestContext.Current.CancellationToken);

        await AssertProblemDetailsAsync(response, HttpStatusCode.Forbidden);
    }
}
