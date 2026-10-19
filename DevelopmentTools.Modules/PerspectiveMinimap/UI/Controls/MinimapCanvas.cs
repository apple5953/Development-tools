using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using RevitPerspectiveMinimap.Models;

namespace RevitPerspectiveMinimap.UI.Controls
{
    public enum DragHandleType
    {
        None,
        EyeStation,
        TargetPoint,
        PanView
    }

    public class CameraPlacementEventArgs : EventArgs
    {
        public XYZ EyePosition { get; set; }
        public XYZ TargetPosition { get; set; }

        public CameraPlacementEventArgs(XYZ eye, XYZ target)
        {
            EyePosition = eye;
            TargetPosition = target;
        }
    }

    public class MinimapCanvas : FrameworkElement
    {
        public static readonly DependencyProperty CamerasProperty =
            DependencyProperty.Register("Cameras", typeof(ObservableCollection<PerspectiveCameraModel>), typeof(MinimapCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SelectedCameraProperty =
            DependencyProperty.Register("SelectedCamera", typeof(PerspectiveCameraModel), typeof(MinimapCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSelectedCameraChanged));

        public static readonly DependencyProperty PlanGeometryProperty =
            DependencyProperty.Register("PlanGeometry", typeof(PlanGeometryModel), typeof(MinimapCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsPlacingModeProperty =
            DependencyProperty.Register("IsPlacingMode", typeof(bool), typeof(MinimapCanvas),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnPlacingModeChanged));

        public ObservableCollection<PerspectiveCameraModel> Cameras
        {
            get { return (ObservableCollection<PerspectiveCameraModel>)GetValue(CamerasProperty); }
            set { SetValue(CamerasProperty, value); }
        }

        public PerspectiveCameraModel SelectedCamera
        {
            get { return (PerspectiveCameraModel)GetValue(SelectedCameraProperty); }
            set { SetValue(SelectedCameraProperty, value); }
        }

        public PlanGeometryModel PlanGeometry
        {
            get { return (PlanGeometryModel)GetValue(PlanGeometryProperty); }
            set { SetValue(PlanGeometryProperty, value); }
        }

        public bool IsPlacingMode
        {
            get { return (bool)GetValue(IsPlacingModeProperty); }
            set { SetValue(IsPlacingModeProperty, value); }
        }

        public event EventHandler<PerspectiveCameraModel> CameraSelected;
        public event EventHandler<PerspectiveCameraModel> CameraDoubleClicked;
        public event EventHandler<PerspectiveCameraModel> CameraPositionChanged;
        public event EventHandler<PerspectiveCameraModel> CameraDirectionChanged;
        public event EventHandler<CameraPlacementEventArgs> CameraPlaced;

        private double _scale = 6.0;
        private System.Windows.Point _panOffset = new System.Windows.Point(0, 0);
        private System.Windows.Point _lastMousePos;
        private DragHandleType _currentDrag = DragHandleType.None;
        private PerspectiveCameraModel _draggedCamera = null;

        // 放置模式狀態
        private int _placementStep = 0; // 0: 未開始, 1: 已點選站點，正在拉視線
        private System.Windows.Point _placementEyeScreen;
        private XYZ _placementEyeWorld;
        private System.Windows.Point _currentHoverMousePos;

        public MinimapCanvas()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        private static void OnSelectedCameraChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            MinimapCanvas canvas = d as MinimapCanvas;
            if (canvas != null)
            {
                canvas.InvalidateVisual();
            }
        }

        private static void OnPlacingModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            MinimapCanvas canvas = d as MinimapCanvas;
            if (canvas != null)
            {
                bool isPlacing = (bool)e.NewValue;
                canvas.Cursor = isPlacing ? Cursors.Cross : Cursors.Arrow;
                canvas._placementStep = 0;
                canvas.InvalidateVisual();
            }
        }

        public void ResetView()
        {
            if (PlanGeometry != null && PlanGeometry.Lines.Count > 0)
            {
                double modelW = PlanGeometry.Width;
                double modelH = PlanGeometry.Height;
                if (modelW > 0 && modelH > 0 && ActualWidth > 0 && ActualHeight > 0)
                {
                    double scaleX = (ActualWidth - 60) / modelW;
                    double scaleY = (ActualHeight - 60) / modelH;
                    _scale = Math.Max(1.0, Math.Min(scaleX, scaleY));

                    double centerX = (PlanGeometry.MinX + PlanGeometry.MaxX) / 2.0;
                    double centerY = (PlanGeometry.MinY + PlanGeometry.MaxY) / 2.0;

                    _panOffset = new System.Windows.Point(
                        ActualWidth / 2.0 - centerX * _scale,
                        ActualHeight / 2.0 + centerY * _scale
                    );
                }
            }
            else
            {
                _panOffset = new System.Windows.Point(ActualWidth / 2.0, ActualHeight / 2.0);
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 24, 33)), null, new Rect(0, 0, ActualWidth, ActualHeight));

