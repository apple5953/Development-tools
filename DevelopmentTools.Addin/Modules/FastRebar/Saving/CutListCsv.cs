// 下料單 CSV（UTF-8 含 BOM，Excel 可直接開啟中文）。
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DevelopmentTools.FastRebar.Saving
{
    public static class CutListCsv
    {
        public static string Build(SavingResult r, string projectName)
        {
            var sb = new StringBuilder();
            var o = r.Options;
            Row(sb, "鋼筋減料分析", projectName ?? "");
            Row(sb, "定尺 (mm)", string.Join(" / ", o.StockLengthsMm.Select(x => N0(x))));
            Row(sb, "搭接長度", o.LapText() + "；D36 以上不得搭接，改計續接器。請依結構圖搭接表確認");
            Row(sb, "每刀損耗 (mm)", N0(o.KerfMm));
            Row(sb, "可回收餘料下限 (mm)", N0(o.MinReusableOffcutMm));
            sb.AppendLine();

            Row(sb, "號數", "模型根數", "下料根數", "需搭接根數", "淨用量 (kg)", "最佳化採購 (kg)", "單根下料採購 (kg)",
                "可少買 (kg)", "最佳化損耗率", "單根下料損耗率", "可回收餘料 (m)", "定尺支數", "續接器 (個)");
            foreach (SizeResult s in r.Sizes)
            {
                Row(sb, s.BarSize, s.ModelBars.ToString(), s.CutPieces.ToString(), s.SplicedBars.ToString(),
                    N1(s.NetKg), N1(s.PurchasedKg), N1(s.BaselineKg), N1(s.SavingKg),
                    Pct(s.WasteRate), Pct(s.BaselineWasteRate), N1(s.ReusableOffcutMm / 1000),
                    string.Join(" / ", s.StockCount.OrderBy(k => k.Key).Select(k => N0(k.Key) + "×" + k.Value)), s.Couplers.ToString());
            }
            Row(sb, "合計", r.Sizes.Sum(s => s.ModelBars).ToString(), r.Sizes.Sum(s => s.CutPieces).ToString(),
                r.Sizes.Sum(s => s.SplicedBars).ToString(), N1(r.NetKg), N1(r.PurchasedKg), N1(r.BaselineKg), N1(r.SavingKg));
            sb.AppendLine();

            Row(sb, "號數", "定尺 (mm)", "支數", "裁切組合 (mm)", "餘料 (mm)", "餘料處理");
            foreach (SizeResult s in r.Sizes)
            {
                foreach (CutPattern p in s.Patterns)
                {
                    Row(sb, s.BarSize, N0(p.StockMm), p.Count.ToString(), p.PiecesText(), N0(p.OffcutMm),
                        p.OffcutMm <= 0 ? "" : (p.OffcutReusable ? "回收再用" : "損耗"));
                }
            }

            if (r.Warnings.Count > 0)
            {
                sb.AppendLine();
                Row(sb, "注意事項");
                foreach (string w in r.Warnings) Row(sb, w);
            }
            return sb.ToString();
        }

        private static string N0(double v) { return v.ToString("0", CultureInfo.InvariantCulture); }
        private static string N1(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }
        private static string Pct(double v) { return (v * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%"; }

        private static void Row(StringBuilder sb, params string[] cells)
        {
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        private static string Escape(string v)
        {
            if (v == null) return "";
            if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }
}
