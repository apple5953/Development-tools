using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace DevelopmentTools.Modules.FloorTools.FloorSnapToRoom
{
    /// <summary>
    /// 用於處理 SketchEditScope 的 Failure Preprocessor，防止警告彈出中斷執行
    /// </summary>
    public class SketchEditScopeFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failList = failuresAccessor.GetFailureMessages();
            foreach (FailureMessageAccessor failure in failList)
            {
                // 可以將部分警告消除
                FailureSeverity severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }

    /// <summary>
    /// 樓板貼房間核心業務服務
    /// </summary>
    public class FloorSnapToRoomService
    {
        /// <summary>
        /// 執行樓板貼房間對齊
        /// </summary>
        public FloorSnapToRoomResult ExecuteFloorSnap(Document doc, IList<Floor> floors, FloorSnapToRoomSettings settings = null)
        {
            // TODO: 使用現有 Settings 系統讀取設定
            // var existingSettings = ExistingSettingsService.Load<FloorSnapToRoomSettings>("DT_FloorSnapToRoom");
            if (settings == null)
            {
                settings = new FloorSnapToRoomSettings();
            }

            // TODO: 使用現有 Log 系統
            // ExistingLogger.Info("DT_FloorSnapToRoom", "開始執行樓板貼房間吸附對齊。");
            DevelopmentTools.App.Log($"[DT_FloorSnapToRoom] 開始執行對齊，樓板數量: {floors.Count}");

            FloorSnapToRoomResult totalResult = new FloorSnapToRoomResult
            {
                TotalFloors = floors.Count
            };

            double maxSnapFeet = FloorSnapGeometryUtils.MmToFeet(settings.MaxSnapDistanceMm);

            foreach (Floor floor in floors)
            {
                // 每塊 Floor 獨立 TransactionGroup，確保單一 Floor 失敗時可 RollBack，不影響其他 Floor
                using (TransactionGroup txGroup = new TransactionGroup(doc, "樓板對齊房間 - " + floor.Id.ToString()))
                {
                    txGroup.Start();

                    try
                    {
                        // 1. 偵測樓板對應的房間
                        double distanceFeet = 0.0;
                        Room room = RoomDetectionUtils.FindAssociatedRoom(doc, floor, maxSnapFeet, out distanceFeet);

                        if (room == null)
                        {
                            if (settings.SkipIfRoomNotFound)
                            {
                                totalResult.SkippedFloors++;
                                totalResult.Warnings.Add($"樓板 ID: {floor.Id} 附近找不到對應房間，已跳過。");
                                DevelopmentTools.App.Log($"[DT_FloorSnapToRoom] 樓板 ID: {floor.Id} 附近找不到對應房間，跳過。");

                                totalResult.Errors.Add(new FloorSnapToRoomError
                                {
                                    FloorId = floor.Id,
                                    ErrorCode = "ROOM_NOT_FOUND",
                                    Message = "找不到對應的房間。",
                                    Suggestion = "請調整 MaxSnapDistance 設定，或確認該樓板中心點同樓層附近是否有房間。"
                                });

                                txGroup.RollBack();
                                continue;
                            }
                            else
                            {
                                totalResult.FailedFloors++;
                                totalResult.Errors.Add(new FloorSnapToRoomError
                                {
                                    FloorId = floor.Id,
                                    ErrorCode = "ROOM_NOT_FOUND",
                                    Message = "找不到對應的房間，且設定為不跳過。",
                                    Suggestion = "請確認房間配置。"
                                });
                                txGroup.RollBack();
                                continue;
                            }
                        }

                        // 2. 獲取房間邊界
                        List<Curve> roomBoundary = RoomDetectionUtils.GetRoomBoundaryCurves(doc, room, settings.UseFinishBoundary);
                        if (roomBoundary == null || roomBoundary.Count == 0)
                        {
                            totalResult.FailedFloors++;
                            totalResult.Errors.Add(new FloorSnapToRoomError
                            {
                                FloorId = floor.Id,
                                RoomId = room.Id,
                                RoomName = room.Name,
                                ErrorCode = "ROOM_BOUNDARY_EMPTY",
                                Message = "配對的房間邊界無效或為空。",
                                Suggestion = "請確認房間是否已放置且有封閉的邊界。"
                            });
                            txGroup.RollBack();
                            continue;
                        }

                        // 3. 獲取樓板草圖 Sketch 的高度 Z
                        ElementId sketchId = floor.SketchId;
                        Sketch sketch = doc.GetElement(sketchId) as Sketch;
                        if (sketch == null)
                        {
                            totalResult.FailedFloors++;
                            totalResult.Errors.Add(new FloorSnapToRoomError
                            {
                                FloorId = floor.Id,
                                ErrorCode = "FLOOR_NO_SKETCH",
                                Message = "無法取得該樓板的 Sketch 草圖物件。",
                                Suggestion = "確認是否為 Revit 原生可編輯邊界的 Floor。"
                            });
                            txGroup.RollBack();
                            continue;
                        }

                        double originalZ = 0.0;
                        var profile = sketch.Profile;
                        if (profile.Size > 0)
                        {
                            var loop = profile.get_Item(0);
                            if (loop.Size > 0)
                            {
                                originalZ = loop.get_Item(0).GetEndPoint(0).Z;
                            }
                        }

                        // 4. 進入 SketchEditScope
                        bool success = false;
                        FloorSnapToRoomResult floorResult = null;

                        using (SketchEditScope editScope = new SketchEditScope(doc, "樓板貼房間"))
                        {
                            editScope.Start(sketchId);

                            try
                            {
                                using (Transaction tx = new Transaction(doc, "更新草圖"))
                                {
                                    tx.Start();
                                    
                                    floorResult = FloorSketchEditor.ProcessFloorSketch(doc, floor, room, settings, originalZ);
                                    
                                    if (floorResult.Errors.Count > 0)
                                    {
                                        tx.RollBack();
                                    }
                                    else
                                    {
                                        tx.Commit();
                                        success = true;
                                    }
                                }

                                if (success)
                                {
                                    editScope.Commit(new SketchEditScopeFailuresPreprocessor());
                                    totalResult.SuccessFloors++;
                                    totalResult.UpdatedLines += floorResult.UpdatedLines;
                                    totalResult.IgnoredCurves += floorResult.IgnoredCurves;
                                    DevelopmentTools.App.Log($"[DT_FloorSnapToRoom] 樓板 ID: {floor.Id} 成功吸附至房間 {room.Name} ({room.Id})。更新線段: {floorResult.UpdatedLines}");
                                }
                                else
                                {
                                    editScope.Cancel();
                                    totalResult.FailedFloors++;
                                    if (floorResult != null)
                                    {
                                        totalResult.Errors.AddRange(floorResult.Errors);
                                    }
                                    DevelopmentTools.App.Log($"[DT_FloorSnapToRoom] 樓板 ID: {floor.Id} 處理失敗，已 Rollback 該樓板。");
                                }
                            }
                            catch (Exception ex)
                            {
                                editScope.Cancel();
                                totalResult.FailedFloors++;
                                totalResult.Errors.Add(new FloorSnapToRoomError
                                {
                                    FloorId = floor.Id,
                                    RoomId = room.Id,
                                    RoomName = room.Name,
                                    ErrorCode = "SKETCH_EDIT_FAILED",
                                    Message = "編輯草圖時發生未預期異常：" + ex.Message,
                                    Suggestion = "請確認草圖是否有自交或非直線圓弧以外的特殊幾何。"
                                });
                                DevelopmentTools.App.Log($"[DT_FloorSnapToRoom] 編輯草圖異常: {ex.Message}");
                            }
                        }

                        if (success)
                        {
                            txGroup.Commit();
                        }
                        else
                        {
                            txGroup.RollBack();
                        }
                    }
                    catch (Exception ex)
                    {
                        totalResult.FailedFloors++;
                        totalResult.Errors.Add(new FloorSnapToRoomError
                        {
                            FloorId = floor.Id,
                            ErrorCode = "GEOMETRY_FAILED",
                            Message = "吸附對齊計算錯誤：" + ex.Message,
                            Suggestion = "請檢查幾何計算是否越界。"
                        });
                        DevelopmentTools.App.Log($"[DT_FloorSnapToRoom] 樓板 ID: {floor.Id} 發生幾何異常: {ex.Message}");
                        txGroup.RollBack();
                    }
                }
            }

            // TODO: 使用現有 Log 系統寫入 Usage 統計
            // ExistingLogger.Info("DT_FloorSnapToRoom", $"對齊結束。成功: {totalResult.SuccessFloors}, 失敗: {totalResult.FailedFloors}");
            DevelopmentTools.App.Log($"[DT_FloorSnapToRoom] 執行完成。成功樓板: {totalResult.SuccessFloors}, 失敗: {totalResult.FailedFloors}, 略過: {totalResult.SkippedFloors}");

            return totalResult;
        }
    }
}
