using Mone.ScreenshotHarness;
using Xunit;

namespace Mone.Dashboard.Tests;

public class ScreenManifestTests
{
    [Fact]
    public void All_HasTwelveScreens()
    {
        Assert.Equal(12, ScreenManifest.All.Count);
    }

    [Fact]
    public void OnlyHostDetailAndStatusHistory_RequireHostId()
    {
        var withHostId = ScreenManifest.All
            .Where(s => s.RequiresHostId)
            .Select(s => s.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(new[] { "HostDetail", "StatusHistory" }, withHostId);
    }

    [Fact]
    public void OnlyLogin_DoesNotRequireAuth()
    {
        var anonymous = ScreenManifest.All
            .Where(s => !s.RequiresAuth)
            .Select(s => s.Name)
            .ToArray();

        Assert.Equal(new[] { "Login" }, anonymous);
    }

    [Fact]
    public void NonParameterizedRoutes_HaveNoPlaceholder()
    {
        foreach (var screen in ScreenManifest.All.Where(s => !s.RequiresHostId))
        {
            Assert.DoesNotContain("{", screen.Route);
        }
    }

    [Fact]
    public void HostIdRoutes_ContainIdPlaceholder()
    {
        foreach (var screen in ScreenManifest.All.Where(s => s.RequiresHostId))
        {
            Assert.Contains("{id}", screen.Route);
        }
    }
}
