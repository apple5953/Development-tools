using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Modules.SheetTools.RoomFinishConfigurator
{
    /// <summary>
    /// RoomFinishConfiguratorWindow.xaml 的互動邏輯
    /// </summary>
    public partial class RoomFinishConfiguratorWindow : Window
    {
        public RoomFinishConfiguratorWindow(UIDocument uidoc)
        {
            InitializeComponent();
            
            var viewModel = new RoomFinishConfiguratorViewModel(uidoc, this);
            this.DataContext = viewModel;
            this.Closed += (_, __) =>
            {
                if (DataContext is System.IDisposable disposable)
                {
                    disposable.Dispose();
                }
            };
        }
    }
}
