using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_AdaptiveRegion : IExternalCommand
    {
        public static readonly Guid PaneGuid = new Guid("c84022a1-e406-444a-b50a-e992b8ef9fef");

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!CommandAccessGuard.EnsureAuthorized("AdaptiveRegion", "智慧剖面框"))
                {
                    return Result.Cancelled;
                }

                var uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null)
                {
                    TaskDialog.Show("錯誤", "找不到活動的 Revit 文件。");
                    return Result.Failed;
                }

                // 獲取 Dockable Pane 實體
                var paneId = new DockablePaneId(PaneGuid);
                var pane = commandData.Application.GetDockablePane(paneId);
                if (pane != null)
                {
                    pane.Show();
                    
                    // 同步當前文件並載入數據
                    Modules.AdaptiveSectionBox.ViewModels.RegionManagerViewModel.Instance.SetDocument(uidoc);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
