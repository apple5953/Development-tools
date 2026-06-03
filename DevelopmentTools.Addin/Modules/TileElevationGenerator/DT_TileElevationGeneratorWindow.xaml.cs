using System;
using System.Windows;

namespace DevelopmentTools.UI
{
    public partial class DT_TileElevationGeneratorWindow : Window
    {
        public DT_TileElevationGeneratorWindow(Modules.TileElevationGenerator.DT_TileElevationGeneratorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // 訂閱事件：使用 Visibility 屬性來做顯示隱藏，避免 ShowDialog 重複呼叫引發異常
            viewModel.RequestHide += () =>
            {
                this.Visibility = Visibility.Collapsed;
            };

            viewModel.RequestShow += () =>
            {
                this.Visibility = Visibility.Visible;
                this.Activate();
                this.Focus();
            };

            viewModel.RequestClose += () =>
            {
                this.DialogResult = true;
                this.Close();
            };
        }
    }
}
