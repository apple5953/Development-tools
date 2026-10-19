using System.Windows;
using OpeningDetailAddin.ViewModels;

namespace OpeningDetailAddin.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseAction = new System.Action(this.Close);
        }
    }
}
