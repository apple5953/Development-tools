using System.Windows.Controls;

namespace BIMDrawingFactory.UI.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }
    }
}
