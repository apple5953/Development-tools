using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.SheetTools.RoomFinishConfigurator;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WallFinishCommand : IExternalCommand
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
                    isAuthorized = GoogleAuthManager.VerifyAccess("WallFinish", "自動粉刷系統");
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

                // 2. 開啟已整合的配置與粉刷生成視窗 (Modeless)
                RoomFinishConfiguratorWindow window = new RoomFinishConfiguratorWindow(uidoc);

                // 設定 Owner 為 Revit 主視窗
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = commandData.Application.MainWindowHandle;

                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"開啟自動粉刷與配置工具失敗：{ex.Message}";
                return Result.Failed;
            }
        }
    }
}
