// 釋疑擷取視窗（WinForms，C# 5）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RfiDeckAgent.Core
{
    public enum CaptureAction { Cancel, KeepDraft, Commit, Discard }

    public class CaptureForm : Form
    {
        readonly RfiDraft d;
        public CaptureAction Action = CaptureAction.Cancel;
        public bool SendToSlides;
        CheckBox chkSlides;

        ComboBox cboCat, cboBuilding, cboStatus;
        TextBox txtLoc, txtDrawing, txtQ, txtS;
        FlowLayoutPanel shots;
        readonly List<TextBox> captionBoxes = new List<TextBox>();
        static readonly string[] Cats = { "H 圖說釋疑", "B 結構設計", "C 建築設計", "D 介面衝突" };
        static readonly Font F = new Font("Microsoft JhengHei UI", 10f);
        static readonly Font FB = new Font("Microsoft JhengHei UI", 10f, FontStyle.Bold);

        public CaptureForm(RfiDraft draft, string contextNote)
        {
            d = draft;
            Text = "釋疑擷取 - DT AI AGENT";
            Font = F;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(980, 760);
            MinimumSize = new Size(820, 620);
            FormBorderStyle = FormBorderStyle.Sizable;
            KeyPreview = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            root.Padding = new Padding(10);
            Controls.Add(root);

            // 左：截圖
            GroupBox gShots = new GroupBox();
            gShots.Text = string.Format("截圖（{0} 張）— 圖下方輸入紅字圖名", d.Shots.Count);
            gShots.Font = FB;
            gShots.Dock = DockStyle.Fill;
            shots = new FlowLayoutPanel();
            shots.Dock = DockStyle.Fill;
            shots.AutoScroll = true;
            shots.FlowDirection = FlowDirection.TopDown;
            shots.WrapContents = false;
            shots.Font = F;
            gShots.Controls.Add(shots);
            root.Controls.Add(gShots, 0, 0);
            BuildShots();

            // 右：欄位
            TableLayoutPanel f = new TableLayoutPanel();
            f.Dock = DockStyle.Fill;
            f.ColumnCount = 2;
            f.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(f, 1, 0);

            Label ctx = new Label();
            ctx.Text = contextNote;
            ctx.AutoSize = false;
            ctx.Height = 66;
            ctx.Dock = DockStyle.Fill;
            ctx.ForeColor = Color.DimGray;
            ctx.Font = new Font("Microsoft JhengHei UI", 9f);
            AddRow(f, "模型情境", ctx, 70);

            cboCat = new ComboBox();
            cboCat.DropDownStyle = ComboBoxStyle.DropDownList;
            cboCat.Items.AddRange(Cats);
            cboCat.SelectedIndex = Math.Max(0, Array.FindIndex(Cats, c => c.StartsWith(d.Category ?? "D")));
            AddRow(f, "類別", cboCat, 32);

            cboBuilding = new ComboBox();
            cboBuilding.Items.AddRange(new object[] { "行政棟", "實驗棟", "地下層", "歷史建築", "全區", "景觀" });
            cboBuilding.Text = d.Building;
            AddRow(f, "棟別", cboBuilding, 32);

            txtLoc = Box(d.Location, false);
            AddRow(f, "位置", txtLoc, 32);

            txtDrawing = Box(d.Drawing, false);
            AddRow(f, "圖說編號名稱", txtDrawing, 32);

            txtQ = Box(d.Question, true);
            AddRow(f, "問題", txtQ, 150);

            txtS = Box(d.Suggest, true);
            AddRow(f, "建議", txtS, 100);

            cboStatus = new ComboBox();
            cboStatus.DropDownStyle = ComboBoxStyle.DropDownList;
            cboStatus.Items.AddRange(new object[] { "預計提出", "已提出", "暫不提出" });
            cboStatus.SelectedItem = string.IsNullOrEmpty(d.Status) ? "預計提出" : d.Status;
            if (cboStatus.SelectedIndex < 0) cboStatus.SelectedIndex = 0;
            AddRow(f, "狀態", cboStatus, 32);

            chkSlides = new CheckBox();
            chkSlides.AutoSize = true;
            if (GSlides.Configured)
            {
                chkSlides.Text = "完成後同步到 Google 共用簡報";
                chkSlides.Checked = GSlides.Load().AutoSend;
            }
            else
            {
                chkSlides.Text = "同步到 Google 共用簡報（尚未設定，請按「設定」）";
                chkSlides.Enabled = false;
            }
            AddRow(f, "", chkSlides, 30);

            Label hint = new Label();
            hint.Text = "提示：問題可分點（1. 2.），句尾用問號；建議寫具體做法（尺寸／位置）。\n" +
                        "圖說編號可留空，AI 會依位置查圖說索引補上。";
            hint.ForeColor = Color.DimGray;
            hint.AutoSize = false;
            hint.Dock = DockStyle.Fill;
            hint.Font = new Font("Microsoft JhengHei UI", 9f);
            AddRow(f, "", hint, 44);

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.Dock = DockStyle.Fill;
            btns.FlowDirection = FlowDirection.RightToLeft;
            Button bCommit = Btn("完成，存入收件匣", Color.FromArgb(46, 139, 62));
            bCommit.Click += delegate { if (Collect(true)) { Action = CaptureAction.Commit; Close(); } };
            Button bMore = Btn("先存草稿，再截下一張", Color.FromArgb(31, 95, 191));
            bMore.Click += delegate { Collect(false); Action = CaptureAction.KeepDraft; Close(); };
            Button bSeek = Btn("找對照圖說…", Color.FromArgb(0x8E, 0x5B, 0x0A));
            bSeek.Click += delegate { SeekDrawings(); };
            Button bDiscard = Btn("放棄這筆", Color.Gray);
            bDiscard.Click += delegate
            {
                if (MessageBox.Show(this, "確定清除這筆草稿與所有截圖？", "放棄", MessageBoxButtons.YesNo) == DialogResult.Yes)
                { Action = CaptureAction.Discard; Close(); }
            };
            btns.Controls.Add(bCommit);
            btns.Controls.Add(bMore);
            btns.Controls.Add(bSeek);
            btns.Controls.Add(bDiscard);
            AddRow(f, "", btns, 48);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.Enter && Collect(true)) { Action = CaptureAction.Commit; Close(); }
            };
            Shown += delegate { txtQ.Focus(); };
        }

        static TextBox Box(string v, bool multi)
        {
            TextBox t = new TextBox();
            t.Text = v ?? "";
            t.Dock = DockStyle.Fill;
            t.Multiline = multi;
            if (multi) { t.ScrollBars = ScrollBars.Vertical; t.AcceptsReturn = true; }
            return t;
        }

        static Button Btn(string text, Color c)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.Height = 36;
            b.Padding = new Padding(8, 2, 8, 2);
            b.BackColor = c;
            b.ForeColor = Color.White;
            b.FlatStyle = FlatStyle.Flat;
            b.Font = new Font("Microsoft JhengHei UI", 10f, FontStyle.Bold);
            return b;
        }

        static void AddRow(TableLayoutPanel t, string label, Control c, int height)
        {
            int r = t.RowCount;
            t.RowCount = r + 1;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Label l = new Label();
            l.Text = label;
            l.TextAlign = ContentAlignment.MiddleRight;
            l.Dock = DockStyle.Fill;
            l.Font = FB;
            t.Controls.Add(l, 0, r);
            t.Controls.Add(c, 1, r);
        }

        void BuildShots()
        {
            shots.Controls.Clear();
            captionBoxes.Clear();
            for (int i = 0; i < d.Shots.Count; i++)
            {
                ShotInfo s = d.Shots[i];
                Panel p = new Panel();
                p.Width = 380;
                p.Height = 262;
                PictureBox pb = new PictureBox();
                pb.SizeMode = PictureBoxSizeMode.Zoom;
                pb.SetBounds(0, 0, 370, 210);
                pb.BorderStyle = BorderStyle.FixedSingle;
                try
                {
                    using (FileStream fs = new FileStream(Path.Combine(Config.DraftDir, s.File), FileMode.Open, FileAccess.Read))
                    {
                        pb.Image = Image.FromStream(fs);
                    }
                }
                catch { }
                TextBox cap = new TextBox();
                cap.Text = s.Caption;
                cap.SetBounds(0, 216, 300, 28);
                cap.ForeColor = Color.FromArgb(224, 48, 48);
                Button del = new Button();
                del.Text = "刪除";
                del.SetBounds(306, 214, 64, 30);
                int idx = i;
                del.Click += delegate
                {
                    Collect(false);
                    d.Shots.RemoveAt(idx);
                    BuildShots();
                };
                p.Controls.Add(pb);
                p.Controls.Add(cap);
                p.Controls.Add(del);
                shots.Controls.Add(p);
                captionBoxes.Add(cap);
            }
        }

        void SeekDrawings()
        {
            Collect(false);
            if (!File.Exists(Path.Combine(PyRunner.ScriptDir, "seek_lookup.py")))
            {
                MessageBox.Show(this, "找不到 seek_lookup.py，請確認 AI AGENT 資料夾設定。", "對照圖說");
                return;
            }
            Cursor = Cursors.WaitCursor;
            Dictionary<string, object> r;
            try { r = SeekForm.Run(d, d.Building); }
            finally { Cursor = Cursors.Default; }
            object ok;
            if (r == null || !r.TryGetValue("ok", out ok) || !(ok is bool) || !(bool)ok)
            {
                object e;
                MessageBox.Show(this, "找圖說失敗：" + (r != null && r.TryGetValue("error", out e) ? Convert.ToString(e) : ""), "對照圖說");
                return;
            }
            using (SeekForm f = new SeekForm(r))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                foreach (SeekPick pk in f.Picked)
                {
                    if (!File.Exists(pk.Png)) continue;
                    string dst = Inbox.NewShotPath(d);
                    File.Copy(pk.Png, dst, true);
                    ShotInfo si = new ShotInfo();
                    si.File = Path.GetFileName(dst);
                    si.Caption = pk.Caption;
                    si.View = "圖說";
                    si.Time = DateTime.Now.ToString("HH:mm:ss");
                    d.Shots.Add(si);
                    if (string.IsNullOrEmpty(d.Drawing)) d.Drawing = pk.Caption;
                    else if (!d.Drawing.Contains(pk.Caption.Split('（')[0])) d.Drawing += "\n" + pk.Caption.Split('（')[0];
                }
                txtDrawing.Text = d.Drawing.Replace("\n", "；");
                Inbox.SaveDraft(d);
                BuildShots();
            }
        }

        bool Collect(bool validate)
        {
            for (int i = 0; i < captionBoxes.Count && i < d.Shots.Count; i++) d.Shots[i].Caption = captionBoxes[i].Text.Trim();
            d.Category = cboCat.SelectedIndex >= 0 ? Cats[cboCat.SelectedIndex].Substring(0, 1) : "D";
            d.Building = cboBuilding.Text.Trim();
            d.Location = txtLoc.Text.Trim();
            d.Drawing = txtDrawing.Text.Trim();
            d.Question = txtQ.Text.Trim();
            d.Suggest = txtS.Text.Trim();
            d.Status = cboStatus.SelectedItem as string ?? "預計提出";
            SendToSlides = chkSlides.Enabled && chkSlides.Checked;
            if (validate)
            {
                if (d.Question.Length == 0)
                {
                    MessageBox.Show(this, "請輸入「問題」內容。", "釋疑擷取");
                    txtQ.Focus();
                    return false;
                }
                if (d.Shots.Count == 0)
                {
                    MessageBox.Show(this, "至少需要一張截圖。", "釋疑擷取");
                    return false;
                }
            }
            return true;
        }
    }
}
