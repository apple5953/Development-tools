using System.Windows;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.SheetTools.BatchSheetRenamer
{
    /// <summary>
    /// BatchSheetRenamerWindow.xaml 的互動邏輯
    /// </summary>
    public partial class BatchSheetRenamerWindow : Window
    {
        public BatchSheetRenamerWindow(Document doc)
        {
            InitializeComponent();
            
            // 建立並綁定 ViewModel，同時把 Window 實例傳入 ViewModel 以便關閉
            var viewModel = new BatchSheetRenamerViewModel(doc, this);
            this.DataContext = viewModel;
        }
    }
}
