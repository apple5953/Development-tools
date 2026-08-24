using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using DevelopmentTools.Modules.ParameterCompareCopy.Models;

namespace DevelopmentTools.Modules.ParameterCompareCopy.Services
{
    public class CompareService
    {
        private readonly Document _sourceDoc;
        private readonly Document _targetDoc;

        public CompareService(Document sourceDoc, Document targetDoc)
        {
            _sourceDoc = sourceDoc;
            _targetDoc = targetDoc;
        }

        public string TransferAssets(List<CompareAsset> assets)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"===== 專案參數複製執行報告 =====");
            sb.AppendLine($"執行時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"來源專案: {_sourceDoc.Title}");
            sb.AppendLine($"目標專案: {_targetDoc.Title}");
            sb.AppendLine("==================================");

            List<CompareAsset> success = new List<CompareAsset>();
            List<CompareAsset> failed = new List<CompareAsset>();

            using (TransactionGroup tg = new TransactionGroup(_targetDoc, "跨專案專案資訊與參數比對移轉"))
            {
                tg.Start();

                // 1. 專案參數轉移
                var paramAssets = assets.Where(x => x.Type == CompareAssetType.ProjectParameter).ToList();
                if (paramAssets.Any())
                {
                    TransferProjectParameters(paramAssets, success, failed);
                }

                // 2. 視圖樣板轉移
                var vtAssets = assets.Where(x => x.Type == CompareAssetType.ViewTemplate).ToList();
                if (vtAssets.Any())
                {
                    TransferViewTemplates(vtAssets, success, failed);
                }

                // 3. 明細表轉移
                var scheduleAssets = assets.Where(x => x.Type == CompareAssetType.Schedule).ToList();
                if (scheduleAssets.Any())
                {
                    TransferSchedules(scheduleAssets, success, failed);
                }

                // 4. 線型設定移轉
                var lineStyleAssets = assets.Where(x => x.Type == CompareAssetType.LineStyle).ToList();
                if (lineStyleAssets.Any())
                {
                    TransferLineStyles(lineStyleAssets, success, failed);
                }

                // 5. 材質設定移轉
                var materialAssets = assets.Where(x => x.Type == CompareAssetType.Material).ToList();
                if (materialAssets.Any())
                {
                    TransferMaterials(materialAssets, success, failed);
                }

                // 6. 符號設定與各種元件型別參數移轉
                var typeAssets = assets.Where(x => x.Type == CompareAssetType.SymbolSetting || x.Type == CompareAssetType.ElementParameter).ToList();
                if (typeAssets.Any())
                {
                    TransferElementTypesAndParameters(typeAssets, success, failed);
                }

                tg.Assimilate();
            }

            sb.AppendLine();
            sb.AppendLine("【SUCCESS】");
            if (success.Any())
            {
                foreach (var a in success)
                {
                    sb.AppendLine($"[✅ 成功] ({a.Type}) {a.Name}");
                }
            }
            else
            {
                sb.AppendLine("(無)");
            }

            sb.AppendLine();
            sb.AppendLine("【FAILED】");
            if (failed.Any())
            {
                foreach (var a in failed)
                {
                    sb.AppendLine($"[❌ 失敗] ({a.Type}) {a.Name} - {a.StatusMessage}");
                }
            }
            else
            {
                sb.AppendLine("(無)");
            }

            return sb.ToString();
        }

