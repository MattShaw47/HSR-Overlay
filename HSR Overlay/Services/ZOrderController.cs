using HSR_Overlay.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services;

internal sealed class ZOrderController
{
    private bool _isTopmost;

    public void ForceRaiseTopmost(IntPtr overlayHwnd)
    {
        // exit topmost, then re-enter to become last in the band
        Win32.SetWindowPos(overlayHwnd, Win32Constants.HWND_NOTOPMOST, 0, 0, 0, 0,
            Win32Constants.SWP_NOMOVE | Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOACTIVATE | Win32Constants.SWP_NOSENDCHANGING);

        Win32.SetWindowPos(overlayHwnd, Win32Constants.HWND_TOPMOST, 0, 0, 0, 0,
            Win32Constants.SWP_NOMOVE | Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOACTIVATE | Win32Constants.SWP_NOSENDCHANGING | Win32Constants.SWP_SHOWWINDOW);

        _isTopmost = true;
    }

    public void SetOverlayTopmost(IntPtr overlayHwnd, bool on)
    {
        if (on) { ForceRaiseTopmost(overlayHwnd); return; }
        if (!_isTopmost) return;

        HSR_Overlay.Util.Log.Trace("Z", "ForceRaiseTopmost");
        Win32.SetWindowPos(overlayHwnd, Win32Constants.HWND_NOTOPMOST, 0, 0, 0, 0,
            Win32Constants.SWP_NOMOVE | Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOACTIVATE | Win32Constants.SWP_NOSENDCHANGING);
        _isTopmost = false;
    }
}
