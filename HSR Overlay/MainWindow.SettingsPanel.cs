using HSR_Overlay.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisibilityEnum = System.Windows.Visibility;

namespace HSR_Overlay;

public partial class MainWindow
{
    // private Settings _settingsDraft = Settings.Current.DeepCopy();

    private void ToggleConfigPanel()
    {
        if (ConfigPanel.Visibility != VisibilityEnum.Visible)
        {
            _settingsDraft = Settings.Current.DeepCopy();
            ConfigPanel.DataContext = _settingsDraft;
            ConfigPanel.Visibility = VisibilityEnum.Visible;
        }
        else
        {
            ConfigPanel.Visibility = VisibilityEnum.Collapsed;
        }
    }
}
