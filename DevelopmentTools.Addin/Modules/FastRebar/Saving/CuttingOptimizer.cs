// 定尺裁切最佳化：同號數的下料長度以 Best-Fit Decreasing 排入定尺，
// 每根定尺最後縮到放得下的最短定尺，並和「每根各取一支定尺」的單根下料比較。
using System;
using System.Collections.Generic;
using System.Linq;

namespace DevelopmentTools.FastRebar.Saving
{
    public static class CuttingOptimizer
    {
        private const double Eps = 1e-6;

        private sealed class Bin
        {
            public double Used;   // 已用長度（含每刀損耗）
            public List<double> Pieces = new List<double>();
        }

        public static SavingResult Optimize(IEnumerable<BarPiece> pieces, SavingOptions options)
        {
            if (options == null) options = new SavingOptions();
            options.Validate();
            var result = new SavingResult { Options = options };

            var groups = (pieces ?? Enumerable.Empty<BarPiece>())
                .Where(p => p != null && p.Quantity > 0 && p.LengthMm > 0)
                .GroupBy(p => string.IsNullOrEmpty(p.BarSize) ? "未知號數" : p.BarSize)
                .OrderBy(g => g.Max(p => p.DiameterMm));

            foreach (var g in groups)
            {
                result.Sizes.Add(OptimizeSize(g.Key, g.ToList(), options, result.Warnings));
            }
            return result;
        }

        /// <summary>
        /// 超過最長定尺的長度拆成數段，段與段之間加搭接長度。
        /// 前面各段用滿最長定尺（零損耗），最後一段取餘數，交給裁切最佳化和其他短料合併。
        /// </summary>
        public static List<double> SplitForStock(double lengthMm, double lapMm, double maxStockMm)
        {
            var segs = new List<double>();
            if (lengthMm <= maxStockMm + Eps)
            {
                segs.Add(lengthMm);
                return segs;
            }
            if (lapMm >= maxStockMm - Eps) throw new ArgumentException("搭接長度不小於最長定尺，無法分段。");

            int n = (int)Math.Ceiling((lengthMm - lapMm) / (maxStockMm - lapMm) - Eps);
            double total = lengthMm + (n - 1) * lapMm;
            for (int i = 0; i < n - 1; i++) segs.Add(maxStockMm);
            // n 取最小段數，因此最後一段必定介於搭接長度與最長定尺之間
            segs.Add(total - (n - 1) * maxStockMm);
            return segs;
        }

