using System;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.FloorTools.FloorSnapToRoom
{
    /// <summary>
    /// 樓板吸附對齊專用的幾何運算工具
    /// </summary>
    public static class FloorSnapGeometryUtils
    {
        private const double FeetToMmRatio = 304.8;

        /// <summary>
        /// 公釐 (mm) 轉英制呎 (Feet)
        /// </summary>
        public static double MmToFeet(double mm)
        {
            return mm / FeetToMmRatio;
        }

        /// <summary>
        /// 英制呎 (Feet) 轉公釐 (mm)
        /// </summary>
        public static double FeetToMm(double feet)
        {
            return feet * FeetToMmRatio;
        }

        /// <summary>
        /// 將 3D 曲線投影至指定的 Z 平面
        /// </summary>
        public static Curve ProjectCurveToPlane(Curve curve, double z)
        {
            if (curve == null) return null;

            if (curve is Line line)
            {
                XYZ p0 = line.GetEndPoint(0);
                XYZ p1 = line.GetEndPoint(1);
                return Line.CreateBound(new XYZ(p0.X, p0.Y, z), new XYZ(p1.X, p1.Y, z));
            }
            else if (curve is Arc arc)
            {
                XYZ p0 = arc.GetEndPoint(0);
                XYZ p1 = arc.GetEndPoint(1);
                XYZ pm = arc.Evaluate(0.5, true); // 取得弧線中點
                return Arc.Create(new XYZ(p0.X, p0.Y, z), new XYZ(p1.X, p1.Y, z), new XYZ(pm.X, pm.Y, z));
            }
            
            // 對於無法識別的曲線，退化成以起終點構成的 Line (保險措施)
            try
            {
                XYZ p0 = curve.GetEndPoint(0);
                XYZ p1 = curve.GetEndPoint(1);
                return Line.CreateBound(new XYZ(p0.X, p0.Y, z), new XYZ(p1.X, p1.Y, z));
            }
            catch
            {
                return curve;
            }
        }

        /// <summary>
        /// 計算兩條直線的夾角 (度)，考慮 180 度平行特徵
        /// </summary>
        public static double GetAngleBetweenLines(Line l1, Line l2)
        {
            XYZ v1 = l1.Direction.Normalize();
            XYZ v2 = l2.Direction.Normalize();

            double angleRad = v1.AngleTo(v2);
            double angleDeg = angleRad * (180.0 / Math.PI);
            double diff = angleDeg % 180;
            if (diff > 90)
            {
                diff = 180 - diff;
            }
            return diff;
        }

        /// <summary>
        /// 計算點到無限延伸直線的垂直距離
        /// </summary>
        public static double DistanceToUnboundedLine(XYZ point, Line line)
        {
            XYZ lineDir = line.Direction.Normalize();
            XYZ startToPoint = point - line.GetEndPoint(0);
            
            // 投影在線方向的向量
            XYZ projectVec = startToPoint.DotProduct(lineDir) * lineDir;
            
            // 垂直分量
            XYZ perpendicularVec = startToPoint - projectVec;
            return perpendicularVec.GetLength();
        }

        /// <summary>
        /// 計算兩條幾乎平行的線段在 XY 投影面上的重疊長度 (英制呎)
        /// </summary>
        public static double GetOverlapLength(Line l1, Line l2)
        {
            // 將 l1 投影到 l2 的軸線方向上計算一維重疊區間
            XYZ origin = l2.GetEndPoint(0);
            XYZ dir = l2.Direction.Normalize();
            double l2Length = l2.Length;

            // l2 的一維區間為 [0, l2Length]
            // 將 l1 的端點投影至 l2
            XYZ p1s = l1.GetEndPoint(0);
            XYZ p1e = l1.GetEndPoint(1);

            double t1 = (p1s - origin).DotProduct(dir);
            double t2 = (p1e - origin).DotProduct(dir);

            double tMin = Math.Min(t1, t2);
            double tMax = Math.Max(t1, t2);

            // 計算兩區間的交集長度
            double overlapStart = Math.Max(0.0, tMin);
            double overlapEnd = Math.Min(l2Length, tMax);

            double overlap = overlapEnd - overlapStart;
            return overlap > 0 ? overlap : 0.0;
        }

        /// <summary>
        /// 計算平移向量，將 line1 平移使其落在 line2 的無限延伸線上
        /// </summary>
        public static XYZ GetTranslationToLine(Line l1, Line l2)
        {
            // 取 l1 的中點
            XYZ mid = 0.5 * (l1.GetEndPoint(0) + l1.GetEndPoint(1));
            
            // 中點在 l2 無限延伸線上的投影點
            XYZ origin = l2.GetEndPoint(0);
            XYZ dir = l2.Direction.Normalize();
            XYZ vec = mid - origin;
            XYZ proj = origin + vec.DotProduct(dir) * dir;
            
            // 平移向量為投影點減去原中點
            return proj - mid;
        }

        /// <summary>
        /// 計算兩條直線的 2D (XY 平面) 交點
        /// </summary>
        public static XYZ LineLineIntersection2D(Line l1, Line l2)
        {
            XYZ p1 = l1.GetEndPoint(0);
            XYZ v1 = l1.Direction.Normalize();
            XYZ p2 = l2.GetEndPoint(0);
            XYZ v2 = l2.Direction.Normalize();

            double denom = v1.X * v2.Y - v1.Y * v2.X;
            if (Math.Abs(denom) < 1e-7)
            {
                return null; // 平行無交點
            }

            double t = ((p2.X - p1.X) * v2.Y - (p2.Y - p1.Y) * v2.X) / denom;
            return p1 + t * v1;
        }

        /// <summary>
        /// 計算兩條曲線在 2D (XY 平面) 上的交點。若無直接交點，則嘗試延伸直線求交。
        /// </summary>
        public static XYZ GetIntersection2D(Curve c1, Curve c2)
        {
            if (c1 is Line l1 && c2 is Line l2)
            {
                return LineLineIntersection2D(l1, l2);
            }

            // 嘗試使用 Revit 內建 Intersect
            IntersectionResultArray results;
            SetComparisonResult res = c1.Intersect(c2, out results);
            if ((res == SetComparisonResult.Overlap || res == SetComparisonResult.Subset) && results != null && results.Size > 0)
            {
                return results.get_Item(0).XYZPoint;
            }

            // 嘗試無限延伸直線後求交
            if (c1 is Line line1)
            {
                Line unbound = Line.CreateUnbound(line1.GetEndPoint(0), line1.Direction);
                res = unbound.Intersect(c2, out results);
                if (res == SetComparisonResult.Overlap && results != null && results.Size > 0)
                {
                    return results.get_Item(0).XYZPoint;
                }
            }
            else if (c2 is Line line2)
            {
                Line unbound = Line.CreateUnbound(line2.GetEndPoint(0), line2.Direction);
                res = c1.Intersect(unbound, out results);
                if (res == SetComparisonResult.Overlap && results != null && results.Size > 0)
                {
                    return results.get_Item(0).XYZPoint;
                }
            }

            // 退化防禦：若實在求不出交點，直接返回兩曲線相鄰端點的平均位置
            // 一般在幾何排序中，c1 的終點與 c2 的起點相連
            XYZ ep = c1.GetEndPoint(1);
            XYZ sp = c2.GetEndPoint(0);
            return 0.5 * (ep + sp);
        }
    }
}
