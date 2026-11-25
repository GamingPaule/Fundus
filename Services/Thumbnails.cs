using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Fundus.Services;

/// <summary>
/// Lädt Vorschaubilder über den Windows-Thumbnail-Cache (schnell, auch Videos/PDFs) auf eigenen STA-Threads.
/// Zuletzt angefragte Bilder (= gerade sichtbare) kommen zuerst dran.
/// </summary>
public static class Thumbnails
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbitmap);
    }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                                                   [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, ref BITMAP bm);

    const int SIIGBF_THUMBNAILONLY = 0x8, SIIGBF_BIGGERSIZEOK = 0x1;

    static readonly HashSet<string> Ext = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".jfif", ".tif", ".tiff", ".ico", ".heic",
        ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v", ".pdf", ".psd", ".svg",
        ".mp3", ".flac", ".m4a", ".docx", ".pptx", ".xlsx", ".blend", ".stl", ".3mf",
    };

    static readonly ConcurrentDictionary<string, ImageSource> Cache = new();
    static readonly ConcurrentStack<(string path, int size, Action<ImageSource> done)> Jobs = new();
    static readonly SemaphoreSlim Signal = new(0);
    static Dispatcher _ui;

    static Thumbnails()
    {
        for (int i = 0; i < 3; i++)
        {
            var t = new Thread(Worker) { IsBackground = true, Name = "Thumbnails" + i, Priority = ThreadPriority.BelowNormal };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }

    public static bool HasThumb(string path) => Ext.Contains(Path.GetExtension(path));

    public static void Request(string path, int size, Action<ImageSource> done)
    {
        if (!HasThumb(path)) return;
        if (Cache.TryGetValue(path, out var img)) { done(img); return; }
        _ui ??= Application.Current.Dispatcher;
        Jobs.Push((path, size, done));
        Signal.Release();
    }

    static void Worker()
    {
        while (true)
        {
            Signal.Wait();
            if (!Jobs.TryPop(out var job)) continue;
            ImageSource img = null;
            try { img = Cache.GetOrAdd(job.path, p => Load(p, job.size)); }
            catch { }
            if (img != null) _ui.InvokeAsync(() => job.done(img), DispatcherPriority.Background);
        }
    }

    static ImageSource Load(string path, int size)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
        try
        {
            if (factory.GetImage(new SIZE { cx = size * 2, cy = size * 2 }, SIIGBF_THUMBNAILONLY | SIIGBF_BIGGERSIZEOK, out var hbmp) != 0)
                return null;
            try { return FromHBitmap(hbmp); }
            finally { DeleteObject(hbmp); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    /// <summary>HBITMAP → BitmapSource mit Transparenz (CreateBitmapSourceFromHBitmap würde Alpha verwerfen).</summary>
    static BitmapSource FromHBitmap(IntPtr hbmp)
    {
        var bm = new BITMAP();
        GetObject(hbmp, Marshal.SizeOf<BITMAP>(), ref bm);
        if (bm.bmBits == IntPtr.Zero || bm.bmBitsPixel != 32)
        {
            var fallback = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            fallback.Freeze();
            return fallback;
        }
        int stride = bm.bmWidthBytes, h = Math.Abs(bm.bmHeight);
        var bytes = new byte[stride * h];
        Marshal.Copy(bm.bmBits, bytes, 0, bytes.Length);

        // Shell-DIBs sind "bottom-up" → Zeilen umdrehen
        var flipped = new byte[bytes.Length];
        for (int y = 0; y < h; y++) Buffer.BlockCopy(bytes, y * stride, flipped, (h - 1 - y) * stride, stride);

        var src = BitmapSource.Create(bm.bmWidth, h, 96, 96, PixelFormats.Pbgra32, null, flipped, stride);
        src.Freeze();
        return src;
    }
}
