using System;
using System.Collections.Generic;
using System.Linq;

namespace DevelopmentTools.CodeCheck
{
    /// <summary>門的通行開口：中心、牆法線、門寬、穿越深度（牆厚＋兩側餘裕），公尺。</summary>
    public struct DoorBridge
    {
        public P2 Center, Normal;
        public double Width, Depth;
    }

    /// <summary>
    /// 平面步行距離網格。可走範圍 = 該層所有房間（不分來自哪個模型）＋門的開口，
    /// 由終點（樓梯口、出口、設備）做多源 Dijkstra，16 方向移動（誤差約 3% 以內，略偏保守）。
    /// 不需要 Revit 的行走路徑功能，因此拆檔、連結模型的房間與門也能一起計算。
    /// </summary>
    public class WalkGrid
    {
        private readonly double _x0, _y0, _cell;
        private readonly int _nx, _ny;
        private readonly bool[] _walk;
        public double CellSize => _cell;
        public int WalkableCells { get; private set; }

        private WalkGrid(double x0, double y0, double cell, int nx, int ny)
        {
            _x0 = x0; _y0 = y0; _cell = cell; _nx = nx; _ny = ny;
            _walk = new bool[nx * ny];
        }

        public static WalkGrid Build(IList<List<List<P2>>> rooms, IList<DoorBridge> doors, double cell = 0.25, int maxCells = 2000000)
        {
            var all = rooms.SelectMany(r => r).SelectMany(l => l).ToList();
            if (all.Count == 0) return null;
            double minX = all.Min(p => p.X) - 2, minY = all.Min(p => p.Y) - 2, maxX = all.Max(p => p.X) + 2, maxY = all.Max(p => p.Y) + 2;
            while ((maxX - minX) / cell * ((maxY - minY) / cell) > maxCells) cell *= 1.25;
            int nx = (int)Math.Ceiling((maxX - minX) / cell) + 1, ny = (int)Math.Ceiling((maxY - minY) / cell) + 1;
            var g = new WalkGrid(minX, minY, cell, nx, ny);

            foreach (var loops in rooms)
            {
                var pts = loops.SelectMany(l => l).ToList();
                if (pts.Count < 3) continue;
                int i0 = g.Ix(pts.Min(p => p.X)), i1 = g.Ix(pts.Max(p => p.X)), j0 = g.Iy(pts.Min(p => p.Y)), j1 = g.Iy(pts.Max(p => p.Y));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                        if (!g._walk[j * nx + i] && Geo2D.Inside(loops, g.Center(i, j))) g._walk[j * nx + i] = true;
            }

            foreach (var d in doors)
            {
                P2 n = d.Normal.Unit;
                if (n.Len < 0.5) continue;
                P2 t = new P2(-n.Y, n.X);
                double hw = Math.Max(d.Width * 0.9, cell * 2.2) / 2, hd = Math.Max(d.Depth, cell * 2) / 2;
                double r = Math.Sqrt(hw * hw + hd * hd);
                int i0 = g.Ix(d.Center.X - r), i1 = g.Ix(d.Center.X + r), j0 = g.Iy(d.Center.Y - r), j1 = g.Iy(d.Center.Y + r);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        P2 q = g.Center(i, j) - d.Center;
                        if (Math.Abs(q.Dot(n)) <= hd && Math.Abs(q.Dot(t)) <= hw) g._walk[j * nx + i] = true;
                    }
            }
            g.WalkableCells = g._walk.Count(w => w);
            return g;
        }

        private int Ix(double x) => Math.Max(0, Math.Min(_nx - 1, (int)Math.Floor((x - _x0) / _cell)));
        private int Iy(double y) => Math.Max(0, Math.Min(_ny - 1, (int)Math.Floor((y - _y0) / _cell)));
        private P2 Center(int i, int j) => new P2(_x0 + (i + 0.5) * _cell, _y0 + (j + 0.5) * _cell);
        private bool W(int i, int j) => i >= 0 && j >= 0 && i < _nx && j < _ny && _walk[j * _nx + i];

        /// <summary>找最近的可走格（半徑 radius 公尺內），回傳格索引與點到格中心的距離。</summary>
        private bool Snap(P2 p, double radius, out int idx, out double offset)
        {
            int ci = Ix(p.X), cj = Iy(p.Y);
            idx = -1; offset = double.MaxValue;
            int rr = (int)Math.Ceiling(radius / _cell);
            for (int ring = 0; ring <= rr; ring++)
            {
                for (int j = cj - ring; j <= cj + ring; j++)
                    for (int i = ci - ring; i <= ci + ring; i++)
                    {
                        if (Math.Max(Math.Abs(i - ci), Math.Abs(j - cj)) != ring || !W(i, j)) continue;
                        double d = Center(i, j).Dist(p);
                        if (d < offset && d <= radius) { offset = d; idx = j * _nx + i; }
                    }
                if (idx >= 0 && offset <= ring * _cell) break;   // 再外圈不可能更近
            }
            return idx >= 0;
        }

