using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.SheetTools.BatchSheetRenamer;

namespace DevelopmentTools.Commands
{
    /// <summary>
    /// 圖紙批次更名工具的外部指令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_BatchSheetRenamer : IExternalCommand
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
                    isAuthorized = System.Threading.Tasks.Task.Run(async () =>
                    {
                        return await GoogleAuthManager.VerifyAccessAsync("DT_BatchSheetRenamer", "圖紙批次更名");
                    }).GetAwaiter().GetResult();
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

                // 2. 開啟批次更名 Modal 視窗
                BatchSheetRenamerWindow window = new BatchSheetRenamerWindow(doc);
                
                // 設定 Owner 為 Revit 主視窗以防視窗掉到 Revit 後面
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = commandData.Application.MainWindowHandle;

                bool? dialogResult = window.ShowDialog();

                if (dialogResult == true)
                {
                    return Result.Succeeded;
                }

                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"開啟圖紙批次更名工具失敗：{ex.Message}";
                return Result.Failed;
            }
        }
    }
}