        private void TransferProjectParameters(List<CompareAsset> paramAssets, List<CompareAsset> success, List<CompareAsset> failed)
        {
            using (Transaction t = new Transaction(_targetDoc, "複製專案參數"))
            {
                t.Start();
                try
                {
                    BindingMap sourceMap = _sourceDoc.ParameterBindings;
                    BindingMap targetMap = _targetDoc.ParameterBindings;

                    var sharedParamElements = new FilteredElementCollector(_sourceDoc)
                        .OfClass(typeof(SharedParameterElement))
                        .Cast<SharedParameterElement>()
                        .ToDictionary(x => x.GuidValue, x => x);

                    DefinitionBindingMapIterator it = sourceMap.ForwardIterator();
                    while (it.MoveNext())
                    {
                        Definition def = it.Key;
                        Binding binding = it.Current as Binding;

                        var asset = paramAssets.FirstOrDefault(a => a.Name == def.Name);
                        if (asset == null) continue;

                        try
                        {
                            if (targetMap.Contains(def))
                            {
                                targetMap.Remove(def);
                            }

                            if (def is ExternalDefinition extDef)
                            {
                                if (sharedParamElements.TryGetValue(extDef.GUID, out var spe))
                                {
                                    ElementTransformUtils.CopyElements(_sourceDoc, new List<ElementId> { spe.Id }, _targetDoc, Transform.Identity, new CopyPasteOptions());
                                }
                            }

                            CategorySet catSet = _targetDoc.Application.Create.NewCategorySet();
                            CategorySet srcCatSet = (binding is InstanceBinding instB) ? instB.Categories : ((TypeBinding)binding).Categories;
                            foreach (Category cat in srcCatSet)
                            {
                                Category targetCat = _targetDoc.Settings.Categories.get_Item(cat.BuiltInCategory);
                                if (targetCat != null) catSet.Insert(targetCat);
                            }

                            Binding targetBinding = (binding is InstanceBinding)
                                ? (Binding)_targetDoc.Application.Create.NewInstanceBinding(catSet)
                                : (Binding)_targetDoc.Application.Create.NewTypeBinding(catSet);

                            object groupVal = def.ParameterGroup;
                            SafeInsert(targetMap, def, targetBinding, groupVal);
                            success.Add(asset);
                        }
                        catch (Exception ex)
                        {
                            asset.StatusMessage = $"專案參數綁定失敗: {ex.Message}";
                            failed.Add(asset);
                        }
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    foreach (var a in paramAssets)
                    {
                        a.StatusMessage = $"專案參數批次交易失敗: {ex.Message}";
                        failed.Add(a);
                    }
                }
            }
        }

        private void TransferViewTemplates(List<CompareAsset> vtAssets, List<CompareAsset> success, List<CompareAsset> failed)
        {
            List<ElementId> idsToCopy = vtAssets.Select(x => x.ElementId).ToList();
            CopyPasteOptions options = new CopyPasteOptions();
            options.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());

            try
            {
                using (Transaction t = new Transaction(_targetDoc, "複製視圖樣板"))
                {
                    t.Start();
                    ElementTransformUtils.CopyElements(_sourceDoc, idsToCopy, _targetDoc, Transform.Identity, options);
                    t.Commit();
                }
                success.AddRange(vtAssets);
            }
            catch (Exception ex)
            {
                foreach (var a in vtAssets)
                {
                    a.StatusMessage = $"複製樣板失敗: {ex.Message}";
                    failed.Add(a);
                }
            }
        }

        private void TransferSchedules(List<CompareAsset> scheduleAssets, List<CompareAsset> success, List<CompareAsset> failed)
        {
            List<ElementId> idsToCopy = scheduleAssets.Select(x => x.ElementId).ToList();
            CopyPasteOptions options = new CopyPasteOptions();
            options.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());

