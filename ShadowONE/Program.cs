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
            .UsePlatformDetect()
            .UseWaylandWithFallback()
            .WithInterFont()
            .LogToTrace();
}
