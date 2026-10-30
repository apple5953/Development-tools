// 依斷面尺寸與規範配筋觀念自動決定梁柱配筋（沒有配筋表時使用）。不引用 Revit API。
// 這不是結構計算：沒有載重資訊，主筋量取「目標配筋率」與規範最小量的較大值，
// 箍筋、圍束區、間距等細則依建築物混凝土結構設計規範的耐震規定。單位：mm、kgf/cm²。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DevelopmentTools.FastRebar.Saving
{
    public sealed class BarSize
    {
        public string Name;
        public double DiameterMm;
        public double AreaMm2 { get { return Math.PI * DiameterMm * DiameterMm / 4; } }
        public BarSize(string name, double dia) { Name = name; DiameterMm = dia; }
    }

    public sealed class DesignOptions
    {
        public double FcKgf = 280;          // fc′
        public double FyKgf = 4200;         // 主筋 fy
        public double BeamTopRatio = 0.008; // 梁上層目標配筋率（以 b·d 計）
        public double BeamBottomRatio = 0.005;
        public double ColumnRatio = 0.015;  // 柱總配筋率（以 Ag 計），規範 1%～6%
        public double CoverMm = 40;         // 淨保護層（設計用，建模仍讀構件設定）

        public void Validate()
        {
            if (FcKgf < 140 || FcKgf > 1000) throw new ArgumentException("fc′ 應介於 140～1000 kgf/cm²。");
            if (FyKgf < 2800 || FyKgf > 7000) throw new ArgumentException("fy 應介於 2800～7000 kgf/cm²。");
            if (BeamTopRatio <= 0 || BeamBottomRatio <= 0) throw new ArgumentException("梁配筋率需大於 0。");
            if (ColumnRatio < 0.01 || ColumnRatio > 0.06) throw new ArgumentException("柱配筋率應介於 1%～6%。");
            if (CoverMm <= 0) throw new ArgumentException("保護層需大於 0。");
        }

        public string ToText()
        {
            var c = CultureInfo.InvariantCulture;
            return "fc=" + FcKgf.ToString(c) + "\nfy=" + FyKgf.ToString(c) + "\nbeamTop=" + BeamTopRatio.ToString(c) +
                   "\nbeamBottom=" + BeamBottomRatio.ToString(c) + "\ncolumn=" + ColumnRatio.ToString(c) + "\ncover=" + CoverMm.ToString(c) + "\n";
        }

        public static DesignOptions FromText(string text)
        {
            var o = new DesignOptions();
            if (string.IsNullOrEmpty(text)) return o;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                double d;
                if (eq <= 0 || !double.TryParse(line.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) continue;
                switch (line.Substring(0, eq))
                {
                    case "fc": o.FcKgf = d; break;
                    case "fy": o.FyKgf = d; break;
                    case "beamTop": o.BeamTopRatio = d; break;
                    case "beamBottom": o.BeamBottomRatio = d; break;
                    case "column": o.ColumnRatio = d; break;
                    case "cover": o.CoverMm = d; break;
                }
            }
            return o;
        }
    }

    public sealed class BeamDesign
    {
        public BarSize Main, Stirrup, Side;
        public List<int> TopLayers = new List<int>();
        public List<int> BottomLayers = new List<int>();
        public int SideBarsPerFace;
        public double EndZoneSpacingMm, MidSpacingMm, EndZoneDepthFactor = 2;
        public double LayerClearMm;
        public List<string> Notes = new List<string>();
    }

    public sealed class ColumnDesign
    {
        public BarSize Main, Tie;
        public int BarsLongFace, BarsShortFace;
        public double ConfinedZoneMinMm, ConfinedSpacingMm, MidSpacingMm;
        public bool TieEveryIntermediateBar;
        public List<string> Notes = new List<string>();
        public int TotalBars { get { return 2 * (BarsLongFace + BarsShortFace) - 4; } }
    }

    public static class RebarCode
    {
        /// <summary>受拉竹節鋼筋伸展長度 ld（簡易法，符合保護層與間距條件；未含環氧樹脂、輕質混凝土修正）。</summary>
        public static double DevelopmentLengthMm(double dbMm, double fcKgf, double fyKgf, bool topBar)
        {
            double k = dbMm <= 19.1 + 0.5 ? 0.15 : 0.19;
            double ld = k * fyKgf / Math.Sqrt(fcKgf) * dbMm * (topBar ? 1.3 : 1.0);
            return Math.Max(300, ld);
        }

        /// <summary>受拉乙級搭接 1.3 ld，不小於 300 mm。</summary>
        public static double LapClassBMm(double dbMm, double fcKgf, double fyKgf, bool topBar)
        {
            return Math.Max(300, 1.3 * DevelopmentLengthMm(dbMm, fcKgf, fyKgf, topBar));
        }

        /// <summary>大於 D36 的鋼筋不得搭接。</summary>
        public static bool LapAllowed(double dbMm) { return dbMm <= 35.8 + 0.5; }

        /// <summary>撓曲構材最小鋼筋比：max(0.8√fc′/fy, 14/fy)。</summary>
        public static double MinFlexuralRatio(double fcKgf, double fyKgf)
        {
            return Math.Max(0.8 * Math.Sqrt(fcKgf) / fyKgf, 14.0 / fyKgf);
        }
    }

    public static class RuleDesigner
    {
        private static double Floor(double v, double step) { return Math.Floor(v / step + 1e-9) * step; }

        private static BarSize Pick(IList<BarSize> bars, double targetDia)
        {
            return bars.OrderBy(b => Math.Abs(b.DiameterMm - targetDia)).ThenBy(b => b.DiameterMm).First();
        }

        private static List<BarSize> Sorted(IEnumerable<BarSize> bars)
        {
            var list = (bars ?? Enumerable.Empty<BarSize>()).Where(b => b != null && b.DiameterMm > 0).OrderBy(b => b.DiameterMm).ToList();
            if (list.Count == 0) throw new ArgumentException("模型中沒有鋼筋型號（RebarBarType）。");
            return list;
        }

        /// <summary>梁：寬 b、深 h（mm）。</summary>
        public static BeamDesign Beam(double bMm, double hMm, IEnumerable<BarSize> available, DesignOptions o)
        {
            o.Validate();
            var bars = Sorted(available);
            var r = new BeamDesign();
            r.Stirrup = Pick(bars, hMm >= 600 ? 12.7 : 9.53);
            double rhoMin = RebarCode.MinFlexuralRatio(o.FcKgf, o.FyKgf);

            // 主筋號數：由小到大試，取一層放得下（最多兩層）且支數 ≥ 2 的最小號數
            var candidates = bars.Where(x => x.DiameterMm >= 19 && x.DiameterMm <= 36).ToList();
            if (candidates.Count == 0) candidates = bars;
            BarSize chosen = null;
            int top = 0, bottom = 0, perLayer = 0;
            foreach (int maxLayers in new[] { 1, 2 })
            {
                foreach (BarSize m in candidates)
                {
                    double d = hMm - o.CoverMm - r.Stirrup.DiameterMm - m.DiameterMm / 2;
                    double clear = Math.Max(m.DiameterMm, 25);
                    int fit = (int)Math.Floor((bMm - 2 * o.CoverMm - 2 * r.Stirrup.DiameterMm - m.DiameterMm) / (m.DiameterMm + clear) + 1e-9) + 1;
                    if (fit < 2) continue;
                    int t = Math.Max(2, (int)Math.Ceiling(Math.Max(o.BeamTopRatio, rhoMin) * bMm * d / m.AreaMm2 - 1e-9));
                    int btm = Math.Max(2, (int)Math.Ceiling(Math.Max(o.BeamBottomRatio, rhoMin) * bMm * d / m.AreaMm2 - 1e-9));
                    // 耐震：柱面處正彎矩強度不小於負彎矩強度的 1/2
                    btm = Math.Max(btm, (int)Math.Ceiling(t / 2.0));
                    if (Math.Max(t, btm) <= fit * maxLayers)
                    {
                        chosen = m; top = t; bottom = btm; perLayer = fit;
                        break;
                    }
                }
                if (chosen != null) break;
            }
            if (chosen == null)
            {
                chosen = candidates.Last();
                double d = hMm - o.CoverMm - r.Stirrup.DiameterMm - chosen.DiameterMm / 2;
                perLayer = Math.Max(2, (int)Math.Floor((bMm - 2 * o.CoverMm - 2 * r.Stirrup.DiameterMm - chosen.DiameterMm) / (chosen.DiameterMm + Math.Max(chosen.DiameterMm, 25)) + 1e-9) + 1);
                top = Math.Max(2, (int)Math.Ceiling(Math.Max(o.BeamTopRatio, rhoMin) * bMm * d / chosen.AreaMm2 - 1e-9));
                bottom = Math.Max(Math.Max(2, (int)Math.Ceiling(top / 2.0)), (int)Math.Ceiling(Math.Max(o.BeamBottomRatio, rhoMin) * bMm * d / chosen.AreaMm2 - 1e-9));
                r.Notes.Add("斷面太小，兩層仍放不下目標配筋，已排三層以上，請檢討斷面。");
            }
            r.Main = chosen;
            r.TopLayers = Split(top, perLayer);
            r.BottomLayers = Split(bottom, perLayer);
            r.LayerClearMm = Math.Max(25, chosen.DiameterMm);

            double dEff = hMm - o.CoverMm - r.Stirrup.DiameterMm - chosen.DiameterMm / 2;
            // 塑鉸區（2h）：s ≤ d/4、8 倍最小主筋直徑、24 倍箍筋直徑、300 mm
            double sEnd = Math.Min(Math.Min(dEff / 4, 8 * chosen.DiameterMm), Math.Min(24 * r.Stirrup.DiameterMm, 300));
            r.EndZoneSpacingMm = Math.Max(50, Floor(sEnd, 10));
            // 其餘區段：s ≤ d/2
            r.MidSpacingMm = Math.Max(r.EndZoneSpacingMm, Math.Min(Floor(dEff / 2, 50), 300));

            // 梁深超過 900 mm 加腰筋，間距約 300 mm
            if (hMm > 900)
            {
                r.Side = Pick(bars, 12.7);
                r.SideBarsPerFace = Math.Max(1, (int)Math.Ceiling((hMm - 2 * o.CoverMm) / 300.0) - 1);
            }
            return r;
        }

        private static List<int> Split(int total, int perLayer)
        {
            var layers = new List<int>();
            while (total > 0)
            {
                int n = Math.Min(perLayer, total);
                if (total - n == 1 && n > 2) n--;   // 避免最後一層只剩 1 支
                layers.Add(n);
                total -= n;
            }
            return layers;
        }

        /// <summary>柱：斷面兩邊 a、b 與淨高（mm）。</summary>
        public static ColumnDesign Column(double aMm, double bMm, double clearHeightMm, IEnumerable<BarSize> available, DesignOptions o)
        {
            o.Validate();
            var bars = Sorted(available);
            double longSide = Math.Max(aMm, bMm), shortSide = Math.Min(aMm, bMm);
            var r = new ColumnDesign();
            r.Tie = Pick(bars, 12.7);
            double asReq = Math.Max(0.01, o.ColumnRatio) * aMm * bMm;

            var candidates = bars.Where(x => x.DiameterMm >= 19 && x.DiameterMm <= 36).ToList();
            if (candidates.Count == 0) candidates = bars;
            // 每個號數各排出可行配置，取支數最少者（同支數取小號數，超配較少）
            int bestCount = int.MaxValue;
            foreach (BarSize m in candidates)
            {
                double core = 2 * (o.CoverMm + r.Tie.DiameterMm) + m.DiameterMm;
                double minPitch = m.DiameterMm + Math.Max(1.5 * m.DiameterMm, 40);   // 柱主筋淨距 ≥ 1.5 db 且 ≥ 40 mm
                int maxLong = (int)Math.Floor((longSide - core) / minPitch + 1e-9) + 1;
                int maxShort = (int)Math.Floor((shortSide - core) / minPitch + 1e-9) + 1;
                if (maxLong < 2 || maxShort < 2) continue;
                // 每面主筋中心距不超過 200 mm，讓繫筋支撐間距合理
                int nLong = Math.Max(2, (int)Math.Ceiling((longSide - core) / 200.0 - 1e-9) + 1);
                int nShort = Math.Max(2, (int)Math.Ceiling((shortSide - core) / 200.0 - 1e-9) + 1);
                if (nLong > maxLong || nShort > maxShort) continue;
                while ((2 * (nLong + nShort) - 4) * m.AreaMm2 < asReq)
                {
                    if (nLong < maxLong && (nLong - 1) * shortSide <= (nShort - 1) * longSide) nLong++;
                    else if (nShort < maxShort) nShort++;
                    else if (nLong < maxLong) nLong++;
                    else break;
                }
                int count = 2 * (nLong + nShort) - 4;
                if (count * m.AreaMm2 < asReq || count >= bestCount) continue;
                bestCount = count;
                r.Main = m;
                r.BarsLongFace = nLong;
                r.BarsShortFace = nShort;
            }
            if (r.Main == null)
            {
                r.Main = candidates.Last();
                r.BarsLongFace = Math.Max(2, (int)Math.Floor((longSide - 120) / 150.0) + 1);
                r.BarsShortFace = Math.Max(2, (int)Math.Floor((shortSide - 120) / 150.0) + 1);
                r.Notes.Add("斷面放不下目標配筋率，已用最大號數排滿，請檢討斷面。");
            }
            double rho = r.TotalBars * r.Main.AreaMm2 / (aMm * bMm);
            if (rho > 0.06) r.Notes.Add("配筋率 " + rho.ToString("P1") + " 超過 6%。");

            // 圍束區長度 lo ≥ 斷面長邊、淨高/6、450 mm
            r.ConfinedZoneMinMm = Math.Max(450, Math.Max(longSide, clearHeightMm / 6));
            // 圍束區箍距 so ≤ 短邊/4、6 db、100 + (350 − hx)/3（100～150 mm）
            double barPitch = (longSide - 2 * (o.CoverMm + r.Tie.DiameterMm) - r.Main.DiameterMm) / Math.Max(1, r.BarsLongFace - 1);
            r.TieEveryIntermediateBar = barPitch > 175;   // 隔支支撐時 hx 會超過 350 mm
            double hx = r.TieEveryIntermediateBar ? barPitch : 2 * barPitch;
            double sx = Math.Min(150, Math.Max(100, 100 + (350 - hx) / 3));
            r.ConfinedSpacingMm = Math.Max(50, Floor(Math.Min(Math.Min(shortSide / 4, 6 * r.Main.DiameterMm), sx), 10));
            // 圍束區以外：s ≤ 6 db 與 150 mm
            r.MidSpacingMm = Math.Max(r.ConfinedSpacingMm, Floor(Math.Min(6 * r.Main.DiameterMm, 150), 10));
            return r;
        }
    }
}
