using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Core
{
    public static class ViewPreviewCacheManager
    {
        private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "RevitViewPreviews");

        static ViewPreviewCacheManager()
        {
            if (!Directory.Exists(TempDir))
            {
                Directory.CreateDirectory(TempDir);
            }
        }

        /// <summary>
        /// 獲取視圖或圖紙的預覽圖 ImageSource
        /// </summary>
        public static BitmapImage GetPreviewImage(Document doc, ElementId viewId)
        {
            if (doc == null || viewId == null || viewId == ElementId.InvalidElementId) return null;

            string filePath = Path.Combine(TempDir, $"{doc.Title.Replace(".rvt", "")}_{viewId.Value}.png");

            // 1. 檢查快取是否存在且有效
            if (File.Exists(filePath))
            {
                try
                {
                    return LoadBitmapWithoutLock(filePath);
                }
                catch
                {
                    // 若讀取失敗，則嘗試重新導出
                }
            }

            // 2. 導出視圖或圖紙為圖片
            try
            {
                var view = doc.GetElement(viewId) as View;
                if (view == null) return null;

                var options = new ImageExportOptions
                {
                    ZoomType = ZoomFitType.FitToPage,
                    PixelSize = 350,
                    FilePath = filePath,
                    FitDirection = FitDirectionType.Horizontal,
                    ExportRange = ExportRange.SetOfViews
                };
                options.SetViewsAndSheets(new List<ElementId> { viewId });

                doc.ExportImage(options);

                // 修正檔案名稱 (ExportImage 自動附加後綴問題)
                string actualFile = FindActualExportedFile(filePath);
                if (!string.IsNullOrEmpty(actualFile) && actualFile != filePath)
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    File.Move(actualFile, filePath);
                }

                if (File.Exists(filePath))
                {
                    return LoadBitmapWithoutLock(filePath);
                }
            }
            catch
            {
                // 導出失敗
            }

            return null;
        }

        private static string FindActualExportedFile(string baseFilePath)
        {
            if (File.Exists(baseFilePath)) return baseFilePath;

            string dir = Path.GetDirectoryName(baseFilePath);
            string pattern = Path.GetFileNameWithoutExtension(baseFilePath) + "*";
            var files = Directory.GetFiles(dir, pattern);
            if (files.Length > 0)
            {
                return files[0];
            }
            return null;
        }

        private static BitmapImage LoadBitmapWithoutLock(string path)
        {
            var bitmap = new BitmapImage();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        /// 清理所有暫存快取
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                if (Directory.Exists(TempDir))
                {
                    Directory.Delete(TempDir, true);
                    Directory.CreateDirectory(TempDir);
                }
            }
            catch { }
        }
    }
}
