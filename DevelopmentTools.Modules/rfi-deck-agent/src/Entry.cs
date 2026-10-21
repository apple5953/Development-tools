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
            // 尚未設定 AI AGENT 資料夾時，除了「設定」以外都先引導使用者設定（不再預設寫死的磁碟路徑）。
            if (command != "settings" && !Config.IsConfigured)
            {
                TaskDialog.Show("DT AI AGENT", "請先指定 AI AGENT 資料夾位置（範本、程式與收件匣所在的資料夾）。");
                return Settings();
            }

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

    /// <summary>設定：AI AGENT 根目錄存在 %APPDATA%\DT_AIAgent\root.txt</summary>
    public static class Config
    {
        // 不預設任何磁碟路徑：每位使用者在「設定」指定一次，存在 %APPDATA%\DT_AIAgent\root.txt。
        public const string DefaultRoot = "";

        public static bool IsConfigured
        {
            get { string r = Root; return r.Length > 0 && Directory.Exists(r); }
        }

        static string SettingFile
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DT_AIAgent");
                string legacy = Path.Combine(d, "root.txt");
                if (DtStorage.DataDir != null) return DtStorage.File(@"DT_AIAgent\root.txt", legacy);
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return legacy;
            }
        }

        public static string Root
        {
            get
            {
                try
                {
                    if (File.Exists(SettingFile))
                    {
                        string s = File.ReadAllText(SettingFile, System.Text.Encoding.UTF8).Trim();
                        if (s.Length > 0) return s;
                    }
                }
                catch { }
                return DefaultRoot;
            }
            set
            {
                string f = SettingFile;
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, value, System.Text.Encoding.UTF8);
            }
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
