using Mone.Dashboard.Tests.Support;
using Xunit;

namespace Mone.Dashboard.Tests;

// Self-tests for the ColorMath instrument (T02). These validate the measuring tool against
// published reference values before T03 uses it to judge the palette. Each message reports
// expected vs actual so a future failure localizes immediately.
public class ColorMathTests
{
    [Fact]
    public void ContrastRatio_BlackOnWhite_Is21()
    {
        double ratio = ColorMath.ContrastRatio("#000000", "#FFFFFF");
        Assert.True(
            Math.Abs(ratio - 21.0) <= 0.1,
            $"ContrastRatio(#000000,#FFFFFF) expected 21 +/-0.1, got {ratio:0.0000}");
    }

    [Fact]
    public void ContrastRatio_IsSymmetric()
    {
        double forward = ColorMath.ContrastRatio("#000000", "#FFFFFF");
        double reverse = ColorMath.ContrastRatio("#FFFFFF", "#000000");
        Assert.True(
            Math.Abs(forward - reverse) <= 1e-9,
            $"ContrastRatio must be order-independent; got forward {forward:0.0000} vs reverse {reverse:0.0000}");
    }

    [Theory]
    [InlineData("#808080")]
    [InlineData("#0F7A52")]
    [InlineData("#FFFFFF")]
    public void ContrastRatio_IdenticalColors_Is1(string hex)
    {
        double ratio = ColorMath.ContrastRatio(hex, hex);
        Assert.True(
            Math.Abs(ratio - 1.0) <= 1e-9,
            $"ContrastRatio({hex},{hex}) expected 1, got {ratio:0.000000}");
    }

    [Theory]
    [InlineData("#808080")]
    [InlineData("#404040")]
    [InlineData("#C0C0C0")]
    public void GreyIsEffectivelyUnchangedByBothCvdSimulations(string grey)
    {
        string deuter = ColorMath.SimulateDeuteranopia(grey);
        string protan = ColorMath.SimulateProtanopia(grey);
        double deuterDelta = ColorMath.DeltaE76(grey, deuter);
        double protanDelta = ColorMath.DeltaE76(grey, protan);

        Assert.True(
            deuterDelta < 1.0,
            $"Grey {grey} should be near-unchanged under deuteranopia (deltaE < 1.0); got {deuter} deltaE {deuterDelta:0.0000}");
        Assert.True(
            protanDelta < 1.0,
            $"Grey {grey} should be near-unchanged under protanopia (deltaE < 1.0); got {protan} deltaE {protanDelta:0.0000}");
    }

    [Fact]
    public void DeltaE76_IdenticalColors_IsZero()
    {
        double delta = ColorMath.DeltaE76("#0F7A52", "#0F7A52");
        Assert.True(
            delta <= 1e-9,
            $"DeltaE76 of identical colors expected 0, got {delta:0.000000}");
    }

    [Fact]
    public void WhiteStaysNearWhiteUnderDeuteranopia()
    {
        string simulated = ColorMath.SimulateDeuteranopia("#FFFFFF");
        double delta = ColorMath.DeltaE76("#FFFFFF", simulated);
        Assert.True(
            delta < 1.0,
            $"White (#FFFFFF) should stay near white under deuteranopia (deltaE < 1.0); got {simulated} deltaE {delta:0.0000}");
    }

    [Fact]
    public void WhiteStaysNearWhiteUnderProtanopia()
    {
        string simulated = ColorMath.SimulateProtanopia("#FFFFFF");
        double delta = ColorMath.DeltaE76("#FFFFFF", simulated);
        Assert.True(
            delta < 1.0,
            $"White (#FFFFFF) should stay near white under protanopia (deltaE < 1.0); got {simulated} deltaE {delta:0.0000}");
    }

    [Fact]
    public void Lab_PureWhite_IsApproximatelyL100()
    {
        var (l, a, b) = ColorMath.ToLab("#FFFFFF");
        Assert.True(
            Math.Abs(l - 100.0) <= 0.1 && Math.Abs(a) <= 0.5 && Math.Abs(b) <= 0.5,
            $"Lab(#FFFFFF) expected ~(100,0,0), got ({l:0.00},{a:0.00},{b:0.00})");
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF")]
    [InlineData("#0F7A52", "#9E1B12")]
    public void DeltaE76_DistinctColors_IsPositive(string hexA, string hexB)
    {
        double delta = ColorMath.DeltaE76(hexA, hexB);
        Assert.True(
            delta > 0.0,
            $"DeltaE76({hexA},{hexB}) expected > 0 for distinct colors, got {delta:0.0000}");
    }
}
