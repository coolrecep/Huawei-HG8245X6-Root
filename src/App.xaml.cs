using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace HekzOntRace;

public partial class App : Application
{
    private static readonly Mutex _tek = new Mutex(initiallyOwned: false, "HekzOntRace_TekOturum");

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!_tek.WaitOne(0))
        {
            MessageBox.Show("HekzOntRace zaten çalışıyor.\n\nMevcut pencereyi kullanın.", "HekzOntRace",
                MessageBoxButton.OK, MessageBoxImage.Asterisk);
            Shutdown();
        }
        else
        {
            base.OnStartup(e);
        }
    }

    public static void YenidenBaslat()
    {
        string processPath = Environment.ProcessPath;
        try { _tek.ReleaseMutex(); } catch { }
        if (!string.IsNullOrEmpty(processPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = processPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(processPath) ?? ""
                });
            }
            catch { }
        }
        Thread.Sleep(800);
        Application.Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tek.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
