using Mone.Dashboard.Models;
using MudBlazor;

namespace Mone.Dashboard.Theme;

// Maps S01's colorblind-safe MonePalette hex tokens (D051/D052) into a MudTheme so MudBlazor
// components using Color.Success/Warning/Error/Dark render the CVD-safe hues. The four
// operational statuses bind to the SAME semantic slots StatusColorHelper uses:
// Success=Healthy, Warning=Degraded, Error=Unhealthy, Dark=Unreachable. PaletteLight draws
// from LightStatusColors, PaletteDark from DarkStatusColors. No color-science lives here
// (D052) — these are plain token assignments; the test project is the gate on the values.
public static class MoneTheme
{
    private static readonly string[] SansStack = ["IBM Plex Sans", "system-ui", "Segoe UI", "sans-serif"];

    public static MudTheme Instance { get; } = new MudTheme
    {
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = SansStack },
            H1 = new H1Typography { FontFamily = SansStack },
            H2 = new H2Typography { FontFamily = SansStack },
            H3 = new H3Typography { FontFamily = SansStack },
            H4 = new H4Typography { FontFamily = SansStack },
            H5 = new H5Typography { FontFamily = SansStack },
            H6 = new H6Typography { FontFamily = SansStack },
            Subtitle1 = new Subtitle1Typography { FontFamily = SansStack },
            Subtitle2 = new Subtitle2Typography { FontFamily = SansStack },
            Body1 = new Body1Typography { FontFamily = SansStack },
            Body2 = new Body2Typography { FontFamily = SansStack },
            Button = new ButtonTypography { FontFamily = SansStack },
            Caption = new CaptionTypography { FontFamily = SansStack },
            Overline = new OverlineTypography { FontFamily = SansStack },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            DrawerWidthLeft = "248px",
        },
        PaletteLight = new PaletteLight
        {
            Success = MonePalette.LightStatusColors[MonitoringStatus.Healthy],
            Warning = MonePalette.LightStatusColors[MonitoringStatus.Degraded],
            Error = MonePalette.LightStatusColors[MonitoringStatus.Unhealthy],
            Dark = MonePalette.LightStatusColors[MonitoringStatus.Unreachable],
            Surface = MonePalette.SurfaceLight,
            Background = MonePalette.SurfaceLight,
        },
        PaletteDark = new PaletteDark
        {
            Success = MonePalette.DarkStatusColors[MonitoringStatus.Healthy],
            Warning = MonePalette.DarkStatusColors[MonitoringStatus.Degraded],
            Error = MonePalette.DarkStatusColors[MonitoringStatus.Unhealthy],
            Dark = MonePalette.DarkStatusColors[MonitoringStatus.Unreachable],
            Surface = MonePalette.SurfaceDark,
            Background = MonePalette.SurfaceDark,
        },
    };
}
