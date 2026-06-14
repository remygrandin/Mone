using System.Text.Json;

namespace Mone.Dashboard.Services;

/// <summary>
/// Parses an RFC-9457 ProblemDetails response body to extract a human-readable message.
/// Reads only <c>detail</c> then <c>title</c> — the single server error shape D037/S02 guarantees.
/// </summary>
public static class ProblemDetailsParser
{
    public static string? ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                return detail.GetString();
            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                return title.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
