using HSR_Overlay.Ui.Modules;
using HSR_Overlay.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VisibilityEnum = System.Windows.Visibility;

namespace HSR_Overlay;

public partial class MainWindow
{
    // private Settings _settingsDraft = Settings.Current.DeepCopy();

    private void ToggleCategoryPanel(ModuleCategoryViewModel catVm)
    {
        if (!_categoryPanels.TryGetValue(catVm.Category, out var panel))
        {
            // Create a new floating panel for this category
            panel = new CategoryPanelView
            {
                DataContext = catVm,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(
                    OverlayMenuHost.Margin.Left + 230 + _categoryPanels.Count * 240,
                    OverlayMenuHost.Margin.Top,
                    0,
                    0),
                Visibility = Visibility.Collapsed
            };

            _categoryPanels[catVm.Category] = panel;
            RootGrid.Children.Add(panel);
        }

        // Flip open/closed state
        bool opening = !catVm.IsOpen;
        catVm.IsOpen = opening;

        // If the main menu is hidden, keep the panels hidden too;
        if (!_menuVisible)
        {
            panel.Visibility = Visibility.Collapsed;
            return;
        }

        panel.Visibility = opening
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ToggleOverlayMenu()
    {
        SetOverlayMenuVisible(!_menuVisible);
    }

    private void SetOverlayMenuVisible(bool visible)
    {
        _menuVisible = visible;

        OverlayMenuHost.Visibility = visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var kvp in _categoryPanels)
        {
            var panel = kvp.Value;
            if (panel == null) continue;

            if (panel.DataContext is ModuleCategoryViewModel catVm)
            {
                panel.Visibility = visible && catVm.IsOpen
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else
            {
                panel.Visibility = visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
    }

    private void ToggleConfigPanel()
    {
        Log.Debug("debug", "toggling config panel");
        OverlayMenuHost.Visibility =
            OverlayMenuHost.Visibility == VisibilityEnum.Visible
                ? VisibilityEnum.Collapsed
                : VisibilityEnum.Visible;
    }
}
