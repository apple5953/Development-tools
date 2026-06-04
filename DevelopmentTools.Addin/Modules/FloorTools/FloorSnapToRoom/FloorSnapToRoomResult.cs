using System.Collections.Generic;

namespace DevelopmentTools.Modules.FloorTools.FloorSnapToRoom
{
    /// <summary>
    /// 樓板貼房間執行的結果統計與報告類別
    /// </summary>
    public class FloorSnapToRoomResult
    {
        /// <summary>
        /// 處理的樓板總數
        /// </summary>
        public int TotalFloors { get; set; }

        /// <summary>
        /// 成功對齊的樓板數
        /// </summary>
        public int SuccessFloors { get; set; }

        /// <summary>
        /// 失敗的樓板數
        /// </summary>
        public int FailedFloors { get; set; }

        /// <summary>
        /// 略過不處理的樓板數
        /// </summary>
        public int SkippedFloors { get; set; }

        /// <summary>
        /// 更新的線段總數
        /// </summary>
        public int UpdatedLines { get; set; }

        /// <summary>
        /// 忽略未變更的非直線幾何數量 (如圓弧或樣條線)
        /// </summary>
        public int IgnoredCurves { get; set; }

        /// <summary>
        /// 錯誤清單
        /// </summary>
        public List<FloorSnapToRoomError> Errors { get; set; } = new List<FloorSnapToRoomError>();

        /// <summary>
        /// 警告與提示訊息清單
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
