using System.Windows;
using DevelopmentTools.ProjectStandards.UI.ViewModels;

namespace DevelopmentTools.ProjectStandards.UI.Views
{
    public partial class ProjectStandardsWindow : Window
    {
        public ProjectStandardsWindow(ProjectStandardsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
