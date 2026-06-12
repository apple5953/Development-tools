using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.TileElevationGenerator
{
    public static class WallElevationDataBuilder
    {
        public static List<WallElevationData> BuildData(Document doc, List<Wall> walls, GeneratorSettings settings, XYZ floorCenter)
        {
            var dataList = new List<WallElevationData>();

            foreach (var wall in walls)
            {
                var wallLoc = wall.Location as LocationCurve;
                if (wallLoc == null || wallLoc.Curve == null) continue;

                Curve curve = wallLoc.Curve;
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);
                
                // 忽略 Z 軸的中點計算
                XYZ mid = (start + end) / 2.0;
                XYZ dir = (end - start).Normalize();
                
                // 計算指向地板中心（房間內部）的方向
                XYZ toCenter = new XYZ(floorCenter.X - mid.X, floorCenter.Y - mid.Y, 0).Normalize();
                
                // 計算牆面法線，並確保其指向地板中心 (房間內)
                XYZ normal = new XYZ(-dir.Y, dir.X, 0).Normalize();
                if (normal.DotProduct(toCenter) < 0)
                {
                    normal = -normal;
                }
                
                // 計算牆長 (Revit Feet)
                double length = settings.AutoWallLength ? curve.Length : (wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? curve.Length);
                
                // 優先使用 BoundingBox 計算牆的實際高度與底部起點
                double height = 3000.0 / 304.8; // 預設 3 米
                double levelElevation = 0.0;
                ElementId lvlId = wall.LevelId;

                var bbox = wall.get_BoundingBox(null);
                if (bbox != null)
                {
                    double bboxHeight = bbox.Max.Z - bbox.Min.Z;
                    if (bboxHeight > 0.1) height = bboxHeight;
                    levelElevation = bbox.Min.Z;
                }
                else
                {
                    // 備用方案 1：使用牆高參數與 Level 高程
                    var heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (heightParam != null && heightParam.HasValue && heightParam.AsDouble() > 0.1)
                    {
                        height = heightParam.AsDouble();
                    }
                    else if (lvlId != ElementId.InvalidElementId)
                    {
                        double levelHeight = DetectLevelHeight(doc, lvlId);
                        if (levelHeight > 0.1) height = levelHeight;
                    }

                    if (lvlId != ElementId.InvalidElementId)
                    {
                        Level lvl = doc.GetElement(lvlId) as Level;
                        if (lvl != null) levelElevation = lvl.Elevation;
                    }
                }

                var data = new WallElevationData
                {
                    WallId = wall.Id,
                    WallName = wall.Name,
                    StartPoint = start,
                    EndPoint = end,
                    MidPoint = mid,
                    WallLength = length,
                    WallHeight = height,
                    WallDirection = dir,
                    WallNormal = normal,
                    RoomSideDirection = normal,
                    WallThickness = wall.Width,
                    LevelElevation = levelElevation,
                    WallElement = wall
                };

                dataList.Add(data);
            }

            // 如果有給定有效的地板中心點且是在樓板模式下，才進行順時針排序。
            // 牆體模式下保留點選順序 (PickObjects 回傳的順序)。
            if (settings.SourceMode == SourceMode.Floor && floorCenter != null)
            {
                SortClockwise(dataList, floorCenter);
            }

            return dataList;
        }

        public static List<WallElevationData> BuildDataFromFloorsAndSolids(Document doc, List<Element> elements, GeneratorSettings settings)
        {
            var dataList = new List<WallElevationData>();
            if (elements == null || elements.Count == 0) return dataList;

            // 1. 取得所有選取元件的幾何實體並聯集 (Boolean Union)
            Solid combinedSolid = null;
            Options opt = new Options { DetailLevel = ViewDetailLevel.Fine };
            
            foreach (var elem in elements)
            {
                GeometryElement geomElem = elem.get_Geometry(opt);
                if (geomElem != null)
                {
                    foreach (GeometryObject geomObj in geomElem)
                    {
                        if (geomObj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 0)
                        {
                            try {
                                if (combinedSolid == null) combinedSolid = solid;
                                else combinedSolid = BooleanOperationsUtils.ExecuteBooleanOperation(combinedSolid, solid, BooleanOperationsType.Union);
                            } catch { /* 忽略聯集失敗的實體 */ }
                        }
                        else if (geomObj is GeometryInstance instance)
                        {
                            GeometryElement instGeom = instance.GetInstanceGeometry();
                            foreach (GeometryObject instObj in instGeom)
                            {
                                if (instObj is Solid solid2 && solid2.Faces.Size > 0 && solid2.Volume > 0)
                                {
                                    try {
                                        if (combinedSolid == null) combinedSolid = solid2;
                                        else combinedSolid = BooleanOperationsUtils.ExecuteBooleanOperation(combinedSolid, solid2, BooleanOperationsType.Union);
                                    } catch { }
                                }
                            }
                        }
                    }
                }
            }

            if (combinedSolid == null) return dataList;

            // 2. 找出最底部的水平面來取得邊界線 (避免抓到牆體的垂直面)
            PlanarFace bottomFace = null;
            double minZ = double.MaxValue;
            foreach (Face face in combinedSolid.Faces)
            {
                if (face is PlanarFace pf)
                {
                    // 尋找法向量朝下的面，或是最接近底部的水平面
                    if (pf.FaceNormal.IsAlmostEqualTo(new XYZ(0, 0, -1)) || pf.FaceNormal.IsAlmostEqualTo(new XYZ(0, 0, 1)))
                    {
                        if (pf.Origin.Z < minZ)
                        {
                            minZ = pf.Origin.Z;
                            bottomFace = pf;
                        }
                    }
                }
            }

            if (bottomFace == null) return dataList;
            
            IList<CurveLoop> boundaryLoops = bottomFace.GetEdgesAsCurveLoops();

            // 3. 取得基準樓板來獲取參數
            Floor baseFloor = elements.FirstOrDefault(e => e is Floor) as Floor;
            XYZ floorCenter = baseFloor != null ? GetFloorCenter(baseFloor) : bottomFace.Origin;
            double height = 3000.0 / 304.8;
            if (baseFloor != null && baseFloor.LevelId != ElementId.InvalidElementId)
            {
                height = DetectLevelHeight(doc, baseFloor.LevelId);
            }
            double levelElevation = 0.0;
            
            if (baseFloor != null && baseFloor.LevelId != ElementId.InvalidElementId)
            {
                Level lvl = doc.GetElement(baseFloor.LevelId) as Level;
                if (lvl != null) levelElevation = lvl.Elevation;
            }
            else
            {
                levelElevation = bottomFace.Origin.Z;
            }

            int index = 1;
            foreach (var loop in boundaryLoops)
            {
                foreach (Curve curve in loop)
                {
                    // 若邊界線段過短 (小於 5cm)，視為雜訊予以忽略
                    if (curve.Length < (50.0 / 304.8)) continue;

                    XYZ start = curve.GetEndPoint(0);
                    XYZ end = curve.GetEndPoint(1);
                    XYZ mid = (start + end) / 2.0;
                    XYZ dir = (end - start).Normalize();

                    XYZ toCenter = new XYZ(floorCenter.X - mid.X, floorCenter.Y - mid.Y, 0).Normalize();

                    XYZ normal = new XYZ(-dir.Y, dir.X, 0).Normalize();
                    if (normal.DotProduct(toCenter) < 0)
                    {
                        normal = -normal;
                    }

                    var data = new WallElevationData
                    {
                        WallId = ElementId.InvalidElementId,
                        WallName = $"Boundary_{index}",
                        StartPoint = start,
                        EndPoint = end,
                        MidPoint = mid,
                        WallLength = curve.Length,
                        WallHeight = height, 
                        WallDirection = dir,
                        WallNormal = normal,
                        RoomSideDirection = normal,
                        WallThickness = 0.0, 
                        LevelElevation = levelElevation,
                        WallElement = null,
                        BoundaryCurve = curve
                    };

                    dataList.Add(data);
                    index++;
                }
            }

            if (baseFloor != null)
            {
                SortClockwise(dataList, floorCenter);
            }

            return dataList;
        }

        private static XYZ GetFloorCenter(Floor floor)
        {
            var bbox = floor.get_BoundingBox(null);
            if (bbox != null)
            {
                return (bbox.Min + bbox.Max) / 2.0;
            }
            var loc = floor.Location as LocationPoint;
            if (loc != null) return loc.Point;
            return XYZ.Zero;
        }

        private static double DetectLevelHeight(Document doc, ElementId currentLevelId)
        {
            double defaultHeightFeet = 3000.0 / 304.8; // 預設 3.0 米
            if (currentLevelId == ElementId.InvalidElementId) return defaultHeightFeet;
            try
            {
                Level currentLevel = doc.GetElement(currentLevelId) as Level;
                if (currentLevel == null) return defaultHeightFeet;

                double currentElevation = currentLevel.Elevation;

                // 獲取所有 Level，並按照 Elevation 排序
                var levelCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .WhereElementIsNotElementType();

                var levels = new List<Level>();
                foreach (var elem in levelCollector)
                {
                    if (elem is Level lvl)
                    {
                        levels.Add(lvl);
                    }
                }

                levels.Sort((a, b) => a.Elevation.CompareTo(b.Elevation));

                // 找出 elevation 大於 currentElevation，且最接近的下一個 Level
                Level nextLevel = null;
                foreach (var lvl in levels)
                {
                    if (lvl.Elevation > currentElevation + 0.01)
                    {
                        nextLevel = lvl;
                        break;
                    }
                }

                if (nextLevel != null)
                {
                    double delta = nextLevel.Elevation - currentElevation;
                    if (delta > 1.0)
                    {
                        return delta;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return defaultHeightFeet;
        }

        private static void SortClockwise(List<WallElevationData> list, XYZ center)
        {
            // 計算相對於中心點的角度，並依角度從大到小 (順時針) 排序
            list.Sort((a, b) =>
            {
                double angleA = Math.Atan2(a.MidPoint.Y - center.Y, a.MidPoint.X - center.X);
                double angleB = Math.Atan2(b.MidPoint.Y - center.Y, b.MidPoint.X - center.X);
                
                // 角度從大到小排序 (順時針)
                return angleB.CompareTo(angleA);
            });
        }
    }
}
