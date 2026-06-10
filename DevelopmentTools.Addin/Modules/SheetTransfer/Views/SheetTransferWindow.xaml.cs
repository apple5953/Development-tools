using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.SheetTransfer.ViewModels;

namespace DevelopmentTools.Modules.SheetTransfer.Views
{
    public partial class SheetTransferWindow : Window
    {
        private SheetTransferViewModel _viewModel;

        public SheetTransferWindow(UIApplication uiapp)
        {
            InitializeComponent();
            _viewModel = new SheetTransferViewModel(uiapp);
            this.DataContext = _viewModel;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel.IsTransferring || _viewModel.IsAnalyzing)
            {
                MessageBox.Show("請等待目前作業完成後再關閉視窗。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Cancel = true;
                return;
            }

            _viewModel.CloseSourceDocument();
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new BoolToVisibilityConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToInvertConverter : IValueConverter
    {
        public static readonly BoolToInvertConverter Instance = new BoolToInvertConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b) ? !b : value;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
