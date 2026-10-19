using System.Windows.Controls;

namespace BIMDrawingFactory.UI.Views
{
    public partial class DrawingRulesView : UserControl
    {
        public DrawingRulesView()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }
    }
}
