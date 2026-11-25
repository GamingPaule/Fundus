using System.Diagnostics;
using System.Windows;
using Fundus.Services;

namespace Fundus;

public partial class App : Application
{
    static readonly HashSet<string> Shown = new();

    public static string ExePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, a) =>
        {
            a.Handled = true;
            // Gleiche Fehlermeldung nur einmal zeigen, sonst stapeln sich Dialoge
            if (Shown.Add(a.Exception.Message))
                MessageBox.Show(a.Exception.Message, "Fundus", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        base.OnStartup(e);

        // Kommandozeile: --register / --unregister (Notfall-Weg zurück zum Explorer)
        if (e.Args.Contains("--unregister"))
        {
            DefaultHandler.Unregister();
            Background.Disable();
            MessageBox.Show("Windows-Explorer ist wieder der Standard.", "Fundus");
            Shutdown();
            return;
        }
        if (e.Args.Contains("--register"))
        {
            DefaultHandler.Register(ExePath);
            MessageBox.Show("Fundus ist jetzt der Standard-Dateimanager (Win+E und Ordner).", "Fundus");
            Shutdown();
            return;
        }

        // --background: Autostart bei der Anmeldung, unsichtbar, nur für Win+E und Tray
        bool background = e.Args.Contains("--background");
        var path = SingleInstance.CleanPath(e.Args.FirstOrDefault(a => !a.StartsWith("--")));
        if (!SingleInstance.Claim(path, forward: !background)) { Shutdown(); return; }

        var win = new MainWindow();
        MainWindow = win;
        SingleInstance.Listen(win.Present);
        if (Background.IsEnabled) Background.Start(() => win.Present(null), win.ExitForReal);
        if (!background) win.Present(path);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Background.Stop(); // Tray-Symbol entfernen, Hook lösen
        base.OnExit(e);
    }
}
