using System;
using Avalonia;
using ShadowONE.Services;

namespace ShadowONE;

// ReSharper disable once ClassNeverInstantiated.Global
internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        FileAssociationService.RegisterFileAssociation();
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetectPreferWayland()
            .WithInterFont()
            .LogToTrace();
}

internal static class LinuxPlatformDetector
{
    public static AppBuilder UsePlatformDetectPreferWayland(this AppBuilder builder)
    {
        if (!OperatingSystem.IsLinux())
            return builder.UsePlatformDetect();

        if (IsWaylandSession())
            builder.UseWayland();
        else
            builder.UseX11();

        builder.UseSkia().UseHarfBuzz();
        return builder;
    }

    private static bool IsWaylandSession()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
           || string.Equals(
               Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
               "wayland",
               StringComparison.OrdinalIgnoreCase);
}