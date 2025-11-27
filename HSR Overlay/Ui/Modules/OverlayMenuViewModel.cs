using HSR_Overlay.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace HSR_Overlay.Ui.Modules;

internal class OverlayMenuViewModel : INotifyPropertyChanged
{
    private readonly Action _applySettings;
    private readonly Action<ModuleCategoryViewModel> _toggleCategoryPanel;

    public ObservableCollection<ModuleCategoryViewModel> Categories { get; } = new();

    public ICommand SelectCategoryCommand { get; }

    public OverlayMenuViewModel(Action applySettings,
                                Action<ModuleCategoryViewModel> toggleCategoryPanel)
    {
        _applySettings = applySettings;
        _toggleCategoryPanel = toggleCategoryPanel;

        SelectCategoryCommand = new RelayCommand(c =>
        {
            if (c is ModuleCategoryViewModel cat)
                _toggleCategoryPanel(cat);
        });
    }

    public void BuildDefaultModules()
    {
        Categories.Clear();

        var relicCat = new ModuleCategoryViewModel(ModuleCategory.RelicTools, "Relic Tools");
        var debugCat = new ModuleCategoryViewModel(ModuleCategory.Debug, "Debug");
        var generalCat = new ModuleCategoryViewModel(ModuleCategory.General, "General");

        relicCat.Modules.Add(new OverlayModule(
            id: "relic-popup",
            title: "Relic Popup",
            category: ModuleCategory.RelicTools,
            initialEnabled: Settings.Current.EnableRelicPopup,
            onToggled: enabled =>
            {
                Log.Debug("modules", $"Relic Popup toggled: {enabled}");
                Settings.Current.EnableRelicPopup = enabled;
                Settings.Save();
                _applySettings();
            },
            onShowDetails: () =>
            {
                Log.Debug("modules", "Relic Popup details requested");
                // later: open per-module settings panel
            }));

        relicCat.Modules.Add(new OverlayModule(
            id: "relic-found-indicator",
            title: "Relic Found Indicator",
            category: ModuleCategory.RelicTools,
            initialEnabled: Settings.Current.ShowRelicFoundIndicator,
            onToggled: enabled =>
            {
                Log.Debug("modules", $"Relic Found Indicator toggled: {enabled}");
                Settings.Current.ShowRelicFoundIndicator = enabled;
                Settings.Save();
                _applySettings();
            },
            onShowDetails: () =>
            {
                Log.Debug("modules", "Relic Found Indicator details requested");
            }
            ));

        debugCat.Modules.Add(new OverlayModule(
            id: "debug-visualization",
            title: "Debug Overlay",
            category: ModuleCategory.Debug,
            initialEnabled: Settings.Current.DebugVisualizationEnabled,
            onToggled: enabled =>
            {
                Log.Debug("modules", $"Debug Overlay toggled: {enabled}");
                Settings.Current.DebugVisualizationEnabled = enabled;
                Settings.Save();
                _applySettings();
            },
            onShowDetails: () =>
            {
                Log.Debug("modules", "Debug Overlay details requested");
            }));

        debugCat.Modules.Add(new OverlayModule(
            id: "verbose-logging",
            title: "Verbose Logging",
            category: ModuleCategory.Debug,
            initialEnabled: Settings.Current.LogLevel <= Microsoft.Extensions.Logging.LogLevel.Debug,
            onToggled: enabled =>
            {
                Log.Debug("modules", $"Verbose logging toggled: {enabled}");
                Settings.Current.LogLevel = enabled
                    ? Microsoft.Extensions.Logging.LogLevel.Debug
                    : Microsoft.Extensions.Logging.LogLevel.Information;
                Settings.Save();
                _applySettings();
            },
            onShowDetails: () =>
            {
                Log.Debug("modules", "Verbose logging details requested");
            }));

        Categories.Add(relicCat);
        Categories.Add(debugCat);
        Categories.Add(generalCat);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}