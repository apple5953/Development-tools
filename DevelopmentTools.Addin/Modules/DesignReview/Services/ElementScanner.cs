using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.DesignReview.Services
{
    public class ExtractedElement
    {
        public string UniqueId { get; set; }
        public string ElementId { get; set; }
        public string CategoryName { get; set; }
        public string ElementName { get; set; }
        public string LevelName { get; set; }
        public XYZ Location { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public double? GetDoubleParameter(string name)
        {
            if (Parameters.TryGetValue(name, out var val) && double.TryParse(val, out var res))
                return res;
            if (Parameters.TryGetValue("Type_" + name, out var tVal) && double.TryParse(tVal, out var tRes))
                return tRes;
            return null;
        }

        public string GetStringParameter(string name)
        {
            if (Parameters.TryGetValue(name, out var val))
                return val;
            if (Parameters.TryGetValue("Type_" + name, out var tVal))
                return tVal;
            return null;
        }

        public int? GetIntParameter(string name)
        {
            if (Parameters.TryGetValue(name, out var val) && int.TryParse(val, out var res))
                return res;
            if (Parameters.TryGetValue("Type_" + name, out var tVal) && int.TryParse(tVal, out var tRes))
                return tRes;
            return null;
        }
    }

    public class ElementScanner
    {
        private readonly Document _doc;
        public ElementScanner(Document doc) => _doc = doc;

        public List<ExtractedElement> ScanCategories(List<BuiltInCategory> categories)
        {
            var result = new List<ExtractedElement>();
            if (categories == null || !categories.Any()) return result;

            var filter = new ElementMulticategoryFilter(categories);
            var collector = new FilteredElementCollector(_doc)
                .WherePasses(filter)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var elem in collector)
            {
                var ext = new ExtractedElement
                {
                    UniqueId = elem.UniqueId,
                    ElementId = elem.Id.ToString(),
                    CategoryName = elem.Category?.Name ?? "未知品類",
                    ElementName = elem.Name,
                    LevelName = _doc.GetElement(elem.LevelId)?.Name ?? "無樓層"
                };

                // 獲取 Location 幾何位置
                if (elem.Location is LocationPoint lp)
                {
                    ext.Location = lp.Point;
                }
                else if (elem.Location is LocationCurve lc)
                {
                    ext.Location = lc.Curve.Evaluate(0.5, true); // 中點
                }
                else
                {
                    // 透過 BoundingBox 獲取中心
                    var bbox = elem.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        ext.Location = (bbox.Max + bbox.Min) / 2.0;
                    }
                }

                // 提取實例參數
                foreach (Parameter p in elem.Parameters)
                {
                    if (p.HasValue)
                    {
                        ext.Parameters[p.Definition.Name] = GetParameterValue(p);
                    }
                }

                // 提取類型參數
                ElementId typeId = elem.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    Element typeElem = _doc.GetElement(typeId);
                    if (typeElem != null)
                    {
                        foreach (Parameter tp in typeElem.Parameters)
                        {
                            if (tp.HasValue)
                            {
                                ext.Parameters["Type_" + tp.Definition.Name] = GetParameterValue(tp);
                            }
                        }
                    }
                }
                result.Add(ext);
            }
            return result;
        }

        private string GetParameterValue(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.Double:
                    // 為了便於法規庫使用，長度單位統一由英呎轉換成公釐(mm)
                    if (p.Definition.Name.Contains("寬") || p.Definition.Name.Contains("高") || p.Definition.Name.Contains("深") || 
                        p.Definition.Name.Contains("厚") || p.Definition.Name.Contains("長") || 
                        p.Definition.Name.ToLower().Contains("width") || p.Definition.Name.ToLower().Contains("height") || 
                        p.Definition.Name.ToLower().Contains("depth") || p.Definition.Name.ToLower().Contains("thickness"))
                    {
                        return (p.AsDouble() * 304.8).ToString("F2");
                    }
                    return p.AsDouble().ToString("F4");

                case StorageType.Integer:
                    return p.AsInteger().ToString();

                case StorageType.String:
                    return p.AsString();

                case StorageType.ElementId:
                    return p.AsElementId().ToString();

                default:
                    return p.AsValueString();
            }
        }
    }
}
