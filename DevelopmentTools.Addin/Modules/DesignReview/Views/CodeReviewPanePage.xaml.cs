using System;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.DesignReview.ViewModels;

namespace DevelopmentTools.Modules.DesignReview.Views
{
    public partial class CodeReviewPanePage : Page, IDockablePaneProvider
    {
        public CodeReviewPanePage()
        {
            InitializeComponent();
        }

        public void SetViewModel(CodeReviewPaneViewModel vm)
        {
            this.DataContext = vm;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right,
                MinimumWidth = 320,
                MinimumHeight = 500
            };
        }
    }
}
