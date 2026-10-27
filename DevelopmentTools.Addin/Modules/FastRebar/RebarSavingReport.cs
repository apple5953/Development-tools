using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DevelopmentTools.FastRebar.Saving;

namespace DevelopmentTools.FastRebar
{
    /// <summary>
    /// 減料估算：讀取鋼筋實際下料長度（含彎鉤、彎折），交給 Saving.CuttingOptimizer 排定尺。
    /// 只讀模型，不開 Transaction。
    /// </summary>
    public static class RebarSavingReport
    {
        private const double Mm = 1.0 / 304.8;

        public static SavingResult Analyze(Document doc, IEnumerable<Rebar> rebars, SavingOptions options)
        {
            var warnings = new List<string>();
            List<BarPiece> pieces = Collect(doc, rebars, warnings);
            SavingResult result = CuttingOptimizer.Optimize(pieces, options);
            result.Warnings.InsertRange(0, warnings);
            return result;
        }

        public static List<BarPiece> Collect(Document doc, IEnumerable<Rebar> rebars, List<string> warnings)
        {
            var byKey = new Dictionary<string, BarPiece>();
            int failed = 0;
            foreach (Rebar r in rebars.Where(x => x != null && x.IsValidObject))
            {
                RebarBarType type = doc.GetElement(r.GetTypeId()) as RebarBarType;
                string size = type?.Name ?? "未知號數";
                double dia = (type?.BarNominalDiameter ?? 0) / Mm;
                string source = HostLabel(doc, r);

                int n = r.NumberOfBarPositions;
                bool uniform = r.DistributionType == DistributionType.Uniform;
                double uniformLength = 0;
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        if (!r.DoesBarExistAtPosition(i)) continue;
                        double len = uniform && uniformLength > 0 ? uniformLength : BarLengthMm(r, i);
                        if (len <= 0) continue;
                        if (uniform) uniformLength = len;

                        string key = size + "|" + Math.Round(len).ToString("0") + "|" + source;
                        BarPiece p;
                        if (!byKey.TryGetValue(key, out p))
                        {
                            p = new BarPiece { BarSize = size, DiameterMm = dia, LengthMm = Math.Round(len), Source = source };
                            byKey[key] = p;
                        }
                        p.Quantity++;
                        if (!p.ElementIds.Contains(r.Id.IntegerValue)) p.ElementIds.Add(r.Id.IntegerValue);
                    }
                    catch { failed++; }
                }
            }
            if (failed > 0) warnings.Add($"有 {failed} 根鋼筋無法讀取長度，未計入減料估算。");
            return byKey.Values.ToList();
        }

        /// <summary>單根下料長度：中心線含彎鉤與彎折半徑。</summary>
        private static double BarLengthMm(Rebar r, int position)
        {
            IList<Curve> cs = r.GetTransformedCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, position);
            if (cs == null) return 0;
            return cs.Sum(c => c.Length) / Mm;
        }

        private static string HostLabel(Document doc, Rebar r)
        {
            try
            {
                Element host = doc.GetElement(r.GetHostId());
                if (host != null) return $"{host.Category?.Name} {host.Name}（{host.Id.IntegerValue}）";
            }
            catch { }
            return $"鋼筋 {r.Id.IntegerValue}";
        }

        public static string Summary(SavingResult r, int maxLines)
        {
            var sb = new StringBuilder();
            if (r.Sizes.Count == 0)
            {
                sb.AppendLine("沒有可計算的鋼筋。");
                return sb.ToString();
            }
            var o = r.Options;
            sb.AppendLine($"定尺 {string.Join("/", o.StockLengthsMm.Select(x => (x / 1000).ToString("0.#")))} m，" +
                          $"淨用量 {r.NetKg / 1000:0.00} t");
            double baseRate = r.BaselineKg > 0 ? (r.BaselineKg - r.NetKg) / r.BaselineKg : 0;
            double optWaste = r.PurchasedKg > 0 ? r.WasteKg / r.PurchasedKg : 0;
            sb.AppendLine($"單根下料採購 {r.BaselineKg / 1000:0.00} t（損耗 {baseRate:P1}）→ 最佳化 {r.PurchasedKg / 1000:0.00} t（損耗 {optWaste:P1}）");
            sb.AppendLine($"可少買約 {r.SavingKg:0} kg");
            foreach (SizeResult s in r.Sizes.OrderByDescending(x => x.SavingKg).Take(maxLines))
            {
                string lap = s.SplicedBars > 0 ? $"，需搭接 {s.SplicedBars} 根" : "";
                sb.AppendLine($"・{s.BarSize}：{s.CutPieces} 段 → 定尺 {s.StockBars} 支，損耗 {s.WasteRate:P1}，少買 {s.SavingKg:0} kg{lap}");
            }
            if (r.Sizes.Count > maxLines) sb.AppendLine($"…另有 {r.Sizes.Count - maxLines} 種號數，見下料單。");
            if (r.Sizes.Any(s => s.SplicedBars > 0)) sb.AppendLine($"搭接長度以 {o.LapFactor:0} db 估算，請依結構圖搭接表確認。");
            foreach (string w in r.Warnings.Take(3)) sb.AppendLine("・" + w);
            return sb.ToString();
        }

        public static string WriteCsv(SavingResult r, string projectName, string prefix)
        {
            try
            {
                string dir = Path.GetDirectoryName(FastRebarPresetStore.FilePath);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(path, CutListCsv.Build(r, projectName), new UTF8Encoding(true));
                return path;
            }
            catch
            {
                return null;
            }
        }

        public static void Open(string path)
        {
            try { if (path != null) Process.Start(path); } catch { }
        }
    }

    /// <summary>減料參數（定尺、搭接、刀口、餘料），與樣板存在同一資料夾。</summary>
    public static class RebarSavingOptionsStore
    {
        public static string FilePath => Path.Combine(Path.GetDirectoryName(FastRebarPresetStore.FilePath), "saving-options.txt");

        public static SavingOptions Load()
        {
            try
            {
                if (File.Exists(FilePath)) return SavingOptions.FromText(File.ReadAllText(FilePath, Encoding.UTF8));
            }
            catch { }
            return new SavingOptions();
        }

        public static void Save(SavingOptions o)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, o.ToText(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
