using System;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.TileElevationGenerator
{
    public static class ElevationNamingService
    {
        public static string GenerateViewName(Document doc, string prefix, int index)
        {
            string suffix = GetSuffix(index);
            string baseName = $"{prefix}_{suffix}";
            
            // 檢查 Revit 專案中是否有重複的視圖名稱，如果有，加上 _1, _2 等以避免例外
            string finalName = baseName;
            int counter = 1;
            while (ViewExists(doc, finalName))
            {
                finalName = $"{baseName}_{counter}";
                counter++;
            }

            return finalName;
        }

        private static string GetSuffix(int index)
        {
            if (index < 26)
            {
                return ((char)('A' + index)).ToString();
            }
            
            int first = (index / 26) - 1;
            int second = index % 26;
            return ((char)('A' + first)).ToString() + ((char)('A' + second)).ToString();
        }

        private static bool ViewExists(Document doc, string name)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .WhereElementIsNotElementType();

            foreach (var elem in collector)
            {
                if (elem is View view && view.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
