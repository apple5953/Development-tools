using Autodesk.Revit.DB;

namespace OpeningDetailAddin.Models
{
    /// <summary>
    /// 視埠標題類型模型 (供下拉選單選擇「無標題」、「視圖標題」等)
    /// </summary>
    public class ViewportTypeModel
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
