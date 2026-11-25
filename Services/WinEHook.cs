using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Fundus.Services;

/// <summary>
/// Fängt Win+E systemweit ab (Low-Level-Tastatur-Hook), bevor der Explorer es sieht.
/// Win 11 25H2 fragt für Win+E keine Registry mehr ab – das ist der einzige Weg.
/// Läuft Fundus nicht, öffnet Win+E ganz normal den Explorer.
/// </summary>
public static class WinEHook
{
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const uint VK_E = 0x45;
    const byte VK_MASK = 0xE8; // unbelegte Taste: verhindert, dass beim Loslassen von Win das Startmenü aufgeht

    static readonly HookProc Proc = Callback; // Referenz halten, sonst räumt der GC den Delegate weg
    static IntPtr _hook;
    static bool _eSwallowed;
    static Action _onWinE;
    static Dispatcher _ui;

    public static bool IsActive => _hook != IntPtr.Zero;

    public static void Start(Action onWinE)
    {
        if (IsActive) return;
        _onWinE = onWinE;
        _ui = Dispatcher.CurrentDispatcher;
        using var me = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(me?.ModuleName), 0);
    }

    public static void Stop()
    {
        if (!IsActive) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            // auch simulierte Tasten (AutoHotkey, Makro-Tastaturen) – unsere eigene Maskiertaste ist nicht E
            if (k.vkCode == VK_E)
            {
                bool keyDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
                if (keyDown)
                {
                    bool win = Down(0x5B) || Down(0x5C);
                    bool other = Down(0x10) || Down(0x11) || Down(0x12); // Shift/Strg/Alt → nicht unser Kürzel
                    if (win && !other)
                    {
                        if (!_eSwallowed) // Tastenwiederholung beim Gedrückthalten ignorieren
                        {
                            _eSwallowed = true;
                            keybd_event(VK_MASK, 0, 0, UIntPtr.Zero);
                            keybd_event(VK_MASK, 0, 2, UIntPtr.Zero);
                            _ui.BeginInvoke(_onWinE);
                        }
                        return (IntPtr)1;
                    }
                }
                else if (_eSwallowed)
                {
                    _eSwallowed = false;
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }
}
