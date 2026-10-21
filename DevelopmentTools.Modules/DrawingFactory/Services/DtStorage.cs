// 與 DevelopmentTools.Addin 的 InstallLocation 共用的資料／暫存目錄（C# 5 語法）。
// 外掛啟動時會設定 DEVELOPMENTTOOLS_DATA / DEVELOPMENTTOOLS_TEMP 指向非系統碟（例如 D:\DevelopmentTools），
// 避免公司防毒攔截 C 槽使用者目錄中的檔案。未設定時一律沿用原本路徑。
using System;
using System.IO;

namespace BIMDrawingFactory.UI.Services
{
    internal static class DtStorage
    {
        public static string DataDir
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("DEVELOPMENTTOOLS_DATA");
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
        }

        public static string TempDir
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("DEVELOPMENTTOOLS_TEMP");
                if (string.IsNullOrWhiteSpace(v)) return Path.GetTempPath();
                try { Directory.CreateDirectory(v); return v; }
                catch { return Path.GetTempPath(); }
            }
        }

        /// <summary>新位置不存在時從舊位置複製一次（舊檔保留）。</summary>
        public static string File(string relativePath, string legacyPath)
        {
            string data = DataDir;
            if (data == null) return legacyPath;
            string target = Path.Combine(data, relativePath);
            try
            {
                if (!System.IO.File.Exists(target) && !string.IsNullOrEmpty(legacyPath) && System.IO.File.Exists(legacyPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    System.IO.File.Copy(legacyPath, target, false);
                }
            }
            catch { }
            return target;
        }

        /// <summary>新資料夾第一次建立時搬入舊資料夾內容（舊資料保留）。</summary>
        public static string Folder(string relativePath, string legacyDirectory)
        {
            string data = DataDir;
            if (data == null) return legacyDirectory;
            string target = Path.Combine(data, relativePath);
            try
            {
                if (!Directory.Exists(target) && !string.IsNullOrEmpty(legacyDirectory) && Directory.Exists(legacyDirectory))
                {
                    string staging = target + ".migrating";
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    CopyAll(legacyDirectory, staging);
                    Directory.Move(staging, target);
                }
                Directory.CreateDirectory(target);
            }
            catch { }
            return target;
        }

        private static void CopyAll(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string f in Directory.GetFiles(source))
                System.IO.File.Copy(f, Path.Combine(destination, Path.GetFileName(f)), false);
            foreach (string d in Directory.GetDirectories(source))
                CopyAll(d, Path.Combine(destination, Path.GetFileName(d)));
        }
    }
}
