using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.AdaptiveSectionBox.Models;
using DevelopmentTools.Modules.AdaptiveSectionBox.Models.Gizmo;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Rendering
{
    public class RenderingService
    {
        private static RenderingService _instance;
        public static RenderingService Instance => _instance ?? (_instance = new RenderingService());

        private readonly DirectContext3DServer _server;
        private bool _isRegistered = false;

        private RenderingService()
        {
            _server = new DirectContext3DServer();
        }

        public void Register()
        {
            if (_isRegistered) return;
            try
            {
                var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) as MultiServerService;
                if (service != null)
                {
                    service.AddServer(_server);
                    var activeServers = service.GetActiveServerIds();
                    activeServers.Add(_server.GetServerId());
                    service.SetActiveServers(activeServers);
                    _isRegistered = true;
                    System.Diagnostics.Debug.WriteLine("[RenderingService] DirectContext3DServer 註冊成功。");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RenderingService] 註冊失敗: {ex.Message}");
            }
        }

        public void UpdatePreview(Document doc, PolygonPrism prism, List<GizmoElement> gizmos, bool isEditModeActive)
        {
            if (doc == null) return;

            // 1. 更新著色伺服器記憶體快取
            _server.UpdateData(prism, gizmos, isEditModeActive);

            // 2. 強制刷新所有視圖 (觸發 RenderScene 重繪)
            try
            {
                var uidoc = new UIDocument(doc);
                uidoc.UpdateAllOpenViews();
                uidoc.RefreshActiveView();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RenderingService] 刷新視圖出錯: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_isRegistered) return;
            try
            {
                var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) as MultiServerService;
                if (service != null)
                {
                    var activeServers = service.GetActiveServerIds();
                    activeServers.Remove(_server.GetServerId());
                    service.SetActiveServers(activeServers);
                    // 備註：Revit 的 AddServer 後一般不支援 RemoveServer，但將其從 ActiveServers 中移除即可停用
                    _isRegistered = false;
                }
                _server.Dispose();
            }
            catch { }
        }
    }
}
