using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace HekzOntRace.Linux;

public partial class LinuxApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new LinuxMainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void YenidenBaslat()
    {
        try
        {
            string exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
            }
        }
        catch { }
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
