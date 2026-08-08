using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.AdaptiveSectionBox.Interaction;
using DevelopmentTools.Modules.AdaptiveSectionBox.Models;
using DevelopmentTools.Modules.AdaptiveSectionBox.Storage;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.ViewModels
{
    public class RegionManagerViewModel : INotifyPropertyChanged
    {
        private static RegionManagerViewModel _instance;
        public static RegionManagerViewModel Instance => _instance ?? (_instance = new RegionManagerViewModel());

        public AdaptiveRegionExternalEventHandler ExternalHandler => App.AdaptiveRegionHandler;

        private Document _doc;
        private UIDocument _uidoc;

        private ObservableCollection<AdaptiveRegion> _regions = new ObservableCollection<AdaptiveRegion>();
        private AdaptiveRegion _selectedRegion;
        private bool _isEditMode = false;
        private string _editingName;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<AdaptiveRegion> Regions
        {
            get => _regions;
            set { _regions = value; OnPropertyChanged(); }
        }

        public AdaptiveRegion SelectedRegion
        {
            get => _selectedRegion;
            set
            {
                if (_selectedRegion != value)
                {
                    // 1. 安全關閉上一個選中 Region 的 3D Hook
                    if (_selectedRegion != null)
                    {
                        StopEditing();
                    }

                    _selectedRegion = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DetailsVisibility));
                    OnPropertyChanged(nameof(GuideVisibility));
                    OnPropertyChanged(nameof(ZMin));
                    OnPropertyChanged(nameof(ZMax));
                    OnPropertyChanged(nameof(BasePointsList));
                    EditingName = _selectedRegion?.Name;

                    // 2. 切換時，若為 3D 視景，自動為選中的 Region 加載剖切
                    if (_selectedRegion != null)
                    {
                        SelectAndZoomRegion(_selectedRegion);
                    }
                }
            }
        }

        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode != value)
                {
                    _isEditMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EditModeVisibility));
                    OnPropertyChanged(nameof(NormalModeVisibility));
                }
            }
        }

        public string EditingName
        {
            get => _editingName;
            set { _editingName = value; OnPropertyChanged(); }
        }

        public double ZMin
        {
            get => _selectedRegion?.Geometry?.ZMin ?? 0.0;
            set
            {
                if (_selectedRegion?.Geometry != null)
                {
                    _selectedRegion.Geometry.ZMin = value;
                    OnPropertyChanged();
                    Update3DPreviewOnly();
                }
            }
        }

        public double ZMax
        {
            get => _selectedRegion?.Geometry?.ZMax ?? 0.0;
            set
            {
                if (_selectedRegion?.Geometry != null)
                {
                    _selectedRegion.Geometry.ZMax = value;
                    OnPropertyChanged();
                    Update3DPreviewOnly();
                }
            }
        }

        public List<string> BasePointsList
        {
            get
            {
                var list = new List<string>();
                if (_selectedRegion?.Geometry?.BasePoints != null)
                {
                    int idx = 1;
                    foreach (var p in _selectedRegion.Geometry.BasePoints)
                    {
                        list.Add($"頂點 {idx++} : X: {p.X:F2} ft, Y: {p.Y:F2} ft");
                    }
                }
                return list;
            }
        }

        public System.Windows.Visibility EditModeVisibility => IsEditMode ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility NormalModeVisibility => !IsEditMode ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility DetailsVisibility => SelectedRegion != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility GuideVisibility => SelectedRegion == null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        // Commands
        public ICommand CreateCommand { get; }
        public ICommand CreateLShapeCommand { get; }
        public ICommand CreateUShapeCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand StartEditCommand { get; }
        public ICommand StopEditCommand { get; }
        public ICommand AdjustHeightCommand { get; }
        public ICommand ZoomCommand { get; }

        private RegionManagerViewModel()
        {
            CreateCommand = new RelayCommand(o => CreateNewRegion());
            CreateLShapeCommand = new RelayCommand(o => CreateLShapeRegion());
            CreateUShapeCommand = new RelayCommand(o => CreateUShapeRegion());
            DeleteCommand = new RelayCommand(o => DeleteSelectedRegion(), o => SelectedRegion != null);
            RenameCommand = new RelayCommand(o => RenameSelectedRegion(), o => SelectedRegion != null);
            StartEditCommand = new RelayCommand(o => StartEditing(), o => SelectedRegion != null && !IsEditMode);
            StopEditCommand = new RelayCommand(o => StopEditing(), o => IsEditMode);
            AdjustHeightCommand = new RelayCommand(param => AdjustHeight(param), o => SelectedRegion != null);
            ZoomCommand = new RelayCommand(o => SelectAndZoomRegion(SelectedRegion), o => SelectedRegion != null);
        }

        public void SetDocument(UIDocument uidoc)
        {
            if (uidoc == null) return;

            bool docChanged = _doc == null || _doc.PathName != uidoc.Document.PathName;
            _uidoc = uidoc;
            _doc = uidoc.Document;

            if (docChanged)
            {
                LoadCurrentDocRegions();
            }
            else
            {
                // 若只是切換視窗，且切換到了 3D 視圖，則自動為目前選中區域啟用預覽
                if (_selectedRegion != null && uidoc.ActiveView is View3D)
                {
                    StartEditing();
                }
            }
        }

        private void LoadCurrentDocRegions()
        {
            if (_doc == null)
            {
                Regions.Clear();
                return;
            }

            try
            {
                var loaded = RegionStorageService.Instance.LoadRegions(_doc);
                Regions = new ObservableCollection<AdaptiveRegion>(loaded);
                SelectedRegion = Regions.FirstOrDefault();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RegionVM] LoadRegions 失敗: {ex.Message}");
                Regions = new ObservableCollection<AdaptiveRegion>();
                SelectedRegion = null;
            }
        }

        private void CreateNewRegion()
        {
            if (_doc == null || _uidoc == null) return;

            XYZ center = GetViewCenter(_uidoc);
            double halfW = 10.0;
            var basePoints = new List<Point2D>
            {
                new Point2D(center.X - halfW, center.Y - halfW),
                new Point2D(center.X + halfW, center.Y - halfW),
                new Point2D(center.X + halfW, center.Y + halfW),
                new Point2D(center.X - halfW, center.Y + halfW)
            };

            var geometry = new PolygonPrism(basePoints, center.Z - 5.0, center.Z + 5.0);
            var newRegion = new AdaptiveRegion($"矩形區域 {Regions.Count + 1}", geometry);

            SaveAndSelectNewRegion(newRegion);
        }

        private void CreateLShapeRegion()
        {
            if (_doc == null || _uidoc == null) return;

            XYZ center = GetViewCenter(_uidoc);
            var basePoints = new List<Point2D>
            {
                new Point2D(center.X - 10, center.Y - 10),
                new Point2D(center.X + 10, center.Y - 10),
                new Point2D(center.X + 10, center.Y + 0),
                new Point2D(center.X + 0, center.Y + 0),
                new Point2D(center.X + 0, center.Y + 10),
                new Point2D(center.X - 10, center.Y + 10)
            };

            var geometry = new PolygonPrism(basePoints, center.Z - 5.0, center.Z + 5.0);
            var newRegion = new AdaptiveRegion($"L形區域 {Regions.Count + 1}", geometry);

            SaveAndSelectNewRegion(newRegion);
        }

        private void CreateUShapeRegion()
        {
            if (_doc == null || _uidoc == null) return;

            XYZ center = GetViewCenter(_uidoc);
            var basePoints = new List<Point2D>
            {
                new Point2D(center.X - 10, center.Y - 10),
                new Point2D(center.X + 10, center.Y - 10),
                new Point2D(center.X + 10, center.Y + 10),
                new Point2D(center.X + 5, center.Y + 10),
                new Point2D(center.X + 5, center.Y + 0),
                new Point2D(center.X - 5, center.Y + 0),
                new Point2D(center.X - 5, center.Y + 10),
                new Point2D(center.X - 10, center.Y + 10)
            };

            var geometry = new PolygonPrism(basePoints, center.Z - 5.0, center.Z + 5.0);
            var newRegion = new AdaptiveRegion($"U形區域 {Regions.Count + 1}", geometry);

            SaveAndSelectNewRegion(newRegion);
        }

        private void SaveAndSelectNewRegion(AdaptiveRegion newRegion)
        {
            ExternalHandler.Raise(app =>
            {
                using (var trans = new Transaction(_doc, "新建多邊形剖面區域"))
                {
                    trans.Start();
                    var list = Regions.ToList();
                    list.Add(newRegion);
                    RegionStorageService.Instance.SaveRegions(_doc, list);
                    trans.Commit();
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Regions.Add(newRegion);
                    SelectedRegion = newRegion;
                });
            });
        }

        private void DeleteSelectedRegion()
        {
            if (_doc == null || SelectedRegion == null) return;

            var target = SelectedRegion;
            ExternalHandler.Raise(app =>
            {
                using (var trans = new Transaction(_doc, "刪除多邊形剖面區域"))
                {
                    trans.Start();
                    var list = Regions.Where(r => r.Id != target.Id).ToList();
                    RegionStorageService.Instance.SaveRegions(_doc, list);
                    trans.Commit();
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Regions.Remove(target);
                    SelectedRegion = Regions.FirstOrDefault();
                });
            });
        }

        private void RenameSelectedRegion()
        {
            if (_doc == null || SelectedRegion == null || string.IsNullOrWhiteSpace(EditingName)) return;

            var target = SelectedRegion;
            string newName = EditingName;

            ExternalHandler.Raise(app =>
            {
                using (var trans = new Transaction(_doc, "重新命名多邊形剖面區域"))
                {
                    trans.Start();
                    var loaded = RegionStorageService.Instance.LoadRegions(_doc);
                    var existing = loaded.FirstOrDefault(r => r.Id == target.Id);
                    if (existing != null)
                    {
                        existing.Name = newName;
                    }
                    RegionStorageService.Instance.SaveRegions(_doc, loaded);
                    trans.Commit();
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    target.Name = newName;
                    // 觸發重整屬性
                    LoadCurrentDocRegions();
                });
            });
        }

        private void StartEditing()
        {
            if (_uidoc == null || SelectedRegion == null) return;

            ExternalHandler.Raise(app =>
            {
                var view3D = app.ActiveUIDocument.ActiveView as View3D;
                if (view3D == null)
                {
                    view3D = GetOrCreateActiveView3D(app.ActiveUIDocument);
                    if (view3D != null)
                    {
                        app.ActiveUIDocument.ActiveView = view3D;
                    }
                }

                if (view3D != null)
                {
                    InteractionService.Instance.Start(app.ActiveUIDocument, SelectedRegion);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsEditMode = true;
                    });
                }
            });
        }

        private void StopEditing()
        {
            InteractionService.Instance.Stop();
            IsEditMode = false;
        }

        private void AdjustHeight(object param)
        {
            if (SelectedRegion == null || param == null) return;

            string s = param.ToString();
            string[] parts = s.Split(':');
            if (parts.Length != 2) return;

            string type = parts[0];
            double offset = double.Parse(parts[1]);

            var geometry = SelectedRegion.Geometry;
            if (type == "ZMin")
            {
                ZMin = Math.Min(geometry.ZMax - 1.0, geometry.ZMin + offset);
            }
            else
            {
                ZMax = Math.Max(geometry.ZMin + 1.0, geometry.ZMax + offset);
            }

            // 存檔與剖切更新
            CommitGeometryChange();
        }

        private void CommitGeometryChange()
        {
            var regionToSave = SelectedRegion.Clone();
            var doc = _doc;

            ExternalHandler.Raise(app =>
            {
                var view3D = app.ActiveUIDocument.ActiveView as View3D;
                if (view3D != null)
                {
                    using (var trans = new Transaction(doc, "微調剖面區域高度"))
                    {
                        trans.Start();
                        view3D.CropBoxActive = false;
                        view3D.CropBoxVisible = false;
                        view3D.SetSectionBox(regionToSave.Geometry.GetBoundingBox());

                        var loaded = RegionStorageService.Instance.LoadRegions(doc);
                        var existing = loaded.FirstOrDefault(r => r.Id == regionToSave.Id);
                        if (existing != null)
                        {
                            existing.Geometry = regionToSave.Geometry;
                        }
                        RegionStorageService.Instance.SaveRegions(doc, loaded);
                        trans.Commit();
                    }
                }
                RefreshProperties();
            });
        }

        private void Update3DPreviewOnly()
        {
            if (_uidoc != null && _selectedRegion != null)
            {
                // 用於在拖動 WPF 滑桿或微調高度時，僅更新 3D 著色，暫不提交 Transaction，提供高流暢度
                InteractionService.Instance.Start(_uidoc, _selectedRegion);
            }
        }

        public void RefreshProperties()
        {
            OnPropertyChanged(nameof(ZMin));
            OnPropertyChanged(nameof(ZMax));
            OnPropertyChanged(nameof(BasePointsList));
        }

        private void SelectAndZoomRegion(AdaptiveRegion region)
        {
            if (_uidoc == null || region == null) return;

            ExternalHandler.Raise(app =>
            {
                var view3D = app.ActiveUIDocument.ActiveView as View3D;
                if (view3D == null)
                {
                    view3D = GetOrCreateActiveView3D(_uidoc);
                    if (view3D != null) app.ActiveUIDocument.ActiveView = view3D;
                }

                if (view3D != null)
                {
                    using (var trans = new Transaction(_doc, "剖切定位空間區域"))
                    {
                        trans.Start();
                        view3D.CropBoxActive = false;
                        view3D.CropBoxVisible = false;
                        view3D.SetSectionBox(region.Geometry.GetBoundingBox());
                        trans.Commit();
                    }
                }

                // 啟動著色預覽
                StartEditing();
            });
        }

        private XYZ GetViewCenter(UIDocument uidoc)
        {
            var view = uidoc.ActiveView;
            if (view is View3D view3D)
            {
                return view3D.Origin;
            }

            try
            {
                var uiViews = uidoc.GetOpenUIViews();
                var activeUIView = uiViews.FirstOrDefault(uv => uv.ViewId == view.Id);
                if (activeUIView != null)
                {
                    var corners = activeUIView.GetZoomCorners();
                    if (corners != null && corners.Count >= 2)
                    {
                        return (corners[0] + corners[1]) * 0.5;
                    }
                }
            }
            catch { }

            try
            {
                return view.Origin;
            }
            catch
            {
                return XYZ.Zero;
            }
        }

        private View3D GetOrCreateActiveView3D(UIDocument uidoc)
        {
            var doc = uidoc.Document;
            var activeView = uidoc.ActiveView as View3D;
            if (activeView != null) return activeView;

            var view3D = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);

            if (view3D != null) return view3D;

            using (var trans = new Transaction(doc, "建立預設 3D 視圖"))
            {
                trans.Start();
                var collector = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType));
                var viewFamilyType = collector.Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                view3D = View3D.CreateIsometric(doc, viewFamilyType.Id);
                view3D.Name = "{3D} - 智慧空間區域";
                trans.Commit();
            }
            return view3D;
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
