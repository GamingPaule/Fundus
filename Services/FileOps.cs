using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Fundus.Services;

/// <summary>Datei-Operationen über die Windows-Shell: echte Fortschritts-/Konfliktdialoge, Papierkorb, Undo-fähig.</summary>
public static class FileOps
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    const uint FO_MOVE = 1, FO_COPY = 2, FO_DELETE = 3;
    const ushort FOF_ALLOWUNDO = 0x40, FOF_RENAMEONCOLLISION = 0x8, FOF_NOCONFIRMATION = 0x10, FOF_WANTNUKEWARNING = 0x4000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    static string Multi(IEnumerable<string> paths) => string.Join("\0", paths) + "\0\0";

    static bool Run(IntPtr hwnd, uint func, IEnumerable<string> from, string to, ushort flags)
    {
        var op = new SHFILEOPSTRUCT
        {
            hwnd = hwnd, wFunc = func, pFrom = Multi(from), pTo = to == null ? null : to + "\0\0",
            fFlags = (ushort)(flags | FOF_ALLOWUNDO),
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    public static bool Recycle(IntPtr hwnd, IEnumerable<string> paths) =>
        Run(hwnd, FO_DELETE, paths, null, FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING);

    public static bool Copy(IntPtr hwnd, IEnumerable<string> paths, string targetDir)
    {
        // Gleicher Ordner → automatisch "Kopie" anlegen statt Konfliktdialog
        bool same = paths.All(p => string.Equals(Path.GetDirectoryName(p), targetDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        return Run(hwnd, FO_COPY, paths, targetDir, same ? FOF_RENAMEONCOLLISION : (ushort)0);
    }

    public static bool Move(IntPtr hwnd, IEnumerable<string> paths, string targetDir) =>
        Run(hwnd, FO_MOVE, paths, targetDir, 0);

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Öffnen fehlgeschlagen"); }
    }

    public static void OpenWith(string path) =>
        Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}"));

    public static void ShowInExplorer(string path)
    {
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else Process.Start("explorer.exe", path == "::pc" ? "shell:MyComputerFolder" : $"\"{path}\"");
    }

    public static void OpenTerminal(string dir)
    {
        if (dir == "::pc") dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try { Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{dir.TrimEnd('\\')}\"") { UseShellExecute = true }); }
        catch { Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = dir, UseShellExecute = true }); }
    }

    // ---------- Eigenschaften-Dialog ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon, hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    public static void Properties(string path)
    {
        var info = new SHELLEXECUTEINFO { lpVerb = "properties", lpFile = path, nShow = 5, fMask = 0xC };
        info.cbSize = Marshal.SizeOf(info);
        ShellExecuteEx(ref info);
    }

    /// <summary>Freien Namen finden: "Neuer Ordner", "Neuer Ordner 2", …</summary>
    public static string UniqueName(string dir, string name)
    {
        string baseName = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        string candidate = Path.Combine(dir, name);
        for (int i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{baseName} {i}{ext}");
        return candidate;
    }
}
