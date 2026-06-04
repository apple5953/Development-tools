using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.SheetTools.QuickDimension
{
    /// <summary>
    /// QuickDimensionWindow.xaml 的互動邏輯
    /// </summary>
    public partial class QuickDimensionWindow : Window
    {
        public QuickDimensionWindow(Document doc)
        {
            InitializeComponent();
            
            var viewModel = new QuickDimensionViewModel(doc, this);
            this.DataContext = viewModel;
        }
    }

    /// <summary>
    /// 用於將 RadioButton 索引對應至整數狀態的 Converter
    /// </summary>
    public class CheckedInIndexedConverter : IValueConverter
    {
        public static readonly CheckedInIndexedConverter Instance = new CheckedInIndexedConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return System.Windows.Data.Binding.DoNothing;
            if ((bool)value)
            {
                if (int.TryParse(parameter.ToString(), out int index))
                {
                    return index;
                }
            }
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}
