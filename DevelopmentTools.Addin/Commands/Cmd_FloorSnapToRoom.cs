using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.FloorTools.FloorSnapToRoom;

namespace DevelopmentTools.Commands
{
    /// <summary>
    /// 樓板貼房間指令過濾器，僅允許選取樓板 (Floor)
    /// </summary>
    public class FloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Floor;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    /// <summary>
    /// 樓板貼房間 (Floor Snap To Room) 外部指令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_FloorSnapToRoom : IExternalCommand
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
                    // 同步等待非同步驗證，保留 Revit API Context 鎖
                    isAuthorized = System.Threading.Tasks.Task.Run(async () =>
                    {
                        return await GoogleAuthManager.VerifyAccessAsync("DT_FloorSnapToRoom", "樓板貼房間");
                    }).GetAwaiter().GetResult();
                }

                if (!isAuthorized)
                {
                    return Result.Failed;
                }

                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;

                if (doc == null)
                {
                    message = "無效的活動文件。";
                    return Result.Failed;
                }

                // 2. 取得選取的樓板
                List<Floor> targetFloors = new List<Floor>();
                ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
                
                foreach (ElementId id in selectedIds)
                {
                    if (doc.GetElement(id) is Floor floor)
                    {
                        targetFloors.Add(floor);
                    }
                }

                // 3. 若未預選 Floor，要求使用者手動選取一塊或多塊 Floor
                if (targetFloors.Count == 0)
                {
                    try
                    {
                        IList<Reference> pickedRefs = uidoc.Selection.PickObjects(
                            ObjectType.Element,
                            new FloorSelectionFilter(),
                            "請選取一塊或多塊要貼齊房間的樓板："
                        );

                        foreach (Reference r in pickedRefs)
                        {
                            if (doc.GetElement(r.ElementId) is Floor floor)
                            {
                                targetFloors.Add(floor);
                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        // 使用者取消選取
                        return Result.Cancelled;
                    }
                }

                if (targetFloors.Count == 0)
                {
                    TaskDialog.Show("提示", "未選取任何樓板。");
                    return Result.Cancelled;
                }

                // 4. 讀取設定與執行吸附服務
                FloorSnapToRoomSettings settings = new FloorSnapToRoomSettings();
                
                // TODO: 使用現有 Settings 系統讀取設定
                // settings = ExistingSettingsService.Load<FloorSnapToRoomSettings>("DT_FloorSnapToRoom") ?? settings;

                FloorSnapToRoomService service = new FloorSnapToRoomService();
                FloorSnapToRoomResult result = service.ExecuteFloorSnap(doc, targetFloors, settings);

                // 5. 顯示總結報告
                if (settings.EnableDetailedReport)
                {
                    ShowReportDialog(result);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                DevelopmentTools.App.Log($"[DT_FloorSnapToRoom] 指令執行異常: {ex.Message}\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 彈出詳細總結報告視窗
        /// </summary>
        private void ShowReportDialog(FloorSnapToRoomResult result)
        {
            TaskDialog report = new TaskDialog("樓板貼房間 - 執行結果統計報告")
            {
                MainInstruction = $"成功: {result.SuccessFloors} / 失敗: {result.FailedFloors} / 略過: {result.SkippedFloors}",
                CommonButtons = TaskDialogCommonButtons.Ok
            };

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"■ 樓板統計資訊：");
            sb.AppendLine($"  - 處理樓板總數: {result.TotalFloors} 塊");
            sb.AppendLine($"  - 更新邊界線段: {result.UpdatedLines} 條");
            sb.AppendLine($"  - 略過未改幾何: {result.IgnoredCurves} 條 (如圓弧或非對齊邊)");
            sb.AppendLine();

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine($"■ 提示與警告：");
                foreach (string warn in result.Warnings)
                {
                    sb.AppendLine($"  * {warn}");
                }
                sb.AppendLine();
            }

            if (result.Errors.Count > 0)
            {
                sb.AppendLine($"■ 錯誤記錄 ({result.Errors.Count})：");
                foreach (var err in result.Errors)
                {
                    sb.AppendLine($"  * 樓板 ID: {err.FloorId}");
                    sb.AppendLine($"    代碼: {err.ErrorCode}");
                    sb.AppendLine($"    說明: {err.Message}");
                    if (!string.IsNullOrEmpty(err.Suggestion))
                    {
                        sb.AppendLine($"    建議: {err.Suggestion}");
                    }
                    sb.AppendLine();
                }
            }

            report.MainContent = sb.ToString();
            report.Show();
        }
    }
}
