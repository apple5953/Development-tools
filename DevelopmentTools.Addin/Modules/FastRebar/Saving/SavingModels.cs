// 鋼筋減料的資料模型。本資料夾（FastRebar/Saving）不引用 Revit API，可在 Revit 外單獨測試。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DevelopmentTools.FastRebar.Saving
{
    /// <summary>模型中一組同號數、同長度的鋼筋（長度含彎鉤與彎折，mm）。</summary>
    public sealed class BarPiece
    {
        public string BarSize;      // 例如 D25、#8；用來分組
        public double DiameterMm;   // 標稱直徑，用來算單位重與搭接長度
        public double LengthMm;     // 單根長度
        public int Quantity;        // 根數
        public string Source;       // 主體／編號說明，寫進下料單
        public List<int> ElementIds = new List<int>();
    }

    public sealed class SavingOptions
    {
        /// <summary>可採購的定尺長度（mm）。</summary>
        public List<double> StockLengthsMm = new List<double> { 10000, 11000, 12000 };
        /// <summary>超過最長定尺時，搭接長度 = 倍數 × 標稱直徑。僅為估算，應依結構圖搭接表調整。</summary>
        public double LapFactor = 50;
        /// <summary>每刀損耗（mm）；剪斷機一般為 0。</summary>
        public double KerfMm = 0;
        /// <summary>餘料長度達此值視為可回收再利用，不計入損耗（mm）。</summary>
        public double MinReusableOffcutMm = 1000;

        public double MaxStock { get { return StockLengthsMm.Max(); } }

        public void Validate()
        {
            StockLengthsMm = StockLengthsMm.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
            if (StockLengthsMm.Count == 0) throw new ArgumentException("至少需要一種定尺長度。");
            if (LapFactor < 0) throw new ArgumentException("搭接倍數不可為負。");
            if (KerfMm < 0) throw new ArgumentException("每刀損耗不可為負。");
            if (MinReusableOffcutMm < 0) throw new ArgumentException("可回收餘料下限不可為負。");
        }

        public string ToText()
        {
            var c = CultureInfo.InvariantCulture;
            return "stock=" + string.Join(",", StockLengthsMm.Select(x => x.ToString(c))) + "\n" +
                   "lap=" + LapFactor.ToString(c) + "\n" +
                   "kerf=" + KerfMm.ToString(c) + "\n" +
                   "offcut=" + MinReusableOffcutMm.ToString(c) + "\n";
        }

        public static SavingOptions FromText(string text)
        {
            var o = new SavingOptions();
            if (string.IsNullOrEmpty(text)) return o;
            var c = CultureInfo.InvariantCulture;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                double d;
                switch (key)
                {
                    case "stock":
                        var list = ParseLengths(value);
                        if (list.Count > 0) o.StockLengthsMm = list;
                        break;
                    case "lap": if (double.TryParse(value, NumberStyles.Float, c, out d)) o.LapFactor = d; break;
                    case "kerf": if (double.TryParse(value, NumberStyles.Float, c, out d)) o.KerfMm = d; break;
                    case "offcut": if (double.TryParse(value, NumberStyles.Float, c, out d)) o.MinReusableOffcutMm = d; break;
                }
            }
            return o;
        }

        /// <summary>解析「10000, 11000 12000」這類以逗號或空白分隔的長度。</summary>
        public static List<double> ParseLengths(string text)
        {
            var result = new List<double>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (string part in text.Split(new[] { ',', ' ', ';', '、', '，' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double d;
                if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) result.Add(d);
            }
            return result;
        }
    }

    /// <summary>一根定尺的裁切組合；Count 為相同組合的根數。</summary>
    public sealed class CutPattern
    {
        public string BarSize;
        public double StockMm;
        public List<double> PiecesMm = new List<double>();
        public double OffcutMm;
        public bool OffcutReusable;
        public int Count;

        public string PiecesText()
        {
            return string.Join(" + ", PiecesMm.GroupBy(x => x).OrderByDescending(g => g.Key)
                .Select(g => g.Count() > 1 ? g.Key.ToString("0") + "×" + g.Count() : g.Key.ToString("0")));
        }
    }

    public sealed class SizeResult
    {
        public string BarSize;
        public double DiameterMm;
        public double UnitKgPerM;
        public int ModelBars;           // 模型根數
        public int CutPieces;           // 含搭接分段後的下料根數
        public int SplicedBars;         // 超過最長定尺、需要搭接的根數
        public double NetMm;            // 下料淨長（含搭接）
        public double LapMm;            // 因搭接增加的長度
        public Dictionary<double, int> StockCount = new Dictionary<double, int>();
        public double PurchasedMm;      // 最佳化後採購長度
        public double ReusableOffcutMm; // 可回收餘料
        public double WasteMm;          // 損耗（含鋸切）
        public double BaselinePurchasedMm; // 單根下料（每根各取一支定尺）的採購長度
        public List<CutPattern> Patterns = new List<CutPattern>();

        public int StockBars { get { return StockCount.Values.Sum(); } }
        public double WasteRate { get { return PurchasedMm > 0 ? WasteMm / PurchasedMm : 0; } }
        public double BaselineWasteRate { get { return BaselinePurchasedMm > 0 ? (BaselinePurchasedMm - NetMm) / BaselinePurchasedMm : 0; } }
        public double PurchasedKg { get { return PurchasedMm / 1000 * UnitKgPerM; } }
        public double BaselineKg { get { return BaselinePurchasedMm / 1000 * UnitKgPerM; } }
        public double NetKg { get { return NetMm / 1000 * UnitKgPerM; } }
        /// <summary>和單根下料相比可少買的重量（kg）。</summary>
        public double SavingKg { get { return BaselineKg - PurchasedKg; } }
    }

    public sealed class SavingResult
    {
        public SavingOptions Options;
        public List<SizeResult> Sizes = new List<SizeResult>();
        public List<string> Warnings = new List<string>();

        public double PurchasedKg { get { return Sizes.Sum(s => s.PurchasedKg); } }
        public double BaselineKg { get { return Sizes.Sum(s => s.BaselineKg); } }
        public double NetKg { get { return Sizes.Sum(s => s.NetKg); } }
        public double SavingKg { get { return Sizes.Sum(s => s.SavingKg); } }
        public double WasteKg { get { return Sizes.Sum(s => s.WasteMm / 1000 * s.UnitKgPerM); } }
        public double PurchasedTotalMm { get { return Sizes.Sum(s => s.PurchasedMm); } }
    }
}
