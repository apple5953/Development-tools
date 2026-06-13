using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using DevelopmentTools.Modules.SheetTransfer.Models;

namespace DevelopmentTools.Modules.SheetTransfer.Services
{
    public class SheetTransferService
    {
        private readonly Document _sourceDoc;
        private readonly Document _targetDoc;

        public SheetTransferService(Document sourceDoc, Document targetDoc)
        {
            _sourceDoc = sourceDoc;
            _targetDoc = targetDoc;
        }

        public string TransferAssets(List<TransferAsset> assets)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== 轉移報告 ===");

            List<TransferAsset> success = new List<TransferAsset>();
            List<TransferAsset> failed = new List<TransferAsset>();
            List<TransferAsset> warning = new List<TransferAsset>();

            using (TransactionGroup tg = new TransactionGroup(_targetDoc, "跨專案專案資訊與資源轉移"))
            {
                tg.Start();

                // 1. 專案參數轉移 (優先執行，使後續視圖/明細表能讀取到對應參數)
                var paramAssets = assets.Where(x => x.Type == AssetType.ProjectParameter).ToList();
                if (paramAssets.Any())
                {
                    TransferProjectParameters(paramAssets, success, failed);
                }

                // 2. 視圖樣板轉移
                var vtAssets = assets.Where(x => x.Type == AssetType.ViewTemplate).ToList();
                if (vtAssets.Any())
                {
                    TransferViewTemplates(vtAssets, success, failed);
                }

                // 3. 專案資訊與同名族群型別參數同步
                var infoAssets = assets.Where(x => x.Type == AssetType.ProjectInfoAndSymbol).ToList();
                if (infoAssets.Any())
                {
                    SyncProjectInfoAndSymbols(infoAssets, success, failed);
                }

                // 4. 繪圖視圖 (DraftingView), 明細表 (Schedule) 與圖例 (Legend) 的轉移
                var copyableNonSheetAssets = assets.Where(x => x.Type == AssetType.DraftingView || x.Type == AssetType.Schedule || x.Type == AssetType.Legend).ToList();
                if (copyableNonSheetAssets.Any())
                {
                    List<ElementId> idsToCopy = copyableNonSheetAssets.Select(x => x.ElementId).ToList();
                    CopyPasteOptions options = new CopyPasteOptions();
                    options.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());

                    try
                    {
                        using (Transaction t = new Transaction(_targetDoc, "複製繪圖視圖、圖例與明細表"))
                        {
                            t.Start();
                            ElementTransformUtils.CopyElements(_sourceDoc, idsToCopy, _targetDoc, Transform.Identity, options);
                            t.Commit();
                        }
                        success.AddRange(copyableNonSheetAssets);
                    }
                    catch (Exception ex)
                    {
                        foreach (var a in copyableNonSheetAssets)
                        {
                            a.StatusMessage = $"複製失敗: {ex.Message}";
                            failed.Add(a);
                        }
                    }
                }

                // 5. 圖紙 (Sheet) 的建立與視窗擺放
                var sheetAssets = assets.Where(x => x.Type == AssetType.Sheet).ToList();
                if (sheetAssets.Any())
                {
                    TransferSheets(sheetAssets, success, failed, warning);
                }

                tg.Assimilate();
            }

            // 輸出報告
            sb.AppendLine();
            sb.AppendLine("【SUCCESS】");
            foreach (var s in success) sb.AppendLine($"- [{s.Type}] {s.Number} {s.Name} {(string.IsNullOrEmpty(s.StatusMessage) ? "" : ": " + s.StatusMessage)}");
            if (!success.Any()) sb.AppendLine("無");

            sb.AppendLine();
            sb.AppendLine("【WARNING】");
            foreach (var w in warning) sb.AppendLine($"- [{w.Type}] {w.Number} {w.Name} : {w.StatusMessage}");
            if (!warning.Any()) sb.AppendLine("無");

            sb.AppendLine();
            sb.AppendLine("【FAILED】");
            foreach (var f in failed) sb.AppendLine($"- [{f.Type}] {f.Number} {f.Name} : {f.StatusMessage}");
            if (!failed.Any()) sb.AppendLine("無");

