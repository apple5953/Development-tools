using System.Windows.Media;
using BIMDrawingFactory.UI.Models;

namespace BIMDrawingFactory.UI.Services
{
    public static class StatusPresenter
    {
        public static string GetStatusText(UiItemStatus status)
        {
            switch (status)
            {
                case UiItemStatus.Success: return "可以建立";
                case UiItemStatus.Warning: return "需要確認";
                case UiItemStatus.Error: return "無法建立";
                case UiItemStatus.Inactive: return "尚未建立";
                case UiItemStatus.Completed: return "已完成";
                default: return "未知狀態";
            }
        }

        public static string GetFloorStatusText(FloorStatus status)
        {
            switch (status)
            {
                case FloorStatus.Completed: return "已完成";
                case FloorStatus.Partial: return "部分完成";
                case FloorStatus.Outdated: return "需要更新";
                case FloorStatus.NotCreated: return "尚未建立";
                case FloorStatus.Warning: return "需要確認";
                case FloorStatus.Error: return "有錯誤";
                default: return "尚未建立";
            }
        }

        public static Brush GetStatusForeground(UiItemStatus status)
        {
            switch (status)
            {
                case UiItemStatus.Success: return new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x41));
                case UiItemStatus.Warning: return new SolidColorBrush(Color.FromRgb(0xC4, 0x51, 0x00));
                case UiItemStatus.Error: return new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
                case UiItemStatus.Completed: return new SolidColorBrush(Color.FromRgb(0x02, 0x63, 0xE0));
                default: return new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            }
        }

        public static Brush GetStatusBackground(UiItemStatus status)
        {
            switch (status)
            {
                case UiItemStatus.Success: return new SolidColorBrush(Color.FromRgb(0xDF, 0xF6, 0xDD));
                case UiItemStatus.Warning: return new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xDB));
                case UiItemStatus.Error: return new SolidColorBrush(Color.FromRgb(0xFD, 0xE7, 0xE9));
                case UiItemStatus.Completed: return new SolidColorBrush(Color.FromRgb(0xE8, 0xF2, 0xFD));
                default: return new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
            }
        }

        public static Brush GetFloorStatusBackground(FloorStatus status, bool isSelected)
        {
            if (isSelected)
            {
                return new SolidColorBrush(Color.FromRgb(0x02, 0x63, 0xE0));
            }

            switch (status)
            {
                case FloorStatus.Completed: return new SolidColorBrush(Color.FromRgb(0xDF, 0xF6, 0xDD));
                case FloorStatus.Partial: return new SolidColorBrush(Color.FromRgb(0xE8, 0xF2, 0xFD));
                case FloorStatus.Outdated: return new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xDB));
                case FloorStatus.Warning: return new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xDB));
                case FloorStatus.Error: return new SolidColorBrush(Color.FromRgb(0xFD, 0xE7, 0xE9));
                case FloorStatus.NotCreated: return new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFB));
                default: return new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFB));
            }
        }

        public static Brush GetFloorStatusForeground(FloorStatus status, bool isSelected)
        {
            if (isSelected)
            {
                return Brushes.White;
            }

            switch (status)
            {
                case FloorStatus.Completed: return new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x41));
                case FloorStatus.Partial: return new SolidColorBrush(Color.FromRgb(0x02, 0x63, 0xE0));
                case FloorStatus.Outdated: return new SolidColorBrush(Color.FromRgb(0xC4, 0x51, 0x00));
                case FloorStatus.Warning: return new SolidColorBrush(Color.FromRgb(0xC4, 0x51, 0x00));
                case FloorStatus.Error: return new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
                case FloorStatus.NotCreated: return new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                default: return new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            }
        }
    }
}
