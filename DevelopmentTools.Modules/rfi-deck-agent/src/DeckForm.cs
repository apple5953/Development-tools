// 產生簡報：列出收件匣的釋疑擷取 → 勾選 → 本機直接產出釋疑簡報 PPT。C# 5 語法。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using Form = System.Windows.Forms.Form;
using Rectangle = System.Drawing.Rectangle;

namespace RfiDeckAgent.Core
{
    public class DeckForm : Form
    {
        const int TW = 128, TH = 72;
        readonly ListView lv = new ListView();
        readonly ImageList thumbs = new ImageList();
        readonly TextBox txtTitle = new TextBox();
        readonly TextBox txtDate = new TextBox();
        readonly CheckBox chkDb = new CheckBox();
        readonly CheckBox chkOpen = new CheckBox();
        readonly Button btnGo = new Button();
        readonly Label lblHead = new Label();
        readonly Label lblStatus = new Label();
        readonly Font uiFont = new Font("Microsoft JhengHei UI", 10f);

        public DeckForm()
        {
            Text = "產生釋疑簡報 - DT AI AGENT";
            Font = uiFont;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1180, 720);
            MinimumSize = new Size(900, 520);
            KeyPreview = true;

            lblHead.Dock = DockStyle.Top;
            lblHead.Height = 34;
            lblHead.Padding = new Padding(10, 8, 0, 0);
            lblHead.Font = new Font(uiFont.FontFamily, 11f, FontStyle.Bold);

            thumbs.ImageSize = new Size(TW, TH);
            thumbs.ColorDepth = ColorDepth.Depth32Bit;
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.CheckBoxes = true;
            lv.FullRowSelect = true;
            lv.GridLines = true;
            lv.SmallImageList = thumbs;
            lv.HideSelection = false;
            lv.Columns.Add("截圖", TW + 40);
            lv.Columns.Add("擷取時間", 130);
            lv.Columns.Add("模型", 140);
            lv.Columns.Add("類別", 60);
            lv.Columns.Add("位置", 220);
            lv.Columns.Add("問題", 300);
            lv.Columns.Add("建議", 200);
            lv.Columns.Add("Google簡報", 100);
            lv.ItemChecked += (s, e) => UpdateCount();
            lv.DoubleClick += (s, e) => OpenSelectedImage();

            // 下方設定與按鈕
            TableLayoutPanel bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 128;
            bottom.Padding = new Padding(10, 6, 10, 6);
            bottom.ColumnCount = 4;
            bottom.RowCount = 3;
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

            bottom.Controls.Add(Lbl("簡報標題"), 0, 0);
            txtTitle.Dock = DockStyle.Fill;
            txtTitle.Text = "BIM釋疑-預計提出";
            bottom.Controls.Add(txtTitle, 1, 0);
            bottom.Controls.Add(Lbl("日期"), 2, 0);
            txtDate.Width = 120;
            DateTime t = DateTime.Today;
            txtDate.Text = string.Format("{0}.{1:00}.{2:00}", t.Year - 1911, t.Month, t.Day);
            bottom.Controls.Add(txtDate, 3, 0);

            chkDb.Text = "寫入釋疑問題庫（給追蹤編號 R###，雙週會可追蹤回覆／定案）";
            chkDb.Checked = true;
            chkDb.AutoSize = true;
            bottom.Controls.Add(chkDb, 1, 1);
            bottom.SetColumnSpan(chkDb, 3);
            chkOpen.Text = "完成後自動開啟簡報";
            chkOpen.Checked = true;
            chkOpen.AutoSize = true;

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.Dock = DockStyle.Fill;
            btns.FlowDirection = FlowDirection.LeftToRight;
            btns.WrapContents = false;
            btns.Controls.Add(chkOpen);
            btns.Controls.Add(Btn("全選", (s, e) => SetAll(true)));
            btns.Controls.Add(Btn("全不選", (s, e) => SetAll(false)));
            btns.Controls.Add(Btn("移除勾選項目", (s, e) => RemoveChecked()));
            btns.Controls.Add(Btn("送到 Google 簡報", (s, e) => SendSlides()));
            btns.Controls.Add(Btn("開啟共用簡報", (s, e) => GSlides.OpenDeck(Handle, null)));
            btns.Controls.Add(Btn("開啟收件匣", (s, e) => Entry.OpenFolder(Config.InboxDir)));
            btns.Controls.Add(Btn("開啟成果資料夾", (s, e) => Entry.OpenFolder(Path.Combine(Config.OutputDir, "簡報PPT"))));
            btnGo.Text = "產生簡報";
            btnGo.Width = 170;
            btnGo.Height = 34;
            btnGo.BackColor = Color.FromArgb(0x1F, 0x38, 0x64);
            btnGo.ForeColor = Color.White;
            btnGo.FlatStyle = FlatStyle.Flat;
            btnGo.Font = new Font(uiFont.FontFamily, 10.5f, FontStyle.Bold);
            btnGo.Click += (s, e) => Generate();
            btns.Controls.Add(btnGo);
            btns.Controls.Add(Btn("關閉", (s, e) => Close()));
            bottom.Controls.Add(btns, 0, 2);
            bottom.SetColumnSpan(btns, 4);

