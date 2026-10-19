using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BEAI.QueryHost.DTO;

namespace BEAI.QueryHost.Core
{
    public static class GenericRevitReader
    {
        public static ModelInfoDto ReadModelInfo(Document doc)
        {
            if (doc == null) return null;

            var info = new ModelInfoDto();
            info.DocumentTitle = doc.Title;
            info.DocumentPath = doc.PathName;
            info.IsWorkshared = doc.IsWorkshared;
            info.IsModified = doc.IsModified;
            info.IsFamilyDocument = doc.IsFamilyDocument;
            info.RevitVersion = doc.Application.VersionNumber;

            try
            {
                ProjectInfo projInfo = doc.ProjectInformation;
                if (projInfo != null)
                {
                    info.ProjectName = projInfo.Name;
                    info.ProjectNumber = projInfo.Number;
                }
            }
            catch { }

            try
            {
                View activeView = doc.ActiveView;
                if (activeView != null)
                {
                    info.ActiveViewId = activeView.Id.Value;
                    info.ActiveView = activeView.Name;
                    info.ActiveViewType = activeView.ViewType.ToString();
                }
            }
            catch { }

            return info;
        }

        public static List<LevelDto> ReadLevels(Document doc)
        {
            if (doc == null) return new List<LevelDto>();

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(delegate(Level level) { return level.Elevation; })
                .Select(delegate(Level level)
                {
                    return new LevelDto
                    {
                        Id = level.Id.Value,
                        Name = level.Name,
                        ElevationMm = Math.Round(UnitUtils.ConvertFromInternalUnits(level.Elevation, UnitTypeId.Millimeters), 3)
                    };
                })
                .ToList();
        }

        public static List<CategoryDto> ReadCategories(Document doc)
        {
            if (doc == null) return new List<CategoryDto>();

            var list = new List<CategoryDto>();
            Categories categories = doc.Settings.Categories;

            foreach (Category cat in categories)
            {
                try
                {
                    if (cat == null || cat.CategoryType != CategoryType.Model) continue;

                    int count = 0;
                    try
                    {
                        var ids = new List<ElementId>();
                        ids.Add(cat.Id);
                        var filter = new ElementMulticategoryFilter(ids);
                        var collector = new FilteredElementCollector(doc)
                            .WherePasses(filter)
                            .WhereElementIsNotElementType();
                        count = collector.GetElementCount();
                    }
                    catch
                    {
                        try
                        {
                            var collector = new FilteredElementCollector(doc)
                                .OfCategoryId(cat.Id)
                                .WhereElementIsNotElementType();
                            count = collector.GetElementCount();
                        }
                        catch { }
                    }

                    var catDto = new CategoryDto();
                    catDto.CategoryId = cat.Id.Value;
                    catDto.CategoryName = cat.Name;
                    catDto.CategoryType = cat.CategoryType.ToString();
                    catDto.ElementCount = count;
                    catDto.AllowsBoundParameters = cat.AllowsBoundParameters;
                    list.Add(catDto);
                }
                catch { }
            }

            return list.OrderBy(delegate(CategoryDto c) { return c.CategoryName; }).ToList();
        }

