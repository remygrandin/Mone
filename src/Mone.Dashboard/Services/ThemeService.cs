using Mone.Dashboard.Theme;

namespace Mone.Dashboard.Services;

/// <summary>
/// In-memory source of truth for the operator's Light/Dark/System theme mode. Resolves the
/// effective dark-mode boolean and notifies the layout when the effective value changes. S02
/// keeps this in memory only; S04 persists Mode to the profile and S05 consumes it on login.
/// </summary>
public sealed class ThemeService
{
    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public bool SystemPrefersDark { get; private set; }

    public bool IsDarkMode => Mode switch
    {
        ThemeMode.Light => false,
        ThemeMode.Dark => true,
        _ => SystemPrefersDark,
    };

    public event Action? Changed;

    public void SetMode(ThemeMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        Changed?.Invoke();
    }

    public void SetSystemPreference(bool prefersDark)
    {
        if (SystemPrefersDark == prefersDark) return;
        SystemPrefersDark = prefersDark;
        // Only the System mode derives IsDarkMode from the OS preference, so a change while in
        // Light/Dark leaves the effective theme untouched and must not notify the layout.
        if (Mode == ThemeMode.System)
        {
            Changed?.Invoke();
        }
    }
}
