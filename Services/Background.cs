using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Fundus.Services;

/// <summary>
/// Win+E-Modus: Hook + Tray-Symbol, das X versteckt das Fenster nur.
/// Autostart (damit es auch nach einem Neustart geht) ist ein eigener Schalter.
/// </summary>
public static class Background
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "Fundus";

    static Forms.NotifyIcon _tray;

    /// <summary>Win+E abfangen, solange Fundus läuft.</summary>
    public static bool IsEnabled => Settings.GetBool("WinE");

    /// <summary>Fundus startet bei der Anmeldung unsichtbar mit.</summary>
    public static bool IsAutostart
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunName) != null;
        }
    }

    public static void Enable(Action show, Action exit)
    {
        Settings.SetBool("WinE", true);
        Start(show, exit);
    }

    public static void Disable()
    {
        Settings.SetBool("WinE", false);
        SetAutostart(null);
        Stop();
    }

    /// <summary>exe = Autostart an, null = aus.</summary>
    public static void SetAutostart(string exe)
    {
        if (exe != null)
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            k.SetValue(RunName, $"\"{exe}\" --background");
        }
        else
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            k?.DeleteValue(RunName, false);
        }
    }

    /// <summary>Hook + Tray aktivieren (beim Programmstart, wenn eingeschaltet).</summary>
    public static void Start(Action show, Action exit)
    {
        WinEHook.Start(show);
        if (_tray != null) return;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Fundus öffnen", null, (s, e) => show());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (s, e) => exit());

        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(App.ExePath),
            Text = "Fundus – Win+E öffnet Fundus",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) show(); };
    }

    public static void Stop()
    {
        WinEHook.Stop();
        if (_tray == null) return;
        _tray.Visible = false;
        _tray.Dispose();
        _tray = null;
    }
}
