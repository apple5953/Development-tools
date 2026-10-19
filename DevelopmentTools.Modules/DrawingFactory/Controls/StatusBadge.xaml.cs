using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.Controls
{
    public partial class StatusBadge : UserControl
    {
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register("Status", typeof(UiItemStatus), typeof(StatusBadge),
                new PropertyMetadata(UiItemStatus.Success, OnStatusChanged));

        public static readonly DependencyProperty CustomTextProperty =
            DependencyProperty.Register("CustomText", typeof(string), typeof(StatusBadge),
                new PropertyMetadata(null, OnCustomTextChanged));

        public UiItemStatus Status
        {
            get { return (UiItemStatus)GetValue(StatusProperty); }
            set { SetValue(StatusProperty, value); }
        }

        public string CustomText
        {
            get { return (string)GetValue(CustomTextProperty); }
            set { SetValue(CustomTextProperty, value); }
        }

        public StatusBadge()
        {
            // StatusBadge 會在 MainShellView 的 BAML 尚在解析時建立。
            // 熱載入使用 Assembly.Load(byte[]) 時，此處再透過 pack URI
            // 嵌套載入 StatusBadge.xaml 會找不到當前版本資源。
            // 這個控件的視覺樹很小，直接在程式碼建立可完全
            // 避免熱載入的 BAML 遞迴問題。
            BuildVisualTree();
            UpdateVisuals();
        }

        private void BuildVisualTree()
        {
            BadgeBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            StatusDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            StatusLabel = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(StatusDot);
            panel.Children.Add(StatusLabel);
            BadgeBorder.Child = panel;
            Content = BadgeBorder;
        }

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var badge = d as StatusBadge;
            if (badge != null)
            {
                badge.UpdateVisuals();
            }
        }

        private static void OnCustomTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var badge = d as StatusBadge;
            if (badge != null)
            {
                badge.UpdateVisuals();
            }
        }

        private void UpdateVisuals()
        {
            var fg = StatusPresenter.GetStatusForeground(Status);
            var bg = StatusPresenter.GetStatusBackground(Status);

            BadgeBorder.Background = bg;
            BadgeBorder.BorderBrush = fg;
            StatusDot.Fill = fg;
            StatusLabel.Foreground = fg;
            StatusLabel.Text = !string.IsNullOrEmpty(CustomText) ? CustomText : StatusPresenter.GetStatusText(Status);
        }
    }
}
