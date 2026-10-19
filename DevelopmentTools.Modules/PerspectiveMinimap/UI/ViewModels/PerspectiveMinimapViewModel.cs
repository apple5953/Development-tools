using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPerspectiveMinimap.Core;
using RevitPerspectiveMinimap.Models;

namespace RevitPerspectiveMinimap.UI.ViewModels
{
    public class PerspectiveMinimapViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<LevelInfoModel> _levels = new ObservableCollection<LevelInfoModel>();
        private LevelInfoModel _selectedLevel;
        private ObservableCollection<PerspectiveCameraModel> _allCameras = new ObservableCollection<PerspectiveCameraModel>();
        private ObservableCollection<PerspectiveCameraModel> _filteredCameras = new ObservableCollection<PerspectiveCameraModel>();
        private PerspectiveCameraModel _selectedCamera;
        private PlanGeometryModel _currentPlanGeometry = new PlanGeometryModel();

        private double _customBaseElevationMm = 0;
        private bool _isPlacingMode = false;
        private bool _isLiveSyncEnabled = true;
        private bool _isUpdatingFromRevit = false;
        private double _inputEyeHeightMm = 1500;
        private double _inputYawDegrees = 0;
        private double _inputPitchDegrees = 0;
        private double _inputFovDegrees = 65;
        private bool _isSectionRotated90 = false;
        private string _statusMessage = "就緒 (可點擊「📷 放置相機」在地圖上放置視角)";

        public double CustomBaseElevationMm
        {
            get { return _customBaseElevationMm; }
            set
            {
                if (Math.Abs(_customBaseElevationMm - value) > 1.0)
                {
                    _customBaseElevationMm = value;
                    OnPropertyChanged("CustomBaseElevationMm");
                    OnPropertyChanged("TotalCameraElevationDisplay");
                    if (_selectedCamera != null)
                    {
                        _selectedCamera.AbsoluteElevationMm = value + _inputEyeHeightMm;
                    }
                    if (!_isUpdatingFromRevit && _isLiveSyncEnabled && _selectedCamera != null)
                    {
                        ApplyCustomElevation();
                    }
                }
            }
        }

        public string TotalCameraElevationDisplay
        {
            get
            {
                double totalMm = _customBaseElevationMm + _inputEyeHeightMm;
                return string.Format("{0:+0.00;-0.00;0.00} m ({1:F0} mm)", totalMm / 1000.0, totalMm);
            }
        }

        public ObservableCollection<LevelInfoModel> Levels
        {
            get { return _levels; }
            set { _levels = value; OnPropertyChanged("Levels"); }
        }

        public LevelInfoModel SelectedLevel
        {
            get { return _selectedLevel; }
            set
            {
                if (_selectedLevel != value)
                {
                    _selectedLevel = value;
                    OnPropertyChanged("SelectedLevel");
                    if (value != null)
                    {
                        _customBaseElevationMm = value.ElevationMm;
                        OnPropertyChanged("CustomBaseElevationMm");
                        OnPropertyChanged("TotalCameraElevationDisplay");
                    }
                    ApplyLevelFilter();
                    LoadPlanGeometry();
                }
            }
        }

        public ObservableCollection<PerspectiveCameraModel> AllCameras
        {
            get { return _allCameras; }
            set { _allCameras = value; OnPropertyChanged("AllCameras"); }
        }

        public ObservableCollection<PerspectiveCameraModel> FilteredCameras
        {
            get { return _filteredCameras; }
            set { _filteredCameras = value; OnPropertyChanged("FilteredCameras"); }
        }

        public PerspectiveCameraModel SelectedCamera
        {
            get { return _selectedCamera; }
            set
            {
                if (_selectedCamera != value)
                {
                    _selectedCamera = value;
                    OnPropertyChanged("SelectedCamera");
                    OnPropertyChanged("HasSelectedCamera");
                    UpdateInputsFromSelectedCamera();
                }
            }
        }

        public bool HasSelectedCamera
        {
            get { return _selectedCamera != null; }
        }

        public PlanGeometryModel CurrentPlanGeometry
        {
            get { return _currentPlanGeometry; }
            set { _currentPlanGeometry = value; OnPropertyChanged("CurrentPlanGeometry"); }
        }

