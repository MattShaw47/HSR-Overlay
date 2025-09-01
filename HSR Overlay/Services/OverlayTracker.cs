using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services;

internal sealed class OverlayTracker
{
    private readonly Func<uint> _getDpi;
    private readonly Func<int, uint, double> _pxToDip;
    private readonly Action<double, double, double, double> _apply;
    private readonly Action _hide;

    public OverlayTracker(Func<uint> getDpi,
                          Func<int, uint, double> pxToDip,
                          Action<double, double, double, double> onApplyBounds,
                          Action onHide)
    {
        _getDpi = getDpi;
        _pxToDip = pxToDip;
        _apply = onApplyBounds;
        _hide = onHide;
    }

    public void ApplyBounds(double left, double top, double width, double height)
        => _apply(left, top, width, height);

    public void HideOverlay() => _hide();
}
