using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DevelopmentTools.Modules.SheetTransfer.Models;

namespace DevelopmentTools.Modules.SheetTransfer.Services
{
    public class SheetTransferAnalyzer
    {
        private readonly Document _sourceDoc;
        private readonly Document _targetDoc;

        public SheetTransferAnalyzer(Document sourceDoc, Document targetDoc)
        {
            _sourceDoc = sourceDoc;
            _targetDoc = targetDoc;
        }

        public List<TransferAsset> AnalyzeAssets()
        {
            var assets = new List<TransferAsset>();

            // 1. Sheets 比對
            var sourceSheets = new FilteredElementCollector(_sourceDoc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(x => !x.IsPlaceholder)
                .ToList();

            var targetSheetsDict = new FilteredElementCollector(_targetDoc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToDictionary(x => x.SheetNumber, x => x);

            foreach (var sheet in sourceSheets)
            {
                var asset = new TransferAsset
                {
                    Type = AssetType.Sheet,
                    ElementId = sheet.Id,
                    UniqueId = sheet.UniqueId,
                    Name = sheet.Name,
                    Number = sheet.SheetNumber,
                    IsSelected = false
                };

                if (targetSheetsDict.TryGetValue(sheet.SheetNumber, out var tgtSheet))
                {
                    if (tgtSheet.Name != sheet.Name)
                    {
                        asset.Comparison = AssetComparison.Mismatch;
                        asset.DiffDetails.Add(new ParameterDiffItem
                        {
                            ParamName = "圖紙名稱",
                            SourceValue = sheet.Name,
                            TargetValue = tgtSheet.Name,
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

            // 2. Drafting Views & Legends 比對
            var targetViews = new FilteredElementCollector(_targetDoc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(x => !x.IsTemplate)
                .ToDictionary(x => $"{x.ViewType}_{x.Name}", x => x);

            var views = new FilteredElementCollector(_sourceDoc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(x => !x.IsTemplate)
                .ToList();

            foreach (var view in views)
            {
                if (view.ViewType == ViewType.DraftingView)
                {
                    var asset = new TransferAsset
                    {
                        Type = AssetType.DraftingView,
                        ElementId = view.Id,
                        UniqueId = view.UniqueId,
                        Name = view.Name,
                        IsSelected = false
                    };

                    string key = $"{view.ViewType}_{view.Name}";
                    if (targetViews.TryGetValue(key, out var tgtView))
                    {
                        asset.Comparison = (view.Scale != tgtView.Scale) ? AssetComparison.Mismatch : AssetComparison.Identical;
                        if (view.Scale != tgtView.Scale)
                        {
                            asset.DiffDetails.Add(new ParameterDiffItem
                            {
                                ParamName = "視圖比例",
                                SourceValue = $"1:{view.Scale}",
                                TargetValue = $"1:{tgtView.Scale}",
                                IsDifferent = true
                            });
                        }
                    }
                    else
                    {
                        asset.Comparison = AssetComparison.New;
                    }

                    assets.Add(asset);
                }
                else if (view.ViewType == ViewType.Legend)
                {
                    var asset = new TransferAsset
                    {
                        Type = AssetType.Legend,
                        ElementId = view.Id,
                        UniqueId = view.UniqueId,
                        Name = view.Name,
                        IsSelected = false
                    };

                    string key = $"{view.ViewType}_{view.Name}";
                    if (targetViews.TryGetValue(key, out var tgtView))
                    {
                        asset.Comparison = (view.Scale != tgtView.Scale) ? AssetComparison.Mismatch : AssetComparison.Identical;
                        if (view.Scale != tgtView.Scale)
                        {
                            asset.DiffDetails.Add(new ParameterDiffItem
                            {
                                ParamName = "圖例比例",
                                SourceValue = $"1:{view.Scale}",
                                TargetValue = $"1:{tgtView.Scale}",
                                IsDifferent = true
                            });
                        }
                    }
                    else
                    {
                        asset.Comparison = AssetComparison.New;
                    }

                    assets.Add(asset);
                }
            }

            // 3. Schedules 比對
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
                var asset = new TransferAsset
                {
                    Type = AssetType.Schedule,
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

            // 4. Project Parameters 比對
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

                var asset = new TransferAsset
                {
                    Type = AssetType.ProjectParameter,
                    ElementId = paramId,
                    UniqueId = uniqueId,
                    Name = paramName,
                    IsSelected = false
                };

                if (targetParamsDict.TryGetValue(paramName, out var tgtPair))
                {
                    // 比對 Category 綁定範圍
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
                        asset.DiffDetails.Add(new ParameterDiffItem
                        {
                            ParamName = "目標專案缺失品類",
                            SourceValue = string.Join(", ", missingCats),
                            TargetValue = "無",
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

            // 5. View Templates 比對
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
                var asset = new TransferAsset
                {
                    Type = AssetType.ViewTemplate,
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

            // 6. Project Info 比對
            var infoAsset = new TransferAsset
            {
                Type = AssetType.ProjectInfoAndSymbol,
                ElementId = _sourceDoc.ProjectInformation.Id,
                UniqueId = _sourceDoc.ProjectInformation.UniqueId,
                Name = "專案基本資訊 (Project Information)",
                IsSelected = false
            };

            ProjectInfo srcInfo = _sourceDoc.ProjectInformation;
            ProjectInfo tgtInfo = _targetDoc.ProjectInformation;
            bool infoDiff = false;

            foreach (Parameter srcParam in srcInfo.Parameters)
            {
                if (srcParam.IsReadOnly || !srcParam.HasValue) continue;

                Parameter tgtParam = tgtInfo.LookupParameter(srcParam.Definition.Name);
                string srcVal = srcParam.AsValueString() ?? srcParam.AsString() ?? "";
                string tgtVal = "";
                if (tgtParam != null && tgtParam.HasValue)
                {
                    tgtVal = tgtParam.AsValueString() ?? tgtParam.AsString() ?? "";
                }

                bool isDiff = srcVal != tgtVal;
                if (isDiff) infoDiff = true;

                infoAsset.DiffDetails.Add(new ParameterDiffItem
                {
                    ParamName = srcParam.Definition.Name,
                    SourceValue = srcVal,
                    TargetValue = tgtVal,
                    IsDifferent = isDiff
                });
            }
            infoAsset.Comparison = infoDiff ? AssetComparison.Mismatch : AssetComparison.Identical;
            assets.Add(infoAsset);

            // 7. Family Symbol Parameters 比對
            var symbolSyncAsset = new TransferAsset
            {
                Type = AssetType.ProjectInfoAndSymbol,
                ElementId = ElementId.InvalidElementId,
                UniqueId = "sync_family_symbols",
                Name = "同步所有同名族群型別參數 (Family Symbols Sync)",
                IsSelected = false
            };

            var sourceSymbols = new FilteredElementCollector(_sourceDoc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            var targetSymbolsDict = new FilteredElementCollector(_targetDoc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToDictionary(x => $"{x.Category?.Name}_{x.FamilyName}_{x.Name}", x => x);

            bool symbolDiff = false;
            foreach (var srcSym in sourceSymbols)
            {
                string key = $"{srcSym.Category?.Name}_{srcSym.FamilyName}_{srcSym.Name}";
                if (targetSymbolsDict.TryGetValue(key, out var tgtSym))
                {
                    foreach (Parameter srcParam in srcSym.Parameters)
                    {
                        if (srcParam.IsReadOnly || !srcParam.HasValue) continue;

                        Parameter tgtParam = tgtSym.LookupParameter(srcParam.Definition.Name);
                        string srcVal = srcParam.AsValueString() ?? srcParam.AsString() ?? "";
                        string tgtVal = "";
                        if (tgtParam != null && tgtParam.HasValue)
                        {
                            tgtVal = tgtParam.AsValueString() ?? tgtParam.AsString() ?? "";
                        }

                        if (srcVal != tgtVal)
                        {
                            symbolDiff = true;
                            symbolSyncAsset.DiffDetails.Add(new ParameterDiffItem
                            {
                                ParamName = $"{srcSym.Name} - {srcParam.Definition.Name}",
                                SourceValue = srcVal,
                                TargetValue = tgtVal,
                                IsDifferent = true
                            });
                        }
                    }
                }
            }
            symbolSyncAsset.Comparison = symbolDiff ? AssetComparison.Mismatch : AssetComparison.Identical;
            assets.Add(symbolSyncAsset);

            return assets;
        }
    }
}
