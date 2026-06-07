using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.SheetTools.RoomFinishConfigurator;

namespace DevelopmentTools.Commands
{
    /// <summary>
    /// 房間裝修材質配置工具外部指令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_RoomFinishConfigurator : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                // 1. 檢查既有授權系統
                bool isAuthorized = true;
                if (GoogleAuthManager.IsAuthEnabled())
                {
                    isAuthorized = GoogleAuthManager.VerifyAccess("DT_RoomFinishConfigurator", "房間裝修配置");
                }

                if (!isAuthorized)
                {
                    return Result.Failed;
                }

                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc?.Document;

                if (doc == null)
                {
                    message = "無效的活動文件。";
                    return Result.Failed;
                }

                // 2. 開啟配置視窗 (Modeless)
                RoomFinishConfiguratorWindow window = new RoomFinishConfiguratorWindow(uidoc);

                // 設定 Owner 為 Revit 主視窗以防視窗掉到 Revit 後面
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = commandData.Application.MainWindowHandle;

                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"開啟房間裝修材質配置工具失敗：{ex.Message}";
                return Result.Failed;
            }
        }
    }
}
