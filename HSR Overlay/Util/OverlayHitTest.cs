using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace HSR_Overlay.Util;

public static class OverlayHitTest
{
    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.RegisterAttached(
            "IsInteractive",
            typeof(bool),
            typeof(OverlayHitTest),
            new FrameworkPropertyMetadata(false));

    public static void SetIsInteractive(DependencyObject d, bool value) => d.SetValue(IsInteractiveProperty, value);
    public static bool GetIsInteractive(DependencyObject d) => (bool)d.GetValue(IsInteractiveProperty);
}
