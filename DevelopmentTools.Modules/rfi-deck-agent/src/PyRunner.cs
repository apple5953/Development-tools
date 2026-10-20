// 在本機執行 AI AGENT 的 Python 程式（產生簡報用）。C# 5 語法。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace RfiDeckAgent.Core
{
    public class PyResult
    {
        public int ExitCode = -1;
        public string Output = "";
        public string Error = "";
        public Dictionary<string, object> Result;   // 程式最後一行 RESULT {...}
    }

    public static class PyRunner
    {
        static string SettingFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"DT_AIAgent\python.txt"); }
        }

        public static string ScriptDir { get { return Path.Combine(Config.Root, @"01_AI運行本體\程式"); } }

        /// <summary>找 python.exe；回傳 null 表示找不到</summary>
        public static string FindPython(out string prefixArgs)
        {
            prefixArgs = "";
            try
            {
                if (File.Exists(SettingFile))
                {
                    string s = File.ReadAllText(SettingFile, Encoding.UTF8).Trim().Trim('"');
                    if (File.Exists(s)) return s;
                }
            }
            catch { }
            List<string> roots = new List<string>();
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python"));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            roots.Add(@"C:\");
            foreach (string r in roots)
            {
                if (!Directory.Exists(r)) continue;
                string[] dirs;
                try { dirs = Directory.GetDirectories(r, "Python3*"); } catch { continue; }
                foreach (string d in dirs.OrderByDescending(x => VersionKey(Path.GetFileName(x))))
                {
                    string exe = Path.Combine(d, "python.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            string[] launchers = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Launcher\py.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "py.exe") };
            foreach (string l in launchers)
            {
                if (File.Exists(l)) { prefixArgs = "-3 "; return l; }
            }
            return null;
        }

        static int VersionKey(string name)
        {
            // Python312 → 312, Python39 → 39 → 309
            string digits = new string(name.Where(char.IsDigit).ToArray());
            int v;
            if (!int.TryParse(digits, out v)) return 0;
            if (digits.Length == 2) v = (v / 10) * 100 + v % 10;
            return v;
        }

        public static PyResult Run(string args, int timeoutMs)
        {
            PyResult res = new PyResult();
            string prefix;
            string py = FindPython(out prefix);
            if (py == null)
            {
                res.Error = "找不到 Python。請安裝 Python 3（python.org），或在 %APPDATA%\\DT_AIAgent\\python.txt 填 python.exe 的完整路徑。";
                return res;
            }
            ProcessStartInfo psi = new ProcessStartInfo(py, prefix + args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WorkingDirectory = Directory.Exists(ScriptDir) ? ScriptDir : Path.GetTempPath();
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            StringBuilder err = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.Start();
                p.BeginErrorReadLine();
                res.Output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    res.Error = "執行逾時";
                    return res;
                }
                p.WaitForExit();
                res.ExitCode = p.ExitCode;
            }
            lock (err) res.Error = err.ToString();
            foreach (string line in res.Output.Split('\n').Reverse())
            {
                string t = line.Trim();
                if (t.StartsWith("RESULT "))
                {
                    try { res.Result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(t.Substring(7)); }
                    catch { }
                    break;
                }
            }
            return res;
        }

        /// <summary>確認 python-pptx / openpyxl 已安裝，沒有就自動 pip install --user</summary>
        public static string EnsurePackages(Action<string> status)
        {
            PyResult r = Run("-c \"import pptx, openpyxl, PIL, lxml\"", 60000);
            if (r.ExitCode == 0) return null;
            if (r.ExitCode == -1 && r.Error.StartsWith("找不到")) return r.Error;
            if (status != null) status("第一次使用：安裝簡報套件（python-pptx）中，約 1 分鐘…");
            PyResult ins = Run("-m pip install --user --disable-pip-version-check python-pptx openpyxl", 300000);
            r = Run("-c \"import pptx, openpyxl, PIL, lxml\"", 60000);
            if (r.ExitCode == 0) return null;
            return "安裝 python-pptx 失敗：\n" + ins.Output + "\n" + ins.Error;
        }

        static bool seekChecked;

        /// <summary>查圖需要的套件（PyMuPDF 讀 PDF、Pillow 畫紅框、openpyxl 讀釋疑問題庫）；缺少就自動 pip install --user。每次開 Revit 只檢查一次</summary>
        public static string EnsureSeekPackages()
        {
            if (seekChecked) return null;
            PyResult r = Run("-c \"import fitz, PIL, openpyxl\"", 60000);
            if (r.ExitCode == 0) { seekChecked = true; return null; }
            if (r.ExitCode == -1 && r.Error.StartsWith("找不到")) return r.Error;
            PyResult ins = Run("-m pip install --user --disable-pip-version-check pymupdf pillow openpyxl", 300000);
            r = Run("-c \"import fitz, PIL, openpyxl\"", 60000);
            if (r.ExitCode == 0) { seekChecked = true; return null; }
            return "安裝查圖套件（pymupdf、pillow）失敗，請確認網路或請管理員安裝：\n" + ins.Output + "\n" + ins.Error;
        }

        public static string Q(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }
    }
}
