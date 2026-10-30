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
            get { return DtStorage.File(@"DT_AIAgent\python.txt", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"DT_AIAgent\python.txt")); }
        }

        public static string ScriptDir { get { return Path.Combine(Config.Root, @"01_AI運行本體\程式"); } }

        /// <summary>指定 python.exe（安裝教學裝好後寫入）</summary>
        public static void SetPython(string exe)
        {
            string f = SettingFile;
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.WriteAllText(f, exe ?? "", Encoding.UTF8);
            seekChecked = false;
        }

        public static bool HasPython { get { string p; return FindPython(out p) != null; } }

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
            // 公司電腦常不能裝在 C 槽：安裝教學預設裝到 D:\Python312 等其他磁碟
            try
            {
                foreach (DriveInfo di in DriveInfo.GetDrives())
                {
                    if (di.DriveType != DriveType.Fixed || !di.IsReady) continue;
                    string r0 = di.RootDirectory.FullName;
                    if (!roots.Contains(r0)) roots.Add(r0);
                    roots.Add(Path.Combine(r0, "Python"));
                }
            }
            catch { }
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
                res.Error = "找不到 Python。請安裝 Python 3（python.org），或在「設定」填 python.exe 的完整路徑。";
                return res;
            }
            ProcessStartInfo psi = new ProcessStartInfo(py, prefix + args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WorkingDirectory = Directory.Exists(ScriptStore.Dir) ? ScriptStore.Dir : (Directory.Exists(ScriptDir) ? ScriptDir : DtStorage.TempDir);
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

        /// <summary>圖說包附的離線套件（Python安裝檔\wheels）；沒有回傳 null</summary>
        public static string WheelDir
        {
            get
            {
                string d = Config.DrawingDir;
                if (d.Length == 0) return null;
                foreach (string c in new[] { Path.Combine(d, @"Python安裝檔\wheels"), Path.Combine(d, "Python安裝檔") })
                    if (Directory.Exists(c) && Directory.GetFiles(c, "*.whl").Length > 0) return c;
                return null;
            }
        }

        /// <summary>安裝套件：先用圖說包的離線套件，再上網；先裝進 Python 本身的資料夾（裝在 D 槽時不碰 C 槽），不行才用 --user</summary>
        public static string InstallPackages(string packages, Action<string> status)
        {
            List<string> tries = new List<string>();
            string wheels = WheelDir;
            if (wheels != null)
            {
                tries.Add("--no-index --find-links " + Q(wheels) + " " + packages);
                tries.Add("--user --no-index --find-links " + Q(wheels) + " " + packages);
            }
            tries.Add(packages);
            tries.Add("--user " + packages);
            PyResult last = null;
            foreach (string t in tries)
            {
                if (status != null) status("安裝 Python 套件中（" + packages + "），約 1～2 分鐘…");
                last = Run("-m pip install --disable-pip-version-check " + t, 600000);
                if (last.ExitCode == 0) return null;
            }
            return last == null ? "無法安裝套件" : (last.Output + "\n" + last.Error).Trim();
        }

        /// <summary>產生簡報需要的套件（python-pptx、openpyxl），缺少就自動安裝</summary>
        public static string EnsurePackages(Action<string> status)
        {
            PyResult r = Run("-c \"import pptx, openpyxl, PIL, lxml\"", 60000);
            if (r.ExitCode == 0) return null;
            if (r.ExitCode == -1 && r.Error.StartsWith("找不到")) return r.Error;
            string err = InstallPackages("python-pptx openpyxl", status);
            r = Run("-c \"import pptx, openpyxl, PIL, lxml\"", 60000);
            if (r.ExitCode == 0) return null;
            return "安裝 python-pptx 失敗（請確認網路，或請專案負責人提供附離線套件的圖說包）：\n" + err;
        }

        static bool seekChecked;

        /// <summary>查圖需要的套件（PyMuPDF 讀 PDF、Pillow 畫紅框、openpyxl 讀釋疑問題庫）；缺少就自動安裝。每次開 Revit 只檢查一次</summary>
        public static string EnsureSeekPackages()
        {
            if (seekChecked) return null;
            PyResult r = Run("-c \"import fitz, PIL, openpyxl\"", 60000);
            if (r.ExitCode == 0) { seekChecked = true; return null; }
            if (r.ExitCode == -1 && r.Error.StartsWith("找不到")) return r.Error;
            string err = InstallPackages("pymupdf pillow openpyxl", null);
            r = Run("-c \"import fitz, PIL, openpyxl\"", 60000);
            if (r.ExitCode == 0) { seekChecked = true; return null; }
            return "安裝查圖套件（pymupdf、pillow）失敗（請確認網路，或請專案負責人提供附離線套件的圖說包）：\n" + err;
        }

        public static string Q(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }
    }
}
