using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.TileElevationGenerator
{
    public static class ViewTemplateSelector
    {
        public static List<View> GetSectionViewTemplates(Document doc)
        {
            var list = new List<View>();
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .WhereElementIsNotElementType();

            foreach (var elem in collector)
            {
                if (elem is View view && view.IsTemplate && view.ViewType == ViewType.Section)
                {
                    list.Add(view);
                }
            }

            // 依名稱排序，提升 UI 可讀性
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }
    }
}
