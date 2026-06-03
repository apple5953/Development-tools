using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.TileElevationGenerator
{
    public static class AdjacentWallFinder
    {
        public static List<Wall> FindAdjacentWalls(Document doc, Floor floor, double minLengthMm, bool skipShort)
        {
            var adjacentWalls = new List<Wall>();
            
            // 1. 取得 Floor 邊界
            var boundaryLoops = FloorBoundaryExtractor.GetFloorBoundaryLoops(floor);
            if (boundaryLoops.Count == 0) return adjacentWalls;

            // 2. 獲取專案中所有牆體
            var allWalls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .ToElements();

            double distanceThreshold = 150.0 / 304.8; // 150mm 轉為英呎，容納粉刷層與對位微小偏差
            double minLengthFeet = minLengthMm / 304.8;

            foreach (var elem in allWalls)
            {
                if (elem is Wall wall)
                {
                    // 檢查牆是否處於同一設計選項 (Design Option) 或是被刪除
                    if (wall.Location == null) continue;
                    
                    var wallLoc = wall.Location as LocationCurve;
                    if (wallLoc == null || wallLoc.Curve == null) continue;

                    Curve wallCurve = wallLoc.Curve;

                    // 檢查最小長度過濾
                    if (skipShort && wallCurve.Length < minLengthFeet)
                    {
                        continue;
                    }

                    // 檢查這面牆是否與 Floor 的任何一條邊界 Curve 相鄰
                    if (IsAdjacent(wallCurve, boundaryLoops, distanceThreshold))
                    {
                        adjacentWalls.Add(wall);
                    }
                }
            }

            return adjacentWalls;
        }

        private static bool IsAdjacent(Curve wallCurve, IList<CurveLoop> boundaryLoops, double threshold)
        {
            // 將牆定位線投影到 XY 平面
            XYZ wallStart = ProjectToXY(wallCurve.GetEndPoint(0));
            XYZ wallEnd = ProjectToXY(wallCurve.GetEndPoint(1));
            XYZ wallMid = ProjectToXY((wallStart + wallEnd) / 2.0);

            foreach (var loop in boundaryLoops)
            {
                foreach (Curve edge in loop)
                {
                    // 計算牆的起點、中點、終點到 Floor 邊緣 Curve 的投影距離
                    double dStart = edge.Distance(wallStart);
                    double dEnd = edge.Distance(wallEnd);
                    double dMid = edge.Distance(wallMid);

                    if (dStart <= threshold || dEnd <= threshold || dMid <= threshold)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static XYZ ProjectToXY(XYZ pt)
        {
            return new XYZ(pt.X, pt.Y, 0);
        }
    }
}
