using System;
using System.Collections.Generic;
using System.Linq;

namespace DevelopmentTools.CodeCheck
{
    /// <summary>平面點（公尺）。</summary>
    public struct P2
    {
        public double X, Y;
        public P2(double x, double y) { X = x; Y = y; }
        public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);
        public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);
        public static P2 operator *(P2 a, double k) => new P2(a.X * k, a.Y * k);
        public double Len => Math.Sqrt(X * X + Y * Y);
        public double Dot(P2 b) => X * b.X + Y * b.Y;
        public double Cross(P2 b) => X * b.Y - Y * b.X;
        public P2 Unit => Len < 1e-12 ? new P2(0, 0) : new P2(X / Len, Y / Len);
        public double Dist(P2 b) => (this - b).Len;
    }

    /// <summary>房間平面幾何：最小淨寬、內接圓、取樣點、主軸。全部以公尺計。</summary>
    public static class Geo2D
    {
        public static double SignedArea(IList<P2> poly)
        {
            double a = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                P2 p = poly[i], q = poly[(i + 1) % poly.Count];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a / 2;
        }

        public static P2 Centroid(IList<P2> poly)
        {
            double a = SignedArea(poly);
            if (Math.Abs(a) < 1e-9) return new P2(poly.Average(p => p.X), poly.Average(p => p.Y));
            double cx = 0, cy = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                P2 p = poly[i], q = poly[(i + 1) % poly.Count];
                double f = p.X * q.Y - q.X * p.Y;
                cx += (p.X + q.X) * f;
                cy += (p.Y + q.Y) * f;
            }
            return new P2(cx / (6 * a), cy / (6 * a));
        }

        /// <summary>點是否在（含洞）多邊形內：外框內且不在任何洞內。</summary>
        public static bool Inside(List<List<P2>> loops, P2 p)
        {
            int count = 0;
            foreach (var poly in loops)
            {
                bool inside = false;
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    if (((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) &&
                        (p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y + 1e-15) + poly[i].X))
                        inside = !inside;
                }
                if (inside) count++;
            }
            return count % 2 == 1;
        }

        public static double DistToSegment(P2 p, P2 a, P2 b)
        {
            P2 ab = b - a;
            double t = ab.Dot(ab) < 1e-15 ? 0 : Math.Max(0, Math.Min(1, (p - a).Dot(ab) / ab.Dot(ab)));
            return p.Dist(a + ab * t);
        }

        public static double DistToBoundary(List<List<P2>> loops, P2 p)
        {
            double best = double.MaxValue;
            foreach (var poly in loops)
                for (int i = 0; i < poly.Count; i++)
                    best = Math.Min(best, DistToSegment(p, poly[i], poly[(i + 1) % poly.Count]));
            return best;
        }

        /// <summary>沿射線找與其他邊的第一個交點距離（不含起點所在邊）。</summary>
        private static double RayHit(List<List<P2>> loops, P2 o, P2 d, int skipLoop, int skipEdge)
        {
            double best = double.MaxValue;
            for (int li = 0; li < loops.Count; li++)
            {
                var poly = loops[li];
                for (int i = 0; i < poly.Count; i++)
                {
                    if (li == skipLoop && i == skipEdge) continue;
                    P2 a = poly[i], b = poly[(i + 1) % poly.Count];
                    P2 e = b - a;
                    double den = d.Cross(e);
                    if (Math.Abs(den) < 1e-12) continue;
                    P2 ao = a - o;
                    double t = ao.Cross(e) / den;
                    double u = ao.Cross(d) / den;
                    if (t > 1e-4 && u >= -1e-6 && u <= 1 + 1e-6) best = Math.Min(best, t);
                }
            }
            return best;
        }

        /// <summary>
        /// 最小淨寬：自每段長度 ≥ minEdge 的邊（25%、50%、75% 處）往內側垂直打射線，取到對面邊的最短距離。
        /// 柱、牆凸出造成的縮減會被量到（法規淨寬本來就應扣除）。
        /// </summary>
        public static double MinWidth(List<List<P2>> loops, double minEdge, out List<double> samples)
        {
            samples = new List<double>();
            if (loops.Count == 0) return 0;
            for (int li = 0; li < loops.Count; li++)
            {
                var poly = loops[li];
                bool ccw = SignedArea(poly) > 0;
                for (int i = 0; i < poly.Count; i++)
                {
                    P2 a = poly[i], b = poly[(i + 1) % poly.Count];
                    P2 e = b - a;
                    if (e.Len < minEdge) continue;
                    P2 n = ccw ? new P2(-e.Y, e.X).Unit : new P2(e.Y, -e.X).Unit;
                    foreach (double f in new[] { 0.25, 0.5, 0.75 })
                    {
                        P2 o = a + e * f;
                        // 確認內側方向：往法線方向一點點應在房間內；不在就反過來
                        P2 dir = Inside(loops, o + n * 0.02) ? n : n * -1;
                        double h = RayHit(loops, o, dir, li, i);
                        if (h < double.MaxValue && h > 0.15) samples.Add(h);
                    }
                }
            }
            return samples.Count == 0 ? 0 : samples.Min();
        }

        /// <summary>格點內最大離邊距離 × 2 ≈ 內接圓直徑。</summary>
        public static double InscribedDiameter(List<List<P2>> loops)
        {
            if (loops.Count == 0) return 0;
            var all = loops.SelectMany(l => l).ToList();
            double minX = all.Min(p => p.X), maxX = all.Max(p => p.X), minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
            double step = Math.Max(0.05, Math.Max(maxX - minX, maxY - minY) / 40.0);
            double best = 0;
            for (double x = minX + step / 2; x < maxX; x += step)
                for (double y = minY + step / 2; y < maxY; y += step)
                {
                    var p = new P2(x, y);
                    if (!Inside(loops, p)) continue;
                    best = Math.Max(best, DistToBoundary(loops, p));
                }
            return best * 2;
        }

        /// <summary>房間內的取樣點：格點（最多約 maxCount 個）＋各角落內縮點。</summary>
        public static List<P2> SamplePoints(List<List<P2>> loops, int maxCount, double cornerInset)
        {
            var pts = new List<P2>();
            if (loops.Count == 0) return pts;
            var outer = loops.OrderByDescending(l => Math.Abs(SignedArea(l))).First();
            P2 c = Centroid(outer);
            if (Inside(loops, c)) pts.Add(c);
            foreach (P2 v in outer)
            {
                P2 toC = (c - v).Unit;
                P2 q = v + toC * cornerInset;
                if (Inside(loops, q)) pts.Add(q);
            }
            var all = outer;
            double minX = all.Min(p => p.X), maxX = all.Max(p => p.X), minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
            int n = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, maxCount)));
            double sx = (maxX - minX) / n, sy = (maxY - minY) / n;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    var p = new P2(minX + sx * (i + 0.5), minY + sy * (j + 0.5));
                    if (Inside(loops, p)) pts.Add(p);
                }
            return pts;
        }

        /// <summary>主軸方向（主成分分析）。</summary>
        public static P2 PrincipalAxis(IList<P2> pts, out P2 mean)
        {
            mean = new P2(pts.Average(p => p.X), pts.Average(p => p.Y));
            double sxx = 0, syy = 0, sxy = 0;
            foreach (var p in pts)
            {
                double dx = p.X - mean.X, dy = p.Y - mean.Y;
                sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
            }
            double ang = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            return new P2(Math.Cos(ang), Math.Sin(ang));
        }

        /// <summary>沿主軸投影後的最大間隔（含兩端到牆的距離 × 2，對應「距盡頭牆 ≤ 間距一半」的規定）。</summary>
        public static double MaxGapAlong(IList<P2> outline, IList<P2> devices)
        {
            if (devices.Count == 0 || outline.Count < 3) return double.MaxValue;
            P2 axis = PrincipalAxis(outline, out P2 mean);
            double lo = outline.Min(p => (p - mean).Dot(axis)), hi = outline.Max(p => (p - mean).Dot(axis));
            var t = devices.Select(p => (p - mean).Dot(axis)).OrderBy(v => v).ToList();
            double gap = Math.Max((t[0] - lo) * 2, (hi - t[t.Count - 1]) * 2);
            for (int i = 1; i < t.Count; i++) gap = Math.Max(gap, t[i] - t[i - 1]);
            return gap;
        }
    }
}