        private static SizeResult OptimizeSize(string size, List<BarPiece> group, SavingOptions o, List<string> warnings)
        {
            double dia = group.Max(p => p.DiameterMm);
            var sr = new SizeResult { BarSize = size, DiameterMm = dia, UnitKgPerM = RebarWeights.KgPerM(dia) };
            double lap = o.LapFactor * dia;
            double maxStock = o.MaxStock;

            // 展開成每一根下料長度；同長度合併計數，避免大量鋼筋時排序過慢
            var cutCounts = new Dictionary<double, int>();
            foreach (BarPiece p in group)
            {
                sr.ModelBars += p.Quantity;
                List<double> segs;
                try { segs = SplitForStock(p.LengthMm, lap, maxStock); }
                catch (ArgumentException ex)
                {
                    warnings.Add(size + "：" + ex.Message + "（" + p.Source + "）");
                    continue;
                }
                if (segs.Count > 1)
                {
                    sr.SplicedBars += p.Quantity;
                    sr.LapMm += (segs.Count - 1) * lap * p.Quantity;
                }
                foreach (double s in segs)
                {
                    double key = Math.Round(s, 1);
                    int c;
                    cutCounts.TryGetValue(key, out c);
                    cutCounts[key] = c + p.Quantity;
                }
            }

            var bins = new List<Bin>();          // 依剩餘長度遞增排序，方便二分搜尋 best fit
            foreach (var kv in cutCounts.OrderByDescending(k => k.Key))
            {
                double len = kv.Key;
                double need = len + o.KerfMm;
                sr.CutPieces += kv.Value;
                sr.NetMm += len * kv.Value;
                sr.BaselinePurchasedMm += SmallestStock(o.StockLengthsMm, len) * kv.Value;

                for (int i = 0; i < kv.Value; i++)
                {
                    int idx = FindBestFit(bins, len, maxStock);
                    Bin b;
                    if (idx < 0) b = new Bin();
                    else { b = bins[idx]; bins.RemoveAt(idx); }
                    b.Used += need;
                    b.Pieces.Add(len);
                    InsertSorted(bins, b, maxStock);
                }
            }

            // 定尺級距不密時（例如只有 5m 與 12m），合併後的定尺可能比各自取料還長；這種組合拆回單根
            var finalBins = new List<Bin>();
            foreach (Bin b in bins)
            {
                double combined = SmallestStock(o.StockLengthsMm, b.Used - o.KerfMm);
                double separate = b.Pieces.Sum(x => SmallestStock(o.StockLengthsMm, x));
                if (b.Pieces.Count > 1 && separate < combined - Eps)
                {
                    foreach (double x in b.Pieces) finalBins.Add(new Bin { Used = x + o.KerfMm, Pieces = new List<double> { x } });
                }
                else finalBins.Add(b);
            }

            var patterns = new Dictionary<string, CutPattern>();
            foreach (Bin b in finalBins)
            {
                // 最後一刀若剛好用完定尺，不需要那一刀的損耗
                double net = b.Pieces.Sum();
                double stock = SmallestStock(o.StockLengthsMm, b.Used - o.KerfMm);
                double offcut = Math.Max(0, stock - b.Used);
                if (offcut < Eps) offcut = 0;
                double sawLoss = Math.Max(0, stock - net - offcut);
                bool reusable = offcut >= o.MinReusableOffcutMm && offcut > 0;

                int sc;
                sr.StockCount.TryGetValue(stock, out sc);
                sr.StockCount[stock] = sc + 1;
                sr.PurchasedMm += stock;
                if (reusable) sr.ReusableOffcutMm += offcut;
                sr.WasteMm += sawLoss + (reusable ? 0 : offcut);

                var sorted = b.Pieces.OrderByDescending(x => x).ToList();
                string key = stock.ToString("0") + "|" + string.Join(",", sorted.Select(x => x.ToString("0.0")));
                CutPattern pat;
                if (!patterns.TryGetValue(key, out pat))
                {
                    pat = new CutPattern { BarSize = size, StockMm = stock, PiecesMm = sorted, OffcutMm = offcut, OffcutReusable = reusable };
                    patterns[key] = pat;
                }
                pat.Count++;
            }
            sr.Patterns = patterns.Values.OrderByDescending(p => p.Count).ThenByDescending(p => p.StockMm).ToList();
            return sr;
        }

        private static double SmallestStock(List<double> stocks, double length)
        {
            foreach (double s in stocks)
            {
                if (s + Eps >= length) return s;
            }
            return stocks[stocks.Count - 1];
        }

        private static double Remaining(Bin b, double maxStock) { return maxStock - b.Used; }

        /// <summary>
        /// 剩餘長度放得下且最少的那一根；找不到回傳 -1。
        /// 只比對淨長：最後一段剛好用到定尺尾端時，不需要再切那一刀。
        /// </summary>
        private static int FindBestFit(List<Bin> bins, double length, double maxStock)
        {
            int lo = 0, hi = bins.Count;
            double target = length - Eps;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Remaining(bins[mid], maxStock) >= target) hi = mid; else lo = mid + 1;
            }
            return lo < bins.Count ? lo : -1;
        }

        private static void InsertSorted(List<Bin> bins, Bin b, double maxStock)
        {
            double r = Remaining(b, maxStock);
            int lo = 0, hi = bins.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Remaining(bins[mid], maxStock) < r) lo = mid + 1; else hi = mid;
            }
            bins.Insert(lo, b);
        }
    }
}
