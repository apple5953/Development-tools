using System.Windows;
using DevelopmentTools.Core;

namespace DevelopmentTools.UI
{
    public partial class MainWindow : Window
    {
        private readonly System.DateTime _startTime;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _startTime = System.DateTime.Now;

            // 監聽視窗關閉事件以記錄使用時長
            this.Closed += MainWindow_Closed;

            // Modeless 視窗不需要 Hide/Show 切換，直接保持開啟即可
            // ExternalEvent 會在 Revit idle time 執行，不干擾 Revit UI
        }

        private async void MainWindow_Closed(object sender, System.EventArgs e)
        {
            var duration = (System.DateTime.Now - _startTime).TotalSeconds;
            int seconds = (int)System.Math.Max(1, duration); // 最少 1 秒

            // 非同步發送使用時長至 Google Sheets API，防止卡死視窗關閉的 UI 執行緒
            await System.Threading.Tasks.Task.Run(async () =>
            {
                await GoogleAuthManager.LogUsageDurationAsync(seconds);
            });
        }

        public void BtnFeedback_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var feedbackWin = new FeedbackWindow();
                feedbackWin.Owner = this; // 絕對安全，this 即為當前的 MainWindow
                feedbackWin.ShowDialog();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"無法開啟意見反饋視窗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
