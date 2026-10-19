using System;
using System.IO;
using System.Reflection;
using DT_WordPdfLink.Utils;
using Word = Microsoft.Office.Interop.Word;

namespace DT_WordPdfLink.Services
{
    public class WordDocInfo
    {
        public int PageCount { get; set; }
        public double PageWidthMm { get; set; }
        public double PageHeightMm { get; set; }
        public string Orientation { get; set; }
    }

    public static class WordPdfExporter
    {
        public static WordDocInfo ExportWordToPdf(
            string wordPath, 
            string pdfPath, 
            string sheetName = "", 
            string sheetNo = "", 
            string sheetRevision = "", 
            string sheetDate = "")
        {
            Logger.Info(string.Format("Starting Word PDF Export. Word: {0}, PDF: {1}", wordPath, pdfPath));
            
            if (!File.Exists(wordPath))
            {
                throw new FileNotFoundException("找不到 Word 原始文件。", wordPath);
            }

            Word.Application wordApp = null;
            Word.Documents documents = null;
            Word.Document doc = null;
            Word.PageSetup pageSetup = null;

            try
            {
                wordApp = new Word.Application();
                wordApp.Visible = false;
                wordApp.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
                
                // Disable background save to ensure synchronous operations
                try
                {
                    wordApp.Options.BackgroundSave = false;
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to set BackgroundSave option", ex);
                }

                documents = wordApp.Documents;
                
                // Open read-only (we can still modify in-memory copy before PDF export)
                doc = documents.Open(
                    FileName: wordPath,
                    ConfirmConversions: false,
                    ReadOnly: true,
                    AddToRecentFiles: false,
                    Visible: false
                );

                // Inject Revit metadata into placeholders and custom properties
                InjectMetadata(doc, sheetName, sheetNo, sheetRevision, sheetDate);

                WordDocInfo info = new WordDocInfo();
                
                // Get page count (wdStatisticPages = 2)
                try
                {
                    info.PageCount = doc.ComputeStatistics(Word.WdStatistic.wdStatisticPages);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to get page count, default to 1", ex);
                    info.PageCount = 1;
                }

                // Get page setup
                pageSetup = doc.PageSetup;
                double widthPoints = pageSetup.PageWidth;
                double heightPoints = pageSetup.PageHeight;
                Word.WdOrientation orient = pageSetup.Orientation;

                info.PageWidthMm = widthPoints * 25.4 / 72.0;
                info.PageHeightMm = heightPoints * 25.4 / 72.0;
                info.Orientation = (orient == Word.WdOrientation.wdOrientLandscape) ? "Landscape" : "Portrait";

                Logger.Info(string.Format("Word info: Pages={0}, Width={1:F2}mm, Height={2:F2}mm, Orientation={3}", 
                    info.PageCount, info.PageWidthMm, info.PageHeightMm, info.Orientation));

                // Export to PDF
                doc.ExportAsFixedFormat(
                    pdfPath,
                    Word.WdExportFormat.wdExportFormatPDF,
                    false,
                    Word.WdExportOptimizeFor.wdExportOptimizeForPrint,
                    Word.WdExportRange.wdExportAllDocument,
                    1,
                    1,
                    Word.WdExportItem.wdExportDocumentContent,
                    true,
                    true,
                    Word.WdExportCreateBookmarks.wdExportCreateNoBookmarks,
                    true,
                    true,
                    false
                );

                Logger.Info("Word PDF export completed successfully.");
                return info;
            }
            finally
            {
                if (doc != null)
                {
                    try
                    {
                        doc.Close(false); // wdDoNotSaveChanges = 0
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Error closing Word document", ex);
                    }
                    ComUtils.ReleaseComObject(doc);
                }

                if (wordApp != null)
                {
                    try
                    {
                        wordApp.Quit();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Error quitting Word application", ex);
                    }
                    ComUtils.ReleaseComObject(wordApp);
                }

                ComUtils.ReleaseComObject(pageSetup);
                ComUtils.ReleaseComObject(documents);
            }
        }

