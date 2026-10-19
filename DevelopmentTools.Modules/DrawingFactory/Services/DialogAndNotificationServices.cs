using System;
using System.Threading.Tasks;
using System.Windows;
using BIMDrawingFactory.UI.Models;

namespace BIMDrawingFactory.UI.Services
{
    public interface INotificationService
    {
        event Action<string, UiItemStatus> ToastRequested;
        void ShowToast(string message, UiItemStatus status = UiItemStatus.Success);
    }

    public class NotificationService : INotificationService
    {
        public event Action<string, UiItemStatus> ToastRequested;

        public void ShowToast(string message, UiItemStatus status = UiItemStatus.Success)
        {
            if (ToastRequested != null)
            {
                ToastRequested(message, status);
            }
        }
    }

    public interface IDialogService
    {
        Task<bool> ShowConfirmAsync(string title, string message, string confirmText = "確定", string cancelText = "取消");
        Task<bool> ShowWarningConfirmAsync(string title, string message, string confirmText = "繼續執行", string cancelText = "返回檢查");
        Task<bool> ShowDangerConfirmAsync(string title, string message, string confirmText = "確認刪除", string cancelText = "取消");
    }

    public class DialogService : IDialogService
    {
        public Task<bool> ShowConfirmAsync(string title, string message, string confirmText = "確定", string cancelText = "取消")
        {
            var result = MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
            return Task.FromResult(result == MessageBoxResult.OK);
        }

        public Task<bool> ShowWarningConfirmAsync(string title, string message, string confirmText = "繼續執行", string cancelText = "返回檢查")
        {
            var result = MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            return Task.FromResult(result == MessageBoxResult.OK);
        }

        public Task<bool> ShowDangerConfirmAsync(string title, string message, string confirmText = "確認刪除", string cancelText = "取消")
        {
            var result = MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Stop);
            return Task.FromResult(result == MessageBoxResult.OK);
        }
    }
}
