using System.Globalization;

namespace Mone.Dashboard.Tests.Support;

// Browserless color-science instrument for the palette gate (D052). Pure, dependency-free,
// and confined to the test project so it never ships in product code. All inputs are
// "#RRGGBB" hex strings. Verified by ColorMathTests against published reference values
// before it is trusted to judge MonePalette in T03.
public static class ColorMath
{
    // Machado et al. (2009) severity-1.0 CVD simulation matrices. Per the authors'
    // supplementary materials these are applied directly to gamma-encoded sRGB values.
    // Each row sums to ~1.0, so the achromatic axis (R==G==B) is preserved exactly.
    private static readonly double[,] DeuteranopiaMatrix =
    {
        { 0.367322, 0.860646, -0.227968 },
        { 0.280085, 0.672501, 0.047413 },
        { -0.011820, 0.042940, 0.968881 },
    };

    private static readonly double[,] ProtanopiaMatrix =
    {
        { 0.152286, 1.052583, -0.204868 },
        { 0.114503, 0.786281, 0.099216 },
        { -0.003882, -0.048116, 1.051998 },
    };

    // D65 reference white for the CIELAB conversion.
    private const double Xn = 0.95047;
    private const double Yn = 1.00000;
    private const double Zn = 1.08883;

    public static (int R, int G, int B) ParseHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var s = hex.StartsWith('#') ? hex[1..] : hex;
        if (s.Length != 6)
        {
            throw new FormatException($"Expected '#RRGGBB' hex color, got '{hex}'.");
        }

        int r = int.Parse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int g = int.Parse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int b = int.Parse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (r, g, b);
    }

    // WCAG sRGB relative luminance: linearize each channel, then weight 0.2126/0.7152/0.0722.
    public static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    // WCAG contrast ratio: (Llighter + 0.05) / (Ldarker + 0.05). Range 1..21.
    public static double ContrastRatio(string hexA, string hexB)
    {
        double la = RelativeLuminance(hexA);
        double lb = RelativeLuminance(hexB);
        double lighter = Math.Max(la, lb);
        double darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static string SimulateDeuteranopia(string hex) => ApplyCvd(DeuteranopiaMatrix, hex);

    public static string SimulateProtanopia(string hex) => ApplyCvd(ProtanopiaMatrix, hex);

    // sRGB -> CIELAB (D65). Returns (L*, a*, b*).
    public static (double L, double A, double B) ToLab(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        double rl = Linearize(r);
        double gl = Linearize(g);
        double bl = Linearize(b);

        double x = 0.4124564 * rl + 0.3575761 * gl + 0.1804375 * bl;
        double y = 0.2126729 * rl + 0.7151522 * gl + 0.0721750 * bl;
        double z = 0.0193339 * rl + 0.1191920 * gl + 0.9503041 * bl;

        double fx = LabF(x / Xn);
        double fy = LabF(y / Yn);
        double fz = LabF(z / Zn);

        double l = 116.0 * fy - 16.0;
        double a = 500.0 * (fx - fy);
        double bb = 200.0 * (fy - fz);
        return (l, a, bb);
    }

    public static double DeltaE76((double L, double A, double B) labA, (double L, double A, double B) labB)
    {
        double dl = labA.L - labB.L;
        double da = labA.A - labB.A;
        double db = labA.B - labB.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    public static double DeltaE76(string hexA, string hexB) => DeltaE76(ToLab(hexA), ToLab(hexB));

    private static double Linearize(int channel)
    {
        double c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double LabF(double t)
    {
        const double delta = 6.0 / 29.0;
        return t > delta * delta * delta
            ? Math.Cbrt(t)
            : t / (3.0 * delta * delta) + 4.0 / 29.0;
    }

    private static string ApplyCvd(double[,] matrix, string hex)
    {
        var (r, g, b) = ParseHex(hex);
        double rn = r / 255.0;
        double gn = g / 255.0;
        double bn = b / 255.0;

        double ro = matrix[0, 0] * rn + matrix[0, 1] * gn + matrix[0, 2] * bn;
        double go = matrix[1, 0] * rn + matrix[1, 1] * gn + matrix[1, 2] * bn;
        double bo = matrix[2, 0] * rn + matrix[2, 1] * gn + matrix[2, 2] * bn;

        return ToHex(ClampChannel(ro), ClampChannel(go), ClampChannel(bo));
    }

    private static int ClampChannel(double normalized)
    {
        int value = (int)Math.Round(normalized * 255.0, MidpointRounding.AwayFromZero);
        return Math.Clamp(value, 0, 255);
    }

    private static string ToHex(int r, int g, int b) =>
        string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
}
