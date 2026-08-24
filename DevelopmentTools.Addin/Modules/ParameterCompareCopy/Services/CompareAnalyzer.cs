using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DevelopmentTools.Modules.ParameterCompareCopy.Models;

namespace DevelopmentTools.Modules.ParameterCompareCopy.Services
{
    public class CompareAnalyzer
    {
        private readonly Document _sourceDoc;
        private readonly Document _targetDoc;

        public CompareAnalyzer(Document sourceDoc, Document targetDoc)
        {
            _sourceDoc = sourceDoc;
            _targetDoc = targetDoc;
        }

        public List<CompareAsset> AnalyzeAssets()
        {
            var assets = new List<CompareAsset>();

            // 1. View Templates 比對
            var targetVts = new FilteredElementCollector(_targetDoc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(x => x.IsTemplate)
                .ToDictionary(x => x.Name, x => x);

            var viewTemplates = new FilteredElementCollector(_sourceDoc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(x => x.IsTemplate)
                .ToList();

            foreach (var vt in viewTemplates)
            {
                var asset = new CompareAsset
                {
                    Type = CompareAssetType.ViewTemplate,
                    ElementId = vt.Id,
                    UniqueId = vt.UniqueId,
                    Name = vt.Name,
                    IsSelected = false
                };

                if (targetVts.TryGetValue(vt.Name, out var tgtVt))
                {
                    if (vt.Scale != tgtVt.Scale)
                    {
                        asset.Comparison = AssetComparison.Mismatch;
                        asset.DiffDetails.Add(new ParameterDiffItem
                        {
                            ParamName = "視圖比例",
                            SourceValue = $"1:{vt.Scale}",
                            TargetValue = $"1:{tgtVt.Scale}",
                            IsDifferent = true
                        });
                    }
                    else
                    {
                        asset.Comparison = AssetComparison.Identical;
                    }
                }
                else
                {
                    asset.Comparison = AssetComparison.New;
                }
                assets.Add(asset);
            }

            // 2. Schedules 比對
            var targetSchedulesDict = new FilteredElementCollector(_targetDoc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .ToDictionary(x => x.Name, x => x);

            var schedules = new FilteredElementCollector(_sourceDoc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(x => !x.IsTemplate && !x.IsInternalKeynoteSchedule && !x.Name.Contains("<"))
                .ToList();

            foreach (var schedule in schedules)
            {
                var asset = new CompareAsset
                {
                    Type = CompareAssetType.Schedule,
                    ElementId = schedule.Id,
                    UniqueId = schedule.UniqueId,
                    Name = schedule.Name,
                    IsSelected = false
                };

                if (targetSchedulesDict.ContainsKey(schedule.Name))
                {
                    asset.Comparison = AssetComparison.Identical;
                }
                else
                {
                    asset.Comparison = AssetComparison.New;
                }
                assets.Add(asset);
            }

            // 3. Project Parameters 比對
            var targetParamsDict = new Dictionary<string, (Definition Definition, Binding Binding)>();
            DefinitionBindingMapIterator tgtIt = _targetDoc.ParameterBindings.ForwardIterator();
            while (tgtIt.MoveNext())
            {
                targetParamsDict[tgtIt.Key.Name] = (tgtIt.Key, tgtIt.Current as Binding);
            }

            var sharedParamElements = new FilteredElementCollector(_sourceDoc)
                .OfClass(typeof(SharedParameterElement))
                .Cast<SharedParameterElement>()
                .ToDictionary(x => x.GuidValue, x => x);

            BindingMap parameterBindings = _sourceDoc.ParameterBindings;
            DefinitionBindingMapIterator bindingIt = parameterBindings.ForwardIterator();
            while (bindingIt.MoveNext())
            {
                Definition def = bindingIt.Key;
                Binding binding = bindingIt.Current as Binding;
                string paramName = def.Name;
                ElementId paramId = ElementId.InvalidElementId;
                string uniqueId = "internal_param_" + paramName;

                if (def is ExternalDefinition extDef)
                {
                    uniqueId = extDef.GUID.ToString();
                    if (sharedParamElements.TryGetValue(extDef.GUID, out var spe))
                    {
                        paramId = spe.Id;
                    }
                }

                var asset = new CompareAsset
                {
                    Type = CompareAssetType.ProjectParameter,
                    ElementId = paramId,
                    UniqueId = uniqueId,
                    Name = paramName,
                    IsSelected = false
                };

                if (targetParamsDict.TryGetValue(paramName, out var tgtPair))
                {
                    CategorySet srcCats = null;
                    if (binding is InstanceBinding srcInst) srcCats = srcInst.Categories;
                    else if (binding is TypeBinding srcType) srcCats = srcType.Categories;

                    CategorySet tgtCats = null;
                    if (tgtPair.Binding is InstanceBinding tgtInst) tgtCats = tgtInst.Categories;
                    else if (tgtPair.Binding is TypeBinding tgtType) tgtCats = tgtType.Categories;

                    List<string> missingCats = new List<string>();
                    if (srcCats != null && tgtCats != null)
                    {
                        foreach (Category srcCat in srcCats)
                        {
                            bool found = false;
                            foreach (Category tgtCat in tgtCats)
                            {
                                if (tgtCat.BuiltInCategory == srcCat.BuiltInCategory)
                                {
                                    found = true;
                                    break;
                                }
                            }
                            if (!found) missingCats.Add(srcCat.Name);
                        }
                    }

                    if (missingCats.Any())
                    {
                        asset.Comparison = AssetComparison.Mismatch;
                        asset.DiffDetails.Add(new ParameterDiffItem
                        {
                            ParamName = "綁定品類範圍",
                            SourceValue = string.Join(", ", srcCats.Cast<Category>().Select(x => x.Name)),
                            TargetValue = string.Join(", ", tgtCats.Cast<Category>().Select(x => x.Name)),
                            IsDifferent = true
                        });
                    }
                    else
                    {
                        asset.Comparison = AssetComparison.Identical;
                    }
                }
                else
                {
                    asset.Comparison = AssetComparison.New;
                }
                assets.Add(asset);
            }

            // 4. Line Styles 比對
            Category srcLinesCategory = _sourceDoc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category tgtLinesCategory = _targetDoc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (srcLinesCategory != null && tgtLinesCategory != null)
            {
                var srcSubCats = srcLinesCategory.SubCategories.Cast<Category>().ToList();
                var tgtSubCats = tgtLinesCategory.SubCategories.Cast<Category>().ToDictionary(c => c.Name, c => c);

                foreach (var srcSubCat in srcSubCats)
                {
                    var asset = new CompareAsset
                    {
                        Type = CompareAssetType.LineStyle,
                        ElementId = srcSubCat.Id,
                        UniqueId = "line_style_" + srcSubCat.Name,
                        Name = srcSubCat.Name,
                        IsSelected = false
                    };

                    if (tgtSubCats.TryGetValue(srcSubCat.Name, out var tgtSubCat))
                    {
                        bool isDiff = false;
                        int? srcWeight = srcSubCat.GetLineWeight(GraphicsStyleType.Projection);
                        int? tgtWeight = tgtSubCat.GetLineWeight(GraphicsStyleType.Projection);
                        if (srcWeight != tgtWeight)
                        {
                            isDiff = true;
                            asset.DiffDetails.Add(new ParameterDiffItem
                            {
                                ParamName = "投影線寬",
                                SourceValue = srcWeight?.ToString() ?? "無",
                                TargetValue = tgtWeight?.ToString() ?? "無",
                                IsDifferent = true
                            });
                        }

                        Color srcColor = srcSubCat.LineColor;
                        Color tgtColor = tgtSubCat.LineColor;
                        if (srcColor.Red != tgtColor.Red || srcColor.Green != tgtColor.Green || srcColor.Blue != tgtColor.Blue)
                        {
                            isDiff = true;
                            asset.DiffDetails.Add(new ParameterDiffItem
                            {
                                ParamName = "線條顏色 (RGB)",
                                SourceValue = $"{srcColor.Red},{srcColor.Green},{srcColor.Blue}",
                                TargetValue = $"{tgtColor.Red},{tgtColor.Green},{tgtColor.Blue}",
                                IsDifferent = true
                            });
                        }

                        asset.Comparison = isDiff ? AssetComparison.Mismatch : AssetComparison.Identical;
                    }
                    else
                    {
                        asset.Comparison = AssetComparison.New;
                    }
                    assets.Add(asset);
                }
            }

            // 5. Materials 比對
            var srcMaterials = new FilteredElementCollector(_sourceDoc).OfClass(typeof(Material)).Cast<Material>().ToList();
            var tgtMaterialsDict = new FilteredElementCollector(_targetDoc).OfClass(typeof(Material)).Cast<Material>().ToDictionary(m => m.Name, m => m);

            foreach (var mat in srcMaterials)
            {
                var asset = new CompareAsset
                {
                    Type = CompareAssetType.Material,
                    ElementId = mat.Id,
                    UniqueId = "material_" + mat.Name,
                    Name = mat.Name,
                    IsSelected = false
                };

                if (tgtMaterialsDict.TryGetValue(mat.Name, out var tgtMat))
                {
                    bool isDiff = false;
                    Color srcCol = mat.Color;
                    Color tgtCol = tgtMat.Color;
                    if (srcCol.Red != tgtCol.Red || srcCol.Green != tgtCol.Green || srcCol.Blue != tgtCol.Blue)
                    {
                        isDiff = true;
                        asset.DiffDetails.Add(new ParameterDiffItem
                        {
                            ParamName = "材質顏色 (RGB)",
                            SourceValue = $"{srcCol.Red},{srcCol.Green},{srcCol.Blue}",
                            TargetValue = $"{tgtCol.Red},{tgtCol.Green},{tgtCol.Blue}",
                            IsDifferent = true
                        });
                    }

                    if (mat.Transparency != tgtMat.Transparency)
                    {
                        isDiff = true;
                        asset.DiffDetails.Add(new ParameterDiffItem
                        {
                            ParamName = "透明度",
                            SourceValue = $"{mat.Transparency}%",
                            TargetValue = $"{tgtMat.Transparency}%",
                            IsDifferent = true
                        });
                    }

                    asset.Comparison = isDiff ? AssetComparison.Mismatch : AssetComparison.Identical;
                }
                else
                {
                    asset.Comparison = AssetComparison.New;
                }
                assets.Add(asset);
            }

            // 6. Symbol Settings (泛用註解等符號型別) 比對
            var srcGenericSyms = new FilteredElementCollector(_sourceDoc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(s => s.Category != null && s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_GenericAnnotation)
                .ToList();

            var tgtGenericSymsDict = new FilteredElementCollector(_targetDoc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(s => s.Category != null && s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_GenericAnnotation)
                .ToDictionary(s => $"{s.FamilyName}_{s.Name}", s => s);

            foreach (var sym in srcGenericSyms)
            {
                var asset = new CompareAsset
                {
                    Type = CompareAssetType.SymbolSetting,
                    ElementId = sym.Id,
                    UniqueId = "symbol_" + sym.FamilyName + "_" + sym.Name,
                    Name = $"{sym.FamilyName}: {sym.Name}",
                    IsSelected = false
                };

                string key = $"{sym.FamilyName}_{sym.Name}";
                if (tgtGenericSymsDict.TryGetValue(key, out var tgtSym))
                {
                    bool isDiff = false;
                    foreach (Parameter srcP in sym.Parameters)
                    {
                        if (srcP.IsReadOnly || !srcP.HasValue) continue;
                        Parameter tgtP = tgtSym.LookupParameter(srcP.Definition.Name);
                        string srcVal = srcP.AsValueString() ?? srcP.AsString() ?? srcP.AsDouble().ToString();
                        string tgtVal = tgtP != null && tgtP.HasValue ? (tgtP.AsValueString() ?? tgtP.AsString() ?? tgtP.AsDouble().ToString()) : "無";

                        if (srcVal != tgtVal)
                        {
                            isDiff = true;
                            asset.DiffDetails.Add(new ParameterDiffItem
                            {
                                ParamName = srcP.Definition.Name,
                                SourceValue = srcVal,
                                TargetValue = tgtVal,
                                IsDifferent = true
                            });
                        }
                    }
                    asset.Comparison = isDiff ? AssetComparison.Mismatch : AssetComparison.Identical;
                }
                else
                {
                    asset.Comparison = AssetComparison.New;
                }
                assets.Add(asset);
            }

            // 7. 各種重要元件參數設定比對
            var srcTypes = new List<ElementType>();
            var catList = new List<BuiltInCategory> { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows };
            foreach (var cat in catList)
            {
                var types = new FilteredElementCollector(_sourceDoc)
                    .OfCategory(cat)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();
                srcTypes.AddRange(types);
            }

            var tgtTypesDict = new Dictionary<string, ElementType>();
            foreach (var cat in catList)
            {
                var types = new FilteredElementCollector(_targetDoc)
                    .OfCategory(cat)
                    .WhereElementIsElementType()
                    .Cast<ElementType>();
                foreach (var t in types)
                {
                    if (t.Category != null)
                    {
                        tgtTypesDict[$"{t.Category.Name}_{t.Name}"] = t;
                    }
                }
            }

            foreach (var t in srcTypes)
            {
                if (t.Category == null) continue;

                var asset = new CompareAsset
                {
                    Type = CompareAssetType.ElementParameter,
                    ElementId = t.Id,
                    UniqueId = "elem_type_" + t.Category.Name + "_" + t.Name,
                    Name = $"[{t.Category.Name}] {t.Name}",
                    IsSelected = false
                };

                string key = $"{t.Category.Name}_{t.Name}";
                if (tgtTypesDict.TryGetValue(key, out var tgtT))
                {
                    bool isDiff = false;
                    foreach (Parameter srcP in t.Parameters)
                    {
                        if (srcP.IsReadOnly || !srcP.HasValue) continue;
                        Parameter tgtP = tgtT.LookupParameter(srcP.Definition.Name);
                        string srcVal = srcP.AsValueString() ?? srcP.AsString() ?? srcP.AsDouble().ToString();
                        string tgtVal = tgtP != null && tgtP.HasValue ? (tgtP.AsValueString() ?? tgtP.AsString() ?? tgtP.AsDouble().ToString()) : "無";

                        if (srcVal != tgtVal)
                        {
                            isDiff = true;
                            asset.DiffDetails.Add(new ParameterDiffItem
                            {
                                ParamName = srcP.Definition.Name,
                                SourceValue = srcVal,
                                TargetValue = tgtVal,
                                IsDifferent = true
                            });
                        }
                    }
                    asset.Comparison = isDiff ? AssetComparison.Mismatch : AssetComparison.Identical;
                }
                else
                {
                    asset.Comparison = AssetComparison.New;
                }
                assets.Add(asset);
            }

            return assets;
        }
    }
}
