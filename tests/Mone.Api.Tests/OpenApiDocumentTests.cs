using System.Net;
using System.Text.Json;
using Mone.Api.Tests.Fixtures;
using Xunit;

namespace Mone.Api.Tests;

/// <summary>
/// Contract test for the S06 docs-annotation layer: proves the generated OpenAPI
/// document (/openapi/v1.json) is tag-grouped, every operation carries a summary,
/// the canonical 16-tag set from D045 is present, and every documented error
/// response (status >= 400 with a body) uses application/problem+json. A future
/// endpoint added without annotations breaks this test.
/// </summary>
[Collection("Api")]
public class OpenApiDocumentTests
{
    private readonly ApiFixture _fixture;

    public OpenApiDocumentTests(ApiFixture fixture) => _fixture = fixture;

    // Canonical tag set from D045. The document's used tags must be a superset.
    private static readonly string[] CanonicalTags =
    [
        "Hosts", "Tags", "Assignments", "Host Groups", "Status", "Probe Results",
        "Probes", "Dashboard", "Executor Nodes", "Plugins", "Notifications",
        "Housekeeping", "Roles", "Users", "Permissions", "Auth"
    ];

    private static readonly string[] HttpMethods =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    [Fact]
    public async Task OpenApiDocument_IsTagGroupedSummarizedAndProblemDetailsTyped()
    {
        // (a) Anonymous GET /openapi/v1.json returns 200 and parses as JSON.
        var response = await _fixture.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("paths", out var paths),
            "OpenAPI document has no 'paths' object.");

        var missingTagsOrSummary = new List<string>();
        var missingProblemJson = new List<string>();
        var usedTags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (!HttpMethods.Contains(method.Name, StringComparer.OrdinalIgnoreCase))
                    continue; // skip path-level keys like "parameters"

                var op = method.Value;
                var where = $"{method.Name.ToUpperInvariant()} {path.Name}";

                // (b) non-empty tags array AND non-empty summary string.
                var hasTags = op.TryGetProperty("tags", out var tags)
                    && tags.ValueKind == JsonValueKind.Array
                    && tags.GetArrayLength() > 0;
                var hasSummary = op.TryGetProperty("summary", out var summary)
                    && summary.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(summary.GetString());

                if (!hasTags || !hasSummary)
                {
                    var what = (!hasTags ? "tags" : "") + (!hasTags && !hasSummary ? "+" : "") + (!hasSummary ? "summary" : "");
                    missingTagsOrSummary.Add($"{where} (missing {what})");
                }

                if (hasTags)
                {
                    foreach (var t in tags.EnumerateArray())
                    {
                        if (t.ValueKind == JsonValueKind.String)
                            usedTags.Add(t.GetString()!);
                    }
                }

                // (d) every error response (status >= 400) with content must offer application/problem+json.
                if (op.TryGetProperty("responses", out var responses)
                    && responses.ValueKind == JsonValueKind.Object)
                {
                    foreach (var resp in responses.EnumerateObject())
                    {
                        if (!int.TryParse(resp.Name, out var statusCode) || statusCode < 400)
                            continue; // tolerate 2xx/3xx and "default"

                        if (resp.Value.TryGetProperty("content", out var content)
                            && content.ValueKind == JsonValueKind.Object
                            && content.EnumerateObject().Any()
                            && !content.TryGetProperty("application/problem+json", out _))
                        {
                            missingProblemJson.Add($"{where} -> {resp.Name}");
                        }
                    }
                }
            }
        }

        // (b) assert all operations are tagged and summarized.
        Assert.True(missingTagsOrSummary.Count == 0,
            "Operations missing tags and/or summary:\n  " + string.Join("\n  ", missingTagsOrSummary));

        // (c) used tags must be a superset of the canonical 16-tag set.
        var missingCanonical = CanonicalTags.Where(t => !usedTags.Contains(t)).ToList();
        Assert.True(missingCanonical.Count == 0,
            "Canonical D045 tags absent from the document: " + string.Join(", ", missingCanonical));

        // (d) assert error responses with bodies are ProblemDetails-typed.
        Assert.True(missingProblemJson.Count == 0,
            "Error responses (>=400) with content but no application/problem+json:\n  "
            + string.Join("\n  ", missingProblemJson));
    }
}
