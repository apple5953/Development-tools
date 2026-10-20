// 設定：AI AGENT 資料夾＋Google 簡報連線。C# 5。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using Form = System.Windows.Forms.Form;

namespace RfiDeckAgent.Core
{
    public class SettingsForm : Form
    {
        readonly TextBox txtRoot = new TextBox();
        readonly TextBox txtUrl = new TextBox();
        readonly TextBox txtSecret = new TextBox();
        readonly TextBox txtDeck = new TextBox();
        readonly CheckBox chkAuto = new CheckBox();
        readonly Label lblTest = new Label();

        public SettingsForm()
        {
            Text = "設定 - DT AI AGENT";
            Font = new Font("Microsoft JhengHei UI", 10f);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(760, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.Padding = new Padding(14);
            t.ColumnCount = 3;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            Controls.Add(t);

            GsConfig c = GSlides.Load();
            txtRoot.Text = Config.Root;
            txtUrl.Text = c.Url;
            txtSecret.Text = c.Secret;
            txtDeck.Text = c.DeckUrl;
            chkAuto.Text = "釋疑擷取按「完成」後，自動送到 Google 共用簡報";
            chkAuto.Checked = c.AutoSend;
            chkAuto.AutoSize = true;

            Button browse = new Button();
            browse.Text = "瀏覽…";
            browse.Click += delegate
            {
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.SelectedPath = txtRoot.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK) txtRoot.Text = dlg.SelectedPath;
                }
            };
            Row(t, "AI AGENT 資料夾", txtRoot, browse);
            Header(t, "Google 共用簡報（多人同步）");
            Row(t, "網頁應用程式網址", txtUrl, null);
            Row(t, "密鑰", txtSecret, null);
            Button test = new Button();
            test.Text = "測試連線";
            test.Click += delegate { Test(); };
            Row(t, "共用簡報網址", txtDeck, test);
            Row(t, "", chkAuto, null);
            lblTest.AutoSize = false;
            lblTest.Height = 48;
            lblTest.Dock = DockStyle.Fill;
            lblTest.ForeColor = Color.DimGray;
            lblTest.Text = "網址與密鑰在共用簡報的選單「DT AI AGENT → ② 顯示連線資訊」與 Apps Script「部署」取得。\n同事的 Revit 填同一組即可。";
            Row(t, "", lblTest, null);

            FlowLayoutPanel b = new FlowLayoutPanel();
            b.FlowDirection = FlowDirection.RightToLeft;
            b.Dock = DockStyle.Fill;
            Button ok = new Button(); ok.Text = "儲存"; ok.Width = 100; ok.Height = 34;
            ok.Click += delegate { SaveAll(); DialogResult = DialogResult.OK; Close(); };
            Button cancel = new Button(); cancel.Text = "取消"; cancel.Width = 100; cancel.Height = 34;
            cancel.Click += delegate { Close(); };
            b.Controls.Add(ok);
            b.Controls.Add(cancel);
            Row(t, "", b, null);
            AcceptButton = ok;
        }

        static void Header(TableLayoutPanel t, string text)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            Label l = new Label();
            l.Text = text;
            l.Font = new Font("Microsoft JhengHei UI", 10.5f, FontStyle.Bold);
            l.ForeColor = Color.FromArgb(0x1F, 0x38, 0x64);
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.BottomLeft;
            t.Controls.Add(l, 0, r);
            t.SetColumnSpan(l, 3);
        }

        static void Row(TableLayoutPanel t, string label, Control c, Control right)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label l = new Label();
            l.Text = label;
            l.AutoSize = false;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleRight;
            t.Controls.Add(l, 0, r);
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c, 1, r);
            if (right != null) { right.Dock = DockStyle.Fill; t.Controls.Add(right, 2, r); }
            else t.SetColumnSpan(c, 2);
        }

        GsConfig Current()
        {
            GsConfig c = GSlides.Load();
            c.Url = txtUrl.Text.Trim();
            c.Secret = txtSecret.Text.Trim();
            c.DeckUrl = txtDeck.Text.Trim();
            c.AutoSend = chkAuto.Checked;
            return c;
        }

        void Test()
        {
            GsConfig c = Current();
            if (c.Url.Length == 0 || c.Secret.Length == 0) { lblTest.Text = "請先填網址與密鑰。"; return; }
            lblTest.Text = "連線中…";
            lblTest.Refresh();
            UseWaitCursor = true;
            Dictionary<string, object> r = GSlides.Ping(c);
            UseWaitCursor = false;
            if (GSlides.Ok(r))
            {
                txtDeck.Text = GSlides.Str(r, "presentation");
                lblTest.ForeColor = Color.FromArgb(46, 139, 62);
                lblTest.Text = "✔ 連線成功。共用簡報目前編號到 Q" + GSlides.Str(r, "seq").PadLeft(3, '0') + "。按「儲存」完成設定。";
            }
            else
            {
                lblTest.ForeColor = Color.FromArgb(217, 58, 58);
                lblTest.Text = "✖ " + GSlides.Str(r, "error");
            }
        }

        void SaveAll()
        {
            if (txtRoot.Text.Trim().Length > 0) Config.Root = txtRoot.Text.Trim();
            GSlides.Save(Current());
        }
    }
}
