using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using DevelopmentTools.Core;
using DevelopmentTools.Algorithms;
using DevelopmentTools.Generators;

namespace DevelopmentTools.Test
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=====================================================================");
            Console.WriteLine("      Room Tile Local Coordinate System - 排版與裁切演算法測試");
            Console.WriteLine("=====================================================================");

            try
            {
                // 1. 定義 L 型房間的 3D 邊界頂點 (以毫米 mm 為單位，轉為 Revit 內部的 Feet)
                // L 型房間：
                // (0,0) ----------- (3000, 0)
                //   |                  |
                // (0,1000) -- (1500,1000)
                //               |
                //             (1500,2000) - (3000,2000)
                // 頂點逆時針方向排序以滿足 Sutherland-Hodgman 需求
                List<XYZ> pts = new List<XYZ>
                {
                    new XYZ(0 / 304.8, 0 / 304.8, 0),
                    new XYZ(3000 / 304.8, 0 / 304.8, 0),
                    new XYZ(3000 / 304.8, 2000 / 304.8, 0),
                    new XYZ(1500 / 304.8, 2000 / 304.8, 0),
                    new XYZ(1500 / 304.8, 1000 / 304.8, 0),
                    new XYZ(0 / 304.8, 1000 / 304.8, 0)
                };

                CurveLoop floorLoop = new CurveLoop();
                for (int i = 0; i < pts.Count; i++)
                {
                    XYZ pStart = pts[i];
                    XYZ pEnd = pts[(i + 1) % pts.Count];
                    floorLoop.Append(Line.CreateBound(pStart, pEnd));
                }
                List<CurveLoop> floorLoops = new List<CurveLoop> { floorLoop };

                // 2. 模擬座標控制器 RoomTileCoordinate
                // 假設原點在全域 (X=100mm, Y=100mm)，X軸朝全域 (1,0,0)，Y軸朝全域 (0,1,0)
                XYZ origin = new XYZ(100 / 304.8, 100 / 304.8, 0);
                XYZ basisX = new XYZ(1, 0, 0);
                XYZ basisY = new XYZ(0, 1, 0);
                RoomLocalCoordinate localCoord = new RoomLocalCoordinate(origin, basisX, basisY);

                Console.WriteLine($"[局部座標控制器] 設立成功:");
                Console.WriteLine($"  Origin: X={origin.X * 304.8:F1}mm, Y={origin.Y * 304.8:F1}mm");
                Console.WriteLine($"  U-Axis: ({basisX.X}, {basisX.Y}, {basisX.Z})");
                Console.WriteLine($"  V-Axis: ({basisY.X}, {basisY.Y}, {basisY.Z})");
                Console.WriteLine();

                // 3. 設定排版引數
                double tileWidth = 300.0;   // mm
                double tileHeight = 600.0;  // mm
                double jointWidth = 3.0;    // mm
                double thickness = 10.0;    // mm
                string anchorId = "ANCHOR-TEST-0001";
                string roomId = "WC01";
                string hostId = "FLOOR-HOST-9999";

                Console.WriteLine($"[排版規格]:");
                Console.WriteLine($"  磁磚尺寸: {tileWidth} x {tileHeight} mm");
                Console.WriteLine($"  灰縫寬度: {jointWidth} mm | 厚度: {thickness} mm");
                Console.WriteLine();

                // 4. 執行排版計算
                TileLayoutEngine engine = new TileLayoutEngine();
                TilePatternParams patParams = new TilePatternParams
                {
                    Style = TilePatternStyle.Stack,
                    TileWidth = tileWidth,
                    TileHeight = tileHeight,
                    JointWidth = jointWidth,
                    Thickness = thickness
                };
                SurfaceTileLayoutData floorLayout = engine.LayoutFloor(
                    floorLoops, localCoord, patParams, anchorId, roomId, hostId);

                // 5. 輸出統計資料
                RoomTileLayoutData layoutData = new RoomTileLayoutData
                {
                    Room_ID = roomId,
                    Anchor_ID = anchorId
                };
                layoutData.Surfaces.Add(floorLayout);

                QuantityEngine qEngine = new QuantityEngine();
                RoomTileStatistics stats = qEngine.CalculateStatistics(layoutData);

                Console.WriteLine($"[排版結果統計]:");
                Console.WriteLine($"  總磁磚片數: {stats.TotalTileCount} 片");
                Console.WriteLine($"  整磚 (Full): {stats.FullTileCount} 片");
                Console.WriteLine($"  裁切磚 (Cut): {stats.CutTileCount} 片");
                Console.WriteLine($"  收邊小碎片 (Border): {stats.BorderTileCount} 片");
                Console.WriteLine($"  鋪貼淨面積: {stats.TotalArea:F4} m2");
                Console.WriteLine($"  裁剪廢料面積: {stats.WastedArea:F4} m2");
                Console.WriteLine($"  材料損耗率: {stats.WasteRatio:F2} %");
                Console.WriteLine($"  預估包裝需求數: {stats.BoxCount} 包 (以每包10片計)");
                Console.WriteLine();

                // 6. 印出部分磁磚的詳細幾何邊界，驗證裁剪結果
                Console.WriteLine("[磁磚幾何明細範例 (前 5 片)]:");
                int showCount = 0;
                foreach (var tile in floorLayout.Tiles)
                {
                    if (showCount >= 5) break;
                    Console.WriteLine($"  - Tile ID: {tile.Tile_ID}");
                    Console.WriteLine($"    類型: {tile.Tile_Type} | 狀態: {tile.Cut_Status} | 淨面積: {tile.Area * 1000000:F1} mm2");
                    Console.WriteLine($"    局部 UV 邊界頂點數: {tile.UVBoundary.Count} 個點");
                    for (int k = 0; k < tile.UVBoundary.Count; k++)
                    {
                        var uv = tile.UVBoundary[k];
                        var xyz = tile.XYZBoundary[k];
                        Console.WriteLine($"      點 {k}: UV=({uv.U:F1}, {uv.V:F1}) -> XYZ=({xyz.X * 304.8:F1}, {xyz.Y * 304.8:F1}, {xyz.Z * 304.8:F1}) mm");
                    }
                    Console.WriteLine();
                    showCount++;
                }

                // 7. 模擬報表匯出
                string csvPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WC01_TileQuantity_Report.csv");
                qEngine.ExportToCsv(layoutData, csvPath);
                Console.WriteLine($"[報表導出]: 成功產出 CSV 報表檔：{csvPath}");
                Console.WriteLine("=====================================================================");
                Console.WriteLine("測試執行順利完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"測試過程中發生異常: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
