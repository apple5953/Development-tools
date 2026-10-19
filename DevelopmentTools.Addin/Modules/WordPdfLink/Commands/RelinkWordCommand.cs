using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
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
    public class RelinkWordCommand : IExternalCommand
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

            ImageInstance selectedInst = null;
            foreach (ElementId id in selectedIds)
            {
                ImageInstance inst = doc.GetElement(id) as ImageInstance;
                if (inst != null)
                {
                    selectedInst = inst;
                    break;
                }
            }

            if (selectedInst == null)
            {
                TaskDialog.Show("Word PDF Link", "選取的項目並不是由 Word PDF Link 建立。");
                return Result.Failed;
            }

            ImageType imageType = doc.GetElement(selectedInst.GetTypeId()) as ImageType;
            if (imageType == null)
            {
                TaskDialog.Show("Word PDF Link", "選取的項目無效。");
                return Result.Failed;
            }

            WordLinkRecord record = WordLinkRegistryService.FindRecordByImageTypeUniqueId(doc, imageType.UniqueId);
            if (record == null)
            {
                TaskDialog.Show("Word PDF Link", "選取的 PDF 並不是由 Word PDF Link 建立。");
                return Result.Failed;
            }

            // 1. Pick new Word file
            string newWordPath = string.Empty;
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Word Documents (*.docx)|*.docx";
                ofd.Title = "選擇新的 Word 來源檔案";
                if (ofd.ShowDialog() != DialogResult.OK)
                {
                    return Result.Cancelled;
                }
                newWordPath = ofd.FileName;
            }

            newWordPath = Path.GetFullPath(newWordPath);
            Logger.Info(string.Format("Relinking from {0} to {1}", PathUtils.ResolveLocalPath(record.SourceWordPath), newWordPath));

            string projectKey = string.IsNullOrEmpty(doc.PathName) ? PathUtils.GetPathHash(doc.Title) : PathUtils.GetPathHash(doc.PathName);
            
            // Resolve sheet and parameters
            ViewSheet sheet = doc.GetElement(selectedInst.OwnerViewId) as ViewSheet;
            string sheetUniqueId = sheet != null ? sheet.UniqueId : record.SheetUniqueId;
            
            // ParseDisplayName: 若圖紙名稱格式為「進版封面-(A棟 B2FL 放樣圖)」，
            // Word 只顯示括弧內的文字「A棟 B2FL 放樣圖」。
            string rawSheetName = sheet != null ? sheet.Name : record.LastSheetName;
            string sheetName = SheetNameUtils.ParseDisplayName(rawSheetName);
            string sheetNo = sheet != null ? sheet.SheetNumber : record.LastSheetNumber;
            string sheetRevision = sheet != null ? GetSheetRevision(sheet) : record.LastSheetRevision;
            string sheetDate = sheet != null ? GetSheetDate(sheet) : record.LastSheetDate;

            bool isFallback;
            // Generate new Cache PDF path based on the new Word file and sheet ID
            string newCachePdfPath = CacheManager.GetCachePdfPath(newWordPath, projectKey, sheetUniqueId, out isFallback);
            string tempPdfPath = CacheManager.GetTempPdfPath(newWordPath, projectKey, sheetUniqueId);

            // 2. Export new Word to temp PDF (With metadata injection)
            WordDocInfo info = null;
            try
            {
                info = WordPdfExporter.ExportWordToPdf(newWordPath, tempPdfPath, sheetName, sheetNo, sheetRevision, sheetDate);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to convert new Word during relink", ex);
                TaskDialog.Show("Word PDF Link", string.Format("無法讀取 Word 文件。\n\n錯誤: {0}", ex.Message));
                return Result.Failed;
            }

            // 3. Validate
            string valError;
            if (!PdfValidationService.ValidatePdf(tempPdfPath, doc, out valError))
            {
                Logger.Error(string.Format("Relink PDF validation failed: {0}", valError), null);
                TaskDialog.Show("Word PDF Link", string.Format("PDF 驗證失敗: {0}", valError));
                try { File.Delete(tempPdfPath); } catch {}
                return Result.Failed;
            }

            if (isFallback)
            {
                TaskDialog.Show("Word PDF Link", "此 Word 文件所在位置無法建立共用 PDF 快取資料夾。\n目前使用本機快取模式。");
            }

            // 4. Replace cache PDF
            try
            {
                CacheManager.AtomicReplace(tempPdfPath, newCachePdfPath);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to write to cache path during relink", ex);
                TaskDialog.Show("Word PDF Link", string.Format("寫入快取 PDF 失敗: {0}", ex.Message));
                return Result.Failed;
            }

            // 5. Update Revit inside Transaction
            using (Transaction trans = new Transaction(doc, "重新指定 Word 來源"))
            {
                trans.Start();
                try
                {
                    // Relink image type to new path (Preserve the page number of the original link)
                    ImageTypeOptions options = new ImageTypeOptions(newCachePdfPath, false, ImageTypeSource.Link);
                    options.PageNumber = record.PageNumber;
                    options.Resolution = 300;
                    
                    imageType.ReloadFrom(options);

                    // Determine dimensions
                    double targetWidth = info.PageWidthMm;
                    bool isStandardA4Portrait = Math.Abs(info.PageWidthMm - 210) < 5 && Math.Abs(info.PageHeightMm - 297) < 5;
                    bool isStandardA4Landscape = Math.Abs(info.PageWidthMm - 297) < 5 && Math.Abs(info.PageHeightMm - 210) < 5;
                    if (isStandardA4Portrait) targetWidth = 210;
                    else if (isStandardA4Landscape) targetWidth = 297;

                    // Re-scale all instances in the document that use this type
                    FilteredElementCollector instCollector = new FilteredElementCollector(doc);
                    foreach (Element elem in instCollector.OfClass(typeof(ImageInstance)).ToElements())
                    {
                        ImageInstance inst = elem as ImageInstance;
                        if (inst != null && inst.GetTypeId() == imageType.Id)
                        {
                            RevitPdfLinkService.ScaleInstance(inst, targetWidth);
                        }
                    }

                    // Update Registry Record
                    WordLinkRegistry registry = WordLinkRegistryService.LoadRegistry(doc);
                    
                    // Remove old record
                    WordLinkRecord oldRec = null;
                    foreach (WordLinkRecord r in registry.Links)
                    {
                        if (r.ImageTypeUniqueId == imageType.UniqueId)
                        {
                            oldRec = r;
                            break;
                        }
                    }
                    if (oldRec != null)
                    {
                        registry.Links.Remove(oldRec);
                    }

                    // Add new record
                    WordLinkRecord newRec = new WordLinkRecord();
                    newRec.SourceWordPath = newWordPath;
                    newRec.CachePdfPath = newCachePdfPath;
                    newRec.ImageTypeUniqueId = imageType.UniqueId;
                    newRec.PageNumber = record.PageNumber;
                    newRec.Dpi = 300;
                    newRec.Orientation = info.Orientation;
                    newRec.PageWidthMm = info.PageWidthMm;
                    newRec.PageHeightMm = info.PageHeightMm;
                    newRec.LastWordWriteUtc = File.GetLastWriteTimeUtc(newWordPath).ToString("O");
                    newRec.LastSyncUtc = DateTime.UtcNow.ToString("O");
                    newRec.WordFileLength = new FileInfo(newWordPath).Length;
                    newRec.SheetUniqueId = sheetUniqueId;
                    newRec.LastSheetName = sheetName;
                    newRec.LastSheetNumber = sheetNo;
                    newRec.LastSheetRevision = sheetRevision;
                    newRec.LastSheetDate = sheetDate;

                    registry.Links.Add(newRec);
                    WordLinkRegistryService.SaveRegistry(doc, registry);

                    trans.Commit();
                    
                    TaskDialog.Show("Word PDF Link", "重新指定來源成功。");
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    Logger.Error("Failed to update Revit elements during relink", ex);
                    TaskDialog.Show("Word PDF Link", string.Format("更新 Revit 專案失敗: {0}", ex.Message));
                    return Result.Failed;
                }
            }
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
