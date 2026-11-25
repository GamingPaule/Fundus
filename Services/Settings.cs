using Microsoft.Win32;

namespace Fundus.Services;

/// <summary>Kleine Benutzereinstellungen unter HKCU\Software\Fundus.</summary>
public static class Settings
{
    const string Key = @"Software\Fundus";

    public static bool GetBool(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(name) is int v && v != 0;
    }

    public static void SetBool(string name, bool value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        k.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }
}
