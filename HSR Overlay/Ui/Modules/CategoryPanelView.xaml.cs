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

        // We can handle move/up on the whole control
        this.MouseMove += CategoryPanelView_MouseMove;
        this.MouseLeftButtonUp += CategoryPanelView_MouseLeftButtonUp;
    }

    // Header is the title bar Border in XAML
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        var parent = Parent as FrameworkElement;
        if (parent == null)
            return;

        _dragging = true;
        _dragStart = e.GetPosition(parent);
        _startMargin = this.Margin;

        Mouse.Capture(this);
    }

    private void CategoryPanelView_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;

        var parent = Parent as FrameworkElement;
        if (parent == null)
            return;

        var current = e.GetPosition(parent);
        var dx = current.X - _dragStart.X;
        var dy = current.Y - _dragStart.Y;

        this.Margin = new Thickness(
            _startMargin.Left + dx,
            _startMargin.Top + dy,
            0,
            0);
    }

    private void CategoryPanelView_MouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        Mouse.Capture(null);
    }
}