using System;
using System.IO;
using Autodesk.Revit.DB;
using DT_WordPdfLink.Utils;

namespace DT_WordPdfLink.Services
{
    public static class PdfValidationService
    {
        public static bool ValidatePdf(string pdfPath, Document doc, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!File.Exists(pdfPath))
            {
                errorMessage = "PDF 快取檔案不存在。";
                return false;
            }

            try
            {
                FileInfo fi = new FileInfo(pdfPath);
                if (fi.Length == 0)
                {
                    errorMessage = "PDF 快取檔案大小為 0。";
                    return false;
                }

                if (!pdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "快取檔案副檔名不是 .pdf。";
                    return false;
                }

                // Revit ImageTypeOptions Validation
                ImageTypeOptions options = new ImageTypeOptions(pdfPath, false, ImageTypeSource.Link);
                options.PageNumber = 1;
                options.Resolution = 300;

                if (!options.IsValid(doc))
                {
                    errorMessage = "Revit 判定此 PDF 檔案無效，無法作為 Link 載入。";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("PDF validation exception", ex);
                errorMessage = string.Format("驗證 PDF 過程中發生錯誤: {0}", ex.Message);
                return false;
            }
        }
    }
}