            try
            {
                using (Transaction t = new Transaction(_targetDoc, "複製明細表"))
                {
                    t.Start();
                    ElementTransformUtils.CopyElements(_sourceDoc, idsToCopy, _targetDoc, Transform.Identity, options);
                    t.Commit();
                }
                success.AddRange(scheduleAssets);
            }
            catch (Exception ex)
            {
                foreach (var a in scheduleAssets)
                {
                    a.StatusMessage = $"複製明細表失敗: {ex.Message}";
                    failed.Add(a);
                }
            }
        }

        private void TransferLineStyles(List<CompareAsset> lineStyleAssets, List<CompareAsset> success, List<CompareAsset> failed)
        {
            Category srcLinesCategory = _sourceDoc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category tgtLinesCategory = _targetDoc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

            if (srcLinesCategory == null || tgtLinesCategory == null)
            {
                foreach (var a in lineStyleAssets)
                {
                    a.StatusMessage = "找不到線型根類別";
                    failed.Add(a);
                }
                return;
            }

            using (Transaction t = new Transaction(_targetDoc, "移轉與更新線型"))
            {
                t.Start();
                try
                {
                    var tgtSubCats = tgtLinesCategory.SubCategories.Cast<Category>().ToDictionary(c => c.Name, c => c);

                    foreach (var asset in lineStyleAssets)
                    {
                        try
                        {
                            Category srcSubCat = srcLinesCategory.SubCategories.Cast<Category>().FirstOrDefault(c => c.Id == asset.ElementId || c.Name == asset.Name);
                            if (srcSubCat == null)
                            {
                                asset.StatusMessage = "找不到來源線型";
                                failed.Add(asset);
                                continue;
                            }

                            Category tgtSubCat = null;
                            if (tgtSubCats.TryGetValue(srcSubCat.Name, out var found))
                            {
                                tgtSubCat = found;
                            }
                            else
                            {
                                tgtSubCat = _targetDoc.Settings.Categories.NewSubcategory(tgtLinesCategory, srcSubCat.Name);
                                tgtSubCats[srcSubCat.Name] = tgtSubCat;
                            }

                            if (tgtSubCat != null)
                            {
                                int? srcWeight = srcSubCat.GetLineWeight(GraphicsStyleType.Projection);
                                if (srcWeight.HasValue)
                                {
                                    tgtSubCat.SetLineWeight(srcWeight.Value, GraphicsStyleType.Projection);
                                }
                                tgtSubCat.LineColor = srcSubCat.LineColor;

                                ElementId srcPatternId = srcSubCat.GetLinePatternId(GraphicsStyleType.Projection);
                                if (srcPatternId != ElementId.InvalidElementId)
                                {
                                    LinePatternElement srcPattern = _sourceDoc.GetElement(srcPatternId) as LinePatternElement;
                                    if (srcPattern != null)
                                    {
                                        LinePatternElement tgtPattern = new FilteredElementCollector(_targetDoc)
                                            .OfClass(typeof(LinePatternElement))
                                            .Cast<LinePatternElement>()
                                            .FirstOrDefault(p => p.Name == srcPattern.Name);

                                        if (tgtPattern == null)
                                        {
                                            CopyPasteOptions cpOpts = new CopyPasteOptions();
                                            cpOpts.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());
                                            var copiedIds = ElementTransformUtils.CopyElements(_sourceDoc, new List<ElementId> { srcPatternId }, _targetDoc, Transform.Identity, cpOpts);
                                            if (copiedIds.Any())
                                            {
                                                tgtPattern = _targetDoc.GetElement(copiedIds.First()) as LinePatternElement;
                                            }
                                        }

                                        if (tgtPattern != null)
                                        {
                                            tgtSubCat.SetLinePatternId(tgtPattern.Id, GraphicsStyleType.Projection);
                                        }
                                    }
                                }
                                success.Add(asset);
                            }
                        }
                        catch (Exception ex)
                        {
                            asset.StatusMessage = $"處理失敗: {ex.Message}";
                            failed.Add(asset);
                        }
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    foreach (var a in lineStyleAssets)
                    {
                        a.StatusMessage = $"線型交易出錯: {ex.Message}";
                        failed.Add(a);
                    }
                }
            }
        }

        private void TransferMaterials(List<CompareAsset> materialAssets, List<CompareAsset> success, List<CompareAsset> failed)
        {
            using (Transaction t = new Transaction(_targetDoc, "複製與更新材質"))
            {
                t.Start();
                try
                {
                    var tgtMaterials = new FilteredElementCollector(_targetDoc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .ToDictionary(m => m.Name, m => m);

                    List<ElementId> newMatIdsToCopy = new List<ElementId>();
                    List<CompareAsset> newMatAssets = new List<CompareAsset>();

                    foreach (var asset in materialAssets)
                    {
                        Material srcMat = _sourceDoc.GetElement(asset.ElementId) as Material;
                        if (srcMat == null)
                        {
                            asset.StatusMessage = "找不到來源材質";
                            failed.Add(asset);
                            continue;
                        }

                        if (tgtMaterials.TryGetValue(srcMat.Name, out var tgtMat))
                        {
                            try
                            {
                                tgtMat.Color = srcMat.Color;
                                tgtMat.Transparency = srcMat.Transparency;
                                tgtMat.Shininess = srcMat.Shininess;
                                success.Add(asset);
                            }
                            catch (Exception ex)
                            {
                                asset.StatusMessage = $"更新材質欄位失敗: {ex.Message}";
                                failed.Add(asset);
                            }
                        }
                        else
                        {
                            newMatIdsToCopy.Add(srcMat.Id);
                            newMatAssets.Add(asset);
                        }
                    }

                    if (newMatIdsToCopy.Any())
                    {
                        CopyPasteOptions cpOpts = new CopyPasteOptions();
                        cpOpts.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());
                        try
                        {
                            ElementTransformUtils.CopyElements(_sourceDoc, newMatIdsToCopy, _targetDoc, Transform.Identity, cpOpts);
                            success.AddRange(newMatAssets);
                        }
                        catch (Exception ex)
                        {
                            foreach (var a in newMatAssets)
                            {
                                a.StatusMessage = $"複製材質失敗: {ex.Message}";
                                failed.Add(a);
                            }
                        }
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    foreach (var a in materialAssets)
                    {
                        if (!success.Contains(a) && !failed.Contains(a))
                        {
                            a.StatusMessage = $"材質交易失敗: {ex.Message}";
                            failed.Add(a);
                        }
                    }
                }
            }
        }

        private void TransferElementTypesAndParameters(List<CompareAsset> typeAssets, List<CompareAsset> success, List<CompareAsset> failed)
        {
            using (Transaction t = new Transaction(_targetDoc, "複製與更新族群型別與元件型別參數"))
            {
                t.Start();
                try
                {
                    var tgtTypesDict = new FilteredElementCollector(_targetDoc)
                        .WhereElementIsElementType()
                        .Cast<ElementType>()
                        .GroupBy(x => $"{x.Category?.Name}_{x.Name}")
                        .ToDictionary(g => g.Key, g => g.First());

                    List<ElementId> idsToCopy = new List<ElementId>();
                    List<CompareAsset> assetsToCopy = new List<CompareAsset>();

                    foreach (var asset in typeAssets)
                    {
                        ElementType srcType = _sourceDoc.GetElement(asset.ElementId) as ElementType;
                        if (srcType == null)
                        {
                            asset.StatusMessage = "找不到來源元件型別";
                            failed.Add(asset);
                            continue;
                        }

                        string key = $"{srcType.Category?.Name}_{srcType.Name}";
                        if (tgtTypesDict.TryGetValue(key, out var tgtType))
                        {
                            try
                            {
                                foreach (Parameter srcP in srcType.Parameters)
                                {
                                    if (srcP.IsReadOnly || !srcP.HasValue) continue;
                                    Parameter tgtP = tgtType.LookupParameter(srcP.Definition.Name);
                                    if (tgtP != null && !tgtP.IsReadOnly)
                                    {
                                        CopyParameterValue(srcP, tgtP);
                                    }
                                }
                                success.Add(asset);
                            }
                            catch (Exception ex)
                            {
                                asset.StatusMessage = $"更新參數失敗: {ex.Message}";
                                failed.Add(asset);
                            }
                        }
                        else
                        {
                            idsToCopy.Add(srcType.Id);
                            assetsToCopy.Add(asset);
                        }
                    }

                    if (idsToCopy.Any())
                    {
                        CopyPasteOptions cpOpts = new CopyPasteOptions();
                        cpOpts.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());
                        try
                        {
                            ElementTransformUtils.CopyElements(_sourceDoc, idsToCopy, _targetDoc, Transform.Identity, cpOpts);
                            success.AddRange(assetsToCopy);
                        }
                        catch (Exception ex)
                        {
                            foreach (var a in assetsToCopy)
                            {
                                a.StatusMessage = $"複製元件型別失敗: {ex.Message}";
                                failed.Add(a);
                            }
                        }
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    foreach (var a in typeAssets)
                    {
                        if (!success.Contains(a) && !failed.Contains(a))
                        {
                            a.StatusMessage = $"元件型別交易失敗: {ex.Message}";
                            failed.Add(a);
                        }
                    }
                }
            }
        }

        private void CopyParameterValue(Parameter srcParam, Parameter tgtParam)
        {
            switch (srcParam.StorageType)
            {
                case StorageType.Double:
                    tgtParam.Set(srcParam.AsDouble());
                    break;
                case StorageType.Integer:
                    tgtParam.Set(srcParam.AsInteger());
                    break;
                case StorageType.String:
                    tgtParam.Set(srcParam.AsString());
                    break;
                case StorageType.ElementId:
                    tgtParam.Set(srcParam.AsElementId());
                    break;
            }
        }

        private static bool SafeInsert(BindingMap bindingMap, Definition def, Binding binding, object groupObj)
        {
            if (groupObj == null) groupObj = BuiltInParameterGroup.PG_DATA;

            var insertMethod = bindingMap.GetType().GetMethod("Insert", new Type[] { typeof(Definition), typeof(Binding), groupObj.GetType() });
            if (insertMethod != null)
            {
                return (bool)insertMethod.Invoke(bindingMap, new object[] { def, binding, groupObj });
            }

            var oldInsert = bindingMap.GetType().GetMethod("Insert", new Type[] { typeof(Definition), typeof(Binding), typeof(BuiltInParameterGroup) });
            if (oldInsert != null)
            {
                var oldGroup = (groupObj is BuiltInParameterGroup) ? (BuiltInParameterGroup)groupObj : BuiltInParameterGroup.PG_DATA;
                return (bool)oldInsert.Invoke(bindingMap, new object[] { def, binding, oldGroup });
            }

            throw new InvalidOperationException("找不到適合的 BindingMap.Insert 方法");
        }
    }

    public class DuplicateTypeNamesHandler : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }
}
