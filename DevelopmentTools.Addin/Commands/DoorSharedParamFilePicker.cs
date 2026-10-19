using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DevelopmentTools.Commands
{
    /// <summary>
    /// 讓使用者選擇共用參數檔，並記住上次選的位置（只存在本機使用者設定資料夾）。
    /// </summary>
    internal static class DoorSharedParamFilePicker
    {
        private static string SettingPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DevelopmentTools", "door-shared-param-path.txt");
            }
        }

        public static string Pick()
        {
            string last = ReadLast();
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "請選擇共用參數檔 (.txt)";
                dialog.Filter = "Revit 共用參數檔 (*.txt)|*.txt|所有檔案 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (!string.IsNullOrEmpty(last) && File.Exists(last))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(last);
                    dialog.FileName = Path.GetFileName(last);
                }
                if (dialog.ShowDialog() != DialogResult.OK) return null;
                SaveLast(dialog.FileName);
                return dialog.FileName;
            }
        }

        private static string ReadLast()
        {
            try
            {
                return File.Exists(SettingPath) ? File.ReadAllText(SettingPath, Encoding.UTF8).Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveLast(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingPath));
                File.WriteAllText(SettingPath, path, Encoding.UTF8);
            }
            catch
            {
                // 記不住上次路徑不影響功能
            }
        }
    }
}