            DrawGridBackground(dc);
            DrawPlanGeometry(dc);
            DrawCameras(dc);
            DrawPlacementPreview(dc);
        }

        private void DrawGridBackground(DrawingContext dc)
        {
            System.Windows.Media.Pen pen = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 255, 255, 255)), 1.0);
            pen.Freeze();
            double step = 30.0;
            for (double x = _panOffset.X % step; x < ActualWidth; x += step)
            {
                dc.DrawLine(pen, new System.Windows.Point(x, 0), new System.Windows.Point(x, ActualHeight));
            }
            for (double y = _panOffset.Y % step; y < ActualHeight; y += step)
            {
                dc.DrawLine(pen, new System.Windows.Point(0, y), new System.Windows.Point(ActualWidth, y));
            }
        }

        private void DrawPlanGeometry(DrawingContext dc)
        {
            if (PlanGeometry == null || PlanGeometry.Lines == null) return;

            System.Windows.Media.Pen wallPen = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 210, 220, 235)), 2.2);
            wallPen.Freeze();

            System.Windows.Media.Pen gridPen = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 100, 180, 255)), 1.0);
            gridPen.DashStyle = DashStyles.Dash;
            gridPen.Freeze();

            foreach (LineSegment2D line in PlanGeometry.Lines)
            {
                System.Windows.Point pt1 = WorldToScreen(line.Start.X, line.Start.Y);
                System.Windows.Point pt2 = WorldToScreen(line.End.X, line.End.Y);
                System.Windows.Media.Pen p = (line.Category == "Grid") ? gridPen : wallPen;
                dc.DrawLine(p, pt1, pt2);
            }
        }

        private void DrawCameras(DrawingContext dc)
        {
            if (Cameras == null) return;

            Typeface typeface = new Typeface("Segoe UI");

            foreach (PerspectiveCameraModel cam in Cameras)
            {
                bool isSel = (SelectedCamera != null && SelectedCamera.ViewId == cam.ViewId);
                System.Windows.Point eyeScreen = WorldToScreen(cam.EyePosition.X, cam.EyePosition.Y);
                XYZ fwd = cam.ForwardDirection != null ? cam.ForwardDirection : XYZ.BasisX;
                double forwardAngle = Math.Atan2(-fwd.Y, fwd.X);

                System.Windows.Media.Color baseColor;
                try
                {
                    baseColor = (System.Windows.Media.Color)ColorConverter.ConvertFromString(cam.ColorHex != null ? cam.ColorHex : "#2196F3");
                }
                catch
                {
                    baseColor = Colors.DodgerBlue;
                }

                double fovDeg = cam.FovDegrees > 10 ? cam.FovDegrees : 65.0;
                double fovHalfRad = (fovDeg / 2.0) * (Math.PI / 180.0);
                double coneRadius = isSel ? 90.0 : 60.0;

                double angle1 = forwardAngle - fovHalfRad;
                double angle2 = forwardAngle + fovHalfRad;

                System.Windows.Point ptWing1 = new System.Windows.Point(eyeScreen.X + coneRadius * Math.Cos(angle1), eyeScreen.Y + coneRadius * Math.Sin(angle1));
                System.Windows.Point ptWing2 = new System.Windows.Point(eyeScreen.X + coneRadius * Math.Cos(angle2), eyeScreen.Y + coneRadius * Math.Sin(angle2));

                StreamGeometry coneGeom = new StreamGeometry();
                using (StreamGeometryContext ctx = coneGeom.Open())
                {
                    ctx.BeginFigure(eyeScreen, true, true);
                    ctx.LineTo(ptWing1, true, false);
                    ctx.ArcTo(ptWing2, new System.Windows.Size(coneRadius, coneRadius), 0, false, SweepDirection.Clockwise, true, false);
                }
                coneGeom.Freeze();

                byte alphaCone = isSel ? (byte)70 : (byte)30;
                SolidColorBrush coneBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alphaCone, baseColor.R, baseColor.G, baseColor.B));
                System.Windows.Media.Pen conePen = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, baseColor.R, baseColor.G, baseColor.B)), isSel ? 1.8 : 1.0);
                dc.DrawGeometry(coneBrush, conePen, coneGeom);

                double targetDist = isSel ? 75.0 : 45.0;
                System.Windows.Point targetScreen = new System.Windows.Point(eyeScreen.X + targetDist * Math.Cos(forwardAngle), eyeScreen.Y + targetDist * Math.Sin(forwardAngle));
                System.Windows.Media.Pen sightPen = new System.Windows.Media.Pen(new SolidColorBrush(baseColor), isSel ? 2.5 : 1.2);
                dc.DrawLine(sightPen, eyeScreen, targetScreen);

                if (isSel)
                {
                    // 目標點操作握把 (白色圓點 + 外框)
                    dc.DrawEllipse(new SolidColorBrush(Colors.White), new System.Windows.Media.Pen(new SolidColorBrush(baseColor), 3.0), targetScreen, 6, 6);
                }

                // 相機站點 Eye Pin
                double eyeRadius = isSel ? 9.0 : 6.5;
                if (isSel)
                {
                    // 選中光暈
                    dc.DrawEllipse(new SolidColorBrush(System.Windows.Media.Color.FromArgb(70, baseColor.R, baseColor.G, baseColor.B)), null, eyeScreen, 16, 16);
                }
                dc.DrawEllipse(new SolidColorBrush(baseColor), new System.Windows.Media.Pen(new SolidColorBrush(Colors.White), 2.0), eyeScreen, eyeRadius, eyeRadius);

                // 標籤 Badge
                string labelText = cam.ViewName != null ? cam.ViewName : "View3D";
                System.Windows.Media.FormattedText ft = new System.Windows.Media.FormattedText(
                    labelText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    11,
                    new SolidColorBrush(Colors.White),
                    1.0
                );

                Rect badgeRect = new Rect(eyeScreen.X + 12, eyeScreen.Y - 18, ft.Width + 10, ft.Height + 4);
                dc.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 16, 20, 28)), new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(140, baseColor.R, baseColor.G, baseColor.B)), 1.0), badgeRect, 4, 4);
                dc.DrawText(ft, new System.Windows.Point(badgeRect.X + 5, badgeRect.Y + 2));
            }
        }

        private void DrawPlacementPreview(DrawingContext dc)
        {
            if (!IsPlacingMode) return;

            var typeface = new Typeface("Segoe UI");

            if (_placementStep == 0)
            {
                // 提示文字跟隨滑鼠
                System.Windows.Media.FormattedText tip = new System.Windows.Media.FormattedText(
                    "📷 點擊設定相機站點 (Eye)",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    12,
                    new SolidColorBrush(Colors.Gold),
                    1.0
                );
                dc.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 20, 25, 35)), new System.Windows.Media.Pen(new SolidColorBrush(Colors.Gold), 1.0), new Rect(_currentHoverMousePos.X + 15, _currentHoverMousePos.Y + 15, tip.Width + 10, tip.Height + 6), 4, 4);
                dc.DrawText(tip, new System.Windows.Point(_currentHoverMousePos.X + 20, _currentHoverMousePos.Y + 18));
            }
            else if (_placementStep == 1)
            {
                // 繪製放置中的站點與動態視角線
                dc.DrawEllipse(new SolidColorBrush(Colors.Gold), new System.Windows.Media.Pen(new SolidColorBrush(Colors.White), 2.0), _placementEyeScreen, 8, 8);

                double dx = _currentHoverMousePos.X - _placementEyeScreen.X;
                double dy = _currentHoverMousePos.Y - _placementEyeScreen.Y;
                double angle = Math.Atan2(dy, dx);
                double fovHalfRad = (65.0 / 2.0) * (Math.PI / 180.0);
                double coneRadius = Math.Max(50.0, Math.Sqrt(dx * dx + dy * dy));

                double angle1 = angle - fovHalfRad;
                double angle2 = angle + fovHalfRad;

                var ptWing1 = new System.Windows.Point(_placementEyeScreen.X + coneRadius * Math.Cos(angle1), _placementEyeScreen.Y + coneRadius * Math.Sin(angle1));
                var ptWing2 = new System.Windows.Point(_placementEyeScreen.X + coneRadius * Math.Cos(angle2), _placementEyeScreen.Y + coneRadius * Math.Sin(angle2));

                var coneGeom = new StreamGeometry();
                using (var ctx = coneGeom.Open())
                {
                    ctx.BeginFigure(_placementEyeScreen, true, true);
                    ctx.LineTo(ptWing1, true, false);
                    ctx.ArcTo(ptWing2, new System.Windows.Size(coneRadius, coneRadius), 0, false, SweepDirection.Clockwise, true, false);
                }
                coneGeom.Freeze();

                var coneBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 255, 215, 0));
                var conePen = new System.Windows.Media.Pen(new SolidColorBrush(Colors.Gold), 1.5) { DashStyle = DashStyles.Dash };
                dc.DrawGeometry(coneBrush, conePen, coneGeom);

                var sightPen = new System.Windows.Media.Pen(new SolidColorBrush(Colors.Gold), 2.0);
                dc.DrawLine(sightPen, _placementEyeScreen, _currentHoverMousePos);
                dc.DrawEllipse(new SolidColorBrush(Colors.White), new System.Windows.Media.Pen(new SolidColorBrush(Colors.Gold), 2.5), _currentHoverMousePos, 6, 6);

                System.Windows.Media.FormattedText tip = new System.Windows.Media.FormattedText(
                    "🎯 點擊設定視線目標 (Target)",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    12,
                    new SolidColorBrush(Colors.Gold),
                    1.0
                );
                dc.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 20, 25, 35)), new System.Windows.Media.Pen(new SolidColorBrush(Colors.Gold), 1.0), new Rect(_currentHoverMousePos.X + 15, _currentHoverMousePos.Y + 15, tip.Width + 10, tip.Height + 6), 4, 4);
                dc.DrawText(tip, new System.Windows.Point(_currentHoverMousePos.X + 20, _currentHoverMousePos.Y + 18));
            }
        }

        public System.Windows.Point WorldToScreen(double x, double y)
        {
            return new System.Windows.Point(_panOffset.X + x * _scale, _panOffset.Y - y * _scale);
        }

        public System.Windows.Point ScreenToWorld(System.Windows.Point screenPt)
        {
            return new System.Windows.Point((screenPt.X - _panOffset.X) / _scale, (_panOffset.Y - screenPt.Y) / _scale);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            System.Windows.Point mousePos = e.GetPosition(this);
            _lastMousePos = mousePos;

            if (e.ChangedButton == MouseButton.Left)
            {
                if (IsPlacingMode)
                {
                    if (_placementStep == 0)
                    {
                        // 步驟 1: 確定站點
                        _placementEyeScreen = mousePos;
                        var worldEye = ScreenToWorld(mousePos);
                        _placementEyeWorld = new XYZ(worldEye.X, worldEye.Y, 0);
                        _placementStep = 1;
                        InvalidateVisual();
                        return;
                    }
                    else if (_placementStep == 1)
                    {
                        // 步驟 2: 確定目標點
                        var worldTarget = ScreenToWorld(mousePos);
                        var targetXYZ = new XYZ(worldTarget.X, worldTarget.Y, 0);

                        // 若兩點太近給予預設距離
                        if ((targetXYZ - _placementEyeWorld).GetLength() < 2.0)
                        {
                            targetXYZ = _placementEyeWorld + new XYZ(20.0, 0, 0);
                        }

                        _placementStep = 0;
                        IsPlacingMode = false;

                        if (CameraPlaced != null)
                        {
                            CameraPlaced(this, new CameraPlacementEventArgs(_placementEyeWorld, targetXYZ));
                        }
                        InvalidateVisual();
                        return;
                    }
                }

                if (e.ClickCount == 2)
                {
                    DragHandleType dummyHandle;
                    PerspectiveCameraModel hitCam = HitTestCamera(mousePos, out dummyHandle);
                    if (hitCam != null)
                    {
                        SelectedCamera = hitCam;
                        if (CameraDoubleClicked != null)
                        {
                            CameraDoubleClicked(this, hitCam);
                        }
                        return;
                    }
                }

                DragHandleType handle;
                PerspectiveCameraModel clickedCam = HitTestCamera(mousePos, out handle);
                if (clickedCam != null)
                {
                    SelectedCamera = clickedCam;
                    if (CameraSelected != null)
                    {
                        CameraSelected(this, clickedCam);
                    }
                    _draggedCamera = clickedCam;
                    _currentDrag = handle;
                    CaptureMouse();
                    InvalidateVisual();
                    return;
                }

                _currentDrag = DragHandleType.PanView;
                CaptureMouse();
            }
            else if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
            {
                if (IsPlacingMode)
                {
                    // 右鍵取消放置模式
                    IsPlacingMode = false;
                    _placementStep = 0;
                    InvalidateVisual();
                    return;
                }

                _currentDrag = DragHandleType.PanView;
                CaptureMouse();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            System.Windows.Point mousePos = e.GetPosition(this);
            _currentHoverMousePos = mousePos;
            Vector delta = mousePos - _lastMousePos;
            _lastMousePos = mousePos;

            if (IsPlacingMode)
            {
                InvalidateVisual();
                return;
            }

            if (_currentDrag == DragHandleType.PanView)
            {
                _panOffset.X += delta.X;
                _panOffset.Y += delta.Y;
                InvalidateVisual();
            }
            else if (_currentDrag == DragHandleType.EyeStation && _draggedCamera != null)
            {
                System.Windows.Point worldPos = ScreenToWorld(mousePos);
                _draggedCamera.EyePosition = new XYZ(worldPos.X, worldPos.Y, _draggedCamera.EyePosition.Z);
                InvalidateVisual();
                if (CameraPositionChanged != null)
                {
                    CameraPositionChanged(this, _draggedCamera);
                }
            }
            else if (_currentDrag == DragHandleType.TargetPoint && _draggedCamera != null)
            {
                System.Windows.Point eyeScreen = WorldToScreen(_draggedCamera.EyePosition.X, _draggedCamera.EyePosition.Y);
                double screenAngle = Math.Atan2(mousePos.Y - eyeScreen.Y, mousePos.X - eyeScreen.X);
                double modelAngleRad = -screenAngle;
                double deg = (modelAngleRad * 180.0 / Math.PI + 360.0) % 360.0;

                double pitchDeg = _draggedCamera.PitchAngleDegrees;
                double cosPitch = Math.Cos(pitchDeg * Math.PI / 180.0);
                double sinPitch = Math.Sin(pitchDeg * Math.PI / 180.0);

                XYZ fwd = new XYZ(Math.Cos(modelAngleRad) * cosPitch, Math.Sin(modelAngleRad) * cosPitch, sinPitch).Normalize();
                _draggedCamera.ForwardDirection = fwd;
                InvalidateVisual();
                if (CameraDirectionChanged != null)
                {
                    CameraDirectionChanged(this, _draggedCamera);
                }
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
            _currentDrag = DragHandleType.None;
            _draggedCamera = null;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            System.Windows.Point mousePos = e.GetPosition(this);
            double zoomFactor = e.Delta > 0 ? 1.15 : 0.87;

            _panOffset.X = mousePos.X - (mousePos.X - _panOffset.X) * zoomFactor;
            _panOffset.Y = mousePos.Y - (mousePos.Y - _panOffset.Y) * zoomFactor;
            _scale *= zoomFactor;

            InvalidateVisual();
        }

        private PerspectiveCameraModel HitTestCamera(System.Windows.Point screenPt, out DragHandleType handle)
        {
            handle = DragHandleType.None;
            if (Cameras == null) return null;

            if (SelectedCamera != null)
            {
                System.Windows.Point eyeScreen = WorldToScreen(SelectedCamera.EyePosition.X, SelectedCamera.EyePosition.Y);
                XYZ fwd = SelectedCamera.ForwardDirection != null ? SelectedCamera.ForwardDirection : XYZ.BasisX;
                double forwardAngle = Math.Atan2(-fwd.Y, fwd.X);
                System.Windows.Point targetScreen = new System.Windows.Point(eyeScreen.X + 75.0 * Math.Cos(forwardAngle), eyeScreen.Y + 75.0 * Math.Sin(forwardAngle));

                if ((screenPt - targetScreen).Length <= 14)
                {
                    handle = DragHandleType.TargetPoint;
                    return SelectedCamera;
                }
            }

            foreach (PerspectiveCameraModel cam in Cameras)
            {
                System.Windows.Point eyeScreen = WorldToScreen(cam.EyePosition.X, cam.EyePosition.Y);
                if ((screenPt - eyeScreen).Length <= 16)
                {
                    handle = DragHandleType.EyeStation;
                    return cam;
                }
            }

            return null;
        }
    }
}