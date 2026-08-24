using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.ParameterCompareCopy.ViewModels;
using DevelopmentTools.Modules.ParameterCompareCopy.Models;

namespace DevelopmentTools.Modules.ParameterCompareCopy.Views
{
    public partial class CompareWindow : Window
    {
        private CompareViewModel _viewModel;

        public CompareWindow(UIApplication uiapp)
        {
            InitializeComponent();
            _viewModel = new CompareViewModel(uiapp);
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

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_viewModel != null)
            {
                _viewModel.SelectedAsset = e.NewValue as CompareAsset;
            }
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

    public class ComparisonColorConverter : IValueConverter
    {
        public static readonly ComparisonColorConverter Instance = new ComparisonColorConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AssetComparison comp)
            {
                switch (comp)
                {
                    case AssetComparison.New:
                        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950")); // Green
                    case AssetComparison.Mismatch:
                        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E3B341")); // Yellow
                    case AssetComparison.Identical:
                        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B949E")); // Grey
                }
            }
            return Brushes.White;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
