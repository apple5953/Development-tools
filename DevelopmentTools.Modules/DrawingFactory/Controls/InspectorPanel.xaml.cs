using System.Windows.Controls;

namespace BIMDrawingFactory.UI.Controls
{
    public partial class InspectorPanel : UserControl
    {
        public InspectorPanel()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }
    }
}
