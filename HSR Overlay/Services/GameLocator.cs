using HSR_Overlay.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services;

internal sealed class GameLocator
{
    private readonly string _processName;
    private readonly string _titleHint;

    public GameLocator(string processName, string titleHint)
    {
        _processName = processName;
        _titleHint = titleHint ?? string.Empty;
    }

    public IntPtr FindGameWindow()
    {
        foreach (var p in Process.GetProcessesByName(_processName))
            if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;

        // fallback: title match (handles launcher->game transitions sometimes)
        IntPtr found = IntPtr.Zero;
        bool EnumProc(IntPtr hwnd, IntPtr _)
        {
            var t = Win32.GetWindowTextSafe(hwnd);
            if (!string.IsNullOrEmpty(t) && t.IndexOf(_titleHint, StringComparison.OrdinalIgnoreCase) >= 0)
            { found = hwnd; return false; }
            return true;
        }
        // Local EnumWindows wrapper
        NativeEnumWindows(EnumProc);
        return found;
    }

    // Tiny local EnumWindows wrapper
    private static void NativeEnumWindows(Func<IntPtr, IntPtr, bool> callback)
    {
        Win32Enum.EnumWindows((h, l) => callback(h, l), IntPtr.Zero);
    }

    private static class Win32Enum
    {
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    }
}
