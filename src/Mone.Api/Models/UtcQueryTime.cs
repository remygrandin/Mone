using System.Globalization;

namespace Mone.Api.Models;

/// <summary>
/// A query-string datetime that is always normalized to UTC. A value carrying a timezone
/// offset is converted to UTC; a value without one is assumed to already be UTC. This keeps
/// the API contract unambiguous regardless of how the client formats the instant, and matches
/// the database which stores every timestamp as UTC.
/// </summary>
public readonly struct UtcQueryTime : IParsable<UtcQueryTime>
{
    public DateTimeOffset Utc { get; }

    private UtcQueryTime(DateTimeOffset utc) => Utc = utc;

    public static UtcQueryTime Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var result)
            ? result
            : throw new FormatException($"'{s}' is not a valid datetime.");

    public static bool TryParse(string? s, IFormatProvider? provider, out UtcQueryTime result)
    {
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            result = new UtcQueryTime(dto);
            return true;
        }

        result = default;
        return false;
    }
}
