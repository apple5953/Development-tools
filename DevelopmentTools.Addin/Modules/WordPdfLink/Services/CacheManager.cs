using System;
using System.IO;
using DT_WordPdfLink.Utils;

namespace DT_WordPdfLink.Services
{
    public static class CacheManager
    {
        public static string GetCacheDirectory(string wordPath, string projectKey, out bool isFallback)
        {
            isFallback = false;
            try
            {
                string wordDir = Path.GetDirectoryName(wordPath);
                if (!string.IsNullOrEmpty(wordDir))
                {
                    string cacheDir = Path.Combine(wordDir, ".RevitWordPdfCache");
                    
                    // Test write access by creating the directory (if not exists) and trying to create/delete a dummy file
                    if (!Directory.Exists(cacheDir))
                    {
                        Directory.CreateDirectory(cacheDir);
                    }
                    
                    string testFile = Path.Combine(cacheDir, ".write_test");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);

                    return cacheDir;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unable to write to Word directory, falling back to LocalAppData", ex);
            }

            // Fallback
            isFallback = true;
            string localDir = Path.Combine(
                global::DevelopmentTools.Core.InstallLocation.DataDir ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DT_WordPdfLink",
                "Cache",
                projectKey
            );
            if (!Directory.Exists(localDir))
            {
                Directory.CreateDirectory(localDir);
            }
            return localDir;
        }

        public static string GetCachePdfPath(string wordPath, string projectKey, string sheetUniqueId, out bool isFallback)
        {
            string cacheDir = GetCacheDirectory(wordPath, projectKey, out isFallback);
            string fileName = Path.GetFileNameWithoutExtension(wordPath);
            string pathHash = PathUtils.GetPathHash(wordPath);
            string sheetHash = string.IsNullOrEmpty(sheetUniqueId) ? "default" : PathUtils.GetPathHash(sheetUniqueId).Substring(0, 8);
            return Path.Combine(cacheDir, string.Format("{0}_{1}_{2}.pdf", fileName, pathHash, sheetHash));
        }

        public static string GetTempPdfPath(string wordPath, string projectKey, string sheetUniqueId)
        {
            string tempDir = Path.Combine(global::DevelopmentTools.Core.InstallLocation.TempDir, "DT_WordPdfLink");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }
            string fileName = Path.GetFileNameWithoutExtension(wordPath);
            string pathHash = PathUtils.GetPathHash(wordPath);
            string sheetHash = string.IsNullOrEmpty(sheetUniqueId) ? "default" : PathUtils.GetPathHash(sheetUniqueId).Substring(0, 8);
            return Path.Combine(tempDir, string.Format("{0}_{1}_{2}.tmp.pdf", fileName, pathHash, sheetHash));
        }

        public static void AtomicReplace(string tempPdfPath, string destPdfPath)
        {
            if (!File.Exists(tempPdfPath))
            {
                throw new FileNotFoundException("Temp PDF file not found.", tempPdfPath);
            }

            try
            {
                // Attempt to copy and overwrite
                File.Copy(tempPdfPath, destPdfPath, true);
                
                // Try to delete temp file
                try
                {
                    File.Delete(tempPdfPath);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to delete temp PDF file, but replacement succeeded", ex);
                }
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("無法替換快取 PDF 檔案。錯誤: {0}", ex.Message), ex);
            }
        }
    }
}