            lblStatus.Dock = DockStyle.Bottom;
            lblStatus.Height = 26;
            lblStatus.Padding = new Padding(10, 4, 0, 0);
            lblStatus.ForeColor = Color.DimGray;

            Controls.Add(lv);
            Controls.Add(lblHead);
            Controls.Add(bottom);
            Controls.Add(lblStatus);
            AcceptButton = btnGo;
            KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.A) { SetAll(true); e.Handled = true; } };
            Load += (s, e) => Reload();
        }

        static Label Lbl(string t)
        {
            Label l = new Label();
            l.Text = t;
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            return l;
        }

        static Button Btn(string t, EventHandler h)
        {
            Button b = new Button();
            b.Text = t;
            b.AutoSize = true;
            b.Height = 34;
            b.Click += h;
            return b;
        }

        // ------------------------------------------------------------ 清單
        void Reload()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (Image im in thumbs.Images) im.Dispose();
            thumbs.Images.Clear();
            List<string> dirs = Inbox.ListPending();
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in dirs)
            {
                RfiDraft d = Inbox.LoadItem(dir);
                if (d == null) continue;
                string first = d.Shots.Count > 0 ? Path.Combine(dir, d.Shots[0].File) : null;
                thumbs.Images.Add(Thumb(first));
                ListViewItem it = new ListViewItem(string.Format("{0} 張", d.Shots.Count), thumbs.Images.Count - 1);
                it.SubItems.Add(d.CreatedAt);
                it.SubItems.Add(d.Project);
                it.SubItems.Add(d.Category);
                it.SubItems.Add((d.Building + " " + d.Location).Trim());
                it.SubItems.Add(OneLine(d.Question));
                it.SubItems.Add(OneLine(d.Suggest));
                string gno = GSlides.SentNo(dir);
                it.SubItems.Add(gno.Length > 0 ? "✔ " + gno : "未同步");
                it.Tag = dir;
                it.ToolTipText = d.Question;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            lv.ShowItemToolTips = true;
            lblHead.Text = dirs.Count == 0
                ? "收件匣目前是空的 — 請先用「釋疑擷取」截圖並輸入問題"
                : string.Format("收件匣共 {0} 筆釋疑擷取，勾選要放進這份簡報的項目（雙擊可看截圖）", lv.Items.Count);
            UpdateCount();
        }

        static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 80 ? s.Substring(0, 79) + "…" : s;
        }

        static Image Thumb(string path)
        {
            Bitmap bmp = new Bitmap(TW, TH);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                if (path != null && File.Exists(path))
                {
                    try
                    {
                        using (FileStream fs = File.OpenRead(path))
                        using (Image src = Image.FromStream(fs))
                        {
                            double k = Math.Min((double)TW / src.Width, (double)TH / src.Height);
                            int w = (int)(src.Width * k), h = (int)(src.Height * k);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(src, new Rectangle((TW - w) / 2, (TH - h) / 2, w, h));
                        }
                    }
                    catch { }
                }
                g.DrawRectangle(Pens.Silver, 0, 0, TW - 1, TH - 1);
            }
            return bmp;
        }

        List<string> Checked()
        {
            return lv.CheckedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList();
        }

        void UpdateCount()
        {
            int n = lv.CheckedItems.Count;
            btnGo.Text = n > 0 ? string.Format("產生簡報（{0} 筆）", n) : "產生簡報";
            btnGo.Enabled = n > 0;
        }

        void SetAll(bool v)
        {
            lv.BeginUpdate();
            foreach (ListViewItem i in lv.Items) i.Checked = v;
            lv.EndUpdate();
            UpdateCount();
        }

        void OpenSelectedImage()
        {
            if (lv.SelectedItems.Count == 0) return;
            string dir = (string)lv.SelectedItems[0].Tag;
            RfiDraft d = Inbox.LoadItem(dir);
            if (d != null && d.Shots.Count > 0)
            {
                string f = Path.Combine(dir, d.Shots[0].File);
                if (File.Exists(f)) Process.Start(f);
            }
            else Entry.OpenFolder(dir);
        }

        void RemoveChecked()
        {
            List<string> sel = Checked();
            if (sel.Count == 0) { MessageBox.Show(this, "請先勾選要移除的項目。", Text); return; }
            if (MessageBox.Show(this, string.Format("把勾選的 {0} 筆移到「收件匣\\_已移除」？\n（不會真的刪除，之後可以再搬回來）", sel.Count),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string dst = Path.Combine(Config.InboxDir, "_已移除");
            Directory.CreateDirectory(dst);
            foreach (string d in sel)
            {
                try
                {
                    string target = Path.Combine(dst, Path.GetFileName(d));
                    if (Directory.Exists(target)) target += "_" + DateTime.Now.ToString("HHmmss");
                    Directory.Move(d, target);
                }
                catch (Exception ex) { MessageBox.Show(this, "移除失敗：" + ex.Message, Text); }
            }
            Reload();
        }

        // ------------------------------------------------------------ Google 簡報
        void SendSlides()
        {
            if (!GSlides.Configured)
            {
                MessageBox.Show(this, "還沒設定 Google 簡報連線，請在「釋疑簡報」選單按「設定」。", Text);
                return;
            }
            List<string> sel = Checked();
            if (sel.Count == 0) { MessageBox.Show(this, "請先勾選要送出的項目。", Text); return; }
            UseWaitCursor = true;
            int ok = 0, skip = 0;
            List<string> errs = new List<string>();
            try
            {
                for (int i = 0; i < sel.Count; i++)
                {
                    string dir = sel[i];
                    if (GSlides.SentNo(dir).Length > 0) { skip++; continue; }
                    Status(string.Format("送到 Google 簡報 {0}/{1}…", i + 1, sel.Count));
                    RfiDraft d = Inbox.LoadItem(dir);
                    if (d == null) { errs.Add(Path.GetFileName(dir) + "：讀不到資料"); continue; }
                    Dictionary<string, object> r = GSlides.Send(dir, d);
                    if (GSlides.Ok(r)) ok++;
                    else errs.Add(Path.GetFileName(dir) + "：" + GSlides.Str(r, "error"));
                }
            }
            finally { UseWaitCursor = false; }
            Status(string.Format("Google 簡報：送出 {0} 筆" + (skip > 0 ? "，{1} 筆先前已同步" : "") + (errs.Count > 0 ? "，{2} 筆失敗" : ""), ok, skip, errs.Count));
            if (errs.Count > 0) MessageBox.Show(this, string.Join("\n", errs.ToArray()), "送出失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Reload();
        }

        // ------------------------------------------------------------ 產生
        void Status(string s)
        {
            lblStatus.Text = s;
            lblStatus.Refresh();
            Application.DoEvents();
        }

        void Generate()
        {
            List<string> sel = Checked();
            if (sel.Count == 0) return;
            // 產生簡報的程式內嵌在外掛裡；工作資料夾（收件匣、問題庫、成果）以 --root 傳入
            string script = ScriptStore.Resolve("revit_deck.py");
            if (!File.Exists(script))
            {
                MessageBox.Show(this, "找不到產生簡報的程式：" + script, Text);
                return;
            }
            if (!PythonSetupForm.EnsureInteractive(this)) return;
            UseWaitCursor = true;
            btnGo.Enabled = false;
            try
            {
                Status("檢查 Python 環境…");
                string err = PyRunner.EnsurePackages(Status);
                if (err != null)
                {
                    MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Status(string.Format("產生簡報中（{0} 筆）…", sel.Count));
                string args = PyRunner.Q(script) + " --root " + PyRunner.Q(Config.Root) + " --items " + string.Join(" ", sel.Select(d => PyRunner.Q(Path.GetFileName(d))).ToArray()) +
                              " --title " + PyRunner.Q(txtTitle.Text.Trim()) + " --date " + PyRunner.Q(txtDate.Text.Trim()) +
                              (chkDb.Checked ? "" : " --no-db");
                PyResult r = PyRunner.Run(args, 300000);
                object ok;
                if (r.Result != null && r.Result.TryGetValue("ok", out ok) && ok is bool && (bool)ok)
                {
                    string pptx = Convert.ToString(r.Result["pptx"]);
                    string tids = "";
                    object o;
                    if (r.Result.TryGetValue("tids", out o) && o is System.Collections.ArrayList && ((System.Collections.ArrayList)o).Count > 0)
                        tids = "　追蹤編號：" + string.Join("、", ((System.Collections.ArrayList)o).Cast<object>().Select(x => x.ToString()).ToArray());
                    Status("✔ 已產出：" + Path.GetFileName(pptx) + tids);
                    if (chkOpen.Checked && File.Exists(pptx)) Process.Start(pptx);
                    Reload();
                }
                else
                {
                    string msg = r.Result != null && r.Result.ContainsKey("error") ? Convert.ToString(r.Result["error"]) : "";
                    if (r.Result != null && r.Result.ContainsKey("trace")) msg += "\n\n" + r.Result["trace"];
                    if (msg.Length == 0) msg = (r.Error + "\n" + r.Output).Trim();
                    Status("✖ 產生失敗");
                    MessageBox.Show(this, "產生簡報失敗：\n" + msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                UseWaitCursor = false;
                UpdateCount();
            }
        }
    }
}
