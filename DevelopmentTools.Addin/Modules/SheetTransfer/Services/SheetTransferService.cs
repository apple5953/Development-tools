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

            using (TransactionGroup tg = new TransactionGroup(_targetDoc, "跨專案圖紙轉移"))
            {
                tg.Start();

                // 1. 轉移視圖、圖例、明細表 (Drafting View, Legend, Schedule)
                var nonSheetAssets = assets.Where(x => x.Type != AssetType.Sheet).ToList();
                if (nonSheetAssets.Any())
                {
                    List<ElementId> idsToCopy = nonSheetAssets.Select(x => x.ElementId).ToList();
                    CopyPasteOptions options = new CopyPasteOptions();
                    options.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());

                    try
                    {
                        using (Transaction t = new Transaction(_targetDoc, "複製視圖與明細表"))
                        {
                            t.Start();
                            ElementTransformUtils.CopyElements(_sourceDoc, idsToCopy, _targetDoc, Transform.Identity, options);
                            t.Commit();
                        }
                        success.AddRange(nonSheetAssets);
                    }
                    catch (Exception ex)
                    {
                        foreach (var a in nonSheetAssets)
                        {
                            a.StatusMessage = $"複製失敗: {ex.Message}";
                            failed.Add(a);
                        }
                    }
                }

                // 2. 轉移 Sheets (因 Revit API 無法直接 CopyElements ViewSheet，必須手動重建)
                var sheetAssets = assets.Where(x => x.Type == AssetType.Sheet).ToList();
                
                // 嘗試取得目標專案的任意 TitleBlock FamilySymbol
                FamilySymbol defaultTitleBlock = new FilteredElementCollector(_targetDoc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();

                foreach (var sheetAsset in sheetAssets)
                {
                    try
                    {
                        using (Transaction t = new Transaction(_targetDoc, $"建立圖紙: {sheetAsset.Number}"))
                        {
                            t.Start();
                            
                            ViewSheet sourceSheet = _sourceDoc.GetElement(sheetAsset.ElementId) as ViewSheet;
                            if (sourceSheet == null) continue;

                            // TODO: TitleBlockResolver 應該去找同名的 TitleBlock，這裡為了穩定先用目標專案預設的
                            ElementId titleBlockId = defaultTitleBlock?.Id ?? ElementId.InvalidElementId;
                            
                            ViewSheet newSheet = ViewSheet.Create(_targetDoc, titleBlockId);
                            
                            // 設定編號與名稱 (處理衝突)
                            string newNumber = ResolveSheetNumber(sourceSheet.SheetNumber);
                            newSheet.SheetNumber = newNumber;
                            newSheet.Name = sourceSheet.Name;

                            // TODO: 複製參數 (如 Drawn By, Checked By 等)
                            
                            // 複製 Viewports
                            var viewports = sourceSheet.GetAllViewports();
                            foreach (var vpId in viewports)
                            {
                                Viewport vp = _sourceDoc.GetElement(vpId) as Viewport;
                                if (vp == null) continue;

                                // 這裡需要一個映射表 (Mapping Table)，找到新目標專案中對應的 View
                                // 這在 V1 版先略過，避免因為依賴 View 沒複製導致出錯，或者依賴使用者有勾選對應的 View
                                // 由於是雛型，先記警告
                            }

                            t.Commit();
                            
                            if (newNumber != sourceSheet.SheetNumber)
                            {
                                sheetAsset.StatusMessage = $"編號衝突，已重新命名為 {newNumber}";
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

                tg.Assimilate();
            }

            // 輸出報告
            sb.AppendLine();
            sb.AppendLine("【SUCCESS】");
            foreach (var s in success) sb.AppendLine($"- [{s.Type}] {s.Number} {s.Name}");
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
