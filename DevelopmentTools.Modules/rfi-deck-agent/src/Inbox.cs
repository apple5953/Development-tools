// 草稿與收件匣：草稿在 %LOCALAPPDATA%\DT_AIAgent\draft，完成後搬到 AI AGENT\03_資料庫\釋疑問題庫\收件匣\yyyyMMdd_HHmmss_使用者\
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace RfiDeckAgent.Core
{
    public class ShotInfo
    {
        public string File = "";
        public string Caption = "3D 透視圖";
        public string View = "";
        public string Time = "";
    }

    public class RfiDraft
    {
        public string Schema = "dt-aiagent-rfi/1";
        public string Project = "";
        public string ProjectPath = "";
        public string Category = "D";        // H/B/C/D
        public string Building = "";
        public string Location = "";
        public string Drawing = "";
        public string Question = "";
        public string Suggest = "";
        public string Status = "預計提出";
        public string Author = "";
        public string CreatedAt = "";
        public string Level = "";
        public string Grid = "";
        public string CellPos = "";      // 柱線格內相對位置 "fx,fy"
        public string Anchors = "";      // 周圍結構柱位置標記
        public string PositionNote = ""; // 距 8 軸 2.35m、距 B' 軸 1.20m
        public List<string> ElementIds = new List<string>();
        public List<string> Elements = new List<string>();
        public List<ShotInfo> Shots = new List<ShotInfo>();
    }

    public static class Inbox
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        static string DraftFile { get { return Path.Combine(Config.DraftDir, "draft.json"); } }

        public static RfiDraft LoadDraft()
        {
            try
            {
                if (File.Exists(DraftFile))
                {
                    RfiDraft d = Json.Deserialize<RfiDraft>(File.ReadAllText(DraftFile, Encoding.UTF8));
                    if (d != null)
                    {
                        d.Shots = d.Shots.Where(s => File.Exists(Path.Combine(Config.DraftDir, s.File))).ToList();
                        return d;
                    }
                }
            }
            catch { }
            return null;
        }

        public static void SaveDraft(RfiDraft d)
        {
            File.WriteAllText(DraftFile, Json.Serialize(d), new UTF8Encoding(false));
        }

        public static void ClearDraft()
        {
            foreach (string f in Directory.GetFiles(Config.DraftDir))
            {
                try { File.Delete(f); } catch { }
            }
        }

        public static string NewShotPath(RfiDraft d)
        {
            int i = d.Shots.Count + 1;
            string name;
            do { name = string.Format("shot_{0:00}.png", i++); } while (File.Exists(Path.Combine(Config.DraftDir, name)));
            return Path.Combine(Config.DraftDir, name);
        }

        /// <summary>草稿正式存入收件匣，回傳資料夾路徑</summary>
        public static string Commit(RfiDraft d)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string folder = Path.Combine(Config.InboxDir, stamp + "_" + Safe(d.Author));
            Directory.CreateDirectory(folder);
            int n = 1;
            foreach (ShotInfo s in d.Shots)
            {
                string src = Path.Combine(Config.DraftDir, s.File);
                string dstName = string.Format("img_{0:00}.png", n++);
                File.Copy(src, Path.Combine(folder, dstName), true);
                s.File = dstName;
            }
            File.WriteAllText(Path.Combine(folder, "item.json"), Pretty(Json.Serialize(d)), new UTF8Encoding(false));
            ClearDraft();
            return folder;
        }

        /// <summary>讀收件匣某一筆的 item.json</summary>
        public static RfiDraft LoadItem(string folder)
        {
            try
            {
                string f = Path.Combine(folder, "item.json");
                if (!File.Exists(f)) return null;
                RfiDraft d = Json.Deserialize<RfiDraft>(File.ReadAllText(f, Encoding.UTF8));
                if (d != null && d.Shots == null) d.Shots = new List<ShotInfo>();
                return d;
            }
            catch { return null; }
        }

        public static List<string> ListPending()
        {
            List<string> r = new List<string>();
            if (!Directory.Exists(Config.InboxDir)) return r;
            foreach (string dir in Directory.GetDirectories(Config.InboxDir))
            {
                if (Path.GetFileName(dir).StartsWith("_")) continue;   // _已匯入 等
                if (File.Exists(Path.Combine(dir, "item.json"))) r.Add(dir);
            }
            return r;
        }

        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "user";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        /// <summary>簡易縮排，讓 item.json 人眼可讀</summary>
        static string Pretty(string json)
        {
            StringBuilder sb = new StringBuilder();
            int indent = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr;
                if (inStr) { sb.Append(ch); continue; }
                switch (ch)
                {
                    case '{':
                    case '[':
                        sb.Append(ch); sb.Append('\n'); indent++; sb.Append(new string(' ', indent * 2)); break;
                    case '}':
                    case ']':
                        sb.Append('\n'); indent--; sb.Append(new string(' ', indent * 2)); sb.Append(ch); break;
                    case ',':
                        sb.Append(ch); sb.Append('\n'); sb.Append(new string(' ', indent * 2)); break;
                    case ':':
                        sb.Append(": "); break;
                    default:
                        sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
