using System;
using System.Windows;

namespace DevelopmentTools.UI
{
    public partial class RebarConfigWindow : Window
    {
        // CNS 560 鋼筋號數對應標稱直徑 db (mm)
        private static readonly double[] RebarDbMap = { 9.53, 12.7, 15.9, 19.1, 22.2, 25.4, 28.7, 32.2, 35.8 };

        public double SelectedTopDb { get; private set; }
        public double SelectedBottomDb { get; private set; }
        public double SelectedStirrupDb { get; private set; }
        public double StirrupSpacing { get; private set; }
        public double CoverThickness { get; private set; }

        public bool IsConfirmed { get; private set; }

        public RebarConfigWindow()
        {
            InitializeComponent();
            IsConfirmed = false;
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 解析鋼筋直徑
                SelectedTopDb = RebarDbMap[ComboTopSize.SelectedIndex];
                SelectedBottomDb = RebarDbMap[ComboBottomSize.SelectedIndex];
                SelectedStirrupDb = RebarDbMap[ComboStirrupSize.SelectedIndex];

                // 解析間距與保護層
                if (!double.TryParse(TxtStirrupSpacing.Text, out double spacing) || spacing <= 0)
                {
                    MessageBox.Show("請輸入有效的箍筋配置間距數值！", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                StirrupSpacing = spacing;

                if (!double.TryParse(TxtCover.Text, out double cover) || cover <= 0)
                {
                    MessageBox.Show("請輸入有效的保護層厚度數值！", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                CoverThickness = cover;

                IsConfirmed = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("解析輸入參數錯誤: " + ex.Message, "系統錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            this.Close();
        }
    }
}