        public static SearchElementsResponseDto SearchElements(Document doc, SearchElementsRequestDto request)
        {
            if (doc == null)
            {
                var r = new SearchElementsResponseDto();
                r.Success = false;
                return r;
            }

            int pageSize = Math.Max(1, Math.Min(request.PageSize > 0 ? request.PageSize : 100, 500));
            int page = Math.Max(1, request.Page);

            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();

            Category targetCategory = ResolveCategory(doc, request.Category, request.CategoryId);
            if (targetCategory != null)
            {
                collector.OfCategoryId(targetCategory.Id);
            }
            else if (!string.IsNullOrEmpty(request.Category))
            {
                var emptyResp = new SearchElementsResponseDto();
                emptyResp.Success = true;
                emptyResp.Total = 0;
                emptyResp.Page = page;
                emptyResp.PageSize = pageSize;
                emptyResp.HasMore = false;
                emptyResp.Items = new List<ElementSummaryDto>();
                return emptyResp;
            }

            IEnumerable<Element> query = collector.ToElements();

            // Level filter
            if (request.LevelId.HasValue)
            {
                long targetLevelId = request.LevelId.Value;
                query = query.Where(delegate(Element e) { return GetElementLevelId(e) == targetLevelId; });
            }
            else if (!string.IsNullOrEmpty(request.Level))
            {
                string targetLevelName = request.Level;
                query = query.Where(delegate(Element e) { return string.Equals(GetElementLevelName(doc, e), targetLevelName, StringComparison.OrdinalIgnoreCase); });
            }

            // Workset filter
            if (request.WorksetId.HasValue && doc.IsWorkshared)
            {
                int targetWorksetId = (int)request.WorksetId.Value;
                query = query.Where(delegate(Element e) { return e.WorksetId.IntegerValue == targetWorksetId; });
            }
            else if (!string.IsNullOrEmpty(request.Workset) && doc.IsWorkshared)
            {
                string targetWorksetName = request.Workset;
                query = query.Where(delegate(Element e) { return string.Equals(GetElementWorksetName(doc, e), targetWorksetName, StringComparison.OrdinalIgnoreCase); });
            }

            // Family & Type Name filter
            if (!string.IsNullOrEmpty(request.FamilyName))
            {
                string targetFamName = request.FamilyName;
                query = query.Where(delegate(Element e) { return string.Equals(GetElementFamilyName(doc, e), targetFamName, StringComparison.OrdinalIgnoreCase); });
            }

            if (!string.IsNullOrEmpty(request.TypeName))
            {
                string targetTypeName = request.TypeName;
                query = query.Where(delegate(Element e) { return string.Equals(GetElementTypeName(doc, e), targetTypeName, StringComparison.OrdinalIgnoreCase); });
            }

            // NameContains filter
            if (!string.IsNullOrEmpty(request.NameContains))
            {
                string nameQuery = request.NameContains;
                query = query.Where(delegate(Element e) { return !string.IsNullOrEmpty(e.Name) && e.Name.IndexOf(nameQuery, StringComparison.OrdinalIgnoreCase) >= 0; });
            }

            var allMatched = query.ToList();
            int total = allMatched.Count;
            int skip = (page - 1) * pageSize;
            var pageElements = allMatched.Skip(skip).Take(pageSize).ToList();

            var items = new List<ElementSummaryDto>();
            foreach (var elem in pageElements)
            {
                items.Add(ReadElementSummary(doc, elem));
            }

            var resp = new SearchElementsResponseDto();
            resp.Success = true;
            resp.Total = total;
            resp.Page = page;
            resp.PageSize = pageSize;
            resp.HasMore = (skip + items.Count) < total;
            resp.Items = items;
            return resp;
        }

        public static int CountElements(Document doc, CountRequestDto request)
        {
            if (doc == null) return 0;

            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            Category targetCategory = ResolveCategory(doc, request.Category, request.CategoryId);
            if (targetCategory != null)
            {
                collector.OfCategoryId(targetCategory.Id);
            }
            else if (!string.IsNullOrEmpty(request.Category))
            {
                return 0;
            }

            if (string.IsNullOrEmpty(request.FamilyName) &&
                string.IsNullOrEmpty(request.TypeName) &&
                string.IsNullOrEmpty(request.Level) &&
                !request.LevelId.HasValue &&
                string.IsNullOrEmpty(request.Workset) &&
                string.IsNullOrEmpty(request.NameContains))
            {
                return collector.GetElementCount();
            }

            IEnumerable<Element> query = collector.ToElements();

            if (request.LevelId.HasValue)
            {
                long targetLevelId = request.LevelId.Value;
                query = query.Where(delegate(Element e) { return GetElementLevelId(e) == targetLevelId; });
            }
            else if (!string.IsNullOrEmpty(request.Level))
            {
                string targetLevelName = request.Level;
                query = query.Where(delegate(Element e) { return string.Equals(GetElementLevelName(doc, e), targetLevelName, StringComparison.OrdinalIgnoreCase); });
            }

            if (!string.IsNullOrEmpty(request.FamilyName))
            {
                string targetFamName = request.FamilyName;
                query = query.Where(delegate(Element e) { return string.Equals(GetElementFamilyName(doc, e), targetFamName, StringComparison.OrdinalIgnoreCase); });
            }

            if (!string.IsNullOrEmpty(request.TypeName))
            {
                string targetTypeName = request.TypeName;
                query = query.Where(delegate(Element e) { return string.Equals(GetElementTypeName(doc, e), targetTypeName, StringComparison.OrdinalIgnoreCase); });
            }

            if (!string.IsNullOrEmpty(request.NameContains))
            {
                string nameQuery = request.NameContains;
                query = query.Where(delegate(Element e) { return !string.IsNullOrEmpty(e.Name) && e.Name.IndexOf(nameQuery, StringComparison.OrdinalIgnoreCase) >= 0; });
            }

            return query.Count();
        }

