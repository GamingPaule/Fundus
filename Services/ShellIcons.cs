using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fundus.Services;

/// <summary>Holt die echten Windows-Explorer-Symbole (SHGetFileInfo) mit Cache pro Endung.</summary>
public static class ShellIcons
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_LARGEICON = 0x0,
               SHGFI_USEFILEATTRIBUTES = 0x10, SHGFI_TYPENAME = 0x400;
    const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr h);

    static readonly ConcurrentDictionary<string, ImageSource> Cache = new();

    // Endungen mit individuellen Icons, die nicht pro Endung gecacht werden dürfen
    static readonly HashSet<string> PerFile = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".url", ".appref-ms" };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex, iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
    }

    [DllImport("shell32.dll")]
    static extern int SHGetStockIconInfo(uint id, uint flags, ref SHSTOCKICONINFO info);

    /// <summary>Windows-Systemsymbol, z.B. 94 = Computer ("Dieser PC").</summary>
    static ImageSource Stock(uint id, bool large)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        if (SHGetStockIconInfo(id, SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON), ref info) != 0 || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DestroyIcon(info.hIcon); }
    }

    public static ImageSource Get(string path, bool isDir, bool large = false)
    {
        if (path == "::pc") return Cache.GetOrAdd(large ? "L|::pc" : "S|::pc", _ => Stock(94, large));
        string ext = isDir ? "" : Path.GetExtension(path);
        bool isRoot = isDir && Path.GetPathRoot(path) == path;
        bool special = isRoot || PerFile.Contains(ext) || (isDir && IsSpecialFolder(path));
        string key = (large ? "L|" : "S|") + (special ? path : isDir ? "<dir>" : ext.ToLowerInvariant());
        return Cache.GetOrAdd(key, _ => Load(path, isDir, large, special));
    }

    static bool IsSpecialFolder(string p)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return p.StartsWith(home, StringComparison.OrdinalIgnoreCase) &&
               Path.GetDirectoryName(p)?.Equals(home, StringComparison.OrdinalIgnoreCase) == true
               || p.Equals(home, StringComparison.OrdinalIgnoreCase);
    }

    static ImageSource Load(string path, bool isDir, bool large, bool real)
    {
        var info = new SHFILEINFO();
        uint flags = SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
        if (!real) flags |= SHGFI_USEFILEATTRIBUTES;
        uint attr = isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        SHGetFileInfo(path, attr, ref info, (uint)Marshal.SizeOf(info), flags);
        if (info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DestroyIcon(info.hIcon); }
    }

    static readonly ConcurrentDictionary<string, string> TypeNames = new();

    /// <summary>Windows-Typbezeichnung, z.B. "PNG-Datei".</summary>
    public static string TypeName(string path, bool isDir)
    {
        string key = isDir ? "<dir>" : Path.GetExtension(path).ToLowerInvariant();
        return TypeNames.GetOrAdd(key, _ =>
        {
            var info = new SHFILEINFO();
            SHGetFileInfo(path, isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, ref info,
                (uint)Marshal.SizeOf(info), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES);
            return string.IsNullOrEmpty(info.szTypeName) ? (isDir ? "Ordner" : "Datei") : info.szTypeName;
        });
    }
}
