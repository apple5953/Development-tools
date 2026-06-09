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

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is SheetDuplicatorViewModel vm)
            {
                if (e.NewValue is SheetNode sheetNode)
                {
                    vm.SelectedSheetNode = sheetNode;
                    vm.SelectedViewNode = null;
                    
                    // Clear view selections
                    foreach (var view in sheetNode.Views)
                    {
                        view.IsSelected = false;
                    }
                }
                else if (e.NewValue is ViewNode viewNode)
                {
                    // Find the parent sheet
                    foreach (var sheet in vm.AnalyzedSheets)
                    {
                        if (sheet.Views.Contains(viewNode))
                        {
                            vm.SelectedSheetNode = sheet;
                            break;
                        }
                    }
                    vm.SelectedViewNode = viewNode;

                    // Update IsSelected for visuals
                    if (vm.SelectedSheetNode != null)
                    {
                        foreach (var view in vm.SelectedSheetNode.Views)
                        {
                            view.IsSelected = (view == viewNode);
                        }
                    }
                }
            }
        }
    }
}
