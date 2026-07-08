using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.FloorTools.FloorSnapToRoom;
using Autodesk.Revit.DB.Architecture;

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
    /// 樓板貼房間指令過濾器，僅允許選取房間 (Room)
    /// </summary>
    public class RoomSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is Room) return true;
            if (elem.Category != null && elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Rooms) return true;
            return false;
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
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int selectedFloorCount = 0;
            FloorSnapToRoomResult result = new FloorSnapToRoomResult();

            try
            {
                // 1. 檢查既有授權系統
                bool isAuthorized = true;
                if (GoogleAuthManager.IsAuthEnabled())
                {
                    isAuthorized = GoogleAuthManager.VerifyAccess("DT_FloorSnapToRoom", "樓板貼房間");
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

                // 2. 取得選取的樓板與房間
                List<Floor> targetFloors = new List<Floor>();
                List<Room> targetRooms = new List<Room>();
                ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
                
                foreach (ElementId id in selectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem is Floor floor)
                    {
                        targetFloors.Add(floor);
                    }
                    else if (elem is Room room)
                    {
                        targetRooms.Add(room);
                    }
                    else if (elem != null && elem.Category != null && elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Rooms)
                    {
                        if (elem is Room r)
                        {
                            targetRooms.Add(r);
                        }
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
                            "請選取要吸附的樓板 (選完請按 Enter 或右鍵完成)："
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

                selectedFloorCount = targetFloors.Count;
                if (targetFloors.Count == 0)
                {
                    TaskDialog.Show("提示", "未選取任何樓板。");
                    return Result.Cancelled;
                }

                // 處理目標房間
                Room targetRoom = null;
                List<Room> allRoomsInDoc = new List<Room>();
                
                if (targetRooms.Count > 0)
                {
                    targetRoom = targetRooms[0];
                }
                else
                {
                    FilteredElementCollector col = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType();
                    
                    foreach (Element e in col)
                    {
                        if (e is Room r && r.Location != null && r.Area > 0)
                        {
                            allRoomsInDoc.Add(r);
                        }
                    }

                    if (allRoomsInDoc.Count == 0)
                    {
                        try
                        {
                            Reference refRoom = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                new RoomSelectionFilter(),
                                "專案中無已放置房間，請手動點選對齊的目標房間："
                            );
                            targetRoom = doc.GetElement(refRoom) as Room;
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            return Result.Cancelled;
                        }
                    }
                }

                // 4. 讀取設定與執行吸附服務
                FloorSnapToRoomSettings settings = new FloorSnapToRoomSettings();
                
                // TODO: 使用現有 Settings 系統讀取設定
                // settings = ExistingSettingsService.Load<FloorSnapToRoomSettings>("DT_FloorSnapToRoom") ?? settings;

                FloorSnapToRoomService service = new FloorSnapToRoomService();
                result = service.ExecuteFloorSnap(doc, targetFloors, targetRoom, allRoomsInDoc, settings);

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
            finally
            {
                stopwatch.Stop();
                string userId = GoogleAuthManager.CurrentUserEmail ?? "UnknownUser";
                string projectName = commandData.Application.ActiveUIDocument?.Document?.Title ?? "UnknownProject";
                string docPath = commandData.Application.ActiveUIDocument?.Document?.PathName ?? "UnknownPath";
                long runTime = stopwatch.ElapsedMilliseconds;

                DevelopmentTools.App.Log($"[UsageStats] ToolName=DT_FloorSnapToRoom; UserId={userId}; ProjectName={projectName}; DocumentPath={docPath}; RunTime={runTime}ms; SelectedFloorCount={selectedFloorCount}; SuccessFloorCount={result.SuccessFloors}; UpdatedCurveCount={result.UpdatedLines}; ErrorCount={result.Errors.Count}");
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
