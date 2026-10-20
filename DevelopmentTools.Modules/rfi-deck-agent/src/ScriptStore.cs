// 內嵌在外掛裡的 Python 程式（查圖規則 seek_lookup.py）：外掛更新，規則就一起更新，不必另外發給同事。C# 5。
// 執行前寫到 %LOCALAPPDATA%\DT_AIAgent\py\（內容不同才覆寫）；找不到內嵌資源時退回 AI AGENT 資料夾的版本。
using System;
using System.IO;
using System.Reflection;

namespace RfiDeckAgent.Core
{
    public static class ScriptStore
    {
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"DT_AIAgent\py"); }
        }

        /// <summary>取得內嵌程式的實體路徑；沒有內嵌時回傳 null</summary>
        public static string Ensure(string name)
        {
            try
            {
                Assembly asm = typeof(ScriptStore).Assembly;
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    byte[] data;
                    using (MemoryStream ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                    Directory.CreateDirectory(Dir);
                    string path = Path.Combine(Dir, name);
                    if (!File.Exists(path) || !Same(File.ReadAllBytes(path), data))
                    {
                        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        File.WriteAllBytes(tmp, data);
                        try { if (File.Exists(path)) File.Delete(path); File.Move(tmp, path); }
                        catch { try { File.Delete(tmp); } catch { } if (!File.Exists(path)) return null; }
                    }
                    return path;
                }
            }
            catch { return null; }
        }

        /// <summary>內嵌版本優先，沒有就用 AI AGENT 資料夾 01_AI運行本體\程式 的同名檔</summary>
        public static string Resolve(string name)
        {
            return Ensure(name) ?? Path.Combine(PyRunner.ScriptDir, name);
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
