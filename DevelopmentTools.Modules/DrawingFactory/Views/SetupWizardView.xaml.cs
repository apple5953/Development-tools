using System.Windows.Controls;

namespace BIMDrawingFactory.UI.Views
{
    public partial class SetupWizardView : UserControl
    {
        public SetupWizardView()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }
    }
}
