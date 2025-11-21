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

namespace HSR_Overlay;

public partial class MainWindow
{
    // optional fields:
    // private SentinelDebugPoint[]? _lastSentinelDebug;
    // private DRectangle _lastOcrRect;
    // private bool _showProbeMarkers;

    private void DrawSentinelPoints(object? payload)
    {
        if (!Settings.Current.DebugVisualizationEnabled) return;
        if (payload is not IEnumerable<SentinelDebugPoint> pts) return;

        _lastSentinelDebug = pts.ToArray();
        RenderDebugOverlay();
    }

    private void DrawOcrRects(DRectangle[] rects)
    {
        if (!Settings.Current.DebugVisualizationEnabled) return;
        //if (roi.Width <= 0 || roi.Height <= 0) return;

        _lastOcrRects.Clear();

        foreach (var r in rects)
        {
            if (r.Width <= 0 || r.Height <= 0) continue;
            _lastOcrRects.Add(r);
        }

        //if (_lastOcrRects.Count > 0)
            RenderDebugOverlay();
    }

    private void DrawOcrRect(DRectangle roi) => DrawOcrRects(new DRectangle[] { roi });

    private void RenderDebugOverlay()
    {
        if (!Settings.Current.DebugVisualizationEnabled) return;

        Dispatcher.Invoke(() =>
        {
            ProbeLayer.Children.Clear();

            const double r = 4.0;

            // sentinel dots
            if (_lastSentinelDebug is not null)
            {
                foreach (var p in _lastSentinelDebug)
                {
                    var local = PointFromScreen(new WPoint(p.Cx, p.Cy));
                    var dot = new Ellipse
                    {
                        Width = r * 2,
                        Height = r * 2,
                        StrokeThickness = 2,
                        Stroke = p.Match ? MBrushes.Lime : MBrushes.Red,
                        Fill = MBrushes.Transparent,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(dot, local.X - r);
                    Canvas.SetTop(dot, local.Y - r);
                    ProbeLayer.Children.Add(dot);
                }
            }

            // OCR rectangles
            if (_lastOcrRects != null && _lastOcrRects.Count > 0)
            {
                foreach (var roi in _lastOcrRects)
                {
                    if (roi.Width <= 0 || roi.Height <= 0)
                        continue;

                    var topLeft = PointFromScreen(new WPoint(roi.Left, roi.Top));
                    var bottomRight = PointFromScreen(new WPoint(roi.Right, roi.Bottom));

                    var w = Math.Max(0, bottomRight.X - topLeft.X);
                    var h = Math.Max(0, bottomRight.Y - topLeft.Y);

                    var rect = new Rectangle
                    {
                        Width = w,
                        Height = h,
                        StrokeThickness = 2,
                        Stroke = MBrushes.Yellow,
                        Fill = Brushes.Transparent,
                        IsHitTestVisible = false
                    };

                    Canvas.SetLeft(rect, topLeft.X);
                    Canvas.SetTop(rect, topLeft.Y);
                    ProbeLayer.Children.Add(rect);
                }
            }
        });
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        if (msg != WM_NCHITTEST) return IntPtr.Zero;

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
