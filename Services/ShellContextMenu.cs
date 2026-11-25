using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Fundus.Services;

/// <summary>Zeigt das originale Explorer-Kontextmenü (inkl. Erweiterungen wie 7-Zip, „Senden an", „Öffnen mit").</summary>
public static class ShellContextMenu
{
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName();
        [PreserveSig] int EnumObjects();
        [PreserveSig] int BindToObject();
        [PreserveSig] int BindToStorage();
        [PreserveSig] int CompareIDs();
        [PreserveSig] int CreateViewObject();
        [PreserveSig] int GetAttributesOf();
        [PreserveSig] int GetUIObjectOf(IntPtr hwnd, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
                                        ref Guid riid, IntPtr reserved, out IntPtr ppv);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CMINVOKECOMMANDINFO
    {
        public int cbSize, fMask;
        public IntPtr hwnd, lpVerb, lpParameters, lpDirectory;
        public int nShow, dwHotKey;
        public IntPtr hIcon;
    }

    [ComImport, Guid("BCFCE0A0-EC17-11d0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
        [PreserveSig] int GetCommandString(UIntPtr cmd, uint flags, IntPtr reserved, IntPtr buf, int cch);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr w, IntPtr l);
        [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr w, IntPtr l, out IntPtr result);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")]
    static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr pidlLast);
    [DllImport("shell32.dll")] static extern void ILFree(IntPtr pidl);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr h);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr h, uint flags, int x, int y, IntPtr hwnd, IntPtr p);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    struct POINT { public int X, Y; }

    const uint CMF_NORMAL = 0, CMF_EXPLORE = 4, CMF_CANRENAME = 0x10, CMF_EXTENDEDVERBS = 0x100;
    const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    const int FirstCmd = 1;

    static IContextMenu3 _cm3;

    public static void Show(IntPtr hwnd, IList<string> paths, bool extended)
    {
        var pidls = new List<IntPtr>();
        var children = new List<IntPtr>();
        object parentObj = null;
        IntPtr pcm = IntPtr.Zero, menu = IntPtr.Zero;
        HwndSource src = HwndSource.FromHwnd(hwnd);
        try
        {
            Guid iidFolder = typeof(IShellFolder).GUID;
            foreach (var p in paths)
            {
                if (SHParseDisplayName(p, IntPtr.Zero, out var pidl, 0, out _) != 0) return;
                pidls.Add(pidl);
                if (SHBindToParent(pidl, ref iidFolder, out var ppv, out var last) != 0) return;
                children.Add(last);
                if (parentObj == null) parentObj = Marshal.GetObjectForIUnknown(ppv);
                Marshal.Release(ppv);
            }
            var parent = (IShellFolder)parentObj;
            Guid iidMenu = typeof(IContextMenu).GUID;
            if (parent.GetUIObjectOf(hwnd, (uint)children.Count, children.ToArray(), ref iidMenu, IntPtr.Zero, out pcm) != 0) return;

            var cm = (IContextMenu)Marshal.GetObjectForIUnknown(pcm);
            _cm3 = cm as IContextMenu3;
            menu = CreatePopupMenu();
            cm.QueryContextMenu(menu, 0, FirstCmd, 0x7FFF, CMF_NORMAL | CMF_EXPLORE | CMF_CANRENAME | (extended ? CMF_EXTENDEDVERBS : 0));

            src?.AddHook(Hook);
            GetCursorPos(out var pt);
            int cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, hwnd, IntPtr.Zero);
            src?.RemoveHook(Hook);

            if (cmd >= FirstCmd)
            {
                var info = new CMINVOKECOMMANDINFO { hwnd = hwnd, lpVerb = (IntPtr)(cmd - FirstCmd), nShow = 1 };
                info.cbSize = Marshal.SizeOf(info);
                cm.InvokeCommand(ref info);
            }
        }
        catch { }
        finally
        {
            _cm3 = null;
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (pcm != IntPtr.Zero) Marshal.Release(pcm);
            foreach (var p in pidls) ILFree(p);
        }
    }

    /// <summary>Leitet Menü-Nachrichten weiter, damit Untermenüs („Senden an", „Öffnen mit") gefüllt werden.</summary>
    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        const int WM_INITMENUPOPUP = 0x117, WM_DRAWITEM = 0x2B, WM_MEASUREITEM = 0x2C, WM_MENUCHAR = 0x120;
        if (_cm3 != null && msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
        {
            if (_cm3.HandleMenuMsg2((uint)msg, w, l, out var result) == 0)
            {
                handled = true;
                return result;
            }
        }
        return IntPtr.Zero;
    }
}
