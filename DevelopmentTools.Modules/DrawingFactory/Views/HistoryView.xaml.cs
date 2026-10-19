using System.Windows.Controls;

namespace BIMDrawingFactory.UI.Views
{
    public partial class HistoryView : UserControl
    {
        public HistoryView()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }
    }
}
