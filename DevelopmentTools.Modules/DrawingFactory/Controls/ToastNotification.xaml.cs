using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.Controls
{
    public partial class ToastNotification : UserControl
    {
        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register("Message", typeof(string), typeof(ToastNotification),
                new PropertyMetadata(string.Empty, OnMessageChanged));

        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register("Status", typeof(UiItemStatus), typeof(ToastNotification),
                new PropertyMetadata(UiItemStatus.Success, OnStatusChanged));

        public string Message
        {
            get { return (string)GetValue(MessageProperty); }
            set { SetValue(MessageProperty, value); }
        }

        public UiItemStatus Status
        {
            get { return (UiItemStatus)GetValue(StatusProperty); }
            set { SetValue(StatusProperty, value); }
        }

        public ToastNotification()
        {
            // This control is instantiated while MainShellView BAML is still
            // being parsed. With Assembly.Load(byte[]) hot reload, nesting a
            // second pack-URI XAML load can resolve against an older assembly
            // identity and produce "cannot locate controls/toastnotification".
            // Build this small visual tree directly, as StatusBadge does.
            BuildVisualTree();
            UpdateVisuals();
        }

        private void BuildVisualTree()
        {
            ToastBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 32),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 4,
                    Opacity = 0.25,
                    Color = Colors.Black
                }
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            ToastIcon = new Path
            {
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var icon = TryFindResource("IconSuccess") as Geometry;
            if (icon != null) ToastIcon.Data = icon;

            TxtMessage = new TextBlock
            {
                Text = Message ?? string.Empty,
                FontSize = 14,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(ToastIcon);
            panel.Children.Add(TxtMessage);
            ToastBorder.Child = panel;
            Content = ToastBorder;
        }

        private void UpdateVisuals()
        {
            if (ToastIcon != null) ToastIcon.Fill = StatusPresenter.GetStatusForeground(Status);
            if (TxtMessage != null) TxtMessage.Text = Message ?? string.Empty;
        }

        private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var toast = d as ToastNotification;
            if (toast != null)
            {
                toast.UpdateVisuals();
            }
        }

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var toast = d as ToastNotification;
            if (toast != null && e.NewValue is UiItemStatus)
            {
                var status = (UiItemStatus)e.NewValue;
                toast.UpdateVisuals();
            }
        }
    }
}
