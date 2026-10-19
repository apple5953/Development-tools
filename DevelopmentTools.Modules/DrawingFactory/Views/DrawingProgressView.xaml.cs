using System.Windows.Controls;

namespace BIMDrawingFactory.UI.Views
{
    public partial class DrawingProgressView : UserControl
    {
        public DrawingProgressView()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }
    }
}
