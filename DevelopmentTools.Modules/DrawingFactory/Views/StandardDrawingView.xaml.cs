using System.Windows.Controls;

namespace BIMDrawingFactory.UI.Views
{
    public partial class StandardDrawingView : UserControl
    {
        public StandardDrawingView()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }
    }
}
