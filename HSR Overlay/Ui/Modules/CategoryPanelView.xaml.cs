using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HSR_Overlay.Ui.Modules;

/// <summary>
/// Interaction logic for CategoryPanelView.xaml
/// </summary>
public partial class CategoryPanelView : UserControl
{
    private bool _dragging;
    private Point _dragStart;
    private Thickness _startMargin;

    public CategoryPanelView()
    {
        InitializeComponent();
        Root.MouseMove += Root_MouseMove;
        Root.MouseLeftButtonUp += Root_MouseLeftButtonUp;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        _dragging = true;
        _dragStart = e.GetPosition((UIElement)(Parent ?? this));
        _startMargin = Root.Margin;
        Mouse.Capture(Root);
    }

    private void Root_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        var current = e.GetPosition((UIElement)(Parent ?? this));
        var dx = current.X - _dragStart.X;
        var dy = current.Y - _dragStart.Y;

        Root.Margin = new Thickness(
            _startMargin.Left + dx,
            _startMargin.Top + dy,
            0,
            0);
    }

    private void Root_MouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Mouse.Capture(null);
    }
}