        public static void ExportSinglePagePdf(string wordPath, string singlePagePdfPath, int pageNumber)
        {
            Word.Application wordApp = null;
            Word.Documents documents = null;
            Word.Document doc = null;

            try
            {
                wordApp = new Word.Application();
                wordApp.Visible = false;
                wordApp.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;

                documents = wordApp.Documents;
                doc = documents.Open(FileName: wordPath, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);

                // Export page range (From = pageNumber, To = pageNumber, Range = wdExportFromTo = 4)
                doc.ExportAsFixedFormat(
                    singlePagePdfPath,
                    Word.WdExportFormat.wdExportFormatPDF,
                    false,
                    Word.WdExportOptimizeFor.wdExportOptimizeForPrint,
                    Word.WdExportRange.wdExportFromTo,
                    pageNumber,
                    pageNumber
                );
            }
            finally
            {
                if (doc != null) { try { doc.Close(false); } catch {} ComUtils.ReleaseComObject(doc); }
                if (wordApp != null) { try { wordApp.Quit(); } catch {} ComUtils.ReleaseComObject(wordApp); }
                ComUtils.ReleaseComObject(documents);
            }
        }

        private static void InjectMetadata(Word.Document doc, string sheetName, string sheetNo, string sheetRevision, string sheetDate)
        {
            if (string.IsNullOrEmpty(sheetName) && string.IsNullOrEmpty(sheetNo) && string.IsNullOrEmpty(sheetRevision) && string.IsNullOrEmpty(sheetDate))
            {
                return;
            }

            Logger.Info(string.Format("Injecting Revit metadata: Name={0}, No={1}, Rev={2}, Date={3}", sheetName, sheetNo, sheetRevision, sheetDate));

            // 1. Set Custom Document Properties
            SetCustomProperty(doc, "Revit_DrawingName", sheetName);
            SetCustomProperty(doc, "Revit_DrawingNo", sheetNo);
            SetCustomProperty(doc, "Revit_Revision", sheetRevision);
            SetCustomProperty(doc, "Revit_Date", sheetDate);

            // 2. Perform search-and-replace for text placeholders
            SearchAndReplace(doc, "{{圖紙名稱}}", sheetName);
            SearchAndReplace(doc, "[圖紙名稱]", sheetName);
            SearchAndReplace(doc, "{{圖紙編號}}", sheetNo);
            SearchAndReplace(doc, "[圖紙編號]", sheetNo);
            SearchAndReplace(doc, "{{版次}}", sheetRevision);
            SearchAndReplace(doc, "[版次]", sheetRevision);
            SearchAndReplace(doc, "{{日期}}", sheetDate);
            SearchAndReplace(doc, "[日期]", sheetDate);

            // 3. Force update all fields (including DOCPROPERTY fields)
            try
            {
                foreach (Word.Range range in doc.StoryRanges)
                {
                    Word.Range r = range;
                    while (r != null)
                    {
                        r.Fields.Update();
                        r = r.NextStoryRange;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to update Word fields", ex);
            }
        }

        private static void SetCustomProperty(Word.Document doc, string name, string value)
        {
            if (value == null) value = string.Empty;
            try
            {
                object customProps = doc.CustomDocumentProperties;
                Type typeCustomProps = customProps.GetType();
                object prop = null;
                try
                {
                    prop = typeCustomProps.InvokeMember("Item",
                        BindingFlags.GetProperty, null, customProps, new object[] { name });
                }
                catch
                {
                    // Property doesn't exist, create it (4 = msoPropertyTypeString)
                    typeCustomProps.InvokeMember("Add",
                        BindingFlags.InvokeMethod, null, customProps,
                        new object[] { name, false, 4, value });
                    return;
                }

                if (prop != null)
                {
                    prop.GetType().InvokeMember("Value",
                        BindingFlags.SetProperty, null, prop, new object[] { value });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(string.Format("Failed to set Word custom property: {0}", name), ex);
            }
        }

        private static void SearchAndReplace(Word.Document doc, string findText, string replaceText)
        {
            if (string.IsNullOrEmpty(findText)) return;
            if (replaceText == null) replaceText = string.Empty;

            try
            {
                foreach (Word.Range range in doc.StoryRanges)
                {
                    Word.Range r = range;
                    while (r != null)
                    {
                        Word.Find find = r.Find;
                        find.ClearFormatting();
                        find.Replacement.ClearFormatting();
                        find.Text = findText;
                        find.Replacement.Text = replaceText;
                        find.Forward = true;
                        find.Wrap = Word.WdFindWrap.wdFindContinue;
                        find.Format = false;
                        find.MatchCase = false;
                        find.MatchWholeWord = false;

                        object replaceAll = Word.WdReplace.wdReplaceAll;
                        find.Execute(Replace: ref replaceAll);
                        
                        r = r.NextStoryRange;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(string.Format("Failed to perform search-and-replace for findText: {0}", findText), ex);
            }
        }
    }
}
