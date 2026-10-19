using Autodesk.Revit.DB;

namespace OpeningDetailAddin.Models
{
    /// <summary>
    /// 視圖樣板資料模型
    /// </summary>
    public class ViewTemplateModel
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public bool IsAutoCreate { get; set; }
        public bool IsNone { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