        public static ElementSummaryDto ReadElementSummary(Document doc, Element e)
        {
            if (e == null) return null;

            var dto = new ElementSummaryDto();
            dto.ElementId = e.Id.Value;
            dto.UniqueId = e.UniqueId;
            dto.Name = e.Name;
            dto.IsPinned = e.Pinned;

            if (e.Category != null)
            {
                dto.CategoryId = e.Category.Id.Value;
                dto.CategoryName = e.Category.Name;
            }

            dto.FamilyName = GetElementFamilyName(doc, e);
            dto.TypeName = GetElementTypeName(doc, e);

            ElementId typeId = e.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                dto.TypeId = typeId.Value;
            }

            long? levelId = GetElementLevelId(e);
            if (levelId.HasValue)
            {
                dto.LevelId = levelId;
                dto.LevelName = GetElementLevelName(doc, e);
            }

            if (doc.IsWorkshared)
            {
                dto.WorksetId = e.WorksetId.IntegerValue;
                dto.WorksetName = GetElementWorksetName(doc, e);
            }

            FamilyInstance fi = e as FamilyInstance;
            if (fi != null && fi.Host != null)
            {
                dto.HostElementId = fi.Host.Id.Value;
            }

            return dto;
        }

        public static ElementParametersDto ReadElementParameters(Document doc, ElementId elementId)
        {
            if (doc == null || elementId == null || elementId == ElementId.InvalidElementId)
            {
                return null;
            }

            Element element = doc.GetElement(elementId);
            if (element == null) return null;

            var result = new ElementParametersDto();
            result.Success = true;

            var header = new ElementHeaderDto();
            header.ElementId = element.Id.Value;
            header.UniqueId = element.UniqueId;
            header.Category = element.Category != null ? element.Category.Name : null;
            header.FamilyName = GetElementFamilyName(doc, element);
            header.TypeName = GetElementTypeName(doc, element);
            result.Element = header;

            result.InstanceParameters = ExtractParameters(doc, element, "instance");

            ElementId typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                Element typeElement = doc.GetElement(typeId);
                if (typeElement != null)
                {
                    var typeHeader = new TypeHeaderDto();
                    typeHeader.ElementId = typeElement.Id.Value;
                    typeHeader.Name = typeElement.Name;
                    result.Type = typeHeader;

                    result.TypeParameters = ExtractParameters(doc, typeElement, "type");
                }
            }

