using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ShadowONE
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
                
                var args = desktop.Args ?? Array.Empty<string>();
                if (args.Length > 0)
                {
                    var filePath = args[0];
                    if (filePath.EndsWith(".one", StringComparison.OrdinalIgnoreCase) && 
                        System.IO.File.Exists(filePath))
                    {
                        (desktop.MainWindow as MainWindow)?.OpenOneFile(filePath);
                    }
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
