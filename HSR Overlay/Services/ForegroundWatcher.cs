using HSR_Overlay.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services;

internal sealed class ForegroundWatcher : IDisposable
{
    private readonly Func<IntPtr, bool> _belongsToGame;
    private readonly Action _onGameFg, _onOtherFg;

    private Win32.WinEventDelegate? _proc;
    private IntPtr _hook = IntPtr.Zero;

    public ForegroundWatcher(Func<IntPtr, bool> belongsToGame, Action onGameForeground, Action onOtherForeground)
    {
        _belongsToGame = belongsToGame;
        _onGameFg = onGameForeground;
        _onOtherFg = onOtherForeground;
    }

    public void Start()
    {
        _proc = OnWinEvent;
        _hook = Win32.SetWinEventHook(
            Win32Constants.EVENT_SYSTEM_FOREGROUND,
            Win32Constants.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0,
            Win32Constants.WINEVENT_OUTOFCONTEXT | Win32Constants.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) { _onOtherFg(); return; }
        var root = Win32.GetAncestor(hwnd, Win32Constants.GA_ROOT);
        if (root == IntPtr.Zero) { _onOtherFg(); return; }

        if (_belongsToGame(root)) _onGameFg();
        else _onOtherFg();
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            Win32.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
