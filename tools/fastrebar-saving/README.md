# 快速配筋：減料估算測試

`DevelopmentTools.Addin/Modules/FastRebar/Saving/` 是定尺裁切最佳化的純計算部分，不引用 Revit API。
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
2. 超過最長定尺的鋼筋拆段：前段用滿最長定尺，段間加搭接長度（預設 50 db，僅為估算）。
3. 同號數的下料長度由長到短，以 Best-Fit Decreasing 排入定尺；每支定尺最後縮到放得下的最短定尺。
4. 和「每根各取一支最短可用定尺」的單根下料比較，得出可少買的重量。定尺級距不密時，合併反而較長的組合會拆回單根，因此結果不會比單根下料差。
5. 餘料達「可回收餘料下限」（預設 1000 mm）視為回收再用，不計入損耗。

單位重依 CNS 560 標稱值；非標準直徑以 7850 kg/m³ 計算。
