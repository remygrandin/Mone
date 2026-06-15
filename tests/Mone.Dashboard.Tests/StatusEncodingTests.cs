using Mone.Dashboard.Helpers;
using Mone.Dashboard.Models;
using Xunit;

namespace Mone.Dashboard.Tests;

// The objective gate for S03 (R032): proves every MonitoringStatus carries a distinct
// non-color encoding (icon + label), never color alone. Browserless: asserts directly
// over StatusColorHelper.ToIcon/ToLabel (the dual-encoding chokepoint). Enum.GetValues
// drives the cases so a future enum addition is automatically covered.
public class StatusEncodingTests
{
    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Status_HasNonColorEncoding_IconAndLabel(MonitoringStatus status)
    {
        string icon = StatusColorHelper.ToIcon(status);
        string label = StatusColorHelper.ToLabel(status);

        Assert.False(
            string.IsNullOrEmpty(icon),
            $"{status} has no icon — encoded by color alone");
        Assert.False(
            string.IsNullOrEmpty(label),
            $"{status} has no label — encoded by color alone");
    }

    [Fact]
    public void Icons_ArePairwiseDistinct_AcrossAllStatuses()
    {
        var statuses = Enum.GetValues<MonitoringStatus>();
        var icons = statuses.Select(StatusColorHelper.ToIcon).ToHashSet();

        Assert.Equal(statuses.Length, icons.Count);
    }

    [Fact]
    public void Labels_ArePairwiseDistinct_AcrossAllStatuses()
    {
        var statuses = Enum.GetValues<MonitoringStatus>();
        var labels = statuses.Select(StatusColorHelper.ToLabel).ToHashSet();

        Assert.Equal(statuses.Length, labels.Count);
    }

    public static IEnumerable<object[]> AllStatuses() =>
        Enum.GetValues<MonitoringStatus>().Select(s => new object[] { s });
}
