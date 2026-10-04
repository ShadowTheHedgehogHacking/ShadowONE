using System;
using Avalonia;

namespace ShadowONE;

// ReSharper disable once ClassNeverInstantiated.Global
internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            //.UseWaylandWithFallback() // Disabling Wayland for now, too buggy with Rename dialog etc
            .WithInterFont()
            .LogToTrace();
}
