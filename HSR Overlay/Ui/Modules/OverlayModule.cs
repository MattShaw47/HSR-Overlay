using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace HSR_Overlay.Ui.Modules;

internal class OverlayModule : INotifyPropertyChanged
{
    public string Id { get; }
    public string Title { get; }
    public ModuleCategory Category { get; }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            _onToggled?.Invoke(value);
        }
    }

    private readonly Action<bool>? _onToggled;
    private readonly Action? _onShowDetails;

    public bool HasDetails => _onShowDetails != null;

    public ICommand ToggleCommand { get; }
    public ICommand ShowDetailsCommand { get; }

    public OverlayModule(
        string id,
        string title,
        ModuleCategory category,
        bool initialEnabled,
        Action<bool>? onToggled = null,
        Action? onShowDetails = null)
    {
        Id = id;
        Title = title;
        Category = category;
        _isEnabled = initialEnabled;
        _onToggled = onToggled;
        _onShowDetails = onShowDetails;

        ToggleCommand = new RelayCommand(_ => IsEnabled = !IsEnabled);
        ShowDetailsCommand = new RelayCommand(_ => _onShowDetails?.Invoke());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
