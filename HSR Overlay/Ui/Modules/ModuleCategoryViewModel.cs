using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Ui.Modules;

internal class ModuleCategoryViewModel : INotifyPropertyChanged
{
    public string Name { get; }
    public ModuleCategory Category { get; }
    public ObservableCollection<OverlayModule> Modules { get; } = new();

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            OnPropertyChanged();
        }
    }

    public ModuleCategoryViewModel(ModuleCategory category, string name)
    {
        Category = category;
        Name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
