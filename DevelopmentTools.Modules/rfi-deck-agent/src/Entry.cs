// RfiDeckAgent.Core — 釋疑擷取與簡報的功能本體（由 Commands/OpenCommand.cs 呼叫）。C# 5 語法。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RfiDeckAgent.Core
{
    public static class Entry
    {
        public static Result Run(string command, ExternalCommandData data, ref string message)
        {
            // 工作資料夾不必設定：沒指定時自動用 RTS 資料位置下的「工作區」（程式與範本內嵌在外掛裡）。
            Config.EnsureWorkFolders();

            switch (command)
            {
                case "capture": return CaptureCommand.Execute(data.Application);
                case "inbox":
                    OpenFolder(Config.InboxDir);
                    return Result.Succeeded;
                case "output":
                    OpenFolder(Config.OutputDir);
                    return Result.Succeeded;
                case "handoff": return Handoff();
                case "settings": return Settings();
                case "slides":
                    GSlides.OpenDeck(Process.GetCurrentProcess().MainWindowHandle, null);
                    return Result.Succeeded;
            }
            message = "未知的指令：" + command;
            return Result.Failed;
        }

        public static void OpenFolder(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        static Result Handoff()
        {
            using (DeckForm f = new DeckForm())
            {
                f.ShowDialog(new RevitWindow(Process.GetCurrentProcess().MainWindowHandle));
            }
            return Result.Succeeded;
        }

        static Result Settings()
        {
            using (SettingsForm f = new SettingsForm())
            {
                f.ShowDialog(new RevitWindow(Process.GetCurrentProcess().MainWindowHandle));
            }
            return Result.Succeeded;
        }
    }

    /// <summary>設定：工作資料夾（收件匣、問題庫、成果）與圖說包資料夾。沒設定時自動使用預設位置，新同事不必另外給資料夾。</summary>
    public static class Config
    {
        /// <summary>預設工作資料夾：RTS 資料位置（非系統碟，例如 D:\DevelopmentTools\Data）\DT_AIAgent\工作區</summary>
        public static string DefaultRoot
        {
            get
            {
                string baseDir = DtStorage.DataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DT_AIAgent");
                return Path.Combine(DtStorage.DataDir != null ? Path.Combine(baseDir, "DT_AIAgent") : baseDir, "工作區");
            }
        }

        /// <summary>使用者有自己指定工作資料夾（舊版的 AI AGENT 資料夾）</summary>
        public static bool HasCustomRoot
        {
            get { string r = ReadSetting(SettingFile); return r.Length > 0 && Directory.Exists(r); }
        }

        public static bool IsConfigured { get { return true; } }

        static string Setting(string name)
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DT_AIAgent");
            string legacy = Path.Combine(d, name);
            if (DtStorage.DataDir != null) return DtStorage.File(@"DT_AIAgent\" + name, legacy);
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            return legacy;
        }

        static string SettingFile { get { return Setting("root.txt"); } }
        static string DrawingSettingFile { get { return Setting("drawings.txt"); } }

        static string ReadSetting(string f)
        {
            try { if (File.Exists(f)) return File.ReadAllText(f, System.Text.Encoding.UTF8).Trim(); }
            catch { }
            return "";
        }

        static void WriteSetting(string f, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.WriteAllText(f, value ?? "", System.Text.Encoding.UTF8);
        }

        public static string Root
        {
            get
            {
                string s = ReadSetting(SettingFile);
                return s.Length > 0 && Directory.Exists(s) ? s : DefaultRoot;
            }
            set { WriteSetting(SettingFile, value); }
        }

        /// <summary>建立工作資料夾需要的子資料夾（收件匣、成果）</summary>
        public static void EnsureWorkFolders()
        {
            try
            {
                Directory.CreateDirectory(InboxDir);
                Directory.CreateDirectory(Path.Combine(OutputDir, "簡報PPT"));
            }
            catch { }
        }

        // ------------------------------------------------------------ 圖說包
        public const string IndexFile = "圖說查找索引.db";

        /// <summary>圖說包資料夾（內有 圖說查找索引.db、查圖設定.json、PDF）。沒設定時沿用舊版 AI AGENT 資料夾的 03_資料庫\圖說</summary>
        public static string DrawingDir
        {
            get
            {
                string s = ReadSetting(DrawingSettingFile);
                if (s.Length > 0 && File.Exists(Path.Combine(s, IndexFile))) return s;
                string legacy = Path.Combine(Root, @"03_資料庫\圖說");
                if (File.Exists(Path.Combine(legacy, IndexFile))) return legacy;
                return "";
            }
            set { WriteSetting(DrawingSettingFile, value); }
        }

        /// <summary>還沒有圖說包時請使用者選一次（ask=false 只檢查不問）。選好後順便帶入包裡的 Google 共用簡報連線</summary>
        public static bool EnsureDrawingDir(IWin32Window owner, bool ask)
        {
            if (DrawingDir.Length > 0) return true;
            if (!ask) return false;
            MessageBox.Show(owner, "第一次使用「對照圖說」：請選擇專案的「圖說包」資料夾\n（裡面有 圖說查找索引.db 和 PDF 圖說，由專案負責人提供）。\n選一次之後就會記住。",
                "對照圖說", MessageBoxButtons.OK, MessageBoxIcon.Information);
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "選擇圖說包資料夾（內有 " + IndexFile + "）";
                while (dlg.ShowDialog(owner) == DialogResult.OK)
                {
                    string dir = FindIndexDir(dlg.SelectedPath);
                    if (dir != null)
                    {
                        DrawingDir = dir;
                        GSlides.ImportFromBundle(dir);
                        return true;
                    }
                    MessageBox.Show(owner, "這個資料夾裡找不到 " + IndexFile + "，請選圖說包的資料夾。", "對照圖說");
                }
            }
            return false;
        }

        /// <summary>選的資料夾本身或下一層有索引檔就接受（使用者常選到上一層）</summary>
        public static string FindIndexDir(string dir)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, IndexFile))) return dir;
                foreach (string d in Directory.GetDirectories(dir))
                    if (File.Exists(Path.Combine(d, IndexFile))) return d;
                string legacy = Path.Combine(dir, @"03_資料庫\圖說");
                if (File.Exists(Path.Combine(legacy, IndexFile))) return legacy;
            }
            catch { }
            return null;
        }

        public static string InboxDir { get { return Path.Combine(Root, @"03_資料庫\釋疑問題庫\收件匣"); } }
        public static string OutputDir { get { return Path.Combine(Root, "04_成果"); } }

        public static string DraftDir
        {
            get
            {
                string d = DtStorage.Folder(@"DT_AIAgent\draft", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"DT_AIAgent\draft"));
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }
    }
}
