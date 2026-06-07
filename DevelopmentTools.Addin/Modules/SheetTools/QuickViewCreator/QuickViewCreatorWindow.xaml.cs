using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.SheetTools.QuickViewCreator
{
    /// <summary>
    /// QuickViewCreatorWindow.xaml 的互動邏輯
    /// </summary>
    public partial class QuickViewCreatorWindow : Window
    {
        public QuickViewCreatorViewModel ViewModel { get; }

        public QuickViewCreatorWindow(Document doc)
        {
            InitializeComponent();
            
            ViewModel = new QuickViewCreatorViewModel(doc, this);
            this.DataContext = ViewModel;
        }

        private void TreeViewItem_ToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.ToolTip is ToolTip toolTip && fe.DataContext is ViewTreeItemViewModel item)
            {
                if (item.Type == "View")
                {
                    var stackPanel = toolTip.Content as StackPanel;
                    if (stackPanel == null) return;

                    var previewImg = stackPanel.Children.OfType<Image>().FirstOrDefault();
                    var loadingText = stackPanel.Children.OfType<TextBlock>().LastOrDefault();

                    if (previewImg != null && loadingText != null)
                    {
                        var bitmap = DevelopmentTools.Core.ViewPreviewCacheManager.GetPreviewImage(ViewModel.Doc, item.Id);
                        if (bitmap != null)
                        {
                            previewImg.Source = bitmap;
                            previewImg.Visibility = System.Windows.Visibility.Visible;
                            loadingText.Visibility = System.Windows.Visibility.Collapsed;
                        }
                        else
                        {
                            previewImg.Visibility = System.Windows.Visibility.Collapsed;
                            loadingText.Text = "無法產生此視圖的預覽圖";
                            loadingText.Visibility = System.Windows.Visibility.Visible;
                        }
                    }
                }
                else
                {
                    e.Handled = true;
                }
            }
        }
    }
}