        private static readonly int[][] Moves =
        {
            new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 },
            new[] { 1, 1 }, new[] { 1, -1 }, new[] { -1, 1 }, new[] { -1, -1 },
            new[] { 1, 2 }, new[] { 2, 1 }, new[] { -1, 2 }, new[] { -2, 1 }, new[] { 1, -2 }, new[] { 2, -1 }, new[] { -1, -2 }, new[] { -2, -1 }
        };

        /// <summary>由多個終點出發的距離場（公尺），不可達為 +∞。</summary>
        public float[] DistanceField(IEnumerable<P2> ends, double snapRadius, out int snapped)
        {
            var dist = new float[_walk.Length];
            for (int k = 0; k < dist.Length; k++) dist[k] = float.PositiveInfinity;
            var heap = new MinHeap();
            snapped = 0;
            foreach (P2 e in ends)
            {
                if (!Snap(e, snapRadius, out int idx, out double off)) continue;
                snapped++;
                if (off < dist[idx]) { dist[idx] = (float)off; heap.Push(idx, off); }
            }
            double[] cost = Moves.Select(m => Math.Sqrt(m[0] * m[0] + m[1] * m[1]) * _cell).ToArray();
            while (heap.Count > 0)
            {
                heap.Pop(out int cur, out double d);
                if (d > dist[cur] + 1e-6) continue;
                int ci = cur % _nx, cj = cur / _nx;
                for (int m = 0; m < Moves.Length; m++)
                {
                    int dx = Moves[m][0], dy = Moves[m][1];
                    int ni = ci + dx, nj = cj + dy;
                    if (!W(ni, nj)) continue;
                    // 不切角：斜向需兩側皆可走；馬步需經過的兩格可走
                    if (Math.Abs(dx) == 1 && Math.Abs(dy) == 1 && (!W(ci + dx, cj) || !W(ci, cj + dy))) continue;
                    if (Math.Abs(dx) + Math.Abs(dy) == 3)
                    {
                        int sx = Math.Sign(dx), sy = Math.Sign(dy);
                        if (Math.Abs(dx) == 2 ? (!W(ci + sx, cj) || !W(ci + sx, cj + sy)) : (!W(ci, cj + sy) || !W(ci + sx, cj + sy))) continue;
                    }
                    int ni2 = nj * _nx + ni;
                    double nd = d + cost[m];
                    if (nd < dist[ni2]) { dist[ni2] = (float)nd; heap.Push(ni2, nd); }
                }
            }
            return dist;
        }

        /// <summary>各起點到最近終點的步行距離，取最大值；同時回傳無法到達的起點數。</summary>
        public double? MaxDistance(IList<P2> starts, IList<P2> ends, out int unreachable, out P2 worstPoint)
        {
            unreachable = 0;
            worstPoint = new P2(0, 0);
            if (starts.Count == 0 || ends.Count == 0) return null;
            float[] field = DistanceField(ends, 3.0, out int snappedEnds);
            if (snappedEnds == 0) { unreachable = starts.Count; return null; }
            double worst = -1;
            foreach (P2 s in starts)
            {
                if (!Snap(s, 1.0, out int idx, out double off) || float.IsInfinity(field[idx])) { unreachable++; continue; }
                double d = field[idx] + off;
                if (d > worst) { worst = d; worstPoint = s; }
            }
            return worst >= 0 ? worst : (double?)null;
        }

        private class MinHeap
        {
            private readonly List<int> _i = new List<int>();
            private readonly List<double> _k = new List<double>();
            public int Count => _i.Count;

            public void Push(int idx, double key)
            {
                _i.Add(idx); _k.Add(key);
                int c = _i.Count - 1;
                while (c > 0)
                {
                    int p = (c - 1) / 2;
                    if (_k[p] <= _k[c]) break;
                    Swap(p, c); c = p;
                }
            }

            public void Pop(out int idx, out double key)
            {
                idx = _i[0]; key = _k[0];
                int last = _i.Count - 1;
                _i[0] = _i[last]; _k[0] = _k[last];
                _i.RemoveAt(last); _k.RemoveAt(last);
                int c = 0;
                while (true)
                {
                    int l = 2 * c + 1, r = l + 1, m = c;
                    if (l < _i.Count && _k[l] < _k[m]) m = l;
                    if (r < _i.Count && _k[r] < _k[m]) m = r;
                    if (m == c) break;
                    Swap(m, c); c = m;
                }
            }

            private void Swap(int a, int b)
            {
                int ti = _i[a]; _i[a] = _i[b]; _i[b] = ti;
                double tk = _k[a]; _k[a] = _k[b]; _k[b] = tk;
            }
        }
    }
}
