// 內嵌在外掛裡的 Python 程式（查圖、產生簡報、問題庫）：外掛更新，程式就一起更新，不必另外發給同事。C# 5。
// 執行前全部寫到 <RTS 資料位置>\DT_AIAgent\py\（內容不同才覆寫；彼此會 import，所以放同一個資料夾）。
// 找不到內嵌資源時退回舊版 AI AGENT 資料夾的 01_AI運行本體\程式。
using System;
using System.IO;
using System.Reflection;

namespace RfiDeckAgent.Core
{
    public static class ScriptStore
    {
        public static string Dir
        {
            get { return DtStorage.DataDir != null ? Path.Combine(DtStorage.DataDir, @"DT_AIAgent\py") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"DT_AIAgent\py"); }
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

        /// <summary>把所有內嵌的 .py 寫出（程式之間會互相 import）</summary>
        public static void EnsureAll()
        {
            try
            {
                foreach (string n in typeof(ScriptStore).Assembly.GetManifestResourceNames())
                    if (n.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) Ensure(n);
            }
            catch { }
        }

        /// <summary>內嵌版本優先，沒有就用舊版 AI AGENT 資料夾 01_AI運行本體\程式 的同名檔</summary>
        public static string Resolve(string name)
        {
            EnsureAll();
            string p = Path.Combine(Dir, name);
            if (File.Exists(p)) return p;
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
