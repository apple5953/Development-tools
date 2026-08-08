using System;

namespace DevelopmentTools.Core
{
    /// <summary>
    /// 儲存人工校正（兩點校正）後的磁磚排版基準資料模型
    /// </summary>
    public sealed class TilePatternAlignment
    {
        public int SchemaVersion { get; set; } = 1;

        // 磁磚排版唯一識別碼
        public string TilePlanId { get; set; }

        // 宿主 (Floor / Wall) 的 UniqueID
        public string HostUniqueId { get; set; }

        // 點選面的 StableReference 字串
        public string HostFaceStableReference { get; set; }

        // 基準原點投影後的世界座標 (X, Y, Z)
        public double OriginX { get; set; }
        public double OriginY { get; set; }
        public double OriginZ { get; set; }

        // 主要橫向 U 向量 (世界座標方向)
        public double DirUX { get; set; }
        public double DirUY { get; set; }
        public double DirUZ { get; set; }

        // 主要縱向 V 向量 (世界座標方向)
        public double DirVX { get; set; }
        public double DirVY { get; set; }
        public double DirVZ { get; set; }

        // 面法向量
        public double NormalX { get; set; }
        public double NormalY { get; set; }
        public double NormalZ { get; set; }

        // 使用者手動微調偏移值 (mm)
        public double OffsetU { get; set; }
        public double OffsetV { get; set; }

        // 使用者手動微調旋轉值 (Degrees)
        public double RotationAngleDegrees { get; set; }

        // 磁磚與縫隙參數 (保存當次校正時的規格)
        public double TileWidthMm { get; set; }
        public double TileHeightMm { get; set; }
        public double GroutWidthMm { get; set; }
        public double ThicknessMm { get; set; }

        // 關聯 of Anchor (RoomTileCoordinate) UniqueID
        public string AnchorUniqueId { get; set; }

        public string AlignmentStatus { get; set; } // "Calibrated", "Dirty", "NeedsRecalibration"
        
        public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
