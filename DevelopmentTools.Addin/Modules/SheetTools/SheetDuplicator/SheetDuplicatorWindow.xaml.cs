using System.Windows;

namespace DevelopmentTools.Modules.SheetTools.SheetDuplicator
{
    public partial class SheetDuplicatorWindow : Window
    {
        public SheetDuplicatorWindow(SheetDuplicatorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
