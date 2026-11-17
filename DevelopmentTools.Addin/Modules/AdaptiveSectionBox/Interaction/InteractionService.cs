using System;
using System.Collections.Concurrent;
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

        private Document _doc;
        private AdaptiveRegion _activeRegion;
        private bool _isEditModeActive = false;
        private ElementId _proxyElementId = ElementId.InvalidElementId;

        public bool IsEditModeActive => _isEditModeActive;
        public ElementId ProxyElementId => _proxyElementId;

        private InteractionService() { }

        public void Start(UIDocument uidoc, AdaptiveRegion region)
        {
            if (uidoc == null || region?.Geometry?.BasePoints == null) return;
            if (region.Geometry.BasePoints.Count < 3) return;

            Document doc;
            try { doc = uidoc.Document; } catch { return; }
            if (!AdaptiveRegionGuard.CanModify(doc)) return;

            // 若正在編輯另一份文件，先正常結束那一邊（會把那份文件裡的代理量體清掉）
            if (_isEditModeActive && !AdaptiveRegionGuard.SameDocument(_doc, doc))
            {
                Stop();
            }

            _doc = doc;
            _activeRegion = region;
            _isEditModeActive = true;

            // 在 Revit UI 執行緒中非同步建立 DirectShape 代理量體
            App.AdaptiveRegionHandler?.Raise(app =>
            {
                // 事件排隊期間文件可能已關閉、切換，或已經結束編輯
                if (!_isEditModeActive || !AdaptiveRegionGuard.SameDocument(_doc, doc)) return;
                if (!AdaptiveRegionGuard.CanModify(doc)) return;

                AdaptiveRegionGuard.RunTransaction(doc, "建立剖面代理量體", () =>
                {
                    // 反覆 Start（例如拖動高度滑桿）時，先刪掉上一個代理量體，避免累積多個 DirectShape
                    if (_proxyElementId != ElementId.InvalidElementId)
                    {
                        TryDelete(doc, _proxyElementId);
                        _proxyElementId = ElementId.InvalidElementId;
                    }

                    // 1. 建立多邊形拉伸幾何
                    var loop = new CurveLoop();
                    var pts = region.Geometry.BasePoints;
                    int n = pts.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var p1 = pts[i];
                        var p2 = pts[(i + 1) % n];
                        loop.Append(Line.CreateBound(new XYZ(p1.X, p1.Y, region.Geometry.ZMin), new XYZ(p2.X, p2.Y, region.Geometry.ZMin)));
                    }

                    double height = region.Geometry.ZMax - region.Geometry.ZMin;
                    if (height <= 0) return;

                    Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, height);

                    // 2. 建立 DirectShape
                    DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    ds.SetShape(new GeometryObject[] { solid });
                    ds.Name = "剖面框代理元件 (移動我以調整剖面框)";

                    _proxyElementId = ds.Id;

                    // 3. 設置視圖剖面框貼合（只處理本文件目前的 3D 視圖，不會去動其他文件或非 3D 視圖）
                    View3D activeView = null;
                    try
                    {
                        var activeUiDoc = app.ActiveUIDocument;
                        if (activeUiDoc != null && AdaptiveRegionGuard.SameDocument(activeUiDoc.Document, doc))
                        {
                            activeView = activeUiDoc.ActiveView as View3D;
                        }
                    }
                    catch { }

                    if (activeView != null && !activeView.IsTemplate)
                    {
                        var bbox = ds.get_BoundingBox(activeView);
                        if (bbox != null)
                        {
                            activeView.CropBoxActive = false;
                            activeView.CropBoxVisible = false;
                            activeView.SetSectionBox(bbox);
                        }
                    }
                });
            });
        }

        public void Stop()
        {
            bool wasActive = _isEditModeActive;
            _isEditModeActive = false;

            var doc = _doc;
            var proxyId = _proxyElementId;
            var region = _activeRegion;

            _doc = null;
            _activeRegion = null;
            _proxyElementId = ElementId.InvalidElementId;

            if (!wasActive && proxyId == ElementId.InvalidElementId) return;
            if (doc == null || proxyId == ElementId.InvalidElementId) return;

            App.AdaptiveRegionHandler?.Raise(app =>
            {
                // 用建立代理量體時的那份文件，而不是「目前作用中的文件」，避免在別的專案刪錯元件
                if (!AdaptiveRegionGuard.CanModify(doc)) return;

                AdaptiveRegionGuard.RunTransaction(doc, "結束剖面框編輯", () =>
                {
                    var proxy = doc.GetElement(proxyId);
                    if (!(proxy is DirectShape)) return;

                    // 1. 在編輯結束時，讀取代理元件被使用者手動調整/移動後的最終 BoundingBox，更新回資料庫
                    var bbox = proxy.get_BoundingBox(null);
                    if (bbox != null && region?.Geometry != null)
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
                });

                // 通知 ViewModel UI 同步更新屬性數值
                AdaptiveRegionGuard.RunOnUi(() => ViewModels.RegionManagerViewModel.Instance.RefreshProperties());
            });
        }

        private static void TryDelete(Document doc, ElementId id)
        {
            try
            {
                if (doc.GetElement(id) is DirectShape) doc.Delete(id);
            }
            catch (Exception ex)
            {
                AdaptiveRegionGuard.Log("刪除舊代理量體失敗: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Adaptive Region 共用的安全檢查。這些檢查讓本模組在其他外掛操作 Revit 時（沒有開啟文件、族群文件、
    /// 唯讀文件、已有交易進行中、文件已關閉）直接略過，而不是丟出例外。
    /// </summary>
    internal static class AdaptiveRegionGuard
    {
        /// <summary>Revit 每次取得 Document 可能是不同的包裝物件，所以用 Equals 比對，不用參考相等。</summary>
        public static bool SameDocument(Document a, Document b)
        {
            if (a == null || b == null) return false;
            try { return a.Equals(b); } catch { return false; }
        }

        public static bool IsUsable(Document doc)
        {
            try
            {
                return doc != null && doc.IsValidObject && !doc.IsFamilyDocument && !doc.IsLinked;
            }
            catch
            {
                return false;
            }
        }

        public static bool CanModify(Document doc)
        {
            try
            {
                // IsModifiable == true 代表別的程式（例如其他外掛）已開著交易，此時不能再開新交易
                return IsUsable(doc) && !doc.IsReadOnly && !doc.IsModifiable;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>執行交易；失敗時回滾並寫入記錄檔，不往外丟例外。</summary>
        public static bool RunTransaction(Document doc, string name, Action body)
        {
            if (!CanModify(doc) || body == null) return false;

            using (var trans = new Transaction(doc, name))
            {
                try
                {
                    if (trans.Start() != TransactionStatus.Started) return false;
                    body();
                    if (trans.GetStatus() == TransactionStatus.Started)
                    {
                        return trans.Commit() == TransactionStatus.Committed;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    Log($"交易「{name}」失敗: {ex.Message}");
                    try
                    {
                        if (trans.HasStarted() && !trans.HasEnded()) trans.RollBack();
                    }
                    catch { }
                    return false;
                }
            }
        }

        /// <summary>
        /// Revit 不是 WPF 應用程式，System.Windows.Application.Current 常常是 null。
        /// ExternalEvent 本來就在 Revit UI 執行緒執行，可直接呼叫；只有真的跨執行緒時才用 Dispatcher。
        /// </summary>
        public static void RunOnUi(Action action)
        {
            if (action == null) return;
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess()) action();
                else dispatcher.Invoke(action);
            }
            catch (Exception ex)
            {
                Log("更新面板失敗: " + ex.Message);
            }
        }

        public static void Log(string msg)
        {
            System.Diagnostics.Debug.WriteLine("[AdaptiveRegion] " + msg);
            try { App.Log("[AdaptiveRegion] " + msg); } catch { }
        }
    }

    public class AdaptiveRegionExternalEventHandler : IExternalEventHandler
    {
        // 原本只有一個 _pendingAction 欄位：連續 Raise 會覆蓋前一個動作，
        // 而在 Execute 裡再 Raise 的動作會被 finally 清掉。改成佇列，依序執行。
        private readonly ConcurrentQueue<Action<UIApplication>> _pending = new ConcurrentQueue<Action<UIApplication>>();
        private readonly ExternalEvent _externalEvent;

        public AdaptiveRegionExternalEventHandler()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        public void Raise(Action<UIApplication> action)
        {
            if (action == null) return;
            _pending.Enqueue(action);
            try
            {
                _externalEvent?.Raise();
            }
            catch (Exception ex)
            {
                AdaptiveRegionGuard.Log("ExternalEvent.Raise 失敗: " + ex.Message);
            }
        }

        public void Execute(UIApplication app)
        {
            // 沒有開啟任何文件時（例如其他外掛關閉了文件），丟棄排隊中的動作
            bool hasDocument;
            try { hasDocument = app?.ActiveUIDocument?.Document != null; }
            catch { hasDocument = false; }

            int guard = 0;
            while (guard++ < 100 && _pending.TryDequeue(out var action))
            {
                if (!hasDocument) continue;
                try
                {
                    action(app);
                }
                catch (Exception ex)
                {
                    // 不再跳 MessageBox 打斷使用者或其他外掛的流程，只寫入記錄檔
                    AdaptiveRegionGuard.Log("外部事件執行異常: " + ex);
                }
            }
        }

        public string GetName() => "Adaptive Region External Event Handler";
    }
}
