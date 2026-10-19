using System.Windows.Controls;

namespace BIMDrawingFactory.UI.Views
{
    public partial class MappingView : UserControl
    {
        public MappingView()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }
    }
}
