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

        /// <summary>
        /// 由 ViewActivated 全域事件與 Cmd_AdaptiveRegion 呼叫。
        /// 全域事件時只在面板開著時才同步，而且只「讀取」區域清單，不開交易、不切換視圖、不建立代理量體，
        /// 避免在其他外掛（例如簡報外掛）切換視圖或開關文件時被牽動而報錯。
        /// </summary>
        public void SetDocument(UIDocument uidoc, bool fromCommand = false)
        {
            try
            {
                if (uidoc == null) return;

                Document doc = uidoc.Document;
                if (!AdaptiveRegionGuard.IsUsable(doc)) return;

                if (!fromCommand && !IsPaneShown(doc)) return;

                bool docChanged = !AdaptiveRegionGuard.SameDocument(_doc, doc);
                if ((docChanged || fromCommand) && IsEditMode)
                {
                    StopEditing();
                }

                _uidoc = uidoc;
                _doc = doc;

                if (docChanged || fromCommand)
                {
                    LoadCurrentDocRegions();
                }
            }
            catch (Exception ex)
            {
                AdaptiveRegionGuard.Log("SetDocument 失敗: " + ex.Message);
            }
        }

        private static bool IsPaneShown(Document doc)
        {
            try
            {
                var uiapp = new UIApplication(doc.Application);
                var pane = uiapp.GetDockablePane(new DockablePaneId(DevelopmentTools.Commands.Cmd_AdaptiveRegion.PaneGuid));
                return pane != null && pane.IsShown();
            }
            catch
            {
                return false;
            }
        }

        private void LoadCurrentDocRegions()
        {
            List<AdaptiveRegion> loaded = new List<AdaptiveRegion>();
            try
            {
                if (_doc != null)
                {
                    loaded = RegionStorageService.Instance.LoadRegions(_doc);
                }
            }
            catch (Exception ex)
            {
                AdaptiveRegionGuard.Log("LoadRegions 失敗: " + ex.Message);
            }

            Regions = new ObservableCollection<AdaptiveRegion>(loaded);
            // 載入時只選取、不自動剖切或切換視圖；使用者在面板上點選時才會剖切定位
            SetSelectedRegionSilently(Regions.FirstOrDefault());
        }

        private void SetSelectedRegionSilently(AdaptiveRegion region)
        {
            _selectedRegion = region;
            OnPropertyChanged(nameof(SelectedRegion));
            OnPropertyChanged(nameof(DetailsVisibility));
            OnPropertyChanged(nameof(GuideVisibility));
            OnPropertyChanged(nameof(ZMin));
            OnPropertyChanged(nameof(ZMax));
            OnPropertyChanged(nameof(BasePointsList));
            EditingName = _selectedRegion?.Name;
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
            var doc = _doc;
            if (doc == null || newRegion == null) return;
            var list = Regions.ToList();
            list.Add(newRegion);

            ExternalHandler?.Raise(app =>
            {
                bool ok = AdaptiveRegionGuard.RunTransaction(doc, "新建多邊形剖面區域", () =>
                {
                    RegionStorageService.Instance.SaveRegions(doc, list);
                });
                if (!ok) return;

                AdaptiveRegionGuard.RunOnUi(() =>
                {
                    if (!AdaptiveRegionGuard.SameDocument(_doc, doc)) return;
                    Regions.Add(newRegion);
                    SelectedRegion = newRegion;
                });
            });
        }

        private void DeleteSelectedRegion()
        {
            var doc = _doc;
            if (doc == null || SelectedRegion == null) return;

            var target = SelectedRegion;
            var list = Regions.Where(r => r.Id != target.Id).ToList();
            ExternalHandler?.Raise(app =>
            {
                bool ok = AdaptiveRegionGuard.RunTransaction(doc, "刪除多邊形剖面區域", () =>
                {
                    RegionStorageService.Instance.SaveRegions(doc, list);
                });
                if (!ok) return;

                AdaptiveRegionGuard.RunOnUi(() =>
                {
                    if (!AdaptiveRegionGuard.SameDocument(_doc, doc)) return;
                    Regions.Remove(target);
                    SelectedRegion = Regions.FirstOrDefault();
                });
            });
        }

        private void RenameSelectedRegion()
        {
            var doc = _doc;
            if (doc == null || SelectedRegion == null || string.IsNullOrWhiteSpace(EditingName)) return;

            var target = SelectedRegion;
            string newName = EditingName;

            ExternalHandler?.Raise(app =>
            {
                bool ok = AdaptiveRegionGuard.RunTransaction(doc, "重新命名多邊形剖面區域", () =>
                {
                    var loaded = RegionStorageService.Instance.LoadRegions(doc);
                    var existing = loaded.FirstOrDefault(r => r.Id == target.Id);
                    if (existing != null)
                    {
                        existing.Name = newName;
                    }
                    RegionStorageService.Instance.SaveRegions(doc, loaded);
                });
                if (!ok) return;

                AdaptiveRegionGuard.RunOnUi(() =>
                {
                    target.Name = newName;
                    if (AdaptiveRegionGuard.SameDocument(_doc, doc))
                    {
                        // 觸發重整屬性
                        LoadCurrentDocRegions();
                    }
                });
            });
        }

        private void StartEditing()
        {
            var doc = _doc;
            var region = SelectedRegion;
            if (_uidoc == null || doc == null || region == null) return;

            ExternalHandler?.Raise(app =>
            {
                var activeUiDoc = app.ActiveUIDocument;
                // 使用者已切到別的專案，或文件不可編輯（唯讀、族群、其他外掛交易中）時就不啟動
                if (activeUiDoc == null || !AdaptiveRegionGuard.SameDocument(activeUiDoc.Document, doc)) return;
                if (!AdaptiveRegionGuard.CanModify(doc)) return;

                var view3D = activeUiDoc.ActiveView as View3D;
                if (view3D == null)
                {
                    view3D = GetOrCreateActiveView3D(activeUiDoc);
                    if (view3D == null) return;
                    try
                    {
                        activeUiDoc.ActiveView = view3D;
                    }
                    catch (Exception ex)
                    {
                        AdaptiveRegionGuard.Log("切換 3D 視圖失敗: " + ex.Message);
                        return;
                    }
                }

                InteractionService.Instance.Start(activeUiDoc, region);
                AdaptiveRegionGuard.RunOnUi(() => { IsEditMode = true; });
            });
        }

        private void StopEditing()
        {
            InteractionService.Instance.Stop();
            IsEditMode = false;
        }

        private void AdjustHeight(object param)
        {
            if (SelectedRegion?.Geometry == null || param == null) return;

            string s = param.ToString();
            string[] parts = s.Split(':');
            if (parts.Length != 2) return;

            string type = parts[0];
            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double offset)) return;

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
            if (SelectedRegion == null || _doc == null) return;

            var regionToSave = SelectedRegion.Clone();
            var doc = _doc;

            ExternalHandler?.Raise(app =>
            {
                var activeUiDoc = app.ActiveUIDocument;
                if (activeUiDoc == null || !AdaptiveRegionGuard.SameDocument(activeUiDoc.Document, doc)) return;

                var view3D = activeUiDoc.ActiveView as View3D;
                if (view3D != null && !view3D.IsTemplate)
                {
                    AdaptiveRegionGuard.RunTransaction(doc, "微調剖面區域高度", () =>
                    {
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
                    });
                }
                AdaptiveRegionGuard.RunOnUi(RefreshProperties);
            });
        }

        private void Update3DPreviewOnly()
        {
            // 只有使用者正在編輯時才更新代理量體；沒在編輯就不要動模型
            if (_uidoc != null && _selectedRegion != null && IsEditMode && InteractionService.Instance.IsEditModeActive)
            {
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
            var doc = _doc;
            if (_uidoc == null || doc == null || region?.Geometry == null) return;

            ExternalHandler?.Raise(app =>
            {
                var activeUiDoc = app.ActiveUIDocument;
                if (activeUiDoc == null || !AdaptiveRegionGuard.SameDocument(activeUiDoc.Document, doc)) return;
                if (!AdaptiveRegionGuard.CanModify(doc)) return;

                var view3D = activeUiDoc.ActiveView as View3D;
                if (view3D == null)
                {
                    view3D = GetOrCreateActiveView3D(activeUiDoc);
                    if (view3D == null) return;
                    try
                    {
                        activeUiDoc.ActiveView = view3D;
                    }
                    catch (Exception ex)
                    {
                        AdaptiveRegionGuard.Log("切換 3D 視圖失敗: " + ex.Message);
                        return;
                    }
                }

                if (view3D.IsTemplate) return;

                bool ok = AdaptiveRegionGuard.RunTransaction(doc, "剖切定位空間區域", () =>
                {
                    view3D.CropBoxActive = false;
                    view3D.CropBoxVisible = false;
                    view3D.SetSectionBox(region.Geometry.GetBoundingBox());
                });

                // 啟動著色預覽
                if (ok) StartEditing();
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
            var doc = uidoc?.Document;
            if (doc == null) return null;

            var activeView = uidoc.ActiveView as View3D;
            if (activeView != null) return activeView;

            var view3D = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);

            if (view3D != null) return view3D;

            var viewFamilyType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
            if (viewFamilyType == null) return null;

            View3D created = null;
            AdaptiveRegionGuard.RunTransaction(doc, "建立預設 3D 視圖", () =>
            {
                created = View3D.CreateIsometric(doc, viewFamilyType.Id);
                try { created.Name = "{3D} - 智慧空間區域"; } catch { }
            });
            return created != null && created.IsValidObject ? created : null;
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
