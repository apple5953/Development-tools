using System.Windows;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.SheetTools.QuickViewCreator
{
    /// <summary>
    /// QuickViewCreatorWindow.xaml 的互動邏輯
    /// </summary>
    public partial class QuickViewCreatorWindow : Window
    {
        public QuickViewCreatorWindow(Document doc)
        {
            InitializeComponent();
            
            var viewModel = new QuickViewCreatorViewModel(doc, this);
            this.DataContext = viewModel;
        }
    }
}
