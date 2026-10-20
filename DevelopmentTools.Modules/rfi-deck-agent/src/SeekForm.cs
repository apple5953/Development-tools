// 找對照圖說：用擷取情境查「圖說查找索引」，列出候選 PDF 圖說裁切圖，勾選後加入截圖。C# 5。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using Form = System.Windows.Forms.Form;

namespace RfiDeckAgent.Core
{
    public class SeekPick
    {
        public string Png;
        public string Caption;
    }

    public class SeekForm : Form
    {
        public readonly List<SeekPick> Picked = new List<SeekPick>();
        readonly FlowLayoutPanel cards = new FlowLayoutPanel();
        readonly Label head = new Label();
        readonly List<Tuple<CheckBox, TextBox, string>> rows = new List<Tuple<CheckBox, TextBox, string>>();
        readonly List<Tuple<CheckBox, CheckBox, TextBox, string>> zooms = new List<Tuple<CheckBox, CheckBox, TextBox, string>>();

        public SeekForm(Dictionary<string, object> result)
        {
            Text = "對照圖說 - DT AI AGENT";
            Font = new Font("Microsoft JhengHei UI", 10f);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1180, 760);

            head.Dock = DockStyle.Top;
            head.Height = 56;
            head.Padding = new Padding(10, 6, 10, 0);
            Controls.Add(cards);
            Controls.Add(head);
            cards.Dock = DockStyle.Fill;
            cards.AutoScroll = true;
            cards.Padding = new Padding(8);

            FlowLayoutPanel b = new FlowLayoutPanel();
            b.Dock = DockStyle.Bottom;
            b.Height = 50;
            b.FlowDirection = FlowDirection.RightToLeft;
            b.Padding = new Padding(8);
            Button ok = new Button(); ok.Text = "加入勾選的圖說"; ok.AutoSize = true; ok.Height = 34;
            ok.BackColor = Color.FromArgb(46, 139, 62); ok.ForeColor = Color.White; ok.FlatStyle = FlatStyle.Flat;
            ok.Click += delegate
            {
                foreach (Tuple<CheckBox, TextBox, string> r in rows)
                    if (r.Item1.Checked) Picked.Add(new SeekPick { Png = r.Item3, Caption = r.Item2.Text.Trim() });
                foreach (Tuple<CheckBox, CheckBox, TextBox, string> z in zooms)
                    if (z.Item1.Checked && z.Item2.Checked) Picked.Add(new SeekPick { Png = z.Item4, Caption = z.Item3.Text.Trim() + "（局部放大）" });
                DialogResult = DialogResult.OK;
                Close();
            };
            Button cancel = new Button(); cancel.Text = "取消"; cancel.AutoSize = true; cancel.Height = 34;
            cancel.Click += delegate { Close(); };
            b.Controls.Add(ok);
            b.Controls.Add(cancel);
            Controls.Add(b);

            Fill(result);
        }

        static string S(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
        }

        void Fill(Dictionary<string, object> r)
        {
            Dictionary<string, object> q = r.ContainsKey("query") ? r["query"] as Dictionary<string, object> : null;
            string warn = "";
            if (r.ContainsKey("warnings") && r["warnings"] is ArrayList && ((ArrayList)r["warnings"]).Count > 0)
                warn = "　⚠ " + string.Join("；", ((ArrayList)r["warnings"]).Cast<object>().Select(x => x.ToString()).ToArray());
            string hist = HistoryText(r);
            head.Text = string.Format("依 棟別「{0}」樓層「{1}」柱線「{2}」找到的結構／建築圖說。加入的是整張圖說（含圖框），紅框＝位置範圍、紅圈＝物件位置、橘框＝構件編號；預覽為局部放大，雙擊看整張。\n索引建立：{3}（{4}）{5}{6}",
                S(q, "building"), S(q, "level_norm"), S(q, "grid"), S(r, "index_built"), S(r, "index_builder"), warn,
                hist.Length > 0 ? "\n⚠ " + hist : "");
            if (hist.Length > 0) { head.Height = 80; head.ForeColor = Color.FromArgb(160, 60, 0); }
            ArrayList cs = r.ContainsKey("candidates") ? r["candidates"] as ArrayList : null;
            if (cs == null || cs.Count == 0)
            {
                Label l = new Label();
                l.Text = "沒有找到對應圖說。可以先選取問題元件、確認樓層／棟別後再試。";
                l.AutoSize = true;
                cards.Controls.Add(l);
                return;
            }
            bool first = true;
            foreach (object o in cs)
            {
                Dictionary<string, object> c = o as Dictionary<string, object>;
                if (c == null) continue;
                string png = S(c, "png");
                string zoom = S(c, "png_zoom");
                string preview = File.Exists(zoom) ? zoom : png;
                Panel p = new Panel();
                p.Width = 540; p.Height = 424;
                p.BorderStyle = BorderStyle.FixedSingle;
                p.Margin = new Padding(6);
                PictureBox pb = new PictureBox();
                pb.SetBounds(6, 6, 526, 296);
                pb.SizeMode = PictureBoxSizeMode.Zoom;
                if (File.Exists(preview))
                {
                    try { using (FileStream fs = File.OpenRead(preview)) pb.Image = Image.FromStream(fs); } catch { }
                    pb.Cursor = Cursors.Hand;
                    pb.DoubleClick += delegate { System.Diagnostics.Process.Start(png); };
                }
                CheckBox chk = new CheckBox();
                chk.Text = string.Format("{0}  {1}（第 {2} 頁）{3}", S(c, "drawing_no"), S(c, "file"), S(c, "page"),
                    S(c, "located") == "True" ? "　已定位柱線" : "　整張");
                chk.SetBounds(6, 306, 526, 24);
                chk.Checked = first && S(c, "located") == "True";
                TextBox cap = new TextBox();
                cap.Text = S(c, "caption");
                cap.ForeColor = Color.FromArgb(224, 48, 48);
                cap.SetBounds(6, 334, 526, 26);
                Label why = new Label();
                ArrayList w = c.ContainsKey("why") ? c["why"] as ArrayList : null;
                why.Text = "比對依據：" + (w != null ? string.Join("、", w.Cast<object>().Select(x => x.ToString()).ToArray()) : "");
                why.ForeColor = Color.DimGray;
                why.Font = new Font("Microsoft JhengHei UI", 9f);
                why.SetBounds(6, 364, 526, 30);
                p.Controls.Add(pb); p.Controls.Add(chk); p.Controls.Add(cap); p.Controls.Add(why);
                if (File.Exists(zoom))
                {
                    CheckBox z = new CheckBox();
                    z.Text = "另加局部放大圖";
                    z.SetBounds(6, 396, 526, 22);
                    p.Controls.Add(z);
                    zooms.Add(Tuple.Create(chk, z, cap, zoom));
                }
                cards.Controls.Add(p);
                rows.Add(Tuple.Create(chk, cap, png));
                first = false;
            }
        }

