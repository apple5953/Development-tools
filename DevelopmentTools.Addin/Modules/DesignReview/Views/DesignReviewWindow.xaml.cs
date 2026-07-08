using System.Windows;
using DevelopmentTools.Modules.DesignReview.ViewModels;

namespace DevelopmentTools.Modules.DesignReview.Views
{
    /// <summary>
    /// DesignReviewWindow.xaml 的互動邏輯
    /// </summary>
    public partial class DesignReviewWindow : Window
    {
        public DesignReviewWindow(DesignReviewViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
