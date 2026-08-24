using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.SheetTools.QuickDimension;

namespace DevelopmentTools.Commands
{
    /// <summary>
    /// 記錄/更新視圖標註邏輯外部指令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_RecordViewAnnotations : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                if (!CommandAccessGuard.EnsureAuthorized("DT_QuickDimension", "記錄標註邏輯"))
                {
                    return Result.Cancelled;
                }

                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc?.Document;
                View view = uidoc?.ActiveView;

                if (doc == null || view == null)
                {
                    message = "找不到有效的活動文件或視圖。";
                    return Result.Failed;
                }

                // 檢查是否是支援的二維平面視圖
                if (view.ViewType != ViewType.FloorPlan && 
                    view.ViewType != ViewType.EngineeringPlan && 
                    view.ViewType != ViewType.CeilingPlan)
                {
                    message = "此工具僅支援平面圖、結構平面圖或天花板平面圖。";
                    return Result.Failed;
                }

                // 開啟 Modal 視窗以儲存/更新標註邏輯
                var window = new RecordAnnotationWindow(doc, view);
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = commandData.Application.MainWindowHandle;

                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"開啟標註邏輯記錄工具失敗：{ex.Message}";
                return Result.Failed;
            }
        }
    }
}
