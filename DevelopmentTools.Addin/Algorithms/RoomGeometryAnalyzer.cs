using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DevelopmentTools.Core;

namespace DevelopmentTools.Algorithms
{
    public class FaceDrivenGeometryAnalyzer
    {
        private Document _doc;
        private View3D _view3D;

        public FaceDrivenGeometryAnalyzer(Document doc, View3D view3D)
        {
            _doc = doc;
            _view3D = view3D;
        }

        // 輔助方法：判斷元素本身或其所屬群組 (Group) 是否在指定工作集中
        private bool IsElementInWorkset(Element elem, WorksetId filterWorksetId)
        {
            if (filterWorksetId == null) return true; // 未啟用過濾，預設通過
            
            // 1. 檢查元素本身的工作集
            if (elem.WorksetId == filterWorksetId) return true;

            // 2. 檢查其所屬群組 (Group) 的工作集
            if (elem.GroupId != ElementId.InvalidElementId)
            {
                try
                {
                    Element groupElem = _doc.GetElement(elem.GroupId);
                    if (groupElem != null && groupElem.WorksetId == filterWorksetId)
                    {
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        // 輔助方法：獲取控制器所在的 Room (原生 API + 8方向投票容錯探測)
        public Room FindRoomForAnchor(FamilyInstance anchor, View activeView, RoomLocalCoordinate localCoord)
        {
            if (anchor == null) return null;

            // 1. 優先從 Revit 原生 FamilyInstance.Room 取得
            Room room = anchor.Room;
            if (room != null) return room;

            // 2. 嘗試使用視圖的 Phase 取得
            if (activeView != null)
            {
                try
                {
                    Parameter phaseParam = activeView.get_Parameter(BuiltInParameter.VIEW_PHASE);
                    if (phaseParam != null && phaseParam.HasValue)
                    {
                        ElementId phaseId = phaseParam.AsElementId();
                        Phase phase = _doc.GetElement(phaseId) as Phase;
                        if (phase != null)
                        {
                            room = anchor.get_Room(phase);
                            if (room != null) return room;
                        }
                    }
                }
                catch { }
            }

            // 3. Fallback：多距離多方向投票探測 (完全消除牆角與控制器旋轉角度造成的偵測失敗)
            // 優先使用 localCoord.Origin (即 GetTransform().Origin)，這在 Revit 中代表 100% 正確的世界座標放置點，避免 Location 回傳相對座標 0,0,0
            XYZ anchorOrigin = localCoord != null ? localCoord.Origin : (anchor.Location is LocationPoint lp ? lp.Point : XYZ.Zero);
            if (anchorOrigin.IsAlmostEqualTo(XYZ.Zero))
            {
                BoundingBoxXYZ bbox = anchor.get_BoundingBox(null);
                if (bbox != null) anchorOrigin = (bbox.Min + bbox.Max) * 0.5;
            }

            XYZ[] dirs = {
                new XYZ(1, 0, 0), new XYZ(-1, 0, 0), new XYZ(0, 1, 0), new XYZ(0, -1, 0),
                new XYZ(0.707, 0.707, 0), new XYZ(-0.707, 0.707, 0),
                new XYZ(0.707, -0.707, 0), new XYZ(-0.707, -0.707, 0),
                localCoord.BasisX, -localCoord.BasisX,
                localCoord.BasisY, -localCoord.BasisY,
                (localCoord.BasisX + localCoord.BasisY).Normalize(),
                (localCoord.BasisX - localCoord.BasisY).Normalize(),
                (-localCoord.BasisX + localCoord.BasisY).Normalize(),
                (-localCoord.BasisX - localCoord.BasisY).Normalize()
            };

            Dictionary<string, Room> candidates = new Dictionary<string, Room>();
            Dictionary<string, int> votes = new Dictionary<string, int>();

            double checkHeightFeet = 0.5; // 避開地板精度誤差
            double[] offsetDistances = { 0.15, 0.3, 0.6, 1.0 }; // 多重偏置距離投票，避免穿透牆壁進入隔壁房間

            foreach (double dist in offsetDistances)
            {
                foreach (XYZ dir in dirs)
                {
                    XYZ testPt = anchorOrigin + dir.Normalize() * dist + new XYZ(0, 0, checkHeightFeet);
                    Room r = _doc.GetRoomAtPoint(testPt);
                    if (r != null)
                    {
                        string key = r.Number;
                        if (!votes.ContainsKey(key))
                        {
                            votes[key] = 0;
                            candidates[key] = r;
                        }
                        votes[key]++;
                    }
                }
            }

            string bestRoomKey = null;
            int maxVotes = 0;
            foreach (var kvp in votes)
            {
                if (kvp.Value > maxVotes)
                {
                    maxVotes = kvp.Value;
                    bestRoomKey = kvp.Key;
                }
            }

            if (bestRoomKey != null)
            {
                return candidates[bestRoomKey];
            }

            // 備用方案 2：直接在控制器正上方 1.0 呎處進行探測
            try
            {
                room = _doc.GetRoomAtPoint(anchorOrigin + new XYZ(0, 0, 1.0));
                if (room != null) return room;
            }
            catch { }

            return null;
        }

        // 偵測控制器下方的地板 (優先用向下的射線，次選為 BoundingBox 接近值)
        public Floor FindFloorUnderAnchor(XYZ anchorOrigin, RoomLocalCoordinate localCoord, out double topZ, WorksetId filterWorksetId = null)
        {
            topZ = anchorOrigin.Z; // 預設值
            XYZ roomInsidePt = anchorOrigin + (localCoord.BasisX + localCoord.BasisY).Normalize() * 0.5;
            try
            {
                // 從向內偏置點上方 1.0 呎向下射擊 (約 300 mm)，避開牆角縫隙
                XYZ startPt = roomInsidePt + new XYZ(0, 0, 1.0);
                XYZ dir = new XYZ(0, 0, -1);

                ElementClassFilter filter = new ElementClassFilter(typeof(Floor));
                ReferenceIntersector intersector = new ReferenceIntersector(filter, FindReferenceTarget.Element, _view3D);
                
                // 尋找最近的相交
                ReferenceWithContext result = intersector.FindNearest(startPt, dir);
                if (result != null)
                {
                    Reference reference = result.GetReference();
                    Element elem = _doc.GetElement(reference.ElementId);
                    if (elem is Floor floor)
                    {
                        if (IsElementInWorkset(floor, filterWorksetId))
                        {
                            topZ = reference.GlobalPoint.Z;
                            return floor;
                        }
                    }
                }
            }
            catch
            {
                // 射線在某些極端無 3D 幾何或視圖設定下可能失敗，走備用邏輯
            }

            // 備用方案：在控制器周圍在 XY 平面上包含該點且 Z 差最接近的地板
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                ICollection<Element> floors = collector.OfClass(typeof(Floor)).ToElements();
                Floor closestFloor = null;
                double minDiffZ = double.MaxValue;

                foreach (Element elem in floors)
                {
                    if (elem is Floor floor)
                    {
                        if (!IsElementInWorkset(floor, filterWorksetId)) continue;
                        BoundingBoxXYZ bbox = floor.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            // 在 XY 平面上，點必須在 Floor 的 BoundingBox 範圍內 (擴大 0.5 呎容差，同時檢查中心點與偏移點以提升相容性)
                            if ((anchorOrigin.X >= bbox.Min.X - 0.5 && anchorOrigin.X <= bbox.Max.X + 0.5 &&
                                 anchorOrigin.Y >= bbox.Min.Y - 0.5 && anchorOrigin.Y <= bbox.Max.Y + 0.5) ||
                                (roomInsidePt.X >= bbox.Min.X - 0.5 && roomInsidePt.X <= bbox.Max.X + 0.5 &&
                                 roomInsidePt.Y >= bbox.Min.Y - 0.5 && roomInsidePt.Y <= bbox.Max.Y + 0.5))
                            {
                                double floorTopZ = bbox.Max.Z;
                                double diffZ = Math.Abs(floorTopZ - roomInsidePt.Z);
                                if (diffZ < minDiffZ && diffZ < 3.0) // 3 呎 (約 90cm) 內
                                {
                                    minDiffZ = diffZ;
                                    closestFloor = floor;
                                    topZ = floorTopZ;
                                }
                            }
                        }
                    }
                }
                if (closestFloor != null) return closestFloor;
            }
            catch { }

            return null;
        }

        // 1. 基於 Room 空間幾何提取裝修地坪邊界
        public FloorSurfaceGeometry ExtractFloorFace(Room room, XYZ anchorOrigin, RoomLocalCoordinate localCoord, WorksetId filterWorksetId = null)
        {
            if (room == null) return null;

            double topZ = anchorOrigin.Z;
            // 尋找此空間下方的地板 (優先射線，備用 BoundingBox + XY 平面包含過濾)
            Floor floorHost = FindFloorUnderAnchor(anchorOrigin, localCoord, out topZ, filterWorksetId);
            
            PlanarFace topFace = null;
            if (floorHost != null)
            {
                // 尋找 Floor 的頂面 Face 與其 Reference
                double maxZ = -double.MaxValue;
                Options optGeom = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = true };
                GeometryElement geomElem = floorHost.get_Geometry(optGeom);
                if (geomElem != null)
                {
                    foreach (GeometryObject geomObj in geomElem)
                    {
                        if (geomObj is Solid solid && solid.Volume > 0.001)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                if (face is PlanarFace pf)
                                {
                                    if (pf.FaceNormal.IsAlmostEqualTo(new XYZ(0, 0, 1)))
                                    {
                                        if (pf.Origin.Z > maxZ)
                                        {
                                            maxZ = pf.Origin.Z;
                                            topFace = pf;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (floorHost == null || topFace == null)
            {
                // Fallback：當找不到實體地板或其頂面幾何時，直接使用房間 Finish 邊界在房間底部高程 (anchorOrigin.Z) 生成虛擬地坪幾何
                List<CurveLoop> boundaryLoopsFallback = new List<CurveLoop>();
                SpatialElementBoundaryOptions optFallback = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                
                IList<IList<BoundarySegment>> loopsFallback = null;
                try
                {
                    loopsFallback = room.GetBoundarySegments(optFallback);
                }
                catch { }

                if (loopsFallback != null && loopsFallback.Count > 0)
                {
                    foreach (IList<BoundarySegment> loop in loopsFallback)
                    {
                        CurveLoop flatLoop = new CurveLoop();
                        foreach (BoundarySegment seg in loop)
                        {
                            Curve curve = seg.GetCurve();
                            double startZ = curve.GetEndPoint(0).Z;
                            double diffZ = anchorOrigin.Z - startZ;
                            if (Math.Abs(diffZ) > 1e-5)
                            {
                                flatLoop.Append(curve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, diffZ))));
                            }
                            else
                            {
                                flatLoop.Append(curve);
                            }
                        }
                        boundaryLoopsFallback.Add(flatLoop);
                    }
                }

                if (boundaryLoopsFallback.Count > 0)
                {
                    return new FloorSurfaceGeometry
                    {
                        HostElementId = floorHost != null ? floorHost.UniqueId : string.Empty,
                        FaceNormal = new XYZ(0, 0, 1),
                        BoundaryLoops = boundaryLoopsFallback,
                        FaceReference = null, // 沒有 FaceReference
                        FaceObject = null,
                        Origin = new XYZ(anchorOrigin.X, anchorOrigin.Y, anchorOrigin.Z),
                        XVector = XYZ.BasisX,
                        YVector = XYZ.BasisY
                    };
                }
                return null;
            }

            List<CurveLoop> boundaryLoops = new List<CurveLoop>();
            
            // 取得 Room 的裝修面邊界線
            SpatialElementBoundaryOptions opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };
            
            IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(opt);
            if (loops != null)
            {
                foreach (IList<BoundarySegment> loop in loops)
                {
                    CurveLoop flatLoop = new CurveLoop();
                    foreach (BoundarySegment seg in loop)
                    {
                        Curve curve = seg.GetCurve();
                        // 將邊界線的 Z 座標對齊控制點高度 (Origin.Z)，保證模型線建在高度 0
                        double startZ = curve.GetEndPoint(0).Z;
                        double diffZ = anchorOrigin.Z - startZ;
                        if (Math.Abs(diffZ) > 1e-5)
                        {
                            flatLoop.Append(curve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, diffZ))));
                        }
                        else
                        {
                            flatLoop.Append(curve);
                        }
                    }
                    boundaryLoops.Add(flatLoop);
                }
            }

            if (boundaryLoops.Count == 0) return null;

            return new FloorSurfaceGeometry
            {
                HostElementId = floorHost.UniqueId,
                FaceNormal = new XYZ(0, 0, 1),
                BoundaryLoops = boundaryLoops,
                FaceReference = topFace.Reference,
                FaceObject = topFace,
                Origin = topFace.Origin,
                XVector = topFace.XVector,
                YVector = topFace.YVector
            };
        }

