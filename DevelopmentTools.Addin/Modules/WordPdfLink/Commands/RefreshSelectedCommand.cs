using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DT_WordPdfLink.Models;
using DT_WordPdfLink.Services;
using DT_WordPdfLink.Utils;

namespace DT_WordPdfLink.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RefreshSelectedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Failed;
            Document doc = uidoc.Document;

            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds.Count == 0)
            {
                TaskDialog.Show("Word PDF Link", "請先選取圖紙上的 PDF 連結項目。");
                return Result.Failed;
            }

            List<ImageInstance> selectedInstances = new List<ImageInstance>();
            foreach (ElementId id in selectedIds)
            {
                ImageInstance inst = doc.GetElement(id) as ImageInstance;
                if (inst != null)
                {
                    selectedInstances.Add(inst);
                }
            }

            if (selectedInstances.Count == 0)
            {
                TaskDialog.Show("Word PDF Link", "選取的項目並不是由 Word PDF Link 建立。");
                return Result.Failed;
            }

            // Map each unique ImageType to its instances
            Dictionary<string, List<ImageInstance>> typeIdToInstances = new Dictionary<string, List<ImageInstance>>();
            Dictionary<string, ImageType> typeIdToType = new Dictionary<string, ImageType>();

            foreach (ImageInstance inst in selectedInstances)
            {
                ElementId typeId = inst.GetTypeId();
                ImageType type = doc.GetElement(typeId) as ImageType;
                if (type != null)
                {
                    string uniqueId = type.UniqueId;
                    if (!typeIdToInstances.ContainsKey(uniqueId))
                    {
                        typeIdToInstances[uniqueId] = new List<ImageInstance>();
                        typeIdToType[uniqueId] = type;
                    }
                    typeIdToInstances[uniqueId].Add(inst);
                }
            }

            int processedCount = 0;
            int unchangedCount = 0;
            int failCount = 0;

            string projectKey = string.IsNullOrEmpty(doc.PathName) ? PathUtils.GetPathHash(doc.Title) : PathUtils.GetPathHash(doc.PathName);

            foreach (KeyValuePair<string, List<ImageInstance>> entry in typeIdToInstances)
            {
                string typeUniqueId = entry.Key;
                ImageType imageType = typeIdToType[typeUniqueId];
                
                // Lookup record
                WordLinkRecord record = WordLinkRegistryService.FindRecordByImageTypeUniqueId(doc, typeUniqueId);
                if (record == null)
                {
                    TaskDialog.Show("Word PDF Link", "選取的項目並不是由 Word PDF Link 建立的連結 PDF。");
                    failCount++;
                    continue;
                }

                string wordPath = PathUtils.ResolveLocalPath(record.SourceWordPath);
                if (!File.Exists(wordPath))
                {
                    TaskDialog td = new TaskDialog("Word PDF Link");
                    td.MainInstruction = "找不到 Word 原始文件：";
                    td.MainContent = string.Format("{0}\n\n目前 Revit 中已載入的 PDF 將繼續保留。\n請使用「重新指定來源」修復連結。", wordPath);
                    td.Show();
                    failCount++;
                    continue;
                }

                // Resolve sheet and parameters
                ImageInstance firstInst = entry.Value[0];
                ViewSheet sheet = doc.GetElement(firstInst.OwnerViewId) as ViewSheet;
                string sheetUniqueId = sheet != null ? sheet.UniqueId : record.SheetUniqueId;
                
                // ParseDisplayName: 若圖紙名稱格式為「進版封面-(A棟 B2FL 放樣圖)」，
                // Word 只顯示括弧內的文字「A棟 B2FL 放樣圖」。
                string rawSheetName = sheet != null ? sheet.Name : record.LastSheetName;
                string sheetName = SheetNameUtils.ParseDisplayName(rawSheetName);
                string sheetNo = sheet != null ? sheet.SheetNumber : record.LastSheetNumber;
                string sheetRevision = sheet != null ? GetSheetRevision(sheet) : record.LastSheetRevision;
                string sheetDate = sheet != null ? GetSheetDate(sheet) : record.LastSheetDate;

                // Check timestamps with 1-second tolerance
                FileInfo fi = new FileInfo(wordPath);
                string currentWriteUtc = fi.LastWriteTimeUtc.ToString("O");
                long currentLength = fi.Length;

                string cachePdfPath = PathUtils.ResolveLocalPath(record.CachePdfPath);
                bool isCacheMissing = !File.Exists(cachePdfPath);

                DateTime recordWriteTime, currentWriteTime;
                bool isTimeDifferent = !DateTime.TryParse(record.LastWordWriteUtc, out recordWriteTime) ||
                                       !DateTime.TryParse(currentWriteUtc, out currentWriteTime) ||
                                       Math.Abs((currentWriteTime - recordWriteTime).TotalSeconds) > 1.0;

                bool isWordModified = isTimeDifferent || record.WordFileLength != currentLength;
                bool isSheetModified = record.LastSheetName != sheetName || 
                                       record.LastSheetNumber != sheetNo || 
                                       record.LastSheetRevision != sheetRevision || 
                                       record.LastSheetDate != sheetDate;

                Logger.Info(string.Format("RefreshSelected check for [{0}]: WordMod={1}, SheetMod={2}, CacheMissing={3}", 
                    Path.GetFileName(wordPath), isWordModified, isSheetModified, isCacheMissing));

                // Selected refresh always forces refresh for explicit user-selected items
                // to handle cloud drive sync delay or manual force updates cleanly

                // Re-export PDF (With metadata injection)
                string tempPdfPath = CacheManager.GetTempPdfPath(wordPath, projectKey, sheetUniqueId);
                WordDocInfo info = null;
                try
                {
                    info = WordPdfExporter.ExportWordToPdf(wordPath, tempPdfPath, sheetName, sheetNo, sheetRevision, sheetDate);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to convert Word during selected refresh", ex);
                    TaskDialog.Show("Word PDF Link", string.Format("無法讀取 Word 文件。\n請確認 Word 文件已完成儲存，再重新執行更新。\n\n錯誤: {0}", ex.Message));
                    failCount++;
                    continue;
                }

                // Validate
                string valError;
                if (!PdfValidationService.ValidatePdf(tempPdfPath, doc, out valError))
                {
                    Logger.Error(string.Format("PDF Validation failed: {0}", valError), null);
                    TaskDialog.Show("Word PDF Link", string.Format("PDF 驗證失敗: {0}", valError));
                    try { File.Delete(tempPdfPath); } catch {}
                    failCount++;
                    continue;
                }

                // Replace Cache PDF
                try
                {
                    CacheManager.AtomicReplace(tempPdfPath, cachePdfPath);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to replace cache during selected refresh", ex);
                    TaskDialog.Show("Word PDF Link", string.Format("更新快取 PDF 失敗: {0}", ex.Message));
                    failCount++;
                    continue;
                }

                // Revit update inside Transaction
                using (Transaction trans = new Transaction(doc, "更新選取 Word 連結"))
                {
                    trans.Start();
                    try
                    {
                        ImageTypeOptions options = new ImageTypeOptions(cachePdfPath, false, ImageTypeSource.Link);
                        options.PageNumber = record.PageNumber;
                        options.Resolution = 300;
                        imageType.ReloadFrom(options);

                        // Determine target width
                        double targetWidth = info.PageWidthMm;
                        bool isStandardA4Portrait = Math.Abs(info.PageWidthMm - 210) < 5 && Math.Abs(info.PageHeightMm - 297) < 5;
                        bool isStandardA4Landscape = Math.Abs(info.PageWidthMm - 297) < 5 && Math.Abs(info.PageHeightMm - 210) < 5;
                        if (isStandardA4Portrait) targetWidth = 210;
                        else if (isStandardA4Landscape) targetWidth = 297;

                        // Re-scale all instances of this type
                        foreach (ImageInstance inst in entry.Value)
                        {
                            RevitPdfLinkService.ScaleInstance(inst, targetWidth);
                        }

                        // Update registry record
                        record.Orientation = info.Orientation;
                        record.PageWidthMm = info.PageWidthMm;
                        record.PageHeightMm = info.PageHeightMm;
                        record.LastWordWriteUtc = currentWriteUtc;
                        record.LastSyncUtc = DateTime.UtcNow.ToString("O");
                        record.WordFileLength = currentLength;
                        record.LastSheetName = sheetName;
                        record.LastSheetNumber = sheetNo;
                        record.LastSheetRevision = sheetRevision;
                        record.LastSheetDate = sheetDate;

                        WordLinkRegistryService.AddOrUpdateRecord(doc, record);

                        trans.Commit();
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        Logger.Error("Failed to commit Revit reload", ex);
                        TaskDialog.Show("Word PDF Link", string.Format("更新 Revit 項目失敗: {0}", ex.Message));
                        failCount++;
                    }
                }
            }

            if (processedCount > 0)
            {
                TaskDialog.Show("Word PDF Link", string.Format("更新完成。\n成功更新: {0} 筆\n無變更: {1} 筆\n失敗/略過: {2} 筆", processedCount, unchangedCount, failCount));
            }
            else if (unchangedCount > 0 && failCount == 0)
            {
                TaskDialog.Show("Word PDF Link", "此 Word 文件與圖紙欄位皆是最新狀態，無需更新。");
            }
            else if (failCount > 0 && processedCount == 0)
            {
                TaskDialog.Show("Word PDF Link", "更新失敗。請參閱事件記錄。");
            }

            return Result.Succeeded;
        }

        private static string GetParameterValue(Element elem, BuiltInParameter bip)
        {
            Parameter p = elem.get_Parameter(bip);
            return (p != null && p.HasValue) ? p.AsString() : string.Empty;
        }

        private static string GetSheetRevision(ViewSheet sheet)
        {
            string rev = GetParameterValue(sheet, BuiltInParameter.SHEET_CURRENT_REVISION);
            if (string.IsNullOrEmpty(rev))
            {
                Parameter p = sheet.LookupParameter("版次") ?? sheet.LookupParameter("Revision");
                if (p != null && p.HasValue) rev = p.AsString();
            }
            return string.IsNullOrEmpty(rev) ? "第一版" : rev;
        }

        private static string GetSheetDate(ViewSheet sheet)
        {
            string date = GetParameterValue(sheet, BuiltInParameter.SHEET_ISSUE_DATE);
            if (string.IsNullOrEmpty(date))
            {
                Parameter p = sheet.LookupParameter("日期") ?? sheet.LookupParameter("Date");
                if (p != null && p.HasValue) date = p.AsString();
            }
            return string.IsNullOrEmpty(date) ? DateTime.Now.ToString("yyyy/MM/dd") : date;
        }
    }
}
