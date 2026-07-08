using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace DevelopmentTools.Modules.FloorTools.FloorSnapToRoom
{
    /// <summary>
    /// 樓板草圖編輯與拓撲重建引擎
    /// </summary>
    public static class FloorSketchEditor
    {
        private struct ShiftedLineInfo
        {
            public XYZ Pt;
            public XYZ Dir;
            public XYZ OrigPt1;
        }

        /// <summary>
        /// 計算兩條 2D 無限延伸直線的交點，若平行則投影 (對應原 Python 邏輯)
        /// </summary>
        private static XYZ GetShiftedIntersection(ShiftedLineInfo L1, ShiftedLineInfo L2)
        {
            XYZ pt1 = L1.Pt;
            XYZ dir1 = L1.Dir;
            XYZ origPt = L1.OrigPt1;

            XYZ pt2 = L2.Pt;
            XYZ dir2 = L2.Dir;

            double det = dir1.X * dir2.Y - dir1.Y * dir2.X;
            if (Math.Abs(det) < 1e-6)
            {
                XYZ v = origPt - pt1;
                double t = v.X * dir1.X + v.Y * dir1.Y;
                return new XYZ(pt1.X + dir1.X * t, pt1.Y + dir1.Y * t, origPt.Z);
            }

            double dx = pt2.X - pt1.X;
            double dy = pt2.Y - pt1.Y;
            double t1 = (dx * dir2.Y - dy * dir2.X) / det;
            return new XYZ(pt1.X + t1 * dir1.X, pt1.Y + t1 * dir1.Y, origPt.Z);
        }

        /// <summary>
        /// 投影點到平面
        /// </summary>
        private static XYZ ProjectToPlane(XYZ pt, Plane plane)
        {
            double dist = plane.Normal.DotProduct(pt.Subtract(plane.Origin));
            return pt.Subtract(plane.Normal.Multiply(dist));
        }

        /// <summary>
        /// 處理單一樓板的草圖對齊與重建 (100% 複刻原 Python 核心邏輯，並使用 2D 投影進行匹配比對)
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

            // 獲取房間邊界
            List<Curve> roomCurves = RoomDetectionUtils.GetRoomBoundaryCurves(doc, room, settings.UseFinishBoundary);
            if (roomCurves == null || roomCurves.Count == 0)
            {
                result.Errors.Add(new FloorSnapToRoomError
                {
                    FloorId = floor.Id,
                    RoomId = room?.Id ?? ElementId.InvalidElementId,
                    RoomName = room?.Name,
                    ErrorCode = "ROOM_BOUNDARY_EMPTY",
                    Message = "房間邊界為空，無法執行吸附。",
                    Suggestion = "請確認房間是否是閉合的。"
                });
                return result;
            }

            double maxSnapFeet = FloorSnapGeometryUtils.MmToFeet(settings.MaxSnapDistanceMm);
            int matchedLinesTotal = 0;

            try
            {
                Plane spPlane = sketch.SketchPlane.GetPlane();
                List<List<Tuple<Curve, Curve>>> finalLoops = new List<List<Tuple<Curve, Curve>>>();

                // 2. 遍歷 sketch.Profile (CurveArrArray) 中的每一個 Loop (CurveArray)
                foreach (CurveArray profile in sketch.Profile)
                {
                    List<Curve> curves = new List<Curve>();
                    foreach (Curve c in profile)
                    {
                        if (c != null)
                        {
                            curves.Add(c);
                        }
                    }

                    int N = curves.Count;
                    if (N == 0) continue;

                    List<ShiftedLineInfo> shiftedLines = new List<ShiftedLineInfo>();

                    for (int i = 0; i < N; i++)
                    {
                        Curve c = curves[i];
                        if (c is Line line)
                        {
                            XYZ pt3D = line.GetEndPoint(0);
                            XYZ origPt1_3D = line.GetEndPoint(1);

                            // 投影至 Z=0 進行 2D 幾何運算，消除高度差
                            XYZ pt = new XYZ(pt3D.X, pt3D.Y, 0.0);
                            XYZ origPt1 = new XYZ(origPt1_3D.X, origPt1_3D.Y, 0.0);

                            if (pt.DistanceTo(origPt1) < 1e-4)
                            {
                                shiftedLines.Add(new ShiftedLineInfo
                                {
                                    Pt = pt3D,
                                    Dir = line.Direction,
                                    OrigPt1 = origPt1_3D
                                });
                                continue;
                            }

                            XYZ dirV = (origPt1 - pt).Normalize();

                            double bestDist = maxSnapFeet;
                            XYZ bestTranslation = XYZ.Zero;
                            bool hasMatch = false;

                            foreach (Curve rc in roomCurves)
                            {
                                if (rc is Line rcLine)
                                {
                                    XYZ rcStart3D = rcLine.GetEndPoint(0);
                                    XYZ rcEnd3D = rcLine.GetEndPoint(1);

                                    // 房間邊線亦投影至 Z=0
                                    XYZ rcStart = new XYZ(rcStart3D.X, rcStart3D.Y, 0.0);
                                    XYZ rcEnd = new XYZ(rcEnd3D.X, rcEnd3D.Y, 0.0);

                                    if (rcStart.DistanceTo(rcEnd) < 1e-4) continue;
                                    XYZ rcDir = (rcEnd - rcStart).Normalize();

                                    double dotProd = dirV.DotProduct(rcDir);

                                    // A. 平行度檢查：夾角小於約 5 度 (abs(abs(dotProd) - 1.0) < 0.004)
                                    if (Math.Abs(Math.Abs(dotProd) - 1.0) < 0.004)
                                    {
                                        // B. 垂直距離計算 (全部使用投影後的 2D 坐標)
                                        XYZ projPt = rcStart + rcDir.Multiply((pt - rcStart).DotProduct(rcDir));
                                        XYZ translationVec = projPt - pt;
                                        double dist = translationVec.GetLength();

                                        if (dist < bestDist)
                                        {
                                            // C. 物理長度重疊度檢查 (Overlap Check)
                                            double rcLen = rcStart.DistanceTo(rcEnd);
                                            double projS = (pt - rcStart).DotProduct(rcDir);
                                            double projE = (origPt1 - rcStart).DotProduct(rcDir);

                                            double minF = Math.Min(projS, projE);
                                            double maxF = Math.Max(projS, projE);

                                            double overlapStart = Math.Max(0.0, minF);
                                            double overlapEnd = Math.Min(rcLen, maxF);
                                            double overlapLen = overlapEnd - overlapStart;

                                            bool hasOverlap = overlapLen > 0.01;

                                            // Fallback: 如果物理上無直接重疊，但樓板線與房間線相距不遠 (端點外 5.0 呎以內)
                                            if (!hasOverlap)
                                            {
                                                if ((minF < 0.0 && maxF > -5.0) || (minF < rcLen + 5.0 && maxF > rcLen))
                                                {
                                                    hasOverlap = true;
                                                }
                                            }

                                            if (hasOverlap)
                                            {
                                                bestDist = dist;
                                                bestTranslation = translationVec; // 這是 Z 軸為 0.0 的水平平移向量
                                                hasMatch = true;
                                            }
                                        }
                                    }
                                }
                            }

                            if (hasMatch)
                            {
                                // 加上平移向量，Z 坐標完全不變，維持工作平面
                                XYZ shiftedPt = pt3D + bestTranslation;
                                XYZ shiftedOrigPt1 = origPt1_3D + bestTranslation;
                                XYZ newDirV = shiftedPt.DistanceTo(shiftedOrigPt1) > 1e-4 
                                    ? (shiftedOrigPt1 - shiftedPt).Normalize() 
                                    : line.Direction;

                                shiftedLines.Add(new ShiftedLineInfo
                                {
                                    Pt = shiftedPt,
                                    Dir = newDirV,
                                    OrigPt1 = shiftedOrigPt1
                                });
                                matchedLinesTotal++;
                            }
                            else
                            {
                                shiftedLines.Add(new ShiftedLineInfo
                                {
                                    Pt = pt3D,
                                    Dir = line.Direction,
                                    OrigPt1 = origPt1_3D
                                });
                            }
                        }
                        else
                        {
                            XYZ pt3D = c.GetEndPoint(0);
                            XYZ origPt1_3D = c.GetEndPoint(1);
                            XYZ dirV = pt3D.DistanceTo(origPt1_3D) > 1e-4 
                                ? (origPt1_3D - pt3D).Normalize() 
                                : XYZ.BasisX;

                            shiftedLines.Add(new ShiftedLineInfo
                            {
                                Pt = pt3D,
                                Dir = dirV,
                                OrigPt1 = origPt1_3D
                            });
                        }
                    }

                    // 重新計算頂點 (與 Python 一致)
                    List<XYZ> newVertices = new List<XYZ>();
                    for (int i = 0; i < N; i++)
                    {
                        ShiftedLineInfo L1 = shiftedLines[(i - 1 + N) % N];
                        ShiftedLineInfo L2 = shiftedLines[i];
                        newVertices.Add(GetShiftedIntersection(L1, L2));
                    }

                    // 重建並安全擴展極短線段
                    List<Tuple<Curve, Curve>> newLoopMapping = new List<Tuple<Curve, Curve>>();
                    for (int i = 0; i < N; i++)
                    {
                        Curve oldC = curves[i];
                        XYZ startP = ProjectToPlane(newVertices[i], spPlane);
                        XYZ endP = ProjectToPlane(newVertices[(i + 1) % N], spPlane);

                        if (startP.DistanceTo(endP) > 0.003)
                        {
                            Curve newC = Line.CreateBound(startP, endP);
                            newLoopMapping.Add(new Tuple<Curve, Curve>(oldC, newC));
                        }
                        else
                        {
                            XYZ midP = 0.5 * (startP + endP);
                            XYZ dV;
                            if (startP.DistanceTo(endP) > 1e-5)
                            {
                                dV = (endP - startP).Normalize();
                            }
                            else
                            {
                                dV = (oldC is Line oldLine) ? oldLine.Direction : XYZ.BasisX;
                            }

                            XYZ safeStart = midP - dV.Multiply(0.0016);
                            XYZ safeEnd = midP + dV.Multiply(0.0016);
                            Curve newC = Line.CreateBound(safeStart, safeEnd);
                            newLoopMapping.Add(new Tuple<Curve, Curve>(oldC, newC));
                        }
                    }

                    if (newLoopMapping.Count > 0)
                    {
                        finalLoops.Add(newLoopMapping);
                    }
                }

                if (finalLoops.Count == 0)
                {
                    throw new Exception("無法運算出新邊界");
                }

                // 3. 套用幾何修改到草圖中的 ModelCurve
                List<CurveElement> existingCes = new List<CurveElement>();
                foreach (ElementId oid in sketch.GetAllElements())
                {
                    Element el = doc.GetElement(oid);
                    if (el is CurveElement ce)
                    {
                        existingCes.Add(ce);
                    }
                }

                int linesCreated = 0;
                // 只有在真正有線段被成功匹配平移時，才寫入 Revit 草圖模型
                if (matchedLinesTotal > 0)
                {
                    foreach (var loopMapping in finalLoops)
                    {
                        foreach (var mapping in loopMapping)
                        {
                            Curve oldC = mapping.Item1;
                            Curve newC = mapping.Item2;

                            CurveElement ceToModify = null;
                            XYZ mpOld = oldC.Evaluate(0.5, true);
                            XYZ mpOld2D = new XYZ(mpOld.X, mpOld.Y, 0.0);

                            foreach (CurveElement ce in existingCes)
                            {
                                if (ce.GeometryCurve != null)
                                {
                                    XYZ mpCe = ce.GeometryCurve.Evaluate(0.5, true);
                                    XYZ mpCe2D = new XYZ(mpCe.X, mpCe.Y, 0.0);
                                    if (mpCe2D.DistanceTo(mpOld2D) < 0.05)
                                    {
                                        ceToModify = ce;
                                        break;
                                    }
                                }
                            }

                            if (ceToModify != null)
                            {
                                try
                                {
                                    ceToModify.SetGeometryCurve(newC, true);
                                    linesCreated++;
                                }
                                catch
                                {
                                    try
                                    {
                                        doc.Delete(ceToModify.Id);
                                        doc.Create.NewModelCurve(newC, sketch.SketchPlane);
                                        linesCreated++;
                                    }
                                    catch
                                    {
                                        throw new Exception("無法修改線段。");
                                    }
                                }
                            }
                            else
                            {
                                throw new Exception("草圖配對失敗。");
                            }
                        }
                    }
                }

                result.UpdatedLines = matchedLinesTotal; // 返回真正有變動吸附的線段數
            }
            catch (Exception ex)
            {
                result.Errors.Add(new FloorSnapToRoomError
                {
                    FloorId = floor.Id,
                    ErrorCode = "GEOMETRY_ERROR",
                    Message = ex.Message,
                    Suggestion = "請確認草圖邊界幾何形狀是否正常。"
                });
            }

            return result;
        }
    }
}
