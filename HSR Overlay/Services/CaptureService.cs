using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services;

internal sealed class CaptureService : IDisposable
{
    // Intentionally minimal for now; you'll wire Windows.Graphics.Capture here.
    public void TryCaptureOnce(IntPtr gameHwnd)
    {
        HSR_Overlay.Util.Log.Trace("CaptureService", $"TryCaptureOnce hwnd=0x{gameHwnd.ToInt64():X}");
    }

    public void Dispose() { /* stop sessions, release textures, etc. */ }
}
