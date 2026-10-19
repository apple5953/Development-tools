using System;
using System.IO;
using System.Windows.Forms;
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
    public class LinkWordCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null) return Result.Failed;
                Document doc = uidoc.Document;

                Autodesk.Revit.DB.View activeView = doc.ActiveView;
                ViewSheet sheet = activeView as ViewSheet;
                if (sheet == null)
                {
                    TaskDialog td = new TaskDialog("Word PDF Link");
                    td.MainInstruction = "請先開啟一張 Revit 圖紙 Sheet，再執行「連結 Word」。";
                    td.Show();
                    return Result.Failed;
                }

                // 1. Pick Word document
                string wordPath = string.Empty;
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Word Documents (*.docx)|*.docx";
                    ofd.Title = "選擇要連結的 Word 文件";
                    if (ofd.ShowDialog() != DialogResult.OK)
                    {
                        return Result.Cancelled;
                    }
                    wordPath = ofd.FileName;
                }

                wordPath = Path.GetFullPath(wordPath);
                Logger.Info(string.Format("User selected Word path: {0}", wordPath));

                // 2. Prepare Cache paths (Using sheet.UniqueId to prevent parameter conflicts between sheets)
                string sheetUniqueId = sheet.UniqueId;
                string projectKey = string.IsNullOrEmpty(doc.PathName) ? PathUtils.GetPathHash(doc.Title) : PathUtils.GetPathHash(doc.PathName);
                bool isFallback;
                string cachePdfPath = CacheManager.GetCachePdfPath(wordPath, projectKey, sheetUniqueId, out isFallback);
                string tempPdfPath = CacheManager.GetTempPdfPath(wordPath, projectKey, sheetUniqueId);

                // 3. Export Word to Temp PDF (OUTSIDE transaction, with metadata injection)
                WordDocInfo info = null;
                // ParseDisplayName: 若圖紙名稱格式為「進版封面-(A棟 B2FL 放樣圖)」，
                // Word 只顯示括弧內的文字「A棟 B2FL 放樣圖」。
                string sheetName = SheetNameUtils.ParseDisplayName(sheet.Name);
                string sheetNo = sheet.SheetNumber;
                string sheetRevision = GetSheetRevision(sheet);
                string sheetDate = GetSheetDate(sheet);

                try
                {
                    info = WordPdfExporter.ExportWordToPdf(wordPath, tempPdfPath, sheetName, sheetNo, sheetRevision, sheetDate);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to convert Word to PDF", ex);
                    TaskDialog td = new TaskDialog("Word PDF Link");
                    td.MainInstruction = "無法讀取 Word 文件。";
                    td.MainContent = string.Format("請確認 Word 文件已完成儲存，且未被其他程式獨佔開啟。\n\n詳細資訊: {0}", ex.Message);
                    td.Show();
                    return Result.Failed;
                }

                // 4. Validate Temp PDF
                string validationError;
                if (!PdfValidationService.ValidatePdf(tempPdfPath, doc, out validationError))
                {
                    Logger.Error(string.Format("PDF Validation failed: {0}", validationError), null);
                    TaskDialog.Show("Word PDF Link", string.Format("PDF 驗證失敗: {0}", validationError));
                    try { File.Delete(tempPdfPath); } catch {}
                    return Result.Failed;
                }

                // 5. Page selection UI with REAL Visual PDF Preview
                int selectedPageNumber = 1;
                if (info.PageCount > 1)
                {
                    using (DT_WordPdfLink.Forms.PageSelectionForm pageForm = new DT_WordPdfLink.Forms.PageSelectionForm(wordPath, tempPdfPath, info.PageCount))
                    {
                        if (pageForm.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            selectedPageNumber = pageForm.SelectedPageNumber;
                        }
                        else
                        {
                            try { File.Delete(tempPdfPath); } catch {}
                            return Result.Cancelled;
                        }
                    }
                }

                // 6. Paper size check
                double targetWidth = info.PageWidthMm;
                bool isStandardA4Portrait = Math.Abs(info.PageWidthMm - 210) < 5 && Math.Abs(info.PageHeightMm - 297) < 5;
                bool isStandardA4Landscape = Math.Abs(info.PageWidthMm - 297) < 5 && Math.Abs(info.PageHeightMm - 210) < 5;

                if (isStandardA4Portrait)
                {
                    targetWidth = 210;
                }
                else if (isStandardA4Landscape)
                {
                    targetWidth = 297;
                }
                else
                {
                    TaskDialog td = new TaskDialog("Word PDF Link");
                    td.MainInstruction = "此 Word 文件尺寸不是標準 A4。";
                    td.MainContent = string.Format("目前頁面尺寸: {0:F1} × {1:F1} mm\n\n是否仍然依原始尺寸插入？", info.PageWidthMm, info.PageHeightMm);
                    td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                    td.DefaultButton = TaskDialogResult.Yes;
                    if (td.Show() == TaskDialogResult.No)
                    {
                        try { File.Delete(tempPdfPath); } catch {}
                        return Result.Cancelled;
                    }
                }

                // 7. Pick point on sheet with Center fallback
                XYZ pickedPoint = null;
                try
                {
                    pickedPoint = uidoc.Selection.PickPoint("請選擇放置 PDF 的位置 (左上角)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    Logger.Info("Point picking canceled by user.");
                    try { File.Delete(tempPdfPath); } catch {}
                    return Result.Cancelled;
                }
                catch (Exception ex)
                {
                    Logger.Error("PickPoint failed, falling back to Zero/Center", ex);
                    pickedPoint = XYZ.Zero;
                    TaskDialog.Show("Word PDF Link", "無法選取點，已自動放置於圖紙原點，請手動調整位置。");
                }

                // Show cache fallback warning if applicable
                if (isFallback)
                {
                    TaskDialog td = new TaskDialog("Word PDF Link");
                    td.MainInstruction = "此 Word 文件所在位置無法建立共用 PDF 快取資料夾。";
                    td.MainContent = "目前使用本機快取模式。如果此 Revit 模型由多人共同作業，其他電腦可能無法存取此連結。";
                    td.Show();
                }

                // 8. Copy temp to cache
                try
                {
                    CacheManager.AtomicReplace(tempPdfPath, cachePdfPath);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to replace cache file", ex);
                    TaskDialog.Show("Word PDF Link", string.Format("無法替換快取 PDF: {0}", ex.Message));
                    return Result.Failed;
                }

                // 9. Run Revit Transaction
                using (Transaction trans = new Transaction(doc, "連結 Word"))
                {
                    trans.Start();
                    try
                    {
                        bool isNew;
                        ImageType imageType = RevitPdfLinkService.GetOrCreatePdfLink(doc, cachePdfPath, selectedPageNumber, out isNew);
                        
                        ImageInstance instance = RevitPdfLinkService.PlacePdfOnSheet(doc, sheet, imageType, pickedPoint, targetWidth);

                        WordLinkRecord record = new WordLinkRecord();
                        record.SourceWordPath = wordPath;
                        record.CachePdfPath = cachePdfPath;
                        record.ImageTypeUniqueId = imageType.UniqueId;
                        record.PageNumber = selectedPageNumber;
                        record.Dpi = 300;
                        record.Orientation = info.Orientation;
                        record.PageWidthMm = info.PageWidthMm;
                        record.PageHeightMm = info.PageHeightMm;
                        record.LastWordWriteUtc = File.GetLastWriteTimeUtc(wordPath).ToString("O");
                        record.LastSyncUtc = DateTime.UtcNow.ToString("O");
                        record.WordFileLength = new FileInfo(wordPath).Length;
                        record.SheetUniqueId = sheetUniqueId;
                        record.LastSheetName = sheetName;
                        record.LastSheetNumber = sheetNo;
                        record.LastSheetRevision = sheetRevision;
                        record.LastSheetDate = sheetDate;

                        WordLinkRegistryService.AddOrUpdateRecord(doc, record);

                        trans.Commit();
                        Logger.Info("Successfully completed LinkWordCommand.");
                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        Logger.Error("Error inside Revit Transaction for link command", ex);
                        TaskDialog.Show("Word PDF Link", string.Format("連結失敗: {0}", ex.Message));
                        return Result.Failed;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unhandled exception in LinkWordCommand.Execute", ex);
                TaskDialog.Show("Word PDF Link Error", string.Format("執行「連結 Word」時發生未預期的錯誤。\n\n詳細資訊: {0}\n\n請查看日誌或聯絡管理員。", ex.Message));
                return Result.Failed;
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
