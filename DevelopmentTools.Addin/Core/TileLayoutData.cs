using System;
using System.Collections.Generic;

namespace DevelopmentTools.Core
{
    public enum TilePatternStyle
    {
        Stack = 1,                 // 1. 瓷磚堆疊 (對縫)
        RunningBond = 2,           // 2. 瓷磚交錯 (自定義錯縫 %)
        StackSplit = 3,            // 3. 堆疊分割
        StackOffset = 4,           // 4. 堆疊錯位
        HorizontalJoint = 5,       // 5. 水平縫
        VerticalJoint = 6,         // 6. 垂直縫
        DoubleHeightStack = 7,     // 7. 堆疊分割-雙高度
        DoubleWidthStack = 8,      // 8. 堆疊分割-雙寬度
        HexagonSeamless = 9,       // 9. 六邊形-無縫
        HexagonJoint = 10,         // 10. 六邊形-有縫
        FourTriangles = 11,        // 11. 四塊三角組方形
        TwoTriangles = 12,         // 12. 二塊三角組方形
        Herringbone = 14           // 14. 人字拼-2-無縫
    }

    public class TilePatternParams
    {
        public TilePatternStyle Style { get; set; } = TilePatternStyle.Stack;
        public double TileWidth { get; set; } = 300.0;
        public double TileHeight { get; set; } = 300.0;
        public double JointWidth { get; set; } = 3.0;
        public double Thickness { get; set; } = 10.0;
        public double OffsetPercent { get; set; } = 50.0; // 用於 RunningBond / StackOffset (0~100)
        
        // 額外尺寸參數
        public double TileHeight2 { get; set; } = 150.0;
        public double TileWidth2 { get; set; } = 150.0;
    }
    public class UVPoint
    {
        public double U { get; set; }
        public double V { get; set; }

        public UVPoint() { }
        public UVPoint(double u, double v)
        {
            U = u;
            V = v;
        }

        public static UVPoint operator +(UVPoint a, UVPoint b) => new UVPoint(a.U + b.U, a.V + b.V);
        public static UVPoint operator -(UVPoint a, UVPoint b) => new UVPoint(a.U - b.U, a.V - b.V);
        public static UVPoint operator *(UVPoint a, double d) => new UVPoint(a.U * d, a.V * d);
        public static UVPoint operator *(double d, UVPoint a) => new UVPoint(a.U * d, a.V * d);

        public double Length() => Math.Sqrt(U * U + V * V);
        public double DistanceTo(UVPoint other) => Math.Sqrt(Math.Pow(U - other.U, 2) + Math.Pow(V - other.V, 2));
    }

    public class XYZPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public XYZPoint() { }
        public XYZPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public class TileData
    {
        public string Tile_ID { get; set; }        // 格式: {Anchor_ID}_{Surface_ID}_{i}_{j}
        public string Anchor_ID { get; set; }
        public string Room_ID { get; set; }
        public string Surface_ID { get; set; }     // Floor, Wall_0, Wall_1, Wall_2, Wall_3
        public string Tile_Type { get; set; }      // Full, Cut, Border, Special, Void
        public double Width { get; set; }          // 釐米或公釐 (mm)
        public double Height { get; set; }         // 釐米或公釐 (mm)
        public double Thickness { get; set; }      // 釐米或公釐 (mm)
        public double Area { get; set; }           // 面積 (平方米 m2)
        public string Cut_Status { get; set; }     // "Full", "PartiallyCut", "CornerCut"
        
        // 幾何資訊 (使用 Local UV 系統標示，便於排版運算)
        public List<UVPoint> UVBoundary { get; set; } = new List<UVPoint>();
        
        // Revit 3D 空間幾何資訊 (用以生成 DirectShape)
        public List<XYZPoint> XYZBoundary { get; set; } = new List<XYZPoint>();
        public XYZPoint CenterPoint { get; set; }
        public XYZPoint Normal { get; set; }
        public double Rotation { get; set; }       // 相對於局部座標的旋轉角
        public string Material { get; set; }
        public string Host_ID { get; set; }        // Floor 或 Wall 的 Revit UniqueID
    }

    public class SurfaceTileLayoutData
    {
        public string Surface_ID { get; set; }
        public double Tile_Width { get; set; }
        public double Tile_Height { get; set; }
        public double Joint_Width { get; set; }
        public double Rotation_Angle { get; set; }
        public List<TileData> Tiles { get; set; } = new List<TileData>();
    }

    public class RoomTileLayoutData
    {
        public string Room_ID { get; set; }
        public string Anchor_ID { get; set; }

        // 地坪磁磚規格 (mm)
        public double FloorTileWidth { get; set; } = 300.0;
        public double FloorTileHeight { get; set; } = 300.0;
        public double FloorJointWidth { get; set; } = 3.0;
        public double FloorThickness { get; set; } = 10.0;

        // 牆面磁磚規格 (mm)
        public double WallTileWidth { get; set; } = 300.0;
        public double WallTileHeight { get; set; } = 600.0;
        public double WallJointWidth { get; set; } = 3.0;
        public double WallThickness { get; set; } = 10.0;

        public List<SurfaceTileLayoutData> Surfaces { get; set; } = new List<SurfaceTileLayoutData>();
    }

    public class TileOpening
    {
        public double MinU { get; set; }
        public double MaxU { get; set; }
        public double MinV { get; set; }
        public double MaxV { get; set; }
    }
}
