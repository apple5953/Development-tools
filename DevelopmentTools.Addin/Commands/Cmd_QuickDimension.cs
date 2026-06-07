using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.SheetTools.QuickDimension;

namespace DevelopmentTools.Commands
{
    /// <summary>
    /// 快速尺寸標註工具的外部指令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_QuickDimension : IExternalCommand
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
                    isAuthorized = GoogleAuthManager.VerifyAccess("DT_QuickDimension", "快速尺寸標註");
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

                // 2. 以 Modeless 模式開啟快速標註視窗
                QuickDimensionWindow window = new QuickDimensionWindow(doc);
                
                // 設定 Owner 為 Revit 主視窗以防視窗掉到 Revit 後面
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = commandData.Application.MainWindowHandle;

                window.Show(); // Modeless 開啟，不阻塞 Revit

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"開啟快速尺寸標註工具失敗：{ex.Message}";
                return Result.Failed;
            }
        }
    }
}
