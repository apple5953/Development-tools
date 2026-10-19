using Autodesk.Revit.DB;

namespace OpeningDetailAddin.Models
{
    /// <summary>
    /// 圖框模型封裝，供 ComboBox 下拉選單顯示與綁定
    /// </summary>
    public class TitleBlockModel
    {
        public ElementId Id { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }

        public string DisplayName
        {
            get
            {
                return string.Format("{0} : {1}", FamilyName, TypeName);
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
