// Google 簡報同步：把釋疑擷取送到共用 Google 簡報（Apps Script 網頁應用程式），以及開啟共用簡報視窗。C# 5。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RfiDeckAgent.Core
{
    public class GsConfig
    {
        public string Url = "";        // Apps Script 網頁應用程式網址（…/exec）
        public string Secret = "";     // 密鑰（簡報選單「② 顯示連線資訊」）
        public string DeckUrl = "";    // 共用簡報網址（測試連線時自動帶入）
        public bool AutoSend = true;   // 擷取完成後自動送出
    }

    public static class GSlides
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        static string ConfigFile
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DT_AIAgent");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return Path.Combine(d, "gslides.json");
            }
        }

        public static GsConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    GsConfig c = Json.Deserialize<GsConfig>(File.ReadAllText(ConfigFile, Encoding.UTF8));
                    if (c != null) return c;
                }
            }
            catch { }
            return new GsConfig();
        }

        public static void Save(GsConfig c)
        {
            File.WriteAllText(ConfigFile, Json.Serialize(c), new UTF8Encoding(false));
        }

        public static bool Configured
        {
            get { GsConfig c = Load(); return c.Url.Trim().Length > 0 && c.Secret.Trim().Length > 0; }
        }

        // ------------------------------------------------------------ HTTP
        public static Dictionary<string, object> Post(GsConfig c, Dictionary<string, object> payload, int timeoutMs)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            payload["secret"] = c.Secret.Trim();
            byte[] body = Encoding.UTF8.GetBytes(Json.Serialize(payload));
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(c.Url.Trim());
            req.Method = "POST";
            req.ContentType = "text/plain; charset=utf-8";
            req.AllowAutoRedirect = true;     // Apps Script 會 302 轉到 googleusercontent，.NET 會自動改用 GET 取結果
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.ContentLength = body.Length;
            using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            string text;
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    text = r.ReadToEnd();
            }
            catch (WebException ex)
            {
                string detail = "";
                if (ex.Response != null)
                {
                    try { using (StreamReader r = new StreamReader(ex.Response.GetResponseStream())) detail = r.ReadToEnd(); } catch { }
                }
                return Fail("連線失敗：" + ex.Message + (detail.Length > 0 ? "\n" + Short(detail) : ""));
            }
            try
            {
                Dictionary<string, object> o = Json.Deserialize<Dictionary<string, object>>(text);
                if (o != null) return o;
            }
            catch { }
            if (text.Contains("<html") || text.Contains("<!DOCTYPE"))
                return Fail("Google 回傳的是網頁不是資料，通常是部署設定的「誰可以存取」沒有選「所有人」，或網址不是 /exec 結尾。");
            return Fail("無法解讀回應：" + Short(text));
        }

        static Dictionary<string, object> Fail(string msg)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["ok"] = false;
            d["error"] = msg;
            return d;
        }

        static string Short(string s) { s = s ?? ""; return s.Length > 300 ? s.Substring(0, 300) + "…" : s; }

        public static bool Ok(Dictionary<string, object> r)
        {
            object v;
            return r != null && r.TryGetValue("ok", out v) && v is bool && (bool)v;
        }

        public static string Str(Dictionary<string, object> r, string k)
        {
            object v;
            return r != null && r.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
        }

        // ------------------------------------------------------------ 動作
        public static Dictionary<string, object> Ping(GsConfig c)
        {
            Dictionary<string, object> p = new Dictionary<string, object>();
            p["action"] = "ping";
            return Post(c, p, 60000);
        }

        /// <summary>把收件匣的一筆送到共用簡報；成功時在資料夾寫 _gslides.json</summary>
        public static Dictionary<string, object> Send(string folder, RfiDraft d)
        {
            GsConfig c = Load();
            if (c.Url.Trim().Length == 0 || c.Secret.Trim().Length == 0)
                return Fail("還沒設定 Google 簡報連線（按「設定」填網址與密鑰）。");
            Dictionary<string, object> item = new Dictionary<string, object>();
            item["uid"] = Path.GetFileName(folder);
            item["category"] = d.Category;
            item["status"] = d.Status;
            item["building"] = d.Building;
            item["location"] = d.Location;
            item["drawing"] = d.Drawing;
            item["question"] = d.Question;
            item["suggest"] = d.Suggest;
            item["author"] = d.Author;
            item["createdAt"] = d.CreatedAt;
            item["project"] = d.Project;
            item["level"] = d.Level;
            item["grid"] = d.Grid;
            item["elementIds"] = d.ElementIds;
            item["elements"] = d.Elements;
            item["views"] = string.Join("、", d.Shots.Select(s => s.View).Where(v => !string.IsNullOrEmpty(v)).Distinct().ToArray());
            List<Dictionary<string, object>> imgs = new List<Dictionary<string, object>>();
            foreach (ShotInfo s in d.Shots.Take(20))
            {
                string f = Path.Combine(folder, s.File);
                if (!File.Exists(f)) continue;
                Dictionary<string, object> im = new Dictionary<string, object>();
                im["caption"] = s.Caption;
                im["b64"] = Convert.ToBase64String(File.ReadAllBytes(f));
                imgs.Add(im);
            }
            Dictionary<string, object> p = new Dictionary<string, object>();
            p["action"] = "add";
            p["item"] = item;
            p["images"] = imgs;
            Dictionary<string, object> r = Post(c, p, 180000);
            if (Ok(r))
            {
                Dictionary<string, object> mark = new Dictionary<string, object>();
                mark["no"] = Str(r, "no");
                mark["slideId"] = Str(r, "slideId");
                mark["url"] = Str(r, "url");
                mark["sentAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                File.WriteAllText(Path.Combine(folder, "_gslides.json"), Json.Serialize(mark), new UTF8Encoding(false));
            }
            return r;
        }

        /// <summary>已同步的編號（沒同步回傳空字串）</summary>
        public static string SentNo(string folder)
        {
            try
            {
                string f = Path.Combine(folder, "_gslides.json");
                if (!File.Exists(f)) return "";
                Dictionary<string, object> m = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
                return Str(m, "no");
            }
            catch { return ""; }
        }

        // ------------------------------------------------------------ 開共用簡報（Edge 應用程式視窗，貼齊 Revit 右側）
        public static void OpenDeck(IntPtr revitHwnd, string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                GsConfig c = Load();
                url = c.DeckUrl;
            }
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("還沒有共用簡報網址。\n請按「設定」→ 填好網址與密鑰 →「測試連線」，會自動帶入。", "DT AI AGENT");
                return;
            }
            string edge = new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe") }
                .FirstOrDefault(File.Exists);
            if (edge == null) { Process.Start(url); return; }
            System.Drawing.Rectangle wa = Screen.FromHandle(revitHwnd).WorkingArea;
            int w = (int)(wa.Width * 0.40), h = wa.Height;
            int x = wa.Right - w, y = wa.Top;
            string args = string.Format("--app=\"{0}\" --window-position={1},{2} --window-size={3},{4}", url, x, y, w, h);
            try { Process.Start(edge, args); }
            catch { Process.Start(url); }
        }
    }
}
