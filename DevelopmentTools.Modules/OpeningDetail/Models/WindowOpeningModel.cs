using Autodesk.Revit.DB;

namespace OpeningDetailAddin.Models
{
    /// <summary>
    /// 窗開口資料模型
    /// </summary>
    public class WindowOpeningModel
    {
        public FamilyInstance WindowInstance { get; set; }
        public Wall HostWall { get; set; }
        public Level Level { get; set; }
        public string WindowMark { get; set; }
        public string WindowTypeName { get; set; }
        
        public ElementId Id 
        { 
            get { return WindowInstance != null ? WindowInstance.Id : null; } 
        }

        public string DisplayName 
        { 
            get 
            { 
                string idVal = Id != null ? Id.Value.ToString() : "0";
                return string.Format("{0} ({1}) - ID: {2}", WindowMark, WindowTypeName, idVal); 
            } 
        }
    }
}
