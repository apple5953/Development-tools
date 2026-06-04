namespace DevelopmentTools.Modules.FloorTools.FloorSnapToRoom
{
    /// <summary>
    /// 樓板貼房間功能的設定類別
    /// </summary>
    public class FloorSnapToRoomSettings
    {
        /// <summary>
        /// 最大吸附距離 (mm)
        /// </summary>
        public double MaxSnapDistanceMm { get; set; } = 300;

        /// <summary>
        /// 平行容許角度 (度)
        /// </summary>
        public double ParallelToleranceDegree { get; set; } = 5;

        /// <summary>
        /// 最小重疊長度 (mm)
        /// </summary>
        public double MinOverlapMm { get; set; } = 10;

        /// <summary>
        /// 是否只處理直線 (Line)
        /// </summary>
        public bool ProcessOnlyLines { get; set; } = true;

        /// <summary>
        /// 是否保持圓弧 (Arc) 不變
        /// </summary>
        public bool KeepArcUnchanged { get; set; } = true;

        /// <summary>
        /// 是否使用房間完成面 (Finish Boundary) 作為吸附基準
        /// </summary>
        public bool UseFinishBoundary { get; set; } = true;

        /// <summary>
        /// 找不到房間時是否略過該樓板
        /// </summary>
        public bool SkipIfRoomNotFound { get; set; } = true;

        /// <summary>
        /// 是否啟用詳細總結報告
        /// </summary>
        public bool EnableDetailedReport { get; set; } = true;
    }
}
