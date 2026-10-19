using System;
using System.Windows;
using RevitPerspectiveMinimap.Models;
using RevitPerspectiveMinimap.UI.Controls;
using RevitPerspectiveMinimap.UI.ViewModels;

namespace RevitPerspectiveMinimap.UI
{
    public partial class PerspectiveMinimapWindow : Window
    {
        public static PerspectiveMinimapWindow Instance { get; set; }
        public PerspectiveMinimapViewModel ViewModel { get; private set; }

        public PerspectiveMinimapWindow()
        {
            InitializeComponent();
            ViewModel = new PerspectiveMinimapViewModel();
            DataContext = ViewModel;

            MinimapView.CameraPositionChanged += OnMinimapCameraMoved;
            MinimapView.CameraDirectionChanged += OnMinimapCameraDirectionChanged;
            MinimapView.CameraDoubleClicked += OnMinimapCameraDoubleClicked;
            MinimapView.CameraPlaced += OnMinimapCameraPlaced;

            ViewModel.RequestResetCanvasZoom += delegate { MinimapView.ResetView(); };
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Instance = null;
        }

        private void OnMinimapCameraPlaced(object sender, CameraPlacementEventArgs e)
        {
            ViewModel.HandleCameraPlaced(e.EyePosition, e.TargetPosition);
        }

        private void OnMinimapCameraMoved(object sender, PerspectiveCameraModel cam)
        {
            ViewModel.HandleCameraMovedOnCanvas(cam);
        }

        private void OnMinimapCameraDirectionChanged(object sender, PerspectiveCameraModel cam)
        {
            ViewModel.HandleCameraDirectionChangedOnCanvas(cam);
        }

        private void OnMinimapCameraDoubleClicked(object sender, PerspectiveCameraModel cam)
        {
            if (cam == null || cam.ViewId == null) return;
            ViewModel.SwitchToView(cam.ViewId);
        }

        private void OnCameraListItemDoubleClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ViewModel.SelectedCamera != null && ViewModel.SelectedCamera.ViewId != null)
            {
                ViewModel.SwitchToView(ViewModel.SelectedCamera.ViewId);
            }
        }

        private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                if (ViewModel.HasSelectedCamera)
                {
                    ViewModel.ExecuteDeleteCamera();
                }
            }
        }
    }
}