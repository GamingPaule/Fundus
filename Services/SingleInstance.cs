using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows;

namespace Fundus.Services;

/// <summary>
/// Nur ein Fundus-Prozess: weitere Starts (Win+E, Doppelklick auf Ordner) schicken ihren Pfad
/// an die laufende Instanz, die ihn als neuen Tab öffnet.
/// </summary>
public static class SingleInstance
{
    static readonly string Name = "Fundus.SingleInstance." + Environment.UserName;
    static Mutex _mutex;

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

    /// <summary>true = wir sind die erste Instanz. Sonst wurde der Pfad weitergereicht.</summary>
    public static bool Claim(string path, bool forward = true)
    {
        _mutex = new Mutex(true, Name, out bool first);
        if (first) return true;
        if (!forward) return false; // z.B. Autostart, obwohl Fundus schon läuft
        try
        {
            AllowSetForegroundWindow(-1); // laufende Instanz darf sich nach vorne holen
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            pipe.Connect(3000);
            using var w = new StreamWriter(pipe);
            w.WriteLine(path ?? "");
            w.Flush();
            return false;
        }
        catch { return true; } // Instanz hängt → lieber eigenes Fenster öffnen
    }

    /// <summary>Wartet im Hintergrund auf Pfade von weiteren Starts.</summary>
    public static void Listen(Action<string> onPath)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1);
                    pipe.WaitForConnection();
                    using var r = new StreamReader(pipe);
                    var line = r.ReadLine();
                    Application.Current.Dispatcher.InvokeAsync(() => onPath(string.IsNullOrWhiteSpace(line) ? null : line));
                }
                catch { Thread.Sleep(200); }
            }
        }) { IsBackground = true, Name = "SingleInstance" };
        t.Start();
    }

    /// <summary>Explorer übergibt Laufwerke manchmal als C:" (wegen "%1" mit Backslash am Ende).</summary>
    public static string CleanPath(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return null;
        var p = arg.Trim().Trim('"');
        if (p.Length == 2 && p[1] == ':') p += "\\";
        return p;
    }
}
