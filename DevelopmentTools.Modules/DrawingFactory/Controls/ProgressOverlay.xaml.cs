using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;

namespace BIMDrawingFactory.UI.Controls
{
    public partial class ProgressOverlay : UserControl
    {
        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
            "Progress", typeof(GenerationProgressDto), typeof(ProgressOverlay),
            new PropertyMetadata(null));

        public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(
            "CancelCommand", typeof(ICommand), typeof(ProgressOverlay),
            new PropertyMetadata(null));

        public GenerationProgressDto Progress
        {
            get { return (GenerationProgressDto)GetValue(ProgressProperty); }
            set { SetValue(ProgressProperty, value); }
        }

        public ICommand CancelCommand
        {
            get { return (ICommand)GetValue(CancelCommandProperty); }
            set { SetValue(CancelCommandProperty, value); }
        }

        public ProgressOverlay()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }
    }
}