        // 2. 基於 Room 空間幾何與其周圍牆體表面提取裝修牆面
        public List<WallSurfaceGeometry> ExtractWallFaces(Room room, XYZ anchorOrigin, RoomLocalCoordinate localCoord, double startHeightMm, double endHeightMm, WorksetId wallWorksetId = null)
        {
            List<WallSurfaceGeometry> wallsGeo = new List<WallSurfaceGeometry>();
            if (room == null) return wallsGeo;

            SpatialElementBoundaryOptions opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(opt);
            if (loops == null) return wallsGeo;

            HashSet<string> processedFaces = new HashSet<string>();
            XYZ roomInsidePt = anchorOrigin + (localCoord.BasisX + localCoord.BasisY).Normalize() * 0.5;

            foreach (IList<BoundarySegment> loop in loops)
            {
                foreach (BoundarySegment seg in loop)
                    {
                    ElementId elemId = seg.ElementId;
                    if (elemId == ElementId.InvalidElementId) continue;

                    Element elem = _doc.GetElement(elemId);
                    if (elem is Wall wall)
                    {
                        // 工作集篩選 (支援群組過濾)
                        if (!IsElementInWorkset(wall, wallWorksetId)) continue;

                        Options geomOpt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = true };
                        GeometryElement geomElem = wall.get_Geometry(geomOpt);
                        if (geomElem == null) continue;

                        foreach (GeometryObject geomObj in geomElem)
                        {
                            if (geomObj is Solid solid && solid.Volume > 0.001)
                            {
                                foreach (Face face in solid.Faces)
                                {
                                    XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
                                    // 必須是垂直面
                                    if (Math.Abs(normal.Z) > 0.05) continue;

                                    BoundingBoxUV bboxUv = face.GetBoundingBox();
                                    UV centerUv = (bboxUv.Min + bboxUv.Max) * 0.5;
                                    XYZ faceCenter = face.Evaluate(centerUv);

                                    // 計算朝法向偏置 0.1 呎的測試點，並確保其 Z 軸落在地坪線上一呎處，以避免因牆面過高或 Room 無上限造成空間偵測失效
                                    XYZ faceNormal = face.ComputeNormal(centerUv);
                                    XYZ testPt = new XYZ(faceCenter.X + faceNormal.X * 0.1, faceCenter.Y + faceNormal.Y * 0.1, anchorOrigin.Z + 1.0);

                                    // 100% 精準空間鎖定：該點必須在此 Room 空間內部，過濾外部/隔壁空間的牆
                                    if (room.IsPointInRoom(testPt))
                                    {
                                        IList<CurveLoop> faceLoops = face.GetEdgesAsCurveLoops();
                                        if (faceLoops.Count == 0) continue;

                                        Curve baseCurve = FindBaseCurveOfFace(faceLoops);
                                        if (baseCurve == null) continue;

                                        // 避免同一個面重複處理
                                        string faceKey = $"{wall.UniqueId}_{faceCenter.X:F3}_{faceCenter.Y:F3}_{faceCenter.Z:F3}";
                                        if (processedFaces.Contains(faceKey)) continue;
                                        processedFaces.Add(faceKey);

                                        List<TileOpening> openings = ExtractOpeningsFromFace(face, baseCurve);

                                        XYZ origin = XYZ.Zero;
                                        XYZ xVector = XYZ.BasisX;
                                        XYZ yVector = XYZ.BasisY;
                                        if (face is PlanarFace pf)
                                        {
                                            origin = pf.Origin;
                                            xVector = pf.XVector;
                                            yVector = pf.YVector;
                                        }

                                        wallsGeo.Add(new WallSurfaceGeometry
                                        {
                                            Curve = baseCurve,
                                            StartPoint = baseCurve.GetEndPoint(0),
                                            EndPoint = baseCurve.GetEndPoint(1),
                                            Normal = -faceNormal, // 朝向房內
                                            StartHeight = startHeightMm,
                                            EndHeight = endHeightMm,
                                            HostElementId = wall.UniqueId,
                                            Openings = openings,
                                            FaceReference = face.Reference,
                                            FaceObject = face,
                                            Origin = origin,
                                            XVector = xVector,
                                            YVector = yVector
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return wallsGeo;
        }

        // 判斷一個點到一條 Curve 在 XY 平面上的最短距離 (Z=0)
        private double DistancePointToCurveXY(XYZ p, Curve curve)
        {
            XYZ pLt = new XYZ(p.X, p.Y, 0);
            XYZ sLt = new XYZ(curve.GetEndPoint(0).X, curve.GetEndPoint(0).Y, 0);
            XYZ eLt = new XYZ(curve.GetEndPoint(1).X, curve.GetEndPoint(1).Y, 0);

            if (curve is Line)
            {
                XYZ v = eLt - sLt;
                XYZ w = pLt - sLt;
                double c1 = w.DotProduct(v);
                if (c1 <= 0) return pLt.DistanceTo(sLt);
                double c2 = v.DotProduct(v);
                if (c2 <= c1) return pLt.DistanceTo(eLt);
                double b = c1 / c2;
                XYZ pb = sLt + b * v;
                return pLt.DistanceTo(pb);
            }
            else
            {
                try
                {
                    IntersectionResult proj = curve.Project(p);
                    if (proj != null)
                    {
                        XYZ projPt = proj.XYZPoint;
                        return new XYZ(p.X, p.Y, 0).DistanceTo(new XYZ(projPt.X, projPt.Y, 0));
                    }
                }
                catch { }
                return Math.Min(pLt.DistanceTo(sLt), pLt.DistanceTo(eLt));
            }
        }

        // 判斷一個點到多個 CurveLoop 邊界在 XY 平面上的最短距離
        private double DistancePointToLoopsXY(XYZ p, List<CurveLoop> loops)
        {
            double minDist = double.MaxValue;
            foreach (var loop in loops)
            {
                foreach (Curve curve in loop)
                {
                    double dist = DistancePointToCurveXY(p, curve);
                    if (dist < minDist)
                    {
                        minDist = dist;
                    }
                }
            }
            return minDist;
        }

        // 判斷一條 Curve 是否在地坪邊緣上 (XY 平面投影距離小於 tolerance)
        private bool IsCurveOnFloorBoundary(Curve baseCurve, List<CurveLoop> floorLoops, double toleranceFeet = 0.2)
        {
            XYZ ptStart = baseCurve.GetEndPoint(0);
            XYZ ptEnd = baseCurve.GetEndPoint(1);
            XYZ ptMid = baseCurve.Evaluate(0.5, true);

            double distStart = DistancePointToLoopsXY(ptStart, floorLoops);
            double distEnd = DistancePointToLoopsXY(ptEnd, floorLoops);
            double distMid = DistancePointToLoopsXY(ptMid, floorLoops);

            return distStart < toleranceFeet && distEnd < toleranceFeet && distMid < toleranceFeet;
        }

        // 3. 視線阻擋測試 (Line-of-Sight Check)：檢查從控制器到牆面是否被其他同工作集的裝修牆阻擋
        private bool IsLineOfSightBlocked(XYZ start, XYZ end, Wall targetWall, WorksetId filterWorksetId)
        {
            try
            {
                XYZ dir = (end - start).Normalize();
                double dist = (end - start).GetLength();
                if (dist < 0.1) return false;

                // 稍微內縮起點與終點以避開自相交
                XYZ rayStart = start + dir * 0.05;
                XYZ rayEnd = end - dir * 0.05;
                double rayDist = (rayEnd - rayStart).GetLength();

                ElementClassFilter filter = new ElementClassFilter(typeof(Wall));
                ReferenceIntersector intersector = new ReferenceIntersector(filter, FindReferenceTarget.Element, _view3D);
                
                IList<ReferenceWithContext> hits = intersector.Find(rayStart, dir);
                foreach (var hit in hits)
                {
                    if (hit.Proximity < rayDist)
                    {
                        Reference r = hit.GetReference();
                        if (r.ElementId != targetWall.Id)
                        {
                            Element hitElem = _doc.GetElement(r.ElementId);
                            if (hitElem is Wall && IsElementInWorkset(hitElem, filterWorksetId))
                            {
                                return true; // 視線被其他裝修牆遮擋
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        // 4. 從幾何 Face 的內環（Inner Loops）提取開口
        private List<TileOpening> ExtractOpeningsFromFace(Face face, Curve baseCurve)
        {
            List<TileOpening> openings = new List<TileOpening>();
            IList<CurveLoop> loops = face.GetEdgesAsCurveLoops();
            if (loops.Count <= 1) return openings;

            XYZ baseStart = baseCurve.GetEndPoint(0);
            XYZ baseDir = (baseCurve.GetEndPoint(1) - baseStart).Normalize();

            for (int i = 1; i < loops.Count; i++)
            {
                CurveLoop loop = loops[i];
                double minU = double.MaxValue, maxU = double.MinValue;
                double minV = double.MaxValue, maxV = double.MinValue;

                foreach (Curve curve in loop)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        XYZ pt = curve.GetEndPoint(j);
                        double distFeet = (pt - baseStart).DotProduct(baseDir);
                        double uMm = distFeet * 304.8;
                        double vMm = (pt.Z - baseStart.Z) * 304.8;

                        if (uMm < minU) minU = uMm; if (uMm > maxU) maxU = uMm;
                        if (vMm < minV) minV = vMm; if (vMm > maxV) maxV = vMm;
                    }
                }

                openings.Add(new TileOpening
                {
                    MinU = minU,
                    MaxU = maxU,
                    MinV = minV,
                    MaxV = maxV
                });
            }
            return openings;
        }

        // 3. 基於空間幾何尋找最近的天花板，並提取其高度
        public double? ExtractCeilingHeight(XYZ anchorOrigin)
        {
            double searchRadiusFeet = 20.0;
            Outline outline = new Outline(
                anchorOrigin - new XYZ(searchRadiusFeet, searchRadiusFeet, searchRadiusFeet), 
                anchorOrigin + new XYZ(searchRadiusFeet, searchRadiusFeet, searchRadiusFeet)
            );
            
            BoundingBoxIntersectsFilter bboxFilter = new BoundingBoxIntersectsFilter(outline);
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            ICollection<Element> nearbyCeilings = collector
                .OfClass(typeof(Ceiling))
                .WherePasses(bboxFilter)
                .ToElements();

            double minDistance = double.MaxValue;
            bool found = false;

            foreach (Element elem in nearbyCeilings)
            {
                Ceiling ceiling = elem as Ceiling;
                if (ceiling == null) continue;

                Options opt = new Options { DetailLevel = ViewDetailLevel.Fine };
                GeometryElement geomElem = ceiling.get_Geometry(opt);
                if (geomElem == null) continue;

                foreach (GeometryObject geomObj in geomElem)
                {
                    if (geomObj is Solid solid)
                    {
                        if (solid.Volume < 0.001) continue;

                        foreach (Face face in solid.Faces)
                        {
                            XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
                            if (normal.Z > -0.9) continue;

                            IntersectionResult projResult = face.Project(anchorOrigin);
                            if (projResult == null) continue;

                            XYZ projPt = projResult.XYZPoint;
                            if (projPt.Z > anchorOrigin.Z)
                            {
                                double dist = projPt.Z - anchorOrigin.Z;
                                if (dist < minDistance)
                                {
                                    minDistance = dist;
                                    found = true;
                                }
                            }
                        }
                    }
                }
            }

            if (found)
            {
                return minDistance * 304.8;
            }
            return null;
        }

        // 自 Face 的邊界環路中，尋找高度最低的水平線段作為排版底線
        private Curve FindBaseCurveOfFace(IList<CurveLoop> loops)
        {
            Curve lowestCurve = null;
            double lowestZ = double.MaxValue;

            foreach (CurveLoop loop in loops)
            {
                foreach (Curve curve in loop)
                {
                    if (curve is Line line)
                    {
                        XYZ start = line.GetEndPoint(0);
                        XYZ end = line.GetEndPoint(1);

                        if (Math.Abs(start.Z - end.Z) < 1e-4)
                        {
                            double midZ = (start.Z + end.Z) / 2.0;
                            if (midZ < lowestZ)
                            {
                                lowestZ = midZ;
                                lowestCurve = curve;
                            }
                        }
                    }
                }
            }
            return lowestCurve;
        }

        // 5. 手動模式：直接點選的 Wall Face 轉換為 WallSurfaceGeometry
        public WallSurfaceGeometry BuildWallGeometryFromFace(Wall wall, PlanarFace face, Reference faceRef, double startHeightMm, double endHeightMm)
        {
            IList<CurveLoop> faceLoops = face.GetEdgesAsCurveLoops();
            Curve baseCurve = FindBaseCurveOfFace(faceLoops);
            if (baseCurve == null)
            {
                // 若找不到水平底線，就用第一條邊線的投影
                foreach (CurveLoop loop in faceLoops)
                {
                    foreach (Curve curve in loop)
                    {
                        baseCurve = curve;
                        break;
                    }
                    if (baseCurve != null) break;
                }
            }

            if (baseCurve == null) return null;

            List<TileOpening> openings = ExtractOpeningsFromFace(face, baseCurve);

            return new WallSurfaceGeometry
            {
                Curve = baseCurve,
                StartPoint = baseCurve.GetEndPoint(0),
                EndPoint = baseCurve.GetEndPoint(1),
                Normal = face.FaceNormal, // 被選取的 Face Normal 本身就是朝外的
                StartHeight = startHeightMm,
                EndHeight = endHeightMm,
                HostElementId = wall.UniqueId,
                Openings = openings,
                FaceReference = faceRef,
                FaceObject = face,
                Origin = face.Origin,
                XVector = face.XVector,
                YVector = face.YVector
            };
        }

        // 6. 手動模式：直接點選的 Floor Face 轉換為 FloorSurfaceGeometry
        public FloorSurfaceGeometry BuildFloorGeometryFromFace(Floor floor, PlanarFace face, Reference faceRef)
        {
            List<CurveLoop> boundaryLoops = new List<CurveLoop>();
            IList<CurveLoop> faceLoops = face.GetEdgesAsCurveLoops();
            foreach (CurveLoop loop in faceLoops)
            {
                boundaryLoops.Add(loop);
            }

            if (boundaryLoops.Count == 0) return null;

            // 優先使用點選點的真實世界高程 Z，繞過 Revit Face.Origin.Z 在局部座標系為 0 的天坑
            XYZ globalOrigin = face.Origin;
            if (faceRef != null && faceRef.GlobalPoint != null)
            {
                globalOrigin = new XYZ(face.Origin.X, face.Origin.Y, faceRef.GlobalPoint.Z);
            }

            return new FloorSurfaceGeometry
            {
                HostElementId = floor.UniqueId,
                FaceNormal = face.FaceNormal,
                BoundaryLoops = boundaryLoops,
                FaceReference = faceRef,
                FaceObject = face,
                Origin = globalOrigin,
                XVector = face.XVector,
                YVector = face.YVector
            };
        }
    }

    public class FloorSurfaceGeometry
    {
        public string HostElementId { get; set; }
        public XYZ FaceNormal { get; set; }
        public List<CurveLoop> BoundaryLoops { get; set; }
        public Reference FaceReference { get; set; }
        public Face FaceObject { get; set; }
        public XYZ Origin { get; set; }
        public XYZ XVector { get; set; }
        public XYZ YVector { get; set; }
    }

    public class WallSurfaceGeometry
    {
        public Curve Curve { get; set; }
        public XYZ StartPoint { get; set; }
        public XYZ EndPoint { get; set; }
        public XYZ Normal { get; set; }
        public double StartHeight { get; set; }
        public double EndHeight { get; set; }
        public string HostElementId { get; set; }
        public List<TileOpening> Openings { get; set; }
        public Reference FaceReference { get; set; }
        public Face FaceObject { get; set; }
        public XYZ Origin { get; set; }
        public XYZ XVector { get; set; }
        public XYZ YVector { get; set; }
    }
}
