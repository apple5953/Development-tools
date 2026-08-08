using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.AdaptiveSectionBox.Models;
using DevelopmentTools.Modules.AdaptiveSectionBox.Storage;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Interaction
{
    public class InteractionService
    {
        private static InteractionService _instance;
        public static InteractionService Instance => _instance ?? (_instance = new InteractionService());

        private UIDocument _uidoc;
        private AdaptiveRegion _activeRegion;
        private bool _isEditModeActive = false;
        private ElementId _proxyElementId = ElementId.InvalidElementId;

        public bool IsEditModeActive => _isEditModeActive;
        public ElementId ProxyElementId => _proxyElementId;

        private InteractionService() { }

        public void Start(UIDocument uidoc, AdaptiveRegion region)
        {
            if (uidoc == null || region == null) return;

            _uidoc = uidoc;
            _activeRegion = region;
            _isEditModeActive = true;

            // 在安全執行緒中非同步建立 DirectShape 代理量體
            App.AdaptiveRegionHandler.Raise(app =>
            {
                var doc = app.ActiveUIDocument.Document;
                using (var trans = new Transaction(doc, "建立剖面代理量體"))
                {
                    trans.Start();

                    try
                    {
                        // 1. 建立多邊形拉伸幾何
                        List<CurveLoop> profileLoops = new List<CurveLoop>();
                        CurveLoop loop = new CurveLoop();
                        int n = region.Geometry.BasePoints.Count;
                        for (int i = 0; i < n; i++)
                        {
                            var p1 = region.Geometry.BasePoints[i];
                            var p2 = region.Geometry.BasePoints[(i + 1) % n];
                            loop.Append(Line.CreateBound(new XYZ(p1.X, p1.Y, region.Geometry.ZMin), new XYZ(p2.X, p2.Y, region.Geometry.ZMin)));
                        }
                        profileLoops.Add(loop);

                        Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(profileLoops, XYZ.BasisZ, region.Geometry.ZMax - region.Geometry.ZMin);

                        // 2. 建立 DirectShape
                        DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        ds.SetShape(new GeometryObject[] { solid });
                        ds.Name = "剖面框代理元件 (移動我以調整剖面框)";

                        _proxyElementId = ds.Id;

                        // 3. 設置視圖剖面框貼合
                        var activeView = app.ActiveUIDocument.ActiveView as View3D;
                        if (activeView == null)
                        {
                            activeView = new FilteredElementCollector(doc)
                                .OfClass(typeof(View3D))
                                .Cast<View3D>()
                                .FirstOrDefault(v => !v.IsTemplate);
                        }

                        if (activeView != null)
                        {
                            activeView.CropBoxActive = false;
                            activeView.CropBoxVisible = false;
                            activeView.SetSectionBox(ds.get_BoundingBox(activeView));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[InteractionService] 建立代理失敗: {ex.Message}");
                    }

                    trans.Commit();
                }
            });
        }

        public void Stop()
        {
            _isEditModeActive = false;

            if (_proxyElementId != ElementId.InvalidElementId && _uidoc != null)
            {
                var proxyId = _proxyElementId;
                var region = _activeRegion;
                _proxyElementId = ElementId.InvalidElementId;

                App.AdaptiveRegionHandler.Raise(app =>
                {
                    var doc = app.ActiveUIDocument.Document;
                    using (var trans = new Transaction(doc, "結束剖面框編輯"))
                    {
                        trans.Start();
                        try
                        {
                            var proxy = doc.GetElement(proxyId);
                            if (proxy != null)
                            {
                                // 1. 在編輯結束時，讀取代理元件被使用者手動調整/移動後的最終 BoundingBox，更新回資料庫
                                var bbox = proxy.get_BoundingBox(null);
                                if (bbox != null && region != null)
                                {
                                    region.Geometry.ZMin = bbox.Min.Z;
                                    region.Geometry.ZMax = bbox.Max.Z;
                                    region.Geometry.BasePoints = new List<Point2D>
                                    {
                                        new Point2D(bbox.Min.X, bbox.Min.Y),
                                        new Point2D(bbox.Max.X, bbox.Min.Y),
                                        new Point2D(bbox.Max.X, bbox.Max.Y),
                                        new Point2D(bbox.Min.X, bbox.Max.Y)
                                    };

                                    // 保存至專案儲存區
                                    var loaded = RegionStorageService.Instance.LoadRegions(doc);
                                    var existing = loaded.FirstOrDefault(r => r.Id == region.Id);
                                    if (existing != null)
                                    {
                                        existing.Geometry = region.Geometry;
                                        existing.Name = region.Name;
                                    }
                                    RegionStorageService.Instance.SaveRegions(doc, loaded);
                                }

                                // 2. 刪除 3D 代理元件，保持視景乾淨
                                doc.Delete(proxyId);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[InteractionService] 銷毀代理失敗: {ex.Message}");
                        }
                        trans.Commit();
                    }

                    // 通知 ViewModel UI 同步更新屬性數值
                    ViewModels.RegionManagerViewModel.Instance.RefreshProperties();
                });
            }

            _uidoc = null;
            _activeRegion = null;
        }
    }

    public class AdaptiveRegionExternalEventHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pendingAction;
        private readonly ExternalEvent _externalEvent;

        public AdaptiveRegionExternalEventHandler()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        public void Raise(Action<UIApplication> action)
        {
            if (action == null) return;
            _pendingAction = action;
            _externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            try
            {
                _pendingAction?.Invoke(app);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("[AdaptiveRegion] 外部事件執行異常: " + ex.Message);
            }
            finally
            {
                _pendingAction = null;
            }
        }

        public string GetName() => "Adaptive Region External Event Handler";
    }
}
