using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Fundus.Services;

/// <summary>Zuletzt geöffnete Dateien aus dem Windows-Ordner "Recent" (.lnk-Verknüpfungen auflösen).</summary>
public static class RecentFiles
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr fd, uint flags);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistFile
    {
        [PreserveSig] int GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
    }

    public record Entry(string Path, DateTime Used);

    static readonly string RecentDir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    /// <summary>Neueste Dateien (keine Ordner), Duplikate entfernt, nur noch existierende.</summary>
    public static List<Entry> Get(int max)
    {
        var result = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<FileInfo> links;
        try
        {
            links = new DirectoryInfo(RecentDir).EnumerateFiles("*.lnk")
                .OrderByDescending(f => f.LastWriteTime).Take(max * 4).ToList();
        }
        catch { return result; }

        foreach (var lnk in links)
        {
            var target = Resolve(lnk.FullName);
            if (target == null || !seen.Add(target) || !File.Exists(target)) continue;
            result.Add(new Entry(target, lnk.LastWriteTime));
            if (result.Count >= max) break;
        }
        return result;
    }

    static string Resolve(string lnkPath)
    {
        object link = null;
        try
        {
            link = new ShellLink();
            if (((IPersistFile)link).Load(lnkPath, 0) != 0) return null;
            var sb = new StringBuilder(1024);
            return ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0) == 0 && sb.Length > 0 ? sb.ToString() : null;
        }
        catch { return null; }
        finally { if (link != null) Marshal.ReleaseComObject(link); }
    }
}
