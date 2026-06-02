using System;
using System.Collections.Generic;
using DevelopmentTools.Core;

namespace DevelopmentTools.Algorithms
{
    public static class PolygonClipper
    {
        private const double Epsilon = 1e-7;
        // 判斷點是否在裁剪邊的內側
        private static bool IsInside(UVPoint p, UVPoint p1, UVPoint p2)
        {
            // 利用二維叉積判斷點相對於線段 p1->p2 的方位 (是否在內側/左側)
            // 由於裁剪邊 (Clip Loop) 通常是逆時針，左側即為內側
            return (p2.U - p1.U) * (p.V - p1.V) - (p2.V - p1.V) * (p.U - p1.U) >= -1e-7;
        }

        // 計算主多邊形線段 (s->e) 與裁剪邊線段 (p1->p2) 的交點
        private static UVPoint Intersection(UVPoint s, UVPoint e, UVPoint p1, UVPoint p2)
        {
            double num = (p1.U - s.U) * (p2.V - p1.V) - (p1.V - s.V) * (p2.U - p1.U);
            double den = (e.U - s.U) * (p2.V - p1.V) - (e.V - s.V) * (p2.U - p1.U);
            
            if (Math.Abs(den) < 1e-9) return s; // 平行線
            
            double t = num / den;
            return new UVPoint(s.U + t * (e.U - s.U), s.V + t * (e.V - s.V));
        }

        // 判斷多邊形是否為逆時針方向 (基於 Signed Area)
        private static double Cross(UVPoint a, UVPoint b, UVPoint c)
        {
            return (b.U - a.U) * (c.V - a.V) - (b.V - a.V) * (c.U - a.U);
        }

        private static bool IsConvexVertex(UVPoint prev, UVPoint current, UVPoint next)
        {
            return Cross(prev, current, next) > Epsilon;
        }

        private static bool IsPointInTriangle(UVPoint p, UVPoint a, UVPoint b, UVPoint c)
        {
            double c1 = Cross(a, b, p);
            double c2 = Cross(b, c, p);
            double c3 = Cross(c, a, p);

            bool hasNeg = c1 < -Epsilon || c2 < -Epsilon || c3 < -Epsilon;
            bool hasPos = c1 > Epsilon || c2 > Epsilon || c3 > Epsilon;
            return !(hasNeg && hasPos);
        }