        /// <summary>同位置的釋疑歷史，一行文字（沒有回傳空字串）</summary>
        public static string HistoryText(Dictionary<string, object> r)
        {
            object h;
            if (r == null || !r.TryGetValue("history", out h) || !(h is ArrayList) || ((ArrayList)h).Count == 0) return "";
            List<string> parts = new List<string>();
            foreach (object o in (ArrayList)h)
            {
                Dictionary<string, object> x = o as Dictionary<string, object>;
                if (x == null) continue;
                parts.Add(string.Format("{0}（{1}）{2}", S(x, "tid"), S(x, "state"), S(x, "summary")));
                if (parts.Count >= 3) break;
            }
            return string.Format("此位置附近已有 {0} 筆釋疑：", ((ArrayList)h).Count) + string.Join("；", parts.ToArray());
        }

        /// <summary>執行 seek_lookup.py，回傳結果（錯誤時含 error）</summary>
        public static Dictionary<string, object> Run(RfiDraft d, string building)
        {
            // 查圖規則內嵌在外掛裡（外掛更新即更新規則）；--root 告訴它 AI AGENT 資料夾（索引、PDF、專案設定所在）
            string script = ScriptStore.Resolve("seek_lookup.py");
            string pkg = PyRunner.EnsureSeekPackages();
            if (pkg != null)
            {
                Dictionary<string, object> e0 = new Dictionary<string, object>();
                e0["ok"] = false;
                e0["error"] = pkg;
                return e0;
            }
            string outDir = Path.Combine(Path.GetTempPath(), "dtai_seek_" + DateTime.Now.ToString("HHmmssfff"));
            List<string> a = new List<string>();
            a.Add(PyRunner.Q(script));
            a.Add("--root " + PyRunner.Q(Config.Root));
            a.Add("--out " + PyRunner.Q(outDir));
            if (!string.IsNullOrEmpty(building)) a.Add("--building " + PyRunner.Q(building));
            if (!string.IsNullOrEmpty(d.Level)) a.Add("--level " + PyRunner.Q(d.Level));
            if (!string.IsNullOrEmpty(d.Grid)) a.Add("--grid " + PyRunner.Q(d.Grid));
            if (!string.IsNullOrEmpty(d.CellPos)) a.Add("--cellpos " + PyRunner.Q(d.CellPos));
            if (!string.IsNullOrEmpty(d.PointXY)) a.Add("--point " + PyRunner.Q(d.PointXY));
            if (!string.IsNullOrEmpty(d.ColumnRefs)) a.Add("--colrefs " + PyRunner.Q(d.ColumnRefs));
            if (!string.IsNullOrEmpty(d.Labels)) a.Add("--labels " + string.Join(" ", d.Labels.Split('、').Where(x => x.Length > 0).Select(x => PyRunner.Q(x)).ToArray()));
            if (d.Elements.Count > 0)
            {
                a.Add("--cats " + string.Join(" ", d.Elements.Select(e => PyRunner.Q(e.Split(' ')[0])).Distinct().ToArray()));
                a.Add("--types " + string.Join(" ", d.Elements.Take(10).Select(e => PyRunner.Q(e)).ToArray()));
            }
            PyResult r = PyRunner.Run(string.Join(" ", a.ToArray()), 120000);
            if (r.Result != null) return r.Result;
            Dictionary<string, object> err = new Dictionary<string, object>();
            err["ok"] = false;
            err["error"] = (r.Error + "\n" + r.Output).Trim();
            return err;
        }
    }
}
