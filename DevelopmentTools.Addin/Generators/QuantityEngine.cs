using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DevelopmentTools.Core;

namespace DevelopmentTools.Generators
{
    public class RoomTileStatistics
    {
        public string Room_ID { get; set; }
        public string Anchor_ID { get; set; }
        public int TotalTileCount { get; set; }
        public int FullTileCount { get; set; }
        public int CutTileCount { get; set; }
        public int BorderTileCount { get; set; }
        public double TotalArea { get; set; }          // 實際安裝的磁磚淨面積 (m2)
        public double WastedArea { get; set; }         // 裁剪掉的廢料面積 (m2)
        public double TotalRequiredArea { get; set; }   // 原始磁磚總需求面積 (m2) (整磚面積和)
        public double WasteRatio { get; set; }         // 損耗率 (%)
        public int BoxCount { get; set; }              // 估計所需的包數
        public double JointArea { get; set; }          // 灰縫總面積估算 (m2)
    }

    public class QuantityEngine
    {
        public RoomTileStatistics CalculateStatistics(RoomTileLayoutData layoutData)
        {
            RoomTileStatistics stats = new RoomTileStatistics
            {
                Room_ID = layoutData.Room_ID,
                Anchor_ID = layoutData.Anchor_ID
            };

            foreach (var surface in layoutData.Surfaces)
            {
                // 單片磚基準面積
                double singleTileArea = (surface.Tile_Width * surface.Tile_Height) / 1000000.0;

                foreach (var tile in surface.Tiles)
                {
                    stats.TotalTileCount++;
                    stats.TotalArea += tile.Area;
                    stats.TotalRequiredArea += singleTileArea;

                    if (tile.Tile_Type == "Full")
                    {
                        stats.FullTileCount++;
                    }
                    else if (tile.Tile_Type == "Cut")
                    {
                        stats.CutTileCount++;
                        // 廢料面積 = 原始面積 - 裁剪後剩餘面積
                        double wasted = singleTileArea - tile.Area;
                        if (wasted > 0) stats.WastedArea += wasted;
                    }
                    else if (tile.Tile_Type == "Border")
                    {
                        stats.BorderTileCount++;
                        double wasted = singleTileArea - tile.Area;
                        if (wasted > 0) stats.WastedArea += wasted;
                    }
                }
            }

            // 損耗率 = (廢料面積 / 總安裝面積) * 100
            stats.WasteRatio = stats.TotalArea > 0 ? (stats.WastedArea / stats.TotalArea) * 100 : 0;
            
            // 假設每包 10 片磁磚
            stats.BoxCount = (int)Math.Ceiling(stats.TotalTileCount / 10.0);

            return stats;
        }

        // 匯出成 CSV
        public void ExportToCsv(RoomTileLayoutData layoutData, string filePath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Tile_ID,Room_ID,Surface_ID,Tile_Type,Width(mm),Height(mm),Thickness(mm),Area(m2),Cut_Status");

            foreach (var surface in layoutData.Surfaces)
            {
                foreach (var tile in surface.Tiles)
                {
                    sb.AppendLine($"{tile.Tile_ID},{tile.Room_ID},{tile.Surface_ID},{tile.Tile_Type},{tile.Width},{tile.Height},{tile.Thickness},{tile.Area:F4},{tile.Cut_Status}");
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        // 匯出成 JSON
        public void ExportToJson(RoomTileLayoutData layoutData, string filePath)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(layoutData, options);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }
    }
}
