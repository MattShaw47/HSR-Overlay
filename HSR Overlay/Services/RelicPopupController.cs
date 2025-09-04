using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace HSR_Overlay.Services;

internal class RelicPopupController
{
    private readonly FrameworkElement _popup;
    private readonly TextBlock _text;

    public RelicPopupController(FrameworkElement popup, TextBlock text)
    {
        _popup = popup; 
        _text = text;
    }

    public void Show(string message)
    {
        if (_popup.Dispatcher.CheckAccess())
        {
            _text.Text = message ?? string.Empty;
            _popup.Visibility = Visibility.Visible;
        }
        else
        {
            _popup.Dispatcher.Invoke(() => Show(message));
        }
    }

    public void Hide()
    {
        if (_popup.Dispatcher.CheckAccess())
        {
            _popup.Visibility = Visibility.Collapsed;
            _text.Text = string.Empty;
        }
        else
        {
            _popup.Dispatcher.Invoke(Hide);
        }
    }

    public void Update(string message)
    {
        if(_popup.Dispatcher.CheckAccess())
        {
            if (_popup.Visibility != Visibility.Visible)
            {
                _popup.Visibility = Visibility.Visible;
            }
            _text.Text = message ?? string.Empty;
        }
        else
        {
            _popup.Dispatcher.Invoke(() => Update(message));
        }
    }

    public bool isVisible =>
        _popup.Dispatcher.CheckAccess()
            ? _popup.Visibility == Visibility.Visible
            : _popup.Dispatcher.Invoke(() => _popup.Visibility == Visibility.Visible);
}