            return result;
        }

        public static List<ParameterDto> ExtractParameters(Document doc, Element elem, string source)
        {
            var list = new List<ParameterDto>();
            if (elem == null) return list;

            foreach (Parameter p in elem.Parameters)
            {
                try
                {
                    if (p == null || p.Definition == null) continue;

                    var paramDto = new ParameterDto();
                    paramDto.Name = p.Definition.Name;
                    paramDto.ParameterId = p.Id.Value.ToString();
                    paramDto.Source = source;
                    paramDto.StorageType = p.StorageType.ToString();
                    paramDto.HasValue = p.HasValue;
                    paramDto.IsReadOnly = p.IsReadOnly;

                    if (Enum.IsDefined(typeof(BuiltInParameter), (int)p.Id.Value))
                    {
                        paramDto.BuiltInParameter = ((BuiltInParameter)p.Id.Value).ToString();
                    }

                    try
                    {
                        if (p.IsShared)
                        {
                            paramDto.SharedParameterGuid = p.GUID.ToString();
                        }
                    }
                    catch { }

                    try
                    {
                        ForgeTypeId forgeTypeId = p.GetUnitTypeId();
                        if (forgeTypeId != null && !string.IsNullOrEmpty(forgeTypeId.TypeId))
                        {
                            paramDto.Unit = forgeTypeId.TypeId;
                        }
                    }
                    catch { }

                    if (p.HasValue)
                    {
                        switch (p.StorageType)
                        {
                            case StorageType.String:
                                string strVal = p.AsString();
                                paramDto.RawValue = strVal;
                                paramDto.DisplayValue = strVal;
                                break;

                            case StorageType.Integer:
                                int intVal = p.AsInteger();
                                paramDto.RawValue = intVal;
                                try
                                {
                                    string valStr = p.AsValueString();
                                    paramDto.DisplayValue = !string.IsNullOrEmpty(valStr) ? valStr : intVal.ToString();
                                }
                                catch
                                {
                                    paramDto.DisplayValue = intVal.ToString();
                                }
                                break;

                            case StorageType.Double:
                                double dblVal = p.AsDouble();
                                paramDto.RawValue = dblVal;
                                try
                                {
                                    string valStr = p.AsValueString();
                                    paramDto.DisplayValue = !string.IsNullOrEmpty(valStr) ? valStr : dblVal.ToString("G");
                                }
                                catch
                                {
                                    paramDto.DisplayValue = dblVal.ToString("G");
                                }
                                break;

                            case StorageType.ElementId:
                                ElementId refId = p.AsElementId();
                                if (refId != null && refId != ElementId.InvalidElementId)
                                {
                                    paramDto.RawValue = refId.Value;
                                    paramDto.ReferencedElementId = refId.Value;
                                    Element refElem = doc.GetElement(refId);
                                    if (refElem != null)
                                    {
                                        paramDto.ReferencedElementName = refElem.Name;
                                        paramDto.DisplayValue = refElem.Name;
                                    }
                                    else
                                    {
                                        paramDto.DisplayValue = string.Format("<ElementId: {0}>", refId.Value);
                                    }
                                }
                                else
                                {
                                    paramDto.RawValue = -1L;
                                    paramDto.DisplayValue = "<None>";
                                }
                                break;

                            default:
                                paramDto.RawValue = null;
                                paramDto.DisplayValue = "";
                                break;
                        }
                    }
                    else
                    {
                        paramDto.RawValue = null;
                        paramDto.DisplayValue = null;
                    }

                    list.Add(paramDto);
                }
                catch { }
            }

            return list;
        }

        #region Helper Extraction Methods

        private static Category ResolveCategory(Document doc, string categoryName, long? categoryId)
        {
            if (categoryId.HasValue)
            {
                try
                {
                    return Category.GetCategory(doc, new ElementId(categoryId.Value));
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(categoryName))
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (string.Equals(cat.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        return cat;
                    }
                }
            }

            return null;
        }

        public static string GetElementFamilyName(Document doc, Element e)
        {
            FamilyInstance fi = e as FamilyInstance;
            if (fi != null && fi.Symbol != null && fi.Symbol.Family != null)
            {
                return fi.Symbol.Family.Name;
            }

            ElementId typeId = e.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                ElementType typeElem = doc.GetElement(typeId) as ElementType;
                if (typeElem != null)
                {
                    return typeElem.FamilyName != null ? typeElem.FamilyName : typeElem.Name;
                }
            }

            return null;
        }

        public static string GetElementTypeName(Document doc, Element e)
        {
            ElementId typeId = e.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                Element typeElem = doc.GetElement(typeId);
                if (typeElem != null) return typeElem.Name;
            }

            return null;
        }

        public static long? GetElementLevelId(Element e)
        {
            if (e.LevelId != null && e.LevelId != ElementId.InvalidElementId)
            {
                return e.LevelId.Value;
            }

            Parameter p = e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
            if (p == null) p = e.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
            if (p == null) p = e.get_Parameter(BuiltInParameter.LEVEL_PARAM);

            if (p != null && p.StorageType == StorageType.ElementId && p.AsElementId() != ElementId.InvalidElementId)
            {
                return p.AsElementId().Value;
            }

            return null;
        }

        public static string GetElementLevelName(Document doc, Element e)
        {
            long? levelId = GetElementLevelId(e);
            if (levelId.HasValue)
            {
                Element levelElem = doc.GetElement(new ElementId(levelId.Value));
                if (levelElem != null) return levelElem.Name;
            }
            return null;
        }

        public static string GetElementWorksetName(Document doc, Element e)
        {
            if (!doc.IsWorkshared) return null;
            try
            {
                WorksetTable table = doc.GetWorksetTable();
                Workset workset = table.GetWorkset(e.WorksetId);
                return workset != null ? workset.Name : null;
            }
            catch { return null; }
        }

        #endregion
    }
}