        private static bool IsConvexPolygon(List<UVPoint> polygon)
        {
            if (polygon == null || polygon.Count < 4) return true;

            bool? sign = null;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                UVPoint a = polygon[i];
                UVPoint b = polygon[(i + 1) % n];
                UVPoint c = polygon[(i + 2) % n];
                double cross = Cross(a, b, c);
                if (Math.Abs(cross) <= Epsilon) continue;

                bool currentSign = cross > 0;
                if (sign == null)
                {
                    sign = currentSign;
                }
                else if (sign.Value != currentSign)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<List<UVPoint>> TriangulatePolygon(List<UVPoint> polygon)
        {
            List<List<UVPoint>> triangles = new List<List<UVPoint>>();
            if (polygon == null || polygon.Count < 3) return triangles;

            List<UVPoint> work = new List<UVPoint>(polygon);
            EnsureCounterClockwise(work);

            List<int> indices = new List<int>();
            for (int i = 0; i < work.Count; i++)
            {
                indices.Add(i);
            }

            int guard = 0;
            while (indices.Count > 3 && guard++ < 10000)
            {
                bool earFound = false;

                for (int i = 0; i < indices.Count; i++)
                {
                    int prevIndex = indices[(i - 1 + indices.Count) % indices.Count];
                    int currIndex = indices[i];
                    int nextIndex = indices[(i + 1) % indices.Count];

                    UVPoint prev = work[prevIndex];
                    UVPoint current = work[currIndex];
                    UVPoint next = work[nextIndex];

                    if (!IsConvexVertex(prev, current, next))
                    {
                        continue;
                    }

                    bool containsOtherPoint = false;
                    for (int j = 0; j < indices.Count; j++)
                    {
                        int testIndex = indices[j];
                        if (testIndex == prevIndex || testIndex == currIndex || testIndex == nextIndex)
                        {
                            continue;
                        }

                        if (IsPointInTriangle(work[testIndex], prev, current, next))
                        {
                            containsOtherPoint = true;
                            break;
                        }
                    }

                    if (containsOtherPoint)
                    {
                        continue;
                    }

                    triangles.Add(new List<UVPoint> { prev, current, next });
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                {
                    break;
                }
            }

            if (indices.Count == 3)
            {
                triangles.Add(new List<UVPoint>
                {
                    work[indices[0]],
                    work[indices[1]],
                    work[indices[2]]
                });
            }

            return triangles;
        }

        public static bool IsCounterClockwise(List<UVPoint> polygon)
        {
            double area = 0.0;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                UVPoint p1 = polygon[i];
                UVPoint p2 = polygon[(i + 1) % n];
                area += (p1.U * p2.V) - (p2.U * p1.V);
            }
            return area > 0;
        }

        // 強制確保多邊形頂點為逆時針順序
        public static void EnsureCounterClockwise(List<UVPoint> polygon)
        {
            if (!IsCounterClockwise(polygon))
            {
                polygon.Reverse();
            }
        }

        // Sutherland-Hodgman 多邊形裁剪演算法
        // subjectPolygon: 待裁剪磁磚 (必須為凸多邊形，如矩形)
        // clipPolygon: 房間邊界 (必須是閉合多邊形)
        public static List<UVPoint> ClipPolygon(List<UVPoint> subjectPolygon, List<UVPoint> clipPolygon)
        {
            // 強制確保裁剪邊界為逆時針順序
            EnsureCounterClockwise(clipPolygon);

            List<UVPoint> outputList = new List<UVPoint>(subjectPolygon);

            for (int i = 0; i < clipPolygon.Count; i++)
            {
                UVPoint k1 = clipPolygon[i];
                UVPoint k2 = clipPolygon[(i + 1) % clipPolygon.Count];

                List<UVPoint> inputList = new List<UVPoint>(outputList);
                outputList.Clear();

                if (inputList.Count == 0) break;

                UVPoint s = inputList[inputList.Count - 1];

                foreach (UVPoint e in inputList)
                {
                    if (IsInside(e, k1, k2))
                    {
                        if (!IsInside(s, k1, k2))
                        {
                            outputList.Add(Intersection(s, e, k1, k2));
                        }
                        outputList.Add(e);
                    }
                    else if (IsInside(s, k1, k2))
                    {
                        outputList.Add(Intersection(s, e, k1, k2));
                    }
                    s = e;
                }
            }
            return outputList;
        }

        // 計算多邊形面積 (鞋帶公式 Shoelace Formula)
        public static List<List<UVPoint>> ClipPolygonRegions(List<UVPoint> subjectPolygon, List<UVPoint> clipPolygon)
        {
            List<List<UVPoint>> results = new List<List<UVPoint>>();
            if (subjectPolygon == null || clipPolygon == null || subjectPolygon.Count < 3 || clipPolygon.Count < 3)
            {
                return results;
            }

            List<UVPoint> clipCopy = new List<UVPoint>(clipPolygon);
            EnsureCounterClockwise(clipCopy);

            if (IsConvexPolygon(clipCopy))
            {
                List<UVPoint> clipped = ClipPolygon(subjectPolygon, clipCopy);
                if (clipped.Count >= 3 && CalculatePolygonArea(clipped) > Epsilon)
                {
                    EnsureCounterClockwise(clipped);
                    results.Add(clipped);
                }
                return results;
            }

            List<List<UVPoint>> triangles = TriangulatePolygon(clipCopy);
            foreach (List<UVPoint> triangle in triangles)
            {
                List<UVPoint> clipped = ClipPolygon(subjectPolygon, triangle);
                if (clipped.Count < 3) continue;

                double area = CalculatePolygonArea(clipped);
                if (area <= Epsilon) continue;

                EnsureCounterClockwise(clipped);
                results.Add(clipped);
            }

            return results;
        }

        public static double CalculatePolygonArea(List<UVPoint> polygon)
        {
            double area = 0.0;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                UVPoint p1 = polygon[i];
                UVPoint p2 = polygon[(i + 1) % n];
                area += (p1.U * p2.V) - (p2.U * p1.V);
            }
            return Math.Abs(area) / 2.0;
        }

        // 判斷點是否在多邊形內部 (射線法 Ray Casting Algorithm)
        public static bool IsPointInPolygon(UVPoint p, List<UVPoint> poly)
        {
            if (poly == null || poly.Count < 3) return false;
            int n = poly.Count;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((poly[i].V > p.V) != (poly[j].V > p.V)) &&
                    (p.U < (poly[j].U - poly[i].U) * (p.V - poly[i].V) / (poly[j].V - poly[i].V) + poly[i].U))
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