            return sb.ToString();
        }

        private void TransferProjectParameters(List<TransferAsset> paramAssets, List<TransferAsset> success, List<TransferAsset> failed)
        {
            BindingMap sourceBindings = _sourceDoc.ParameterBindings;

            foreach (var asset in paramAssets)
            {
                try
                {
                    Definition sourceDef = null;
                    Binding sourceBinding = null;

                    DefinitionBindingMapIterator it = sourceBindings.ForwardIterator();
                    while (it.MoveNext())
                    {
                        if (asset.UniqueId.StartsWith("internal_param_"))
                        {
                            string name = asset.UniqueId.Substring("internal_param_".Length);
                            if (it.Key.Name == name)
                            {
                                sourceDef = it.Key;
                                sourceBinding = it.Current as Binding;
                                break;
                            }
                        }
                        else
                        {
                            if (it.Key is ExternalDefinition extDef && extDef.GUID.ToString() == asset.UniqueId)
                            {
                                sourceDef = it.Key;
                                sourceBinding = it.Current as Binding;
                                break;
                            }
                        }
                    }

                    if (sourceDef == null || sourceBinding == null)
                    {
                        asset.StatusMessage = "找不到來源參數定義";
                        failed.Add(asset);
                        continue;
                    }

                    // 檢查目標專案是否已存在
                    BindingMap targetBindings = _targetDoc.ParameterBindings;
                    bool exists = false;
                    DefinitionBindingMapIterator targetIt = targetBindings.ForwardIterator();
                    while (targetIt.MoveNext())
                    {
                        if (targetIt.Key.Name == sourceDef.Name)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (exists)
                    {
                        asset.StatusMessage = "參數已存在於目標專案";
                        asset.Status = TransferStatus.Skipped;
                        success.Add(asset);
                        continue;
                    }

                    CategorySet categories = null;
                    bool isInstance = true;

                    if (sourceBinding is InstanceBinding instBinding)
                    {
                        categories = instBinding.Categories;
                        isInstance = true;
                    }
                    else if (sourceBinding is TypeBinding typeBinding)
                    {
                        categories = typeBinding.Categories;
                        isInstance = false;
                    }

                    CategorySet targetCategories = _targetDoc.Application.Create.NewCategorySet();
                    foreach (Category cat in categories)
                    {
                        Category targetCat = _targetDoc.Settings.Categories.get_Item(cat.BuiltInCategory);
                        if (targetCat != null)
                        {
                            targetCategories.Insert(targetCat);
                        }
                    }

                    using (Transaction t = new Transaction(_targetDoc, $"建立專案參數: {sourceDef.Name}"))
                    {
                        t.Start();

                        if (sourceDef is ExternalDefinition extSourceDef)
                        {
                            string oldPath = _targetDoc.Application.SharedParametersFilename;
                            try
                            {
                                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TempTransferSharedParams.txt");
                                using (System.IO.StreamWriter sw = new System.IO.StreamWriter(tempFile, false, System.Text.Encoding.Unicode))
                                {
                                    sw.WriteLine("# This is a Revit shared parameter file.");
                                    sw.WriteLine("# Do not edit manually.");
                                    sw.WriteLine("*META\tVERSION\tMINVERSION");
                                    sw.WriteLine("META\t2\t1");
                                    sw.WriteLine("*GROUP\tID\tNAME");
                                    sw.WriteLine("GROUP\t1\tTransfer Parameters");
                                    sw.WriteLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE");

                                    string datatype = GetDefinitionDataType(extSourceDef);
                                    sw.WriteLine($"PARAM\t{extSourceDef.GUID}\t{extSourceDef.Name}\t{datatype}\t\t1\t1\t\t1");
                                }

                                _targetDoc.Application.SharedParametersFilename = tempFile;
                                DefinitionFile defFile = _targetDoc.Application.OpenSharedParameterFile();
                                DefinitionGroup group = defFile.Groups.get_Item("Transfer Parameters");
                                Definition defToBind = group.Definitions.get_Item(extSourceDef.Name);

                                Binding binding = isInstance
                                    ? (Binding)_targetDoc.Application.Create.NewInstanceBinding(targetCategories)
                                    : (Binding)_targetDoc.Application.Create.NewTypeBinding(targetCategories);

                                targetBindings.Insert(defToBind, binding, sourceDef.ParameterGroup);
                            }
                            finally
                            {
                                _targetDoc.Application.SharedParametersFilename = oldPath;
                            }
                        }
                        else
                        {
                            string oldPath = _targetDoc.Application.SharedParametersFilename;
                            try
                            {
                                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TempTransferSharedParams.txt");
                                Guid tempGuid = Guid.NewGuid();
                                using (System.IO.StreamWriter sw = new System.IO.StreamWriter(tempFile, false, System.Text.Encoding.Unicode))
                                {
                                    sw.WriteLine("# This is a Revit shared parameter file.");
                                    sw.WriteLine("# Do not edit manually.");
                                    sw.WriteLine("*META\tVERSION\tMINVERSION");
                                    sw.WriteLine("META\t2\t1");
                                    sw.WriteLine("*GROUP\tID\tNAME");
                                    sw.WriteLine("GROUP\t1\tTransfer Parameters");
                                    sw.WriteLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE");

                                    string datatype = GetDefinitionDataType(sourceDef);
                                    sw.WriteLine($"PARAM\t{tempGuid}\t{sourceDef.Name}\t{datatype}\t\t1\t1\t\t1");
                                }

                                _targetDoc.Application.SharedParametersFilename = tempFile;
                                DefinitionFile defFile = _targetDoc.Application.OpenSharedParameterFile();
                                DefinitionGroup group = defFile.Groups.get_Item("Transfer Parameters");
                                Definition defToBind = group.Definitions.get_Item(sourceDef.Name);

                                Binding binding = isInstance
                                    ? (Binding)_targetDoc.Application.Create.NewInstanceBinding(targetCategories)
                                    : (Binding)_targetDoc.Application.Create.NewTypeBinding(targetCategories);

                                targetBindings.Insert(defToBind, binding, sourceDef.ParameterGroup);
                                asset.StatusMessage = "非共享普通專案參數，已自動提升為共享專案參數建立";
                            }
                            finally
                            {
                                _targetDoc.Application.SharedParametersFilename = oldPath;
                            }
                        }

                        t.Commit();
                    }

                    success.Add(asset);
                }
                catch (Exception ex)
                {
                    asset.StatusMessage = $"建立失敗: {ex.Message}";
                    failed.Add(asset);
                }
            }
        }

        private void TransferViewTemplates(List<TransferAsset> vtAssets, List<TransferAsset> success, List<TransferAsset> failed)
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
                    a.StatusMessage = $"複製失敗: {ex.Message}";
                    failed.Add(a);
                }
            }
        }

        private void SyncProjectInfoAndSymbols(List<TransferAsset> infoAssets, List<TransferAsset> success, List<TransferAsset> failed)
        {
            foreach (var asset in infoAssets)
            {
                try
                {
                    if (asset.UniqueId == "sync_family_symbols")
                    {
                        int syncCount = 0;
                        using (Transaction t = new Transaction(_targetDoc, "同步族群型別參數"))
                        {
                            t.Start();

                            var sourceSymbols = new FilteredElementCollector(_sourceDoc)
                                .OfClass(typeof(FamilySymbol))
                                .Cast<FamilySymbol>()
                                .ToList();

                            var targetSymbolsDict = new FilteredElementCollector(_targetDoc)
                                .OfClass(typeof(FamilySymbol))
                                .Cast<FamilySymbol>()
                                .ToDictionary(x => $"{x.Category?.Name}_{x.FamilyName}_{x.Name}", x => x);

                            foreach (var srcSym in sourceSymbols)
                            {
                                string key = $"{srcSym.Category?.Name}_{srcSym.FamilyName}_{srcSym.Name}";
                                if (targetSymbolsDict.TryGetValue(key, out var tgtSym))
                                {
                                    bool modified = false;
                                    foreach (Parameter srcParam in srcSym.Parameters)
                                    {
                                        if (srcParam.IsReadOnly || !srcParam.HasValue) continue;

                                        Parameter tgtParam = tgtSym.LookupParameter(srcParam.Definition.Name);
                                        if (tgtParam != null && !tgtParam.IsReadOnly)
                                        {
                                            if (CopyParameterValue(srcParam, tgtParam))
                                            {
                                                modified = true;
                                            }
                                        }
                                    }
                                    if (modified) syncCount++;
                                }
                            }

                            t.Commit();
                        }
                        asset.StatusMessage = $"同步完成，共更新了 {syncCount} 個同名型別的參數值";
                        success.Add(asset);
                    }
                    else
                    {
                        using (Transaction t = new Transaction(_targetDoc, "同步專案資訊"))
                        {
                            t.Start();

                            ProjectInfo srcInfo = _sourceDoc.ProjectInformation;
                            ProjectInfo tgtInfo = _targetDoc.ProjectInformation;

                            foreach (Parameter srcParam in srcInfo.Parameters)
                            {
                                if (srcParam.IsReadOnly || !srcParam.HasValue) continue;

                                Parameter tgtParam = tgtInfo.LookupParameter(srcParam.Definition.Name);
                                if (tgtParam != null && !tgtParam.IsReadOnly)
                                {
                                    CopyParameterValue(srcParam, tgtParam);
                                }
                            }

                            t.Commit();
                        }
                        asset.StatusMessage = "專案基本資訊參數同步成功";
                        success.Add(asset);
                    }
                }
                catch (Exception ex)
                {
                    asset.StatusMessage = $"同步失敗: {ex.Message}";
                    failed.Add(asset);
                }
            }
        }

        private void TransferSheets(List<TransferAsset> sheetAssets, List<TransferAsset> success, List<TransferAsset> failed, List<TransferAsset> warning)
        {
            var targetTitleBlocks = new FilteredElementCollector(_targetDoc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .ToDictionary(x => $"{x.FamilyName}_{x.Name}", x => x);

            FamilySymbol defaultTitleBlock = targetTitleBlocks.Values.FirstOrDefault();

            foreach (var sheetAsset in sheetAssets)
            {
                try
                {
                    ViewSheet sourceSheet = _sourceDoc.GetElement(sheetAsset.ElementId) as ViewSheet;
                    if (sourceSheet == null) continue;

                    using (TransactionGroup tg = new TransactionGroup(_targetDoc, $"建立與配置圖紙: {sourceSheet.SheetNumber}"))
                    {
                        tg.Start();

                        ElementId targetTitleBlockId = ElementId.InvalidElementId;

                        FamilyInstance sourceTB = new FilteredElementCollector(_sourceDoc, sourceSheet.Id)
                            .OfCategory(BuiltInCategory.OST_TitleBlocks)
                            .OfClass(typeof(FamilyInstance))
                            .Cast<FamilyInstance>()
                            .FirstOrDefault();

                        if (sourceTB != null && sourceTB.Symbol != null)
                        {
                            string tbKey = $"{sourceTB.Symbol.FamilyName}_{sourceTB.Symbol.Name}";
                            if (targetTitleBlocks.TryGetValue(tbKey, out var foundTB))
                            {
                                targetTitleBlockId = foundTB.Id;
                            }
                            else
                            {
                                using (Transaction t = new Transaction(_targetDoc, "複製圖框家族"))
                                {
                                    t.Start();
                                    CopyPasteOptions options = new CopyPasteOptions();
                                    options.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());
                                    var copiedIds = ElementTransformUtils.CopyElements(_sourceDoc, new List<ElementId> { sourceTB.Symbol.Id }, _targetDoc, Transform.Identity, options);
                                    t.Commit();

                                    if (copiedIds.Any())
                                    {
                                        var newTBSym = _targetDoc.GetElement(copiedIds.First()) as FamilySymbol;
                                        if (newTBSym != null)
                                        {
                                            targetTitleBlockId = newTBSym.Id;
                                            targetTitleBlocks[tbKey] = newTBSym;
                                        }
                                    }
                                }
                            }
                        }

                        if (targetTitleBlockId == ElementId.InvalidElementId && defaultTitleBlock != null)
                        {
                            targetTitleBlockId = defaultTitleBlock.Id;
                        }

                        ViewSheet newSheet = null;
                        using (Transaction t = new Transaction(_targetDoc, "建立圖紙"))
                        {
                            t.Start();
                            newSheet = ViewSheet.Create(_targetDoc, targetTitleBlockId);
                            t.Commit();
                        }

                        string newNumber = ResolveSheetNumber(sourceSheet.SheetNumber);
                        using (Transaction t = new Transaction(_targetDoc, "設定圖紙名稱與編號"))
                        {
                            t.Start();
                            newSheet.SheetNumber = newNumber;
                            newSheet.Name = sourceSheet.Name;
                            t.Commit();
                        }

                        using (Transaction t = new Transaction(_targetDoc, "同步圖紙參數值"))
                        {
                            t.Start();
                            foreach (Parameter srcParam in sourceSheet.Parameters)
                            {
                                if (srcParam.IsReadOnly || !srcParam.HasValue) continue;

                                if (srcParam.Definition.Name == "Sheet Number" || srcParam.Definition.Name == "Sheet Name") continue;

                                Parameter tgtParam = newSheet.LookupParameter(srcParam.Definition.Name);
                                if (tgtParam != null && !tgtParam.IsReadOnly)
                                {
                                    CopyParameterValue(srcParam, tgtParam);
                                }
                            }
                            t.Commit();
                        }

                        var sourceViewports = sourceSheet.GetAllViewports();
                        StringBuilder vpLog = new StringBuilder();

                        var targetViews = new FilteredElementCollector(_targetDoc)
                            .OfClass(typeof(View))
                            .Cast<View>()
                            .ToDictionary(v => $"{v.ViewType}_{v.Name}", v => v);

                        var targetSchedules = new FilteredElementCollector(_targetDoc)
                            .OfClass(typeof(ViewSchedule))
                            .Cast<ViewSchedule>()
                            .ToDictionary(s => s.Name, s => s);

                        foreach (var vpId in sourceViewports)
                        {
                            Viewport sourceVp = _sourceDoc.GetElement(vpId) as Viewport;
                            if (sourceVp == null) continue;

                            View sourceView = _sourceDoc.GetElement(sourceVp.ViewId) as View;
                            if (sourceView == null) continue;

                            View matchedTargetView = null;
                            if (sourceView is ViewSchedule sourceSchedule)
                            {
                                if (targetSchedules.TryGetValue(sourceSchedule.Name, out var tgtSch))
                                {
                                    matchedTargetView = tgtSch;
                                }
                            }
                            else
                            {
                                string viewKey = $"{sourceView.ViewType}_{sourceView.Name}";
                                if (targetViews.TryGetValue(viewKey, out var tgtView))
                                {
                                    matchedTargetView = tgtView;
                                }
                            }

                            if (matchedTargetView != null)
                            {
                                using (Transaction t = new Transaction(_targetDoc, $"擺放視窗: {matchedTargetView.Name}"))
                                {
                                    t.Start();
                                    try
                                    {
                                        bool canPlace = true;
                                        if (matchedTargetView.ViewType != ViewType.Legend && !(matchedTargetView is ViewSchedule))
                                        {
                                            bool isAlreadyPlaced = new FilteredElementCollector(_targetDoc)
                                                .OfClass(typeof(Viewport))
                                                .Cast<Viewport>()
                                                .Any(vp => vp.ViewId == matchedTargetView.Id);
                                            if (isAlreadyPlaced)
                                            {
                                                canPlace = false;
                                                vpLog.Append($" [⚠️ 視圖 {matchedTargetView.Name} 已被其他圖紙佔用]");
                                            }
                                        }

                                        if (canPlace)
                                        {
                                            if (matchedTargetView is ViewSchedule scheduleToPlace)
                                            {
                                                ScheduleSheetInstance.Create(_targetDoc, newSheet.Id, scheduleToPlace.Id, sourceVp.GetBoxCenter());
                                            }
                                            else
                                            {
                                                Viewport newVp = Viewport.Create(_targetDoc, newSheet.Id, matchedTargetView.Id, sourceVp.GetBoxCenter());
                                                ElementId tgtVpTypeId = GetTargetViewportType(sourceVp.GetTypeId());
                                                if (tgtVpTypeId != ElementId.InvalidElementId)
                                                {
                                                    try { newVp.ChangeTypeId(tgtVpTypeId); } catch { }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        vpLog.Append($" [❌ 擺放 {matchedTargetView.Name} 失敗: {ex.Message}]");
                                    }
                                    t.Commit();
                                }
                            }
                            else
                            {
                                vpLog.Append($" [⚠️ 未能在目標專案中找到同名視圖: {sourceView.Name}]");
                            }
                        }

                        tg.Assimilate();

                        if (newNumber != sourceSheet.SheetNumber || vpLog.Length > 0)
                        {
                            string msg = "";
                            if (newNumber != sourceSheet.SheetNumber) msg += $"編號衝突，已改為 {newNumber}; ";
                            if (vpLog.Length > 0) msg += $"視窗擺放警告: {vpLog}";
                            sheetAsset.StatusMessage = msg.TrimEnd(';', ' ');
                            warning.Add(sheetAsset);
                        }
                        else
                        {
                            success.Add(sheetAsset);
                        }
                    }
                }
                catch (Exception ex)
                {
                    sheetAsset.StatusMessage = $"建立失敗: {ex.Message}";
                    failed.Add(sheetAsset);
                }
            }
        }

        private ElementId GetTargetViewportType(ElementId srcVpTypeId)
        {
            ElementType srcVpType = _sourceDoc.GetElement(srcVpTypeId) as ElementType;
            if (srcVpType == null) return ElementId.InvalidElementId;

            ElementType tgtVpType = new FilteredElementCollector(_targetDoc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .FirstOrDefault(x => x.Name == srcVpType.Name);

            return tgtVpType?.Id ?? ElementId.InvalidElementId;
        }

        private bool CopyParameterValue(Parameter srcParam, Parameter tgtParam)
        {
            bool changed = false;
            switch (srcParam.StorageType)
            {
                case StorageType.Double:
                    if (Math.Abs(tgtParam.AsDouble() - srcParam.AsDouble()) > 1e-6)
                    {
                        tgtParam.Set(srcParam.AsDouble());
                        changed = true;
                    }
                    break;
                case StorageType.Integer:
                    if (tgtParam.AsInteger() != srcParam.AsInteger())
                    {
                        tgtParam.Set(srcParam.AsInteger());
                        changed = true;
                    }
                    break;
                case StorageType.String:
                    if (tgtParam.AsString() != srcParam.AsString())
                    {
                        tgtParam.Set(srcParam.AsString());
                        changed = true;
                    }
                    break;
                case StorageType.ElementId:
                    ElementId srcValId = srcParam.AsElementId();
                    if (srcValId != ElementId.InvalidElementId)
                    {
                        Element srcElem = _sourceDoc.GetElement(srcValId);
                        if (srcElem != null)
                        {
                            Element tgtElem = FindElementByName(srcElem.Name, srcElem.GetType());
                            if (tgtElem != null && tgtParam.AsElementId() != tgtElem.Id)
                            {
                                tgtParam.Set(tgtElem.Id);
                                changed = true;
                            }
                        }
                    }
                    else if (tgtParam.AsElementId() != ElementId.InvalidElementId)
                    {
                        tgtParam.Set(ElementId.InvalidElementId);
                        changed = true;
                    }
                    break;
            }
            return changed;
        }

        private Element FindElementByName(string name, Type type)
        {
            return new FilteredElementCollector(_targetDoc)
                .OfClass(type)
                .FirstOrDefault(x => x.Name == name);
        }

        private string GetDefinitionDataType(Definition def)
        {
            var getSpecTypeIdMethod = def.GetType().GetMethod("GetSpecTypeId");
            if (getSpecTypeIdMethod != null)
            {
                ForgeTypeId forgeTypeId = getSpecTypeIdMethod.Invoke(def, null) as ForgeTypeId;
                if (forgeTypeId != null)
                {
                    return GetDataTypeString(forgeTypeId);
                }
            }

            var parameterTypeProp = def.GetType().GetProperty("ParameterType");
            if (parameterTypeProp != null)
            {
                object val = parameterTypeProp.GetValue(def);
                if (val != null)
                {
                    string typeStr = val.ToString().ToUpper();
                    if (typeStr == "LENGTH") return "LENGTH";
                    if (typeStr == "ANGLE") return "ANGLE";
                    if (typeStr == "INTEGER") return "INTEGER";
                    if (typeStr == "YESNO") return "YESNO";
                    if (typeStr == "AREA") return "AREA";
                    if (typeStr == "VOLUME") return "VOLUME";
                }
            }

            return "TEXT";
        }

        private string GetDataTypeString(ForgeTypeId type)
        {
            if (type == SpecTypeId.Length) return "LENGTH";
            if (type == SpecTypeId.Angle) return "ANGLE";
            if (type == SpecTypeId.Int.Integer) return "INTEGER";
            if (type == SpecTypeId.Boolean.YesNo) return "YESNO";
            if (type == SpecTypeId.Area) return "AREA";
            if (type == SpecTypeId.Volume) return "VOLUME";
            return "TEXT";
        }

        private string ResolveSheetNumber(string originalNumber)
        {
            string number = originalNumber;
            int counter = 1;

            while (true)
            {
                var existing = new FilteredElementCollector(_targetDoc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(s => s.SheetNumber == number);

                if (existing == null) return number;

                if (counter == 1)
                    number = $"{originalNumber}_Copy";
                else
                    number = $"{originalNumber}_Copy_{counter}";

                counter++;
            }
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
