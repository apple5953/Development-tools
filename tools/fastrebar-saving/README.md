# 快速配筋：減料估算與自動配筋測試

`DevelopmentTools.Addin/Modules/FastRebar/Saving/` 是定尺裁切最佳化、重量統計與「依斷面自動配筋」的純計算部分，不引用 Revit API。
這裡的測試不需要 Revit，也不會編進外掛。

## 執行

Windows（.NET Framework 內建 csc）：

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /out:%TEMP%\fastrebar-saving-tests.exe tools\fastrebar-saving\CuttingOptimizerTests.cs DevelopmentTools.Addin\Modules\FastRebar\Saving\*.cs
%TEMP%\fastrebar-saving-tests.exe
```

Linux／macOS（Mono）：

```sh
mcs -langversion:7.2 -out:/tmp/fastrebar-saving-tests.exe tools/fastrebar-saving/CuttingOptimizerTests.cs DevelopmentTools.Addin/Modules/FastRebar/Saving/*.cs
mono /tmp/fastrebar-saving-tests.exe
```

最後一行印出 `ALL PASSED` 才算通過。

## 計算方式

1. 讀取每根鋼筋的中心線長度（含彎鉤、彎折），依號數分組。
2. 超過最長定尺的鋼筋拆段：前段用滿最長定尺，段間加搭接長度（預設依規範受拉乙級 1.3 ld；也可改成固定倍數 db）。D36 以上不得搭接，改計續接器。
3. 同號數的下料長度由長到短，以 Best-Fit Decreasing 排入定尺；每支定尺最後縮到放得下的最短定尺。
4. 和「每根各取一支最短可用定尺」的單根下料比較，得出可少買的重量。定尺級距不密時，合併反而較長的組合會拆回單根，因此結果不會比單根下料差。
5. 餘料達「可回收餘料下限」（預設 1000 mm）視為回收再用，不計入損耗。

單位重依 CNS 560 標稱值；非標準直徑以 7850 kg/m³ 計算。

## 依斷面自動配筋（RuleDesigner）

沒有配筋表時，依斷面尺寸與規範配筋觀念推估每支梁柱的配筋，**不是結構計算**：

- 梁：主筋量取目標配筋率（預設上層 0.8%、下層 0.5%）與 max(0.8√fc′/fy, 14/fy) 的較大值，下層不少於上層一半；主筋號數取一層（最多兩層）排得下的最小號數。端部 2h 箍距 ≤ d/4、8db、24 倍箍筋直徑、300 mm，中段 ≤ d/2。梁深超過 900 mm 加腰筋。
- 柱：配筋率預設 1.5%（1%～6%），主筋淨距 ≥ 1.5db 且 ≥ 40 mm，每面中心距不超過 200 mm，取支數最少的號數。圍束區長度 ≥ 長邊、淨高/6、450 mm；圍束區箍距 ≤ 短邊/4、6db、100+(350−hx)/3。
- 搭接長度 ld 用簡易法（D19 以下 0.15、D22 以上 0.19 × fy/√fc′ × db，不小於 300 mm），乙級 1.3 ld。

以上數值依舊版規範第五章整理，需以 112 年版第 25 章逐條核對。
