using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.SheetTools.QuickViewCreator;

namespace DevelopmentTools.Commands
{
    /// <summary>
    /// 快速開圖與套樣板工具的外部指令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_QuickViewCreator : IExternalCommand
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
                    isAuthorized = GoogleAuthManager.VerifyAccess("DT_QuickViewCreator", "快速開圖與套樣板");
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

                // 2. 開啟配置視窗 (Modal)
                QuickViewCreatorWindow window = new QuickViewCreatorWindow(doc);

                // 設定 Owner 為 Revit 主視窗以防視窗掉到 Revit 後面
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = commandData.Application.MainWindowHandle;

                bool? dialogResult = window.ShowDialog();

                if (dialogResult == true)
                {
                    if (window.ViewModel.LastCreatedSheetId != ElementId.InvalidElementId)
                    {
                        View view = doc.GetElement(window.ViewModel.LastCreatedSheetId) as View;
                        if (view != null)
                        {
                            uidoc.ActiveView = view;
                        }
                    }
                    return Result.Succeeded;
                }

                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"開啟快速開圖工具失敗：{ex.Message}";
                return Result.Failed;
            }
        }
    }
}
