using System.Windows;
using BIMDrawingFactory.UI.ViewModels;

namespace BIMDrawingFactory.UI.Views
{
    public partial class MainShellView : Window
    {
        public MainShellView(MainShellViewModel viewModel)
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
            DataContext = viewModel;
            Loaded += async (sender, e) =>
            {
                try
                {
                    await viewModel.InitializeAsync();
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("[MainShellView Loaded Error] {0}", ex));
                }
            };
        }
    }
}
