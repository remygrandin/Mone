using Mone.Dashboard.Services;
using Mone.Dashboard.Theme;
using Xunit;

namespace Mone.Dashboard.Tests;

// Proves ThemeService is the deterministic source of theme state: Mode defaults to System,
// IsDarkMode resolves Light=false/Dark=true regardless of OS preference and System=SystemPrefersDark,
// and Changed fires only on an effective change (never on a no-op SetMode, never on an OS-preference
// change while Mode is Light/Dark because IsDarkMode is unaffected there).
public class ThemeServiceTests
{
    [Fact]
    public void Mode_Defaults_ToSystem()
    {
        Assert.Equal(ThemeMode.System, new ThemeService().Mode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsDarkMode_Light_AlwaysFalse(bool prefersDark)
    {
        var service = new ThemeService();
        service.SetSystemPreference(prefersDark);
        service.SetMode(ThemeMode.Light);

        Assert.False(service.IsDarkMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsDarkMode_Dark_AlwaysTrue(bool prefersDark)
    {
        var service = new ThemeService();
        service.SetSystemPreference(prefersDark);
        service.SetMode(ThemeMode.Dark);

        Assert.True(service.IsDarkMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsDarkMode_System_FollowsSystemPreference(bool prefersDark)
    {
        var service = new ThemeService();
        service.SetSystemPreference(prefersDark);

        Assert.Equal(ThemeMode.System, service.Mode);
        Assert.Equal(prefersDark, service.IsDarkMode);
    }

    [Fact]
    public void SetMode_RaisesChanged_OnceOnRealChange()
    {
        var service = new ThemeService();
        int count = 0;
        service.Changed += () => count++;

        service.SetMode(ThemeMode.Dark);

        Assert.Equal(1, count);
    }

    [Fact]
    public void SetMode_DoesNotRaiseChanged_WhenValueUnchanged()
    {
        var service = new ThemeService();
        int count = 0;
        service.Changed += () => count++;

        service.SetMode(ThemeMode.System); // already System

        Assert.Equal(0, count);
    }

    [Fact]
    public void SetSystemPreference_RaisesChanged_WhenModeIsSystem()
    {
        var service = new ThemeService();
        int count = 0;
        service.Changed += () => count++;

        service.SetSystemPreference(true);

        Assert.Equal(1, count);
    }

    [Fact]
    public void SetSystemPreference_DoesNotRaiseChanged_WhenModeIsLight()
    {
        var service = new ThemeService();
        service.SetMode(ThemeMode.Light);
        int count = 0;
        service.Changed += () => count++;

        service.SetSystemPreference(true);

        Assert.Equal(0, count);
    }

    [Fact]
    public void SetSystemPreference_DoesNotRaiseChanged_WhenModeIsDark()
    {
        var service = new ThemeService();
        service.SetMode(ThemeMode.Dark);
        int count = 0;
        service.Changed += () => count++;

        service.SetSystemPreference(true);

        Assert.Equal(0, count);
    }
}
