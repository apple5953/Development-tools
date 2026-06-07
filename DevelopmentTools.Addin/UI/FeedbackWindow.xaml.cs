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

            // 預填格式範例，引導使用者提供有價值的反饋數據
            TxtDescription.Text = "【請依以下格式提供資訊，以利 AI 排查與修正】\n" +
                                  "1. 發生問題的工具：\n" +
                                  "2. 錯誤現象與提示文字：\n" +
                                  "3. 重現問題的操作步驟：\n" +
                                  "   - 步驟一：\n" +
                                  "   - 步驟二：\n" +
                                  "4. 期望的改進方式：\n";
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
            if (desc.Length < 10)
            {
                TxtStatus.Text = "描述字數太少，請參照範例提供詳細資訊！";
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
