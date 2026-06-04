using System.Windows;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.SheetTools.RoomFinishConfigurator
{
    /// <summary>
    /// RoomFinishConfiguratorWindow.xaml 的互動邏輯
    /// </summary>
    public partial class RoomFinishConfiguratorWindow : Window
    {
        public RoomFinishConfiguratorWindow(Document doc)
        {
            InitializeComponent();
            
            var viewModel = new RoomFinishConfiguratorViewModel(doc, this);
            this.DataContext = viewModel;
        }
    }
}
