using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.FloorTools.FloorSnapToRoom
{
    /// <summary>
    /// 表示一條樓板草圖線段與其最佳配對的房間邊界線段的對齊關係
    /// </summary>
    public class FloorEdgeMatch
    {
        /// <summary>
        /// 樓板草圖中對應的 ModelCurve 元素 (Revit 元素)
        /// </summary>
        public ModelCurve ModelCurve { get; set; }

        /// <summary>
        /// 投影至平面且相對於原點的原始 Curve 幾何物件
        /// </summary>
        public Curve OriginalCurve { get; set; }

        /// <summary>
        /// 配對到的房間幾何邊界段 (已投射至平面)
        /// </summary>
        public Curve TargetRoomCurve { get; set; }

        /// <summary>
        /// 兩者之間的平移距離 (英制呎)
        /// </summary>
        public double Distance { get; set; }

        /// <summary>
        /// 是否是有效的配對 (符合平行、距離、重疊度篩選條件)
        /// </summary>
        public bool IsValidMatch { get; set; }

        /// <summary>
        /// 吸附並投影平移後產生的新幾何 (端點尚未重新計算交會點)
        /// </summary>
        public Curve SnappedCurve { get; set; }
    }
}
