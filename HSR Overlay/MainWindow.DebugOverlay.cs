using HSR_Overlay.Services.Probing;
using HSR_Overlay.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MBrushes = System.Windows.Media.Brushes;
using WPoint = System.Windows.Point;
using DRectangle = System.Drawing.Rectangle;
using HSR_Overlay.Interop;

namespace HSR_Overlay;

public partial class MainWindow
{
    private void DrawSentinelPoints(string probeKey, object? payload)
    {
        if (!Settings.Current.DebugVisualizationEnabled) return;
        if (payload is not IEnumerable<SentinelDebugPoint> pts) return;

        _sentinelDebugByProbe[probeKey] = pts is List<SentinelDebugPoint> list
            ? list
            : new List<SentinelDebugPoint>(pts);

        RenderDebugOverlay();
    }

    private void DrawOcrRects(DRectangle[] rects)
    {
        if (!Settings.Current.DebugVisualizationEnabled) return;

        _ocrDebugRects.Clear();
        foreach (var roi in rects)
        {
            if (roi.Width <= 0 || roi.Height <= 0) continue;
            _ocrDebugRects.Add(roi);
        }

        RenderDebugOverlay();
    }

    private void DrawOcrRect(DRectangle roi) => DrawOcrRects(new DRectangle[] { roi });

    private void RenderDebugOverlay()
    {
        if (!Settings.Current.DebugVisualizationEnabled) return;

        ProbeLayer.Children.Clear();

        const double r = 4.0;

        // 1) All sentinel dots from all probes
        foreach (var kvp in _sentinelDebugByProbe)
        {
            foreach (var p in kvp.Value)
            {
                var local = PointFromScreen(new Point(p.Cx, p.Cy));

                var dot = new Ellipse
                {
                    Width = r * 2,
                    Height = r * 2,
                    StrokeThickness = 2,
                    Stroke = p.Match
                        ? Brushes.Lime
                        : Brushes.Red,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(dot, local.X - r);
                Canvas.SetTop(dot, local.Y - r);
                ProbeLayer.Children.Add(dot);
            }
        }

        // 2) All current OCR rectangles
        foreach (var roi in _ocrDebugRects)
        {
            var topLeft = PointFromScreen(new Point(roi.Left, roi.Top));
            var bottomRight = PointFromScreen(new Point(roi.Right, roi.Bottom));

            var w = Math.Max(0, bottomRight.X - topLeft.X);
            var h = Math.Max(0, bottomRight.Y - topLeft.Y);
            if (w <= 0 || h <= 0) continue;

            var rect = new Rectangle
            {
                Width = w,
                Height = h,
                StrokeThickness = 2,
                Stroke = Brushes.Yellow,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(rect, topLeft.X);
            Canvas.SetTop(rect, topLeft.Y);
            ProbeLayer.Children.Add(rect);
        }
    }

    private void ClearDebugOverlayState()
    {
        _sentinelDebugByProbe.Clear();
        _ocrDebugRects.Clear();
        ProbeLayer.Children.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;

        if (msg != WM_NCHITTEST) return IntPtr.Zero;

        switch (msg)
        {
            // temp code for keybinds, unused for now because hsr hides keypresses when in focus
            //case HotKeyNative.WM_HOTKEY:
            //{
            //    int id1 = wParam.ToInt32();
            //    uint vk = (uint)((int)lParam >> 16);
            //    uint mods = (uint)((int)lParam & 0xFFFF);

            //    Log.Debug("overlay", $"WM_HOTKEY id={id1}, vk=0x{vk:X}, mods=0x{mods:X}");
            //    int id = wParam.ToInt32();

            //    if (id == HOTKEY_ID_MENU)
            //    {
            //        ToggleConfigPanel();
            //        handled = true;
            //    }
            //    break;
            //}
            case WM_NCHITTEST:
            {
                int x = (short)((int)lParam & 0xFFFF);
                int y = (short)(((int)lParam >> 16) & 0xFFFF);
                var clientPt = PointFromScreen(new WPoint(x, y));

                DependencyObject? hit = null;
                VisualTreeHelper.HitTest(
                    this,
                    null,
                    r => { hit = r.VisualHit; return HitTestResultBehavior.Stop; },
                    new PointHitTestParameters(clientPt));

                bool overInteractive = false;
                for (var d = hit; d != null; d = VisualTreeHelper.GetParent(d))
                {
                    if (d is FrameworkElement fe && OverlayHitTest.GetIsInteractive(fe))
                    {
                        overInteractive = true;
                        break;
                    }
                }

                handled = true;
                return overInteractive ? (IntPtr)1 /* HTCLIENT */ : (IntPtr)(-1) /* HTTRANSPARENT */;
            }
        }

        return IntPtr.Zero;
    }
}
