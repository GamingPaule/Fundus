using Microsoft.Win32;

namespace Fundus.Services;

/// <summary>
/// Macht Fundus zum Standard-Dateimanager – nur für den aktuellen Benutzer (HKCU), ohne Admin.
/// Rückgängig: Unregister() bzw. "Fundus.exe --unregister".
/// </summary>
public static class DefaultHandler
{
    const string Classes = @"Software\Classes\";
    // "Datei-Explorer öffnen" (Win+E, Taskleisten-Symbol)
    const string WinE = Classes + @"CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}";
    static readonly string[] Targets = { "Directory", "Drive" };
    const string Verb = "fundus";

    public static bool IsRegistered
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Classes + @"Directory\shell");
            return Verb.Equals(k?.GetValue("") as string, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Register(string exe)
    {
        foreach (var t in Targets)
        {
            using (var verb = Registry.CurrentUser.CreateSubKey($@"{Classes}{t}\shell\{Verb}"))
            {
                verb.SetValue("", "In Fundus öffnen");
                verb.SetValue("Icon", $"\"{exe}\",0");
            }
            using (var cmd = Registry.CurrentUser.CreateSubKey($@"{Classes}{t}\shell\{Verb}\command"))
                cmd.SetValue("", $"\"{exe}\" \"%1\"");
            // Standardaktion beim Doppelklick
            using var shell = Registry.CurrentUser.CreateSubKey($@"{Classes}{t}\shell");
            shell.SetValue("", Verb);
        }

        using var wine = Registry.CurrentUser.CreateSubKey(WinE + @"\shell\opennewwindow\command");
        wine.SetValue("", $"\"{exe}\"");
        wine.SetValue("DelegateExecute", "");
    }

    public static void Unregister()
    {
        foreach (var t in Targets)
        {
            using (var shell = Registry.CurrentUser.OpenSubKey($@"{Classes}{t}\shell", writable: true))
            {
                if (Verb.Equals(shell?.GetValue("") as string, StringComparison.OrdinalIgnoreCase))
                    shell.DeleteValue("", false);
            }
            Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}{t}\shell\{Verb}", false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(WinE, false);
    }
}
