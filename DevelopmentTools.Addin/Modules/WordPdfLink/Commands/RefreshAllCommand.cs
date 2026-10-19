using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DT_WordPdfLink.Models;
using DT_WordPdfLink.Services;
using DT_WordPdfLink.Storage;
using DT_WordPdfLink.Utils;

namespace DT_WordPdfLink.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RefreshAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Failed;
            Document doc = uidoc.Document;

            WordLinkRegistry registry = WordLinkRegistryService.LoadRegistry(doc);
            if (registry.Links.Count == 0)
            {
                TaskDialog.Show("Word PDF Link", "專案中沒有任何 Word 連結記錄。");
                return Result.Succeeded;
            }

            int totalChecked = registry.Links.Count;
            int updatedCount = 0;
            int missingCount = 0;
            int failCount = 0;

            string projectKey = string.IsNullOrEmpty(doc.PathName) ? PathUtils.GetPathHash(doc.Title) : PathUtils.GetPathHash(doc.PathName);

            // Access via copy to prevent concurrent modification issues
            var recordsToProcess = new System.Collections.Generic.List<WordLinkRecord>(registry.Links);

            // Track processed (Word Path + Sheet Unique ID) to avoid redundant exports in the same run
            var processedExports = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (WordLinkRecord record in recordsToProcess)
            {
                ImageType imageType = null;
                try
                {
                    imageType = doc.GetElement(record.ImageTypeUniqueId) as ImageType;
                }
                catch
                {
                    // ImageType was deleted by user
                }

                if (imageType == null)
                {
                    continue;
                }

                string wordPath = PathUtils.ResolveLocalPath(record.SourceWordPath);
                if (!File.Exists(wordPath))
                {
                    missingCount++;
                    continue;
                }

                // Force refresh Windows FileInfo cache for cloud drive files
                string currentWriteUtc = DateTime.UtcNow.ToString("O");
                long currentLength = 0;
                try
                {
                    FileInfo fi = new FileInfo(wordPath);
                    fi.Refresh();
                    currentWriteUtc = fi.LastWriteTimeUtc.ToString("O");
                    currentLength = fi.Length;
                }
                catch {}

                // Resolve sheet and parameters
                ViewSheet sheet = null;
                if (!string.IsNullOrEmpty(record.SheetUniqueId))
                {
                    sheet = doc.GetElement(record.SheetUniqueId) as ViewSheet;
                }
                
                string sheetUniqueId = sheet != null ? sheet.UniqueId : record.SheetUniqueId;
                string rawSheetName = sheet != null ? sheet.Name : record.LastSheetName;
                string sheetName = SheetNameUtils.ParseDisplayName(rawSheetName);
                string sheetNo = sheet != null ? sheet.SheetNumber : record.LastSheetNumber;
                string sheetRevision = sheet != null ? GetSheetRevision(sheet) : record.LastSheetRevision;
                string sheetDate = sheet != null ? GetSheetDate(sheet) : record.LastSheetDate;

                string cachePdfPath = PathUtils.ResolveLocalPath(record.CachePdfPath);
                string tempPdfPath = CacheManager.GetTempPdfPath(wordPath, projectKey, sheetUniqueId);
                
                // Export and re-sync unconditionally for FORMA / ACC docs cloud synchronization
                string exportKey = string.Format("{0}_{1}", wordPath, sheetUniqueId);
                bool exportSucceeded = true;

                if (!processedExports.Contains(exportKey))
                {
                    try
                    {
                        WordPdfExporter.ExportWordToPdf(wordPath, tempPdfPath, sheetName, sheetNo, sheetRevision, sheetDate);
                        CacheManager.AtomicReplace(tempPdfPath, cachePdfPath);
                        processedExports.Add(exportKey);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(string.Format("Failed to convert Word path: {0}", wordPath), ex);
                        failCount++;
                        exportSucceeded = false;
                    }
                }

                if (!exportSucceeded)
                {
                    continue;
                }

                // Update Revit inside Transaction
                using (Transaction trans = new Transaction(doc, string.Format("更新連結 {0}", Path.GetFileName(wordPath))))
                {
                    trans.Start();
                    try
                    {
                        ImageTypeOptions options = new ImageTypeOptions(cachePdfPath, false, ImageTypeSource.Link);
                        options.PageNumber = record.PageNumber;
                        options.Resolution = 300;
                        imageType.ReloadFrom(options);

                        // Scale all instances of this type in the doc
                        // Get orientation from the cached PDF or estimate. We can get double dimensions from original record or cache
                        // Here we use standard A4 scaling logic
                        double targetWidth = record.PageWidthMm;
                        bool isStandardA4Portrait = Math.Abs(record.PageWidthMm - 210) < 5 && Math.Abs(record.PageHeightMm - 297) < 5;
                        bool isStandardA4Landscape = Math.Abs(record.PageWidthMm - 297) < 5 && Math.Abs(record.PageHeightMm - 210) < 5;
                        if (isStandardA4Portrait) targetWidth = 210;
                        else if (isStandardA4Landscape) targetWidth = 297;

                        FilteredElementCollector instCollector = new FilteredElementCollector(doc);
                        foreach (Element elem in instCollector.OfClass(typeof(ImageInstance)).ToElements())
                        {
                            ImageInstance inst = elem as ImageInstance;
                            if (inst != null && inst.GetTypeId() == imageType.Id)
                            {
                                RevitPdfLinkService.ScaleInstance(inst, targetWidth);
                            }
                        }

                        // Update registry record
                        record.LastWordWriteUtc = currentWriteUtc;
                        record.LastSyncUtc = DateTime.UtcNow.ToString("O");
                        record.WordFileLength = currentLength;
                        record.LastSheetName = sheetName;
                        record.LastSheetNumber = sheetNo;
                        record.LastSheetRevision = sheetRevision;
                        record.LastSheetDate = sheetDate;

                        WordLinkRegistryService.AddOrUpdateRecord(doc, record);

                        trans.Commit();
                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        Logger.Error("Failed to update Revit element during RefreshAll", ex);
                        failCount++;
                    }
                }
            }

            TaskDialog td = new TaskDialog("Word PDF Link 同步完成");
            td.MainInstruction = "FORMA / ACC 雲端 Word 同步完成";
            td.MainContent = string.Format("共處理項目：{0}\n成功重新匯出與更新：{1}\n來源遺失：{2}\n失敗：{3}", 
                totalChecked, updatedCount, missingCount, failCount);
            td.Show();

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
