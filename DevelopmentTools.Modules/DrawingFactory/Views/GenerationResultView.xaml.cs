using System.Windows.Controls;

namespace BIMDrawingFactory.UI.Views
{
    public partial class GenerationResultView : UserControl
    {
        public GenerationResultView()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }
    }
}
