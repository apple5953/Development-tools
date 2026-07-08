using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace DevelopmentTools.Modules.FloorTools.FloorSnapToRoom
{
    /// <summary>
    /// 樓板所處房間偵測與邊界提取工具
    /// </summary>
    public static class RoomDetectionUtils
    {
        /// <summary>
        /// 取得樓板的幾何中心點 (XY平面中心)
        /// </summary>
        public static XYZ GetFloorCenter(Floor floor)
        {
            BoundingBoxXYZ bbox = floor.get_BoundingBox(null);
            if (bbox != null)
            {
                XYZ min = bbox.Min;
                XYZ max = bbox.Max;
                return new XYZ(0.5 * (min.X + max.X), 0.5 * (min.Y + max.Y), 0.5 * (min.Z + max.Z));
            }

            // 備用方案：如果 bbox 為空，則取所有幾何端點的平均值
            double sumX = 0, sumY = 0, sumZ = 0;
            int count = 0;
            Options opt = new Options { DetailLevel = ViewDetailLevel.Coarse };
            GeometryElement geom = floor.get_Geometry(opt);
            if (geom != null)
            {
                foreach (GeometryObject obj in geom)
                {
                    if (obj is Solid solid && solid.Volume > 0)
                    {
                        foreach (Edge edge in solid.Edges)
                        {
                            Curve c = edge.AsCurve();
                            if (c != null)
                            {
                                XYZ p0 = c.GetEndPoint(0);
                                sumX += p0.X; sumY += p0.Y; sumZ += p0.Z;
                                count++;
                            }
                        }
                    }
                }
            }

            if (count > 0)
            {
                return new XYZ(sumX / count, sumY / count, sumZ / count);
            }
            return XYZ.Zero;
        }

        /// <summary>
        /// 偵測樓板對應的房間。若中心點找不到，則搜尋同樓層最近距離內的房間。
        /// </summary>
        public static Room FindAssociatedRoom(Document doc, Floor floor, double maxSnapDistanceFeet, out double outDistanceFeet)
        {
            outDistanceFeet = 0.0;
            XYZ center = GetFloorCenter(floor);
            Level level = doc.GetElement(floor.LevelId) as Level;
            double levelElevation = level?.Elevation ?? center.Z;

            // 1. 優先以中心點尋找 (Z 值略微調高 1.0 呎以防壓在樓板線上失效)
            XYZ lookupPoint = new XYZ(center.X, center.Y, levelElevation + 1.0);
            Room room = doc.GetRoomAtPoint(lookupPoint);
            
            // 若為 null 且 Level 有效，試試以 Level 的 Phase 尋找
            if (room == null)
            {
                room = doc.GetRoomAtPoint(center);
            }

            if (room != null && room.Area > 0)
            {
                return room;
            }

            // 2. 若找不到，搜尋同樓層最近的房間
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();

            Room closestRoom = null;
            double minDistance = double.MaxValue;

            foreach (Element elem in collector)
            {
                if (elem is Room r && r.Area > 0)
                {
                    // 必須是同樓層的房間
                    if (r.LevelId == floor.LevelId)
                    {
                        double dist = CalculateDistanceToRoom(doc, center, r);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            closestRoom = r;
                        }
                    }
                }
            }

            if (closestRoom != null && minDistance <= maxSnapDistanceFeet)
            {
                outDistanceFeet = minDistance;
                return closestRoom;
            }

            return null;
        }

        /// <summary>
        /// 自動搜尋與樓板在同樓層且距離最近的房間 (對應原 Python 邏輯)
        /// </summary>
        public static Room FindClosestRoom(Floor floor, List<Room> allRooms)
        {
            ElementId floorLevelId = floor.LevelId;
            BoundingBoxXYZ bbox = floor.get_BoundingBox(null);
            if (bbox == null) return null;
            XYZ floorCenter = 0.5 * (bbox.Min + bbox.Max);

            // 1. 優先判定：點是否在房間內部 (將 Z 軸略微抬高 1.0 呎，避免剛好在底部平面上判定失敗)
            XYZ checkPt = new XYZ(floorCenter.X, floorCenter.Y, floorCenter.Z + 1.0);
            foreach (Room room in allRooms)
            {
                ElementId roomLevelId = room.Level != null ? room.Level.Id : room.LevelId;
                if (roomLevelId != floorLevelId) continue;
                if (room.Location == null) continue;

                try
                {
                    if (room.IsPointInRoom(checkPt))
                    {
                        return room;
                    }
                }
                catch
                {
                    // 忽略個別房間的幾何異常
                }
            }

            // 2. Fallback 備用判定：距離最近判定
            Room closestRoom = null;
            double minDist = double.MaxValue;

            foreach (Room room in allRooms)
            {
                ElementId roomLevelId = room.Level != null ? room.Level.Id : room.LevelId;
                if (roomLevelId != floorLevelId) continue;
                if (room.Location == null) continue;

                LocationPoint lp = room.Location as LocationPoint;
                if (lp == null) continue;
                XYZ roomPt = lp.Point;

                // 計算 2D 水平距離
                double dist = new XYZ(floorCenter.X, floorCenter.Y, 0.0).DistanceTo(new XYZ(roomPt.X, roomPt.Y, 0.0));
                if (dist < minDist)
                {
                    minDist = dist;
                    closestRoom = room;
                }
            }
            return closestRoom;
        }

        /// <summary>
        /// 計算中心點到房間邊界線段的最短距離 (2D XY平面)
        /// </summary>
        private static double CalculateDistanceToRoom(Document doc, XYZ point, Room room)
        {
            double minDistance = double.MaxValue;
            List<Curve> boundaryCurves = GetRoomBoundaryCurves(doc, room, true); // 優先使用完成面完成計算

            XYZ p2d = new XYZ(point.X, point.Y, 0.0);

            foreach (Curve curve in boundaryCurves)
            {
                // 將曲線投影至 Z=0
                Curve planeCurve = FloorSnapGeometryUtils.ProjectCurveToPlane(curve, 0.0);
                if (planeCurve != null)
                {
                    double dist = planeCurve.Distance(p2d);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                    }
                }
            }

            return minDistance == double.MaxValue ? 9999.0 : minDistance;
        }

        /// <summary>
        /// 取得房間的所有邊界曲線 (以完成面 Finish 或中心面 Center)
        /// </summary>
        public static List<Curve> GetRoomBoundaryCurves(Document doc, Room room, bool useFinishBoundary)
        {
            List<Curve> curves = new List<Curve>();
            SpatialElementBoundaryOptions opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = useFinishBoundary 
                    ? SpatialElementBoundaryLocation.Finish 
                    : SpatialElementBoundaryLocation.Center
            };

            var boundarySegments = room.GetBoundarySegments(opt);
            if (boundarySegments != null)
            {
                foreach (var loop in boundarySegments)
                {
                    foreach (var seg in loop)
                    {
                        Curve curve = seg.GetCurve();
                        if (curve != null)
                        {
                            curves.Add(curve);
                        }
                    }
                }
            }
            return curves;
        }
    }
}