        public bool IsPlacingMode
        {
            get { return _isPlacingMode; }
            set
            {
                if (_isPlacingMode != value)
                {
                    _isPlacingMode = value;
                    OnPropertyChanged("IsPlacingMode");
                    OnPropertyChanged("PlacingModeButtonText");
                    StatusMessage = value ? "👉 請在小地圖上點擊「相機站點」，再拉動點擊「視線目標」" : "就緒";
                }
            }
        }

        public string PlacingModeButtonText
        {
            get { return _isPlacingMode ? "❌ 取消放置" : "📷 放置相機"; }
        }

        public bool IsLiveSyncEnabled
        {
            get { return _isLiveSyncEnabled; }
            set { _isLiveSyncEnabled = value; OnPropertyChanged("IsLiveSyncEnabled"); }
        }

        public double InputEyeHeightMm
        {
            get { return _inputEyeHeightMm; }
            set
            {
                if (Math.Abs(_inputEyeHeightMm - value) > 1.0)
                {
                    _inputEyeHeightMm = value;
                    OnPropertyChanged("InputEyeHeightMm");
                    if (_selectedCamera != null)
                    {
                        _selectedCamera.EyeHeightAboveFloorMm = value;
                        double baseElev = (SelectedLevel != null) ? SelectedLevel.ElevationMm : 0;
                        _selectedCamera.AbsoluteElevationMm = baseElev + value;
                    }
                    if (!_isUpdatingFromRevit && _isLiveSyncEnabled && _selectedCamera != null)
                    {
                        ApplyEyeHeight(value);
                    }
                }
            }
        }

        public double InputYawDegrees
        {
            get { return _inputYawDegrees; }
            set
            {
                if (Math.Abs(_inputYawDegrees - value) > 0.5)
                {
                    _inputYawDegrees = value;
                    OnPropertyChanged("InputYawDegrees");
                    if (!_isUpdatingFromRevit && _isLiveSyncEnabled && _selectedCamera != null)
                    {
                        ApplyDirection();
                    }
                }
            }
        }

        public double InputPitchDegrees
        {
            get { return _inputPitchDegrees; }
            set
            {
                if (Math.Abs(_inputPitchDegrees - value) > 0.5)
                {
                    _inputPitchDegrees = value;
                    OnPropertyChanged("InputPitchDegrees");
                    if (!_isUpdatingFromRevit && _isLiveSyncEnabled && _selectedCamera != null)
                    {
                        ApplyDirection();
                    }
                }
            }
        }

        public double InputFovDegrees
        {
            get { return _inputFovDegrees; }
            set
            {
                if (Math.Abs(_inputFovDegrees - value) > 0.5)
                {
                    _inputFovDegrees = value;
                    OnPropertyChanged("InputFovDegrees");
                }
            }
        }

        public bool IsSectionRotated90
        {
            get { return _isSectionRotated90; }
            set
            {
                _isSectionRotated90 = value;
                OnPropertyChanged("IsSectionRotated90");
                OnPropertyChanged("SectionDirectionLabel");
            }
        }

        public string SectionDirectionLabel
        {
            get { return _isSectionRotated90 ? "垂直視線 (橫向剖 90°)" : "沿視線方向 (縱向剖 0°)"; }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { _statusMessage = value; OnPropertyChanged("StatusMessage"); }
        }

        public ICommand TogglePlaceCameraModeCommand { get; private set; }
        public ICommand PresetHeightStandCommand { get; private set; }
        public ICommand PresetHeightSitCommand { get; private set; }
        public ICommand PresetHeightEyeLevelCommand { get; private set; }
        public ICommand PresetHeightAerialCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand TileFourViewsCommand { get; private set; }
        public ICommand ExportSheetCommand { get; private set; }
        public ICommand RotateSection90Command { get; private set; }
        public ICommand SyncFromActiveViewCommand { get; private set; }
        public ICommand ResetMinimapZoomCommand { get; private set; }
        public ICommand ResetBaseElevationFromLevelCommand { get; private set; }
        public ICommand DeleteCameraCommand { get; private set; }

        public event Action RequestResetCanvasZoom;

