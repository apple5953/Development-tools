using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace DevelopmentTools.Modules.FloorTools.FloorSnapToRoom
{
    /// <summary>
    /// 草圖編輯節點，記錄 ModelCurve、幾何曲線以及拓撲方向
    /// </summary>
    public class LoopNode
    {
        /// <summary>
        /// 草圖中的 ModelCurve 實體
        /// </summary>
        public ModelCurve ModelCurve { get; set; }

        /// <summary>
        /// 運算中的 2D 投影幾何曲線 (Z=0)
        /// </summary>
        public Curve CurrentCurve2D { get; set; }

        /// <summary>
        /// 指示此 ModelCurve 相對於順序方向是否被反向
        /// </summary>
        public bool IsReversed { get; set; }
    }

    /// <summary>
    /// 樓板草圖編輯與拓撲重建引擎
    /// </summary>
    public static class FloorSketchEditor
    {
        /// <summary>
        /// 處理單一樓板的草圖對齊與重建
        /// </summary>
        public static FloorSnapToRoomResult ProcessFloorSketch(
            Document doc,
            Floor floor,
            Room room,
            FloorSnapToRoomSettings settings,
            double originalZ)
        {
            FloorSnapToRoomResult result = new FloorSnapToRoomResult();

            // 1. 取得 Floor 的 Sketch
            Sketch sketch = doc.GetElement(floor.SketchId) as Sketch;
            if (sketch == null)
            {
                result.Errors.Add(new FloorSnapToRoomError
                {
                    FloorId = floor.Id,
                    RoomId = room?.Id ?? ElementId.InvalidElementId,
                    RoomName = room?.Name,
                    ErrorCode = "FLOOR_NO_SKETCH",
                    Message = "找不到樓板的編輯草圖 (Sketch)。",
                    Suggestion = "請確認該樓板是否在 Revit 中具有有效的邊界草圖。"
                });
                return result;
            }

            // 2. 獲取 Sketch 中的所有 ModelCurve
            List<ModelCurve> modelCurves = new List<ModelCurve>();
            foreach (ElementId id in sketch.GetAllElements())
            {
                if (doc.GetElement(id) is ModelCurve mc)
                {
                    modelCurves.Add(mc);
                }
            }

            if (modelCurves.Count == 0)
            {
                result.Errors.Add(new FloorSnapToRoomError
                {
                    FloorId = floor.Id,
                    RoomId = room?.Id ?? ElementId.InvalidElementId,
                    RoomName = room?.Name,
                    ErrorCode = "FLOOR_NO_SKETCH",
                    Message = "樓板草圖中不包含任何曲線元素。",
                    Suggestion = "請檢查樓板是否有繪製邊界。"
                });
                return result;
            }

            // 3. 將曲線拓撲排序成多個閉合環 (Loop)
            List<List<LoopNode>> loops = SortCurvesIntoLoops(modelCurves);
            List<Curve> roomCurves = RoomDetectionUtils.GetRoomBoundaryCurves(doc, room, settings.UseFinishBoundary);

            // 將所有 Room 邊界投影至 Z = 0 的平面
            List<Curve> projectedRoomCurves = new List<Curve>();
            foreach (Curve rc in roomCurves)
            {
                Curve pc = FloorSnapGeometryUtils.ProjectCurveToPlane(rc, 0.0);
                if (pc != null)
                {
                    projectedRoomCurves.Add(pc);
                }
            }

            double maxSnapFeet = FloorSnapGeometryUtils.MmToFeet(settings.MaxSnapDistanceMm);
            double minOverlapFeet = FloorSnapGeometryUtils.MmToFeet(settings.MinOverlapMm);

            int updatedLinesCount = 0;
            int ignoredCurvesCount = 0;

            // 4. 對每個閉合環進行獨立的吸附與重建
            foreach (List<LoopNode> loop in loops)
            {
                // A. 針對 Loop 中的每一條線，尋找最佳配對房間邊界，並進行平移吸附
                foreach (LoopNode node in loop)
                {
                    Curve orig2D = node.CurrentCurve2D;

                    // 若限制只處理直線且其不是直線
                    if (settings.ProcessOnlyLines && !(orig2D is Line))
                    {
                        ignoredCurvesCount++;
                        continue;
                    }

                    if (orig2D is Line origLine)
                    {
                        Curve bestMatchRoomCurve = null;
                        double bestDistance = double.MaxValue;
                        XYZ bestTranslation = XYZ.Zero;

                        foreach (Curve roomCurve in projectedRoomCurves)
                        {
                            if (roomCurve is Line roomLine)
                            {
                                // 檢查平行度
                                double angleDiff = FloorSnapGeometryUtils.GetAngleBetweenLines(origLine, roomLine);
                                if (angleDiff <= settings.ParallelToleranceDegree)
                                {
                                    // 檢查距離
                                    double dist = FloorSnapGeometryUtils.DistanceToUnboundedLine(origLine.GetEndPoint(0), roomLine);
                                    if (dist <= maxSnapFeet && dist < bestDistance)
                                    {
                                        // 檢查重疊長度
                                        double overlap = FloorSnapGeometryUtils.GetOverlapLength(origLine, roomLine);
                                        if (overlap >= minOverlapFeet)
                                        {
                                            bestDistance = dist;
                                            bestMatchRoomCurve = roomLine;
                                            bestTranslation = FloorSnapGeometryUtils.GetTranslationToLine(origLine, roomLine);
                                        }
                                    }
                                }
                            }
                        }

                        if (bestMatchRoomCurve != null)
                        {
                            // 進行平行平移，直接將整條線移過去
                            XYZ p0 = origLine.GetEndPoint(0) + bestTranslation;
                            XYZ p1 = origLine.GetEndPoint(1) + bestTranslation;
                            node.CurrentCurve2D = Line.CreateBound(p0, p1);
                            updatedLinesCount++;
                        }
                    }
                    else if (orig2D is Arc && settings.KeepArcUnchanged)
                    {
                        // 保留圓弧幾何，不進行平移，但後續會重新計算與相鄰線的交點
                        ignoredCurvesCount++;
                    }
                    else
                    {
                        ignoredCurvesCount++;
                    }
                }

                // B. 拓撲端點重建：重新計算相鄰曲線的交點並對齊端點
                int nodeCount = loop.Count;
                XYZ[] newIntersections = new XYZ[nodeCount];

                for (int i = 0; i < nodeCount; i++)
                {
                    LoopNode current = loop[i];
                    LoopNode next = loop[(i + 1) % nodeCount];

                    // 重新求 2D 平面交點
                    XYZ intersect = FloorSnapGeometryUtils.GetIntersection2D(current.CurrentCurve2D, next.CurrentCurve2D);
                    newIntersections[i] = intersect;
                }

                // C. 重新以交點為端點重建每一條曲線幾何
                for (int i = 0; i < nodeCount; i++)
                {
                    LoopNode current = loop[i];
                    
                    // 該曲線的起點交點 (i - 1) 與終點交點 (i)
                    XYZ startInt = newIntersections[(i - 1 + nodeCount) % nodeCount];
                    XYZ endInt = newIntersections[i];

                    // 如果 Loop 節點被反向了，則其開始和結束端點需對調
                    XYZ curveStart = current.IsReversed ? endInt : startInt;
                    XYZ curveEnd = current.IsReversed ? startInt : endInt;

                    if (current.CurrentCurve2D is Line)
                    {
                        current.CurrentCurve2D = Line.CreateBound(curveStart, curveEnd);
                    }
                    else if (current.CurrentCurve2D is Arc arc)
                    {
                        // 圓弧保持原來的圓周形狀：使用新端點和原本的圓弧中點重建
                        XYZ pm = arc.Evaluate(0.5, true);
                        try
                        {
                            current.CurrentCurve2D = Arc.Create(curveStart, curveEnd, pm);
                        }
                        catch
                        {
                            // 萬一三點共線導致 Arc 失敗，降級為 Line 處理以防崩潰
                            current.CurrentCurve2D = Line.CreateBound(curveStart, curveEnd);
                        }
                    }
                    else
                    {
                        // 其他曲線
                        try
                        {
                            current.CurrentCurve2D = Line.CreateBound(curveStart, curveEnd);
                        }
                        catch { }
                    }
                }

                // D. 將計算好的 2D 幾何轉回原高度，並寫回 Revit ModelCurve
                foreach (LoopNode node in loop)
                {
                    Curve updated3D = FloorSnapGeometryUtils.ProjectCurveToPlane(node.CurrentCurve2D, originalZ);
                    if (updated3D != null)
                    {
                        node.ModelCurve.GeometryCurve = updated3D;
                    }
                }
            }

            result.UpdatedLines = updatedLinesCount;
            result.IgnoredCurves = ignoredCurvesCount;
            return result;
        }

        /// <summary>
        /// 鄰近尋找法：將草圖的模型線段重新排序為閉合迴圈
        /// </summary>
        private static List<List<LoopNode>> SortCurvesIntoLoops(List<ModelCurve> modelCurves, double tolerance = 0.005)
        {
            List<List<LoopNode>> loops = new List<List<LoopNode>>();
            List<ModelCurve> remaining = new List<ModelCurve>(modelCurves);

            while (remaining.Count > 0)
            {
                List<LoopNode> currentLoop = new List<LoopNode>();
                ModelCurve firstMc = remaining[0];
                remaining.RemoveAt(0);

                // 投影至 Z=0 平面進行排序
                Curve firstCurve = FloorSnapGeometryUtils.ProjectCurveToPlane(firstMc.GeometryCurve, 0.0);
                XYZ startPoint = firstCurve.GetEndPoint(0);
                XYZ endPoint = firstCurve.GetEndPoint(1);

                currentLoop.Add(new LoopNode
                {
                    ModelCurve = firstMc,
                    CurrentCurve2D = firstCurve,
                    IsReversed = false
                });

                XYZ activeEnd = endPoint;
                bool closed = false;

                while (!closed)
                {
                    bool foundNext = false;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        ModelCurve candidateMc = remaining[i];
                        Curve candCurve = FloorSnapGeometryUtils.ProjectCurveToPlane(candidateMc.GeometryCurve, 0.0);
                        XYZ p0 = candCurve.GetEndPoint(0);
                        XYZ p1 = candCurve.GetEndPoint(1);

                        if (p0.DistanceTo(activeEnd) < tolerance)
                        {
                            currentLoop.Add(new LoopNode
                            {
                                ModelCurve = candidateMc,
                                CurrentCurve2D = candCurve,
                                IsReversed = false
                            });
                            activeEnd = p1;
                            remaining.RemoveAt(i);
                            foundNext = true;
                            break;
                        }
                        else if (p1.DistanceTo(activeEnd) < tolerance)
                        {
                            currentLoop.Add(new LoopNode
                            {
                                ModelCurve = candidateMc,
                                CurrentCurve2D = candCurve,
                                IsReversed = true
                            });
                            activeEnd = p0;
                            remaining.RemoveAt(i);
                            foundNext = true;
                            break;
                        }
                    }

                    if (!foundNext)
                    {
                        // 若搜尋不到相鄰線，但在容許度內起點和目前終點重合，則直接視為閉合
                        if (activeEnd.DistanceTo(startPoint) < tolerance)
                        {
                            closed = true;
                        }
                        else
                        {
                            // 草圖有瑕疵，強制閉合以跳出迴圈
                            closed = true;
                        }
                    }
                    else
                    {
                        if (activeEnd.DistanceTo(startPoint) < tolerance)
                        {
                            closed = true;
                        }
                    }
                }

                loops.Add(currentLoop);
            }

            return loops;
        }
    }
}
