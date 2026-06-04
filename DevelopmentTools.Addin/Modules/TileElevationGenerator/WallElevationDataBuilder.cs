using System;
using System.Collections.Generic;
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
                
                // 計算牆高 (Revit Feet)
                double height = 3000.0 / 304.8; // 預設 3 米
                if (settings.AutoWallHeight)
                {
                    var heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (heightParam != null && heightParam.HasValue)
                    {
                        double pVal = heightParam.AsDouble();
                        if (pVal > 0.1) height = pVal;
                    }
                    else
                    {
                        // 備用方案：使用 BoundingBox 計算高度
                        var bbox = wall.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            double bboxHeight = bbox.Max.Z - bbox.Min.Z;
                            if (bboxHeight > 0.1) height = bboxHeight;
                        }
                    }
                }

                double levelElevation = 0.0;
                ElementId lvlId = wall.LevelId;
                if (lvlId != ElementId.InvalidElementId)
                {
                    Level lvl = doc.GetElement(lvlId) as Level;
                    if (lvl != null) levelElevation = lvl.Elevation;
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

            // 如果有給定有效的地板中心點，則進行順時針排序
            if (settings.SourceMode == SourceMode.Floor && floorCenter != null)
            {
                SortClockwise(dataList, floorCenter);
            }

            return dataList;
        }

        public static List<WallElevationData> BuildDataFromFloorBoundary(Document doc, Floor floor, GeneratorSettings settings)
        {
            var dataList = new List<WallElevationData>();

            // 1. 取得 Floor 的邊界線 CurveLoop
            var boundaryLoops = FloorBoundaryExtractor.GetFloorBoundaryLoops(floor);
            if (boundaryLoops.Count == 0) return dataList;

            // 2. 計算 Floor 的幾何中心點，並決定當層樓高
            XYZ floorCenter = GetFloorCenter(floor);
            double height = DetectLevelHeight(doc, floor); // 取得自適應樓高 (Feet)

            double levelElevation = 0.0;
            ElementId lvlId = floor.LevelId;
            if (lvlId != ElementId.InvalidElementId)
            {
                Level lvl = doc.GetElement(lvlId) as Level;
                if (lvl != null) levelElevation = lvl.Elevation;
            }

            int index = 1;
            foreach (var loop in boundaryLoops)
            {
                foreach (Curve curve in loop)
                {
                    XYZ start = curve.GetEndPoint(0);
                    XYZ end = curve.GetEndPoint(1);
                    XYZ mid = (start + end) / 2.0;
                    XYZ dir = (end - start).Normalize();

                    // 指向 Floor 中心的方向
                    XYZ toCenter = new XYZ(floorCenter.X - mid.X, floorCenter.Y - mid.Y, 0).Normalize();

                    // 計算邊界的法線並確保指向樓板中心 (向房間內側看)
                    XYZ normal = new XYZ(-dir.Y, dir.X, 0).Normalize();
                    if (normal.DotProduct(toCenter) < 0)
                    {
                        normal = -normal;
                    }

                    var data = new WallElevationData
                    {
                        WallId = ElementId.InvalidElementId,
                        WallName = $"FloorBoundary_{index}",
                        StartPoint = start,
                        EndPoint = end,
                        MidPoint = mid,
                        WallLength = curve.Length,
                        WallHeight = height, // 自適應樓高
                        WallDirection = dir,
                        WallNormal = normal,
                        RoomSideDirection = normal,
                        WallThickness = 0.0, // 樓板邊界沒有牆厚度
                        LevelElevation = levelElevation,
                        WallElement = null,
                        BoundaryCurve = curve
                    };

                    dataList.Add(data);
                    index++;
                }
            }

            // 順時針排序 (依據中點與 floorCenter 角度)
            if (floorCenter != null)
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

        private static double DetectLevelHeight(Document doc, Floor floor)
        {
            double defaultHeightFeet = 3000.0 / 304.8; // 預設 3.0 米
            try
            {
                ElementId currentLevelId = floor.LevelId;
                if (currentLevelId == ElementId.InvalidElementId) return defaultHeightFeet;

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