        public PerspectiveMinimapViewModel()
        {
            TogglePlaceCameraModeCommand = new RelayCommand((Action)ExecuteTogglePlaceCameraMode);
            PresetHeightStandCommand = new RelayCommand((Action)delegate { InputEyeHeightMm = 1500; });
            PresetHeightEyeLevelCommand = new RelayCommand((Action)delegate { InputEyeHeightMm = 1600; });
            PresetHeightSitCommand = new RelayCommand((Action)delegate { InputEyeHeightMm = 1100; });
            PresetHeightAerialCommand = new RelayCommand((Action)delegate { InputEyeHeightMm = 2800; });

            ResetBaseElevationFromLevelCommand = new RelayCommand((Action)delegate
            {
                if (SelectedLevel != null)
                {
                    CustomBaseElevationMm = SelectedLevel.ElevationMm;
                    StatusMessage = string.Format("✅ 已重設樓板標高為【{0}】之標高 ({1:F0} mm)", SelectedLevel.Name, SelectedLevel.ElevationMm);
                }
            });

            DeleteCameraCommand = new RelayCommand((Action)ExecuteDeleteCamera, (Func<bool>)delegate { return HasSelectedCamera; });

            RefreshCommand = new RelayCommand((Action)ExecuteRefresh);
            TileFourViewsCommand = new RelayCommand((Action)ExecuteTileFourViews, (Func<bool>)delegate { return HasSelectedCamera; });
            ExportSheetCommand = new RelayCommand((Action)ExecuteExportSheet, (Func<bool>)delegate { return HasSelectedCamera; });
            RotateSection90Command = new RelayCommand((Action)ExecuteRotateSection90, (Func<bool>)delegate { return HasSelectedCamera; });
            SyncFromActiveViewCommand = new RelayCommand((Action)ExecuteSyncFromActiveView);
            ResetMinimapZoomCommand = new RelayCommand((Action)delegate
            {
                if (RequestResetCanvasZoom != null) RequestResetCanvasZoom();
            });
        }

        private void ExecuteTogglePlaceCameraMode()
        {
            IsPlacingMode = !IsPlacingMode;
        }

        public void HandleCameraPlaced(XYZ eyeWorld, XYZ targetWorld)
        {
            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc == null) return;
                Document doc = uiDoc.Document;

                Level level = null;
                if (SelectedLevel != null)
                {
                    level = doc.GetElement(SelectedLevel.LevelId) as Level;
                }
                if (level == null)
                {
                    level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault();
                }

                double totalElevMm = CustomBaseElevationMm + InputEyeHeightMm;
                double eyeZ = totalElevMm / 304.8;
                double targetZ = totalElevMm / 304.8;

                XYZ finalEye = new XYZ(eyeWorld.X, eyeWorld.Y, eyeZ);
                XYZ finalTarget = new XYZ(targetWorld.X, targetWorld.Y, targetZ);

                string viewName = string.Format("透視圖 ({0})", DateTime.Now.ToString("HHmm-ss"));
                View3D newView = CameraManager.CreatePerspectiveView(doc, viewName, finalEye, finalTarget, level);

