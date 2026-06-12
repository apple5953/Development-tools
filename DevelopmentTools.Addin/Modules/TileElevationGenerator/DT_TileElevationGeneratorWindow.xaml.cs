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

        private Point _panStartPoint;
        private double _originTranslateX;
        private double _originTranslateY;
        private bool _isPanning = false;

        private void Canvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var canvas = sender as System.Windows.Controls.Canvas;
            if (canvas == null) return;
            
            var group = canvas.RenderTransform as System.Windows.Media.TransformGroup;
            if (group == null) return;
            var translate = group.Children[1] as System.Windows.Media.TranslateTransform;
            if (translate == null) return;

            _panStartPoint = e.GetPosition(canvas);
            _originTranslateX = translate.X;
            _originTranslateY = translate.Y;
            _isPanning = true;
            canvas.CaptureMouse();
            canvas.Cursor = System.Windows.Input.Cursors.Hand;
        }

        private void Canvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var canvas = sender as System.Windows.Controls.Canvas;
            if (canvas == null) return;

            _isPanning = false;
            canvas.ReleaseMouseCapture();
            canvas.Cursor = System.Windows.Input.Cursors.Arrow;
        }

        private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isPanning) return;
            var canvas = sender as System.Windows.Controls.Canvas;
            if (canvas == null) return;

            var group = canvas.RenderTransform as System.Windows.Media.TransformGroup;
            if (group == null) return;
            var translate = group.Children[1] as System.Windows.Media.TranslateTransform;
            if (translate == null) return;

            Point currentPoint = e.GetPosition(canvas);
            translate.X = _originTranslateX + (currentPoint.X - _panStartPoint.X);
            translate.Y = _originTranslateY + (currentPoint.Y - _panStartPoint.Y);
        }

        private void Canvas_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var canvas = sender as System.Windows.Controls.Canvas;
            if (canvas == null) return;

            var group = canvas.RenderTransform as System.Windows.Media.TransformGroup;
            if (group == null) return;
            var scale = group.Children[0] as System.Windows.Media.ScaleTransform;
            var translate = group.Children[1] as System.Windows.Media.TranslateTransform;
            if (scale == null || translate == null) return;

            double zoom = e.Delta > 0 ? 1.15 : 0.85;
            
            // 限制縮放比例
            double newScaleX = scale.ScaleX * zoom;
            if (newScaleX < 0.15 || newScaleX > 10.0) return;

            // 取得滑鼠在 Canvas 上的相對位置
            Point mousePos = e.GetPosition(canvas);

            // 更新平移以維持滑鼠處不動
            translate.X = mousePos.X - (mousePos.X - translate.X) * zoom;
            translate.Y = mousePos.Y - (mousePos.Y - translate.Y) * zoom;

            scale.ScaleX = newScaleX;
            scale.ScaleY = scale.ScaleY * zoom;

            e.Handled = true;
            
            // 更新 ViewModel 的 ZoomPercentText (可讓 UI 動態呈現當前縮放數值)
            if (DataContext is Modules.TileElevationGenerator.DT_TileElevationGeneratorViewModel vm)
            {
                vm.ZoomFactor = scale.ScaleX;
            }
        }

        private void Button_ResetView_Click(object sender, RoutedEventArgs e)
        {
            // 重置兩個 Canvas 的視圖
            ResetCanvasTransform(PlanCanvas);
            ResetCanvasTransform(SectionCanvas);
            if (DataContext is Modules.TileElevationGenerator.DT_TileElevationGeneratorViewModel vm)
            {
                vm.ZoomFactor = 1.0;
            }
        }

        private void ResetCanvasTransform(System.Windows.Controls.Canvas canvas)
        {
            if (canvas == null) return;
            var group = canvas.RenderTransform as System.Windows.Media.TransformGroup;
            if (group == null) return;
            var scale = group.Children[0] as System.Windows.Media.ScaleTransform;
            var translate = group.Children[1] as System.Windows.Media.TranslateTransform;
            if (scale != null && translate != null)
            {
                scale.ScaleX = 1.0;
                scale.ScaleY = 1.0;
                translate.X = 0.0;
                translate.Y = 0.0;
            }
        }
    }
}
