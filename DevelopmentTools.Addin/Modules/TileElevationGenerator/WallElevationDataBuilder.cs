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