                if (newView != null)
                {
                    uiDoc.ActiveView = newView;
                    if (CoreApp.UIThreadDispatcher != null)
                    {
                        CoreApp.UIThreadDispatcher.Invoke(new Action(delegate
                        {
                            RefreshData(doc, newView);
                            StatusMessage = string.Format("✅ 已成功建立透視圖【{0}】並聚焦！", newView.Name);
                        }));
                    }
                }
            });
        }

        public void RefreshData(Document doc, View3D active3DView)
        {
            if (doc == null) return;

            try
            {
                _isUpdatingFromRevit = true;

                ElementId prevLevelId = (SelectedLevel != null) ? SelectedLevel.LevelId : null;
                ElementId activeCamLevelId = null;

                List<LevelInfoModel> levelsList = SmartLevelDetector.GetAllLevels(doc);
                List<PerspectiveCameraModel> cams = CameraManager.GetAllPerspectiveViews(doc, active3DView);

                if (active3DView != null && active3DView.IsPerspective)
                {
                    PerspectiveCameraModel matchedActiveCam = cams.FirstOrDefault(c => c.ViewId == active3DView.Id);
                    if (matchedActiveCam != null && matchedActiveCam.AssociatedLevelId != null)
                    {
                        activeCamLevelId = matchedActiveCam.AssociatedLevelId;
                    }
                }

                foreach (LevelInfoModel lvl in levelsList)
                {
                    lvl.CameraCount = cams.Count(c => c.AssociatedLevelId == lvl.LevelId);
                }

                Levels.Clear();
                foreach (LevelInfoModel lvl in levelsList) Levels.Add(lvl);

                AllCameras.Clear();
                foreach (PerspectiveCameraModel cam in cams) AllCameras.Add(cam);

                LevelInfoModel targetLevel = null;
                if (activeCamLevelId != null)
                {
                    targetLevel = Levels.FirstOrDefault(l => l.LevelId == activeCamLevelId);
                }
                if (targetLevel == null && prevLevelId != null)
                {
                    targetLevel = Levels.FirstOrDefault(l => l.LevelId == prevLevelId);
                }
                if (targetLevel == null)
                {
                    targetLevel = Levels.FirstOrDefault(l => l.CameraCount > 0) ?? Levels.FirstOrDefault();
                }

                SelectedLevel = targetLevel;
                ApplyLevelFilter();

                if (active3DView != null && active3DView.IsPerspective)
                {
                    SelectedCamera = FilteredCameras.FirstOrDefault(c => c.ViewId == active3DView.Id);
                    if (SelectedCamera == null) SelectedCamera = AllCameras.FirstOrDefault(c => c.ViewId == active3DView.Id);
                }
                else if (SelectedCamera == null || !AllCameras.Any(c => c.ViewId == SelectedCamera.ViewId))
                {
                    SelectedCamera = FilteredCameras.FirstOrDefault();
                }

                StatusMessage = string.Format("共載入 {0} 個透視圖，{1} 個樓層", AllCameras.Count, Levels.Count);
            }
            finally
            {
                _isUpdatingFromRevit = false;
            }
        }

        private void ApplyLevelFilter()
        {
            FilteredCameras.Clear();
            if (SelectedLevel == null)
            {
                foreach (PerspectiveCameraModel c in AllCameras) FilteredCameras.Add(c);
            }
            else
            {
                foreach (PerspectiveCameraModel c in AllCameras.Where(c => c.AssociatedLevelId == SelectedLevel.LevelId))
                {
                    FilteredCameras.Add(c);
                }
            }

            if (SelectedCamera != null && !FilteredCameras.Contains(SelectedCamera))
            {
                SelectedCamera = FilteredCameras.FirstOrDefault();
            }
        }

        private void LoadPlanGeometry()
        {
            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc == null) return;
                Document doc = uiDoc.Document;

                PlanGeometryModel geom = new PlanGeometryModel();
                ElementId lvlId = (SelectedLevel != null) ? SelectedLevel.LevelId : null;

                List<Wall> allDocWalls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .ToList();

                List<Wall> walls = allDocWalls.Where(w =>
                {
                    if (lvlId == null) return true;
                    if (w.LevelId == lvlId) return true;
                    try
                    {
                        Parameter p = w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                        if (p != null && p.AsElementId() == lvlId) return true;
                    }
                    catch { }
                    return false;
                }).ToList();

                // 若該樓層沒有過濾出牆體，則顯示所有牆體避免地圖全黑
                if (walls.Count == 0 && allDocWalls.Count > 0)
                {
                    walls = allDocWalls;
                }

                foreach (Wall wall in walls)
                {
                    LocationCurve lc = wall.Location as LocationCurve;
                    if (lc != null)
                    {
                        Line line = lc.Curve as Line;
                        if (line != null)
                        {
                            XYZ p0 = line.GetEndPoint(0);
                            XYZ p1 = line.GetEndPoint(1);
                            geom.Lines.Add(new LineSegment2D(new Point2D(p0.X, p0.Y), new Point2D(p1.X, p1.Y), "Wall", ""));
                            geom.UpdateBounds(p0.X, p0.Y);
                            geom.UpdateBounds(p1.X, p1.Y);
                        }
                    }
                }

                List<Grid> grids = new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Cast<Grid>()
                    .ToList();

                foreach (Grid grid in grids)
                {
                    Line line = grid.Curve as Line;
                    if (line != null)
                    {
                        XYZ p0 = line.GetEndPoint(0);
                        XYZ p1 = line.GetEndPoint(1);
                        geom.Lines.Add(new LineSegment2D(new Point2D(p0.X, p0.Y), new Point2D(p1.X, p1.Y), "Grid", grid.Name));
                        geom.UpdateBounds(p0.X, p0.Y);
                        geom.UpdateBounds(p1.X, p1.Y);
                    }
                }

                if (CoreApp.UIThreadDispatcher != null)
                {
                    CoreApp.UIThreadDispatcher.Invoke(new Action(delegate
                    {
                        CurrentPlanGeometry = geom;
                        if (RequestResetCanvasZoom != null)
                        {
                            RequestResetCanvasZoom();
                        }
                    }));
                }
            });
        }

        private void UpdateInputsFromSelectedCamera()
        {
            if (_selectedCamera == null) return;

            try
            {
                _isUpdatingFromRevit = true;
                _customBaseElevationMm = _selectedCamera.AbsoluteElevationMm - _selectedCamera.EyeHeightAboveFloorMm;
                OnPropertyChanged("CustomBaseElevationMm");
                OnPropertyChanged("TotalCameraElevationDisplay");

                InputEyeHeightMm = _selectedCamera.EyeHeightAboveFloorMm;
                InputYawDegrees = _selectedCamera.YawAngleDegrees;
                InputPitchDegrees = _selectedCamera.PitchAngleDegrees;
                InputFovDegrees = _selectedCamera.FovDegrees;

                IsSectionRotated90 = SectionIndexManager.GetDirectionMode(_selectedCamera.ViewId) == SectionDirectionMode.AcrossSight;
            }
            finally
            {
                _isUpdatingFromRevit = false;
            }
        }

        public void HandleCameraMovedOnCanvas(PerspectiveCameraModel cam)
        {
            if (cam == null) return;
            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                Document doc = (app.ActiveUIDocument != null) ? app.ActiveUIDocument.Document : null;
                if (doc != null)
                {
                    CameraManager.SetCameraEyePosition(doc, cam.ViewId, cam.EyePosition);
                }
            });
        }

        public void HandleCameraDirectionChangedOnCanvas(PerspectiveCameraModel cam)
        {
            if (cam == null) return;
            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                Document doc = (app.ActiveUIDocument != null) ? app.ActiveUIDocument.Document : null;
                if (doc != null)
                {
                    CameraManager.SetCameraDirection(doc, cam.ViewId, cam.YawAngleDegrees, cam.PitchAngleDegrees);
                }
            });
        }

        private void ApplyCustomElevation()
        {
            if (_selectedCamera == null) return;
            ElementId viewId = _selectedCamera.ViewId;
            double totalZFeet = (CustomBaseElevationMm + InputEyeHeightMm) / 304.8;

            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                Document doc = (app.ActiveUIDocument != null) ? app.ActiveUIDocument.Document : null;
                if (doc != null)
                {
                    CameraManager.SetCameraAbsoluteZ(doc, viewId, totalZFeet);
                }
            });
        }

        private void ApplyEyeHeight(double heightMm)
        {
            ApplyCustomElevation();
        }

        private void ApplyDirection()
        {
            if (_selectedCamera == null) return;
            ElementId viewId = _selectedCamera.ViewId;
            double yaw = InputYawDegrees;
            double pitch = InputPitchDegrees;

            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                Document doc = (app.ActiveUIDocument != null) ? app.ActiveUIDocument.Document : null;
                if (doc != null)
                {
                    CameraManager.SetCameraDirection(doc, viewId, yaw, pitch);
                }
            });
        }

        private void ExecuteRefresh()
        {
            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc != null)
                {
                    View3D active3D = uiDoc.ActiveView as View3D;
                    if (CoreApp.UIThreadDispatcher != null)
                    {
                        CoreApp.UIThreadDispatcher.Invoke(new Action(delegate
                        {
                            RefreshData(uiDoc.Document, active3D);
                        }));
                    }
                }
            });
        }

        public void SwitchToView(ElementId viewId)
        {
            if (viewId == null) return;
            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc == null) return;
                Document doc = uiDoc.Document;

                View targetView = doc.GetElement(viewId) as View;
                if (targetView != null)
                {
                    try
                    {
                        uiDoc.ActiveView = targetView;
                        if (CoreApp.UIThreadDispatcher != null)
                        {
                            CoreApp.UIThreadDispatcher.Invoke(new Action(delegate
                            {
                                StatusMessage = string.Format("👁️ 已切換至透視圖【{0}】", targetView.Name);
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        if (CoreApp.UIThreadDispatcher != null)
                        {
                            CoreApp.UIThreadDispatcher.Invoke(new Action(delegate
                            {
                                StatusMessage = string.Format("⚠️ 切換視圖失敗: {0}", ex.Message);
                            }));
                        }
                    }
                }
            });
        }

        public void ExecuteTileFourViews()
        {
            if (_selectedCamera == null)
            {
                TaskDialog.Show("提示", "請先在清單或小地圖上選取一張透視圖！");
                return;
            }
            ElementId viewId = _selectedCamera.ViewId;

            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                MultiViewManager.OpenAndTileFourViews(app, viewId);
            });
        }

        public void ExecuteExportSheet()
        {
            if (_selectedCamera == null)
            {
                TaskDialog.Show("提示", "請先在清單或小地圖上選取一張透視圖！");
                return;
            }
            ElementId viewId = _selectedCamera.ViewId;

            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                SheetComposer.ComposePerspectiveSheet(app, viewId);
            });
        }

        public void ExecuteRotateSection90()
        {
            if (_selectedCamera == null)
            {
                TaskDialog.Show("提示", "請先在清單或小地圖上選取一張透視圖！");
                return;
            }
            ElementId viewId = _selectedCamera.ViewId;

            SectionIndexManager.ToggleDirectionMode(viewId);
            IsSectionRotated90 = SectionIndexManager.GetDirectionMode(viewId) == SectionDirectionMode.AcrossSight;

            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                Document doc = (app.ActiveUIDocument != null) ? app.ActiveUIDocument.Document : null;
                if (doc != null)
                {
                    View3D view = doc.GetElement(viewId) as View3D;
                    if (view != null)
                    {
                        SectionIndexManager.GetOrCreateSectionView(doc, view);
                        if (CoreApp.UIThreadDispatcher != null)
                        {
                            CoreApp.UIThreadDispatcher.Invoke(new Action(delegate
                            {
                                StatusMessage = string.Format("✅ 剖面已轉向為：{0}", SectionDirectionLabel);
                            }));
                        }
                    }
                }
            });
        }

        private void ExecuteSyncFromActiveView()
        {
            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc != null)
                {
                    View3D active3D = uiDoc.ActiveView as View3D;
                    if (active3D != null && active3D.IsPerspective)
                    {
                        if (CoreApp.UIThreadDispatcher != null)
                        {
                            CoreApp.UIThreadDispatcher.Invoke(new Action(delegate
                            {
                                RefreshData(uiDoc.Document, active3D);
                                StatusMessage = string.Format("🎯 已同步選取當前視角：【{0}】", active3D.Name);
                            }));
                        }
                    }
                    else
                    {
                        if (CoreApp.UIThreadDispatcher != null)
                        {
                            CoreApp.UIThreadDispatcher.Invoke(new Action(delegate
                            {
                                TaskDialog.Show("同步提示", "當前 Revit 作用中視圖不是透視 3D 視圖。\n請先切換至透視圖再點擊同步！");
                            }));
                        }
                    }
                }
            });
        }

        public void ExecuteDeleteCamera()
        {
            if (_selectedCamera == null)
            {
                TaskDialog.Show("提示", "請先在清單或小地圖上選取要刪除的透視圖！");
                return;
            }

            ElementId viewId = _selectedCamera.ViewId;
            string viewName = _selectedCamera.ViewName;

            RevitEventDispatcher.Instance.Enqueue(delegate(UIApplication app)
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc == null) return;
                Document doc = uiDoc.Document;

                try
                {
                    using (Transaction tx = new Transaction(doc, "刪除透視圖"))
                    {
                        tx.Start();
                        doc.Delete(viewId);
                        tx.Commit();
                    }

                    if (CoreApp.UIThreadDispatcher != null)
                    {
                        CoreApp.UIThreadDispatcher.Invoke(new Action(delegate
                        {
                            RefreshData(doc, null);
                            StatusMessage = string.Format("🗑️ 已成功刪除透視圖【{0}】！", viewName);
                        }));
                    }
                }
                catch (Exception ex)
                {
                    if (CoreApp.UIThreadDispatcher != null)
                    {
                        CoreApp.UIThreadDispatcher.Invoke(new Action(delegate
                        {
                            StatusMessage = string.Format("⚠️ 刪除失敗: {0}", ex.Message);
                        }));
                    }
                }
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}