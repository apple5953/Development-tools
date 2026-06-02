using System;
using System.Windows;
using DevelopmentTools.Core;

namespace DevelopmentTools.UI
{
    public partial class FeedbackWindow : Window
    {
        public FeedbackWindow()
        {
            InitializeComponent();
            TxtEmail.Text = string.IsNullOrEmpty(GoogleAuthManager.CurrentUserEmail)
                ? "匿名使用者 (Anonymous)"
                : GoogleAuthManager.CurrentUserEmail;
        }

        public async void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            string title = TxtTitle.Text.Trim();
            string desc = TxtDescription.Text.Trim();

            if (string.IsNullOrEmpty(title))
            {
                TxtStatus.Text = "請輸入反饋標題！";
                return;
            }
            if (string.IsNullOrEmpty(desc))
            {
                TxtStatus.Text = "請輸入詳細描述！";
                return;
            }

            BtnSubmit.IsEnabled = false;
            TxtStatus.Foreground = System.Windows.Media.Brushes.LightBlue;
            TxtStatus.Text = "正在送出...";

            try
            {
                bool success = await GoogleAuthManager.SubmitFeedbackAsync(title, desc);
                if (success)
                {
                    MessageBox.Show("感謝您的寶貴反饋，我們將會持續優化系統！", "提交成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.Close();
                }
                else
                {
                    TxtStatus.Foreground = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString("#F38BA8");
                    TxtStatus.Text = "送出失敗，請檢查網路連線。";
                    BtnSubmit.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Foreground = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString("#F38BA8");
                TxtStatus.Text = $"錯誤: {ex.Message}";
                BtnSubmit.IsEnabled = true;
            }
        }
    }
}
