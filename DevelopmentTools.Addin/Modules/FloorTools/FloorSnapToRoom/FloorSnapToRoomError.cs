using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.FloorTools.FloorSnapToRoom
{
    /// <summary>
    /// 樓板貼房間執行過程中的錯誤記錄類別
    /// </summary>
    public class FloorSnapToRoomError
    {
        /// <summary>
        /// 樓板的 ElementId
        /// </summary>
        public ElementId FloorId { get; set; }

        /// <summary>
        /// 房間的 ElementId (若有)
        /// </summary>
        public ElementId RoomId { get; set; }

        /// <summary>
        /// 房間名稱 (若有)
        /// </summary>
        public string RoomName { get; set; }

        /// <summary>
        /// 錯誤代碼 (例如 ROOM_NOT_FOUND, GEOMETRY_FAILED 等)
        /// </summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// 錯誤詳細訊息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 建議排查方案
        /// </summary>
        public string Suggestion { get; set; }
    }
}
