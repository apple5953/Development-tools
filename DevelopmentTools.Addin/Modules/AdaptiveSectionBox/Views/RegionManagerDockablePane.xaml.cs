using System;
using System.Windows.Controls;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Views
{
    public partial class RegionManagerDockablePane : UserControl, IDockablePaneProvider
    {
        public RegionManagerDockablePane()
        {
            InitializeComponent();
            DataContext = ViewModels.RegionManagerViewModel.Instance;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right,
                MinimumWidth = 280,
                MinimumHeight = 350
            };
        }
    }
}
