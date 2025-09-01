using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Util;

internal static class DpiHelper
{
    // px -> DIP given the window’s DPI. (96 DIP == 1 CSS pixel)
    public static double PxToDip(int px, uint dpi)
    {
        if (dpi == 0) dpi = 96;
        return px * (96.0 / dpi);
    }
}
