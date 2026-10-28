// 快速配筋「減料估算」純計算部分的測試（不需要 Revit）。執行方式見同資料夾 README.md。
using System;
using System.Collections.Generic;
using System.Linq;
using DevelopmentTools.FastRebar.Saving;

internal static class CuttingOptimizerTests
{
    private static int _failed;

    private static int Main()
    {
        Run("短料合併到同一支定尺", ShortPiecesShareStock);
        Run("每支定尺縮到最短可用長度", ShrinksToShortestStock);
        Run("超過最長定尺時加搭接分段", SplitsLongBars);
        Run("分段不超過定尺且長於搭接", SegmentsStayWithinLimits);
        Run("可回收餘料不計損耗", ReusableOffcutNotWaste);
        Run("每刀損耗：剛好用完定尺時省略最後一刀", KerfOnlyBetweenPieces);
        Run("最佳化不會比單根下料多買", NeverWorseThanBaseline);
        Run("定尺級距不密時不合併成更長的定尺", SparseStockFallsBack);
        Run("CNS 560 單位重", Weights);
        Run("設定文字往返", OptionsRoundTrip);
        Run("下料單 CSV 含合計與組合", CsvHasSections);
        Console.WriteLine(_failed == 0 ? "ALL PASSED" : _failed + " FAILED");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS  " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FAIL  " + name + "：" + ex.Message); }
    }

    private static void Eq(double expected, double actual, string what)
    {
        if (Math.Abs(expected - actual) > 1e-3) throw new Exception(what + " 預期 " + expected + "，實際 " + actual);
    }

    private static BarPiece P(string size, double dia, double len, int qty)
    {
        return new BarPiece { BarSize = size, DiameterMm = dia, LengthMm = len, Quantity = qty, Source = "test" };
    }

    private static SavingOptions Opt(params double[] stocks)
    {
        var o = new SavingOptions { MinReusableOffcutMm = 1000 };
        if (stocks.Length > 0) o.StockLengthsMm = stocks.ToList();
        return o;
    }

    private static void ShortPiecesShareStock()
    {
        // 4 根 6m：單根下料要 4 支 10m；最佳化 2 支 12m 剛好用完
        var r = CuttingOptimizer.Optimize(new[] { P("D25", 25.4, 6000, 4) }, Opt(10000, 11000, 12000));
        var s = r.Sizes.Single();
        Eq(2, s.StockBars, "定尺支數");
        Eq(24000, s.PurchasedMm, "採購長度");
        Eq(0, s.WasteMm, "損耗");
        Eq(40000, s.BaselinePurchasedMm, "單根下料採購長度");
        Eq((40000 - 24000) / 1000.0 * 3.98, s.SavingKg, "可少買 kg");
    }

    private static void ShrinksToShortestStock()
    {
        // 9.5m + 0.4m 放進 12m 後只用 9.9m，應改用 10m 定尺
        var r = CuttingOptimizer.Optimize(new[] { P("D16", 15.9, 9500, 1), P("D16", 15.9, 400, 1) }, Opt(10000, 11000, 12000));
        var s = r.Sizes.Single();
        Eq(1, s.StockBars, "定尺支數");
        Eq(10000, s.PurchasedMm, "採購長度");
        Eq(100, s.WasteMm, "損耗");
    }

    private static void SplitsLongBars()
    {
        // D25 搭接 50db = 1270；15m → 12000 + 4270（含一次搭接）
        var segs = CuttingOptimizer.SplitForStock(15000, 1270, 12000);
        Eq(2, segs.Count, "段數");
        Eq(12000, segs[0], "第一段");
        Eq(15000 + 1270 - 12000, segs[1], "第二段");

        var r = CuttingOptimizer.Optimize(new[] { P("D25", 25.4, 15000, 2) }, Opt(10000, 11000, 12000));
        var s = r.Sizes.Single();
        Eq(2, s.SplicedBars, "需搭接根數");
        Eq(4, s.CutPieces, "下料根數");
        Eq(2 * 1270, s.LapMm, "搭接增加長度");
        Eq(2 * 16270, s.NetMm, "淨長");
    }

    private static void SegmentsStayWithinLimits()
    {
        var rnd = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            double stock = rnd.Next(6, 13) * 1000;
            double lap = rnd.Next(200, 2500);
            double len = rnd.Next(100, 60000);
            var segs = CuttingOptimizer.SplitForStock(len, lap, stock);
            Eq(len + (segs.Count - 1) * lap, segs.Sum(), "總長含搭接");
            if (segs.Any(x => x > stock + 1e-6)) throw new Exception("分段超過定尺：" + len);
            if (segs.Count > 1 && segs.Min() <= lap) throw new Exception("分段不長於搭接長度：" + len);
            if (segs.Count > 1 && len <= stock) throw new Exception("不需分段卻分段：" + len);
        }
    }

    private static void ReusableOffcutNotWaste()
    {
        // 單根 8m，只有 12m 定尺：餘 4m ≥ 1m 可回收
        var r = CuttingOptimizer.Optimize(new[] { P("D13", 12.7, 8000, 1) }, Opt(12000));
        var s = r.Sizes.Single();
        Eq(4000, s.ReusableOffcutMm, "可回收餘料");
        Eq(0, s.WasteMm, "損耗");
        if (!s.Patterns.Single().OffcutReusable) throw new Exception("組合未標示回收");
    }

    private static void KerfOnlyBetweenPieces()
    {
        // 6000 + 刀口 5 + 5995 = 12000：最後一段到定尺尾端不必再切，可排進同一支 12m
        var o = Opt(12000);
        o.KerfMm = 5;
        var r = CuttingOptimizer.Optimize(new[] { P("D19", 19.1, 6000, 1), P("D19", 19.1, 5995, 1) }, o);
        Eq(1, r.Sizes.Single().StockBars, "定尺支數");
        // 兩根 6000 之間要切一刀，6000 + 5 + 6000 超過 12m，必須用兩支
        var r1 = CuttingOptimizer.Optimize(new[] { P("D19", 19.1, 6000, 2) }, o);
        Eq(2, r1.Sizes.Single().StockBars, "兩根 6m 定尺支數");
        // 三根 3995：3995+5+3995+5+3995 = 11995 → 餘 5，不可回收，損耗 = 12000 - 11985 = 15
        var r2 = CuttingOptimizer.Optimize(new[] { P("D19", 19.1, 3995, 3) }, o);
        Eq(1, r2.Sizes.Single().StockBars, "三根定尺支數");
        Eq(15, r2.Sizes.Single().WasteMm, "含刀口損耗");
    }

    private static void NeverWorseThanBaseline()
    {
        var rnd = new Random(42);
        for (int round = 0; round < 50; round++)
        {
            var list = new List<BarPiece>();
            int n = rnd.Next(1, 40);
            for (int i = 0; i < n; i++) list.Add(P("D22", 22.2, rnd.Next(300, 20000), rnd.Next(1, 12)));
            var o = round % 2 == 0 ? Opt(6000, 9000, 10000, 11000, 12000) : Opt(5000, 12000);
            o.KerfMm = rnd.Next(0, 2) * 3;
            var s = CuttingOptimizer.Optimize(list, o).Sizes.Single();
            if (s.PurchasedMm > s.BaselinePurchasedMm + 1e-6) throw new Exception("第 " + round + " 組最佳化比單根下料多");
            if (s.PurchasedMm + 1e-6 < s.NetMm) throw new Exception("第 " + round + " 組採購長度小於淨長");
            Eq(s.PurchasedMm, s.NetMm + s.WasteMm + s.ReusableOffcutMm, "第 " + round + " 組長度平衡");
            Eq(s.StockBars, s.Patterns.Sum(p => p.Count), "第 " + round + " 組組合支數");
        }
    }

    private static void SparseStockFallsBack()
    {
        // 定尺只有 5m 與 12m：三根 5m 各取 5m（15m）比 5+5 排進 12m 再加 5m（17m）省
        var s = CuttingOptimizer.Optimize(new[] { P("D16", 15.9, 5000, 3) }, Opt(5000, 12000)).Sizes.Single();
        Eq(15000, s.PurchasedMm, "採購長度");
        Eq(s.BaselinePurchasedMm, s.PurchasedMm, "不比單根下料差");
    }

    private static void Weights()
    {
        Eq(0.994, RebarWeights.KgPerM(12.7), "D13");
        Eq(3.98, RebarWeights.KgPerM(25.0), "D25（25mm 族群）");
        Eq(15.5, RebarWeights.KgPerM(50.2), "D50");
        Eq(20.2, RebarWeights.KgPerM(57.3), "D57");
        Eq(7850 * Math.PI * 20 * 20 / 4 / 1e6, RebarWeights.KgPerM(20), "非標準直徑");
    }

    private static void OptionsRoundTrip()
    {
        var o = new SavingOptions { StockLengthsMm = new List<double> { 12000, 9000 }, LapFactor = 45, KerfMm = 3, MinReusableOffcutMm = 800 };
        var b = SavingOptions.FromText(o.ToText());
        b.Validate();
        if (!b.StockLengthsMm.SequenceEqual(new double[] { 9000, 12000 })) throw new Exception("定尺");
        Eq(45, b.LapFactor, "搭接");
        Eq(3, b.KerfMm, "刀口");
        Eq(800, b.MinReusableOffcutMm, "餘料");
        if (SavingOptions.ParseLengths("10000，11000 12000").Count != 3) throw new Exception("全形逗號");
    }

    private static void CsvHasSections()
    {
        var r = CuttingOptimizer.Optimize(new[] { P("D25", 25.4, 6000, 4), P("D13", 12.7, 2500, 3) }, Opt());
        string csv = CutListCsv.Build(r, "測試專案");
        foreach (string must in new[] { "合計", "裁切組合 (mm)", "6000×2", "D13", "D25" })
        {
            if (csv.IndexOf(must, StringComparison.Ordinal) < 0) throw new Exception("缺少：" + must);
        }
    }
}
