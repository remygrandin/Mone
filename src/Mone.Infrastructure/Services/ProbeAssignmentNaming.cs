using System.Globalization;
using System.Text;

namespace Mone.Infrastructure.Services;

public static class ProbeAssignmentNaming
{
    public const int MaxLength = 128;

    public static string ToSnakeCase(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new StringBuilder(input.Length);
        var lastWasUnderscore = true;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (i > 0 && IsCamelBoundary(input, i))
            {
                if (!lastWasUnderscore)
                {
                    sb.Append('_');
                    lastWasUnderscore = true;
                }
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
                lastWasUnderscore = false;
            }
            else
            {
                if (!lastWasUnderscore && sb.Length > 0)
                {
                    sb.Append('_');
                    lastWasUnderscore = true;
                }
            }
        }

        while (sb.Length > 0 && sb[^1] == '_')
            sb.Length--;

        var result = sb.ToString();
        if (result.Length > MaxLength)
            result = result[..MaxLength].TrimEnd('_');

        return result;
    }

    public static bool TryNormalize(string? rawName, out string name, out string snakeCase, out string? error)
    {
        name = string.Empty;
        snakeCase = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(rawName))
        {
            error = "Name is required.";
            return false;
        }

        var trimmed = rawName.Trim();
        if (trimmed.Length > MaxLength)
        {
            error = $"Name must be {MaxLength} characters or fewer.";
            return false;
        }

        var snake = ToSnakeCase(trimmed);
        if (snake.Length == 0)
        {
            error = "Name must contain at least one alphanumeric character.";
            return false;
        }

        name = trimmed;
        snakeCase = snake;
        return true;
    }

    private static bool IsCamelBoundary(string s, int i)
    {
        if (i == 0) return false;
        var prev = s[i - 1];
        var curr = s[i];
        if (char.IsUpper(curr) && char.IsLower(prev)) return true;
        if (char.IsDigit(curr) && char.IsLetter(prev)) return true;
        if (char.IsLetter(curr) && char.IsDigit(prev)) return true;
        return false;
    }
}
