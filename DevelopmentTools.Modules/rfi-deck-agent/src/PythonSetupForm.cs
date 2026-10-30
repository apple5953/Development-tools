// 第一次使用、電腦沒有 Python 時跳出的安裝教學。公司電腦的 C 槽通常受管理不能安裝，所以預設裝到 D 槽（其他非系統碟）。
// 安裝檔來源：圖說包的「Python安裝檔」資料夾 → 或從 python.org 下載官方安裝檔。裝好後自動安裝套件（圖說包有離線套件就用離線的）。C# 5。
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using Form = System.Windows.Forms.Form;

namespace RfiDeckAgent.Core
{
    public class PythonSetupForm : Form
    {
        public const string PythonVersion = "3.12.10";
        public static string DownloadUrl { get { return "https://www.python.org/ftp/python/" + PythonVersion + "/python-" + PythonVersion + "-amd64.exe"; } }
        const string Packages = "python-pptx openpyxl pymupdf pillow";

        readonly TextBox txtInstaller = new TextBox();
        readonly TextBox txtTarget = new TextBox();
        readonly Label lblStatus = new Label();
        readonly TextBox txtManual = new TextBox();
        readonly Button btnAuto = new Button();
        bool busy;

        /// <summary>有 Python 就直接回傳 true；沒有就顯示安裝教學，裝好回傳 true</summary>
        public static bool EnsureInteractive(IWin32Window owner)
        {
            if (PyRunner.HasPython) return true;
            using (PythonSetupForm f = new PythonSetupForm()) f.ShowDialog(owner);
            return PyRunner.HasPython;
        }

        public PythonSetupForm()
        {
            Text = "安裝 Python - 釋疑簡報";
            Font = new Font("Microsoft JhengHei UI", 10f);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(780, 640);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.Padding = new Padding(16);
            t.ColumnCount = 3;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            Controls.Add(t);

            Label head = new Label();
            head.Text = "第一次使用：需要安裝 Python（約 3 分鐘，只要一次）";
            head.Font = new Font("Microsoft JhengHei UI", 12f, FontStyle.Bold);
            head.ForeColor = Color.FromArgb(0x1F, 0x38, 0x64);
            head.AutoSize = true;
            Span(t, head, 32);

            Label intro = new Label();
            intro.Text = "產生簡報與對照圖說要用到 Python。公司電腦的 C 槽通常受管理不能安裝，所以會裝到下面的位置（預設 D 槽）。\n" +
                         "按「自動安裝」會用下面的安裝檔、只裝給你自己的帳號（不需要系統管理員），裝好後自動安裝需要的套件。";
            intro.AutoSize = false;
            intro.Height = 58;
            intro.ForeColor = Color.FromArgb(70, 70, 70);
            Span(t, intro, 62);

            txtInstaller.Text = FindInstaller();
            Button browse = new Button(); browse.Text = "瀏覽…";
            browse.Click += delegate
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Filter = "Python 安裝檔 (python-*.exe)|python-*.exe|所有檔案|*.*";
                    if (dlg.ShowDialog(this) == DialogResult.OK) txtInstaller.Text = dlg.FileName;
                }
            };
            Row(t, "① 安裝檔", txtInstaller, browse);
            Button dl = new Button(); dl.Text = "從 python.org 下載";
            dl.Click += delegate { Download(); };
            Label dlNote = new Label();
            dlNote.Text = "圖說包的「Python安裝檔」資料夾有附安裝檔就會自動帶入；沒有可以按右邊從官網下載（需要網路）。";
            dlNote.ForeColor = Color.DimGray; dlNote.AutoSize = false; dlNote.Height = 40;
            Row(t, "", dlNote, dl);

            txtTarget.Text = DefaultTarget();
            Button browse2 = new Button(); browse2.Text = "瀏覽…";
            browse2.Click += delegate
            {
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "選擇 Python 安裝位置（請選 C 槽以外的磁碟）";
                    if (dlg.ShowDialog(this) == DialogResult.OK) txtTarget.Text = Path.Combine(dlg.SelectedPath, "Python312");
                }
            };
            Row(t, "② 安裝位置", txtTarget, browse2);

            FlowLayoutPanel acts = new FlowLayoutPanel();
            acts.AutoSize = true;
            btnAuto.Text = "③ 自動安裝";
            btnAuto.AutoSize = true; btnAuto.Height = 36;
            btnAuto.BackColor = Color.FromArgb(46, 139, 62); btnAuto.ForeColor = Color.White; btnAuto.FlatStyle = FlatStyle.Flat;
            btnAuto.Click += delegate { AutoInstall(); };
            Button pick = new Button(); pick.Text = "我已裝好，指定 python.exe…"; pick.AutoSize = true; pick.Height = 36;
            pick.Click += delegate { PickExisting(); };
            Button cancel = new Button(); cancel.Text = "以後再說"; cancel.AutoSize = true; cancel.Height = 36;
            cancel.Click += delegate { if (!busy) Close(); };
            acts.Controls.Add(btnAuto); acts.Controls.Add(pick); acts.Controls.Add(cancel);
            Span(t, acts, 46);

            lblStatus.AutoSize = false; lblStatus.Height = 44;
            lblStatus.ForeColor = Color.FromArgb(31, 95, 191);
            Span(t, lblStatus, 46);

            Label mh = new Label();
            mh.Text = "自動安裝不成功（例如被防毒擋下）時，請照下面手動安裝：";
            mh.Font = new Font("Microsoft JhengHei UI", 10f, FontStyle.Bold);
            mh.AutoSize = true;
            Span(t, mh, 26);
            txtManual.Multiline = true; txtManual.ReadOnly = true; txtManual.ScrollBars = ScrollBars.Vertical;
            txtManual.BackColor = Color.FromArgb(248, 248, 248);
            txtManual.Text = ManualText();
            Span(t, txtManual, 190);
        }

        static void Span(TableLayoutPanel t, Control c, int h)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, 3);
        }

        static void Row(TableLayoutPanel t, string label, Control c, Control right)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Label l = new Label();
            l.Text = label; l.Dock = DockStyle.Fill; l.TextAlign = ContentAlignment.MiddleLeft;
            l.Font = new Font("Microsoft JhengHei UI", 10f, FontStyle.Bold);
            t.Controls.Add(l, 0, r);
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c, 1, r);
            if (right != null) { right.Dock = DockStyle.Fill; t.Controls.Add(right, 2, r); }
            else t.SetColumnSpan(c, 2);
        }

        string ManualText()
        {
            string target = txtTarget.Text.Length > 0 ? txtTarget.Text : @"D:\Python312";
            return string.Join("\r\n", new[] {
                "1. 執行安裝檔（python-" + PythonVersion + "-amd64.exe）。",
                "2. 第一個畫面：最下面兩個勾選框都取消勾選（Use admin privileges…、Add python.exe to PATH 不需要），",
                "   然後按「Customize installation」。不要按「Install Now」，那會裝到 C 槽。",
                "3. Optional Features：保留預設（pip 要勾），按 Next。",
                "4. Advanced Options：",
                "   - 取消勾選「Install Python " + PythonVersion.Substring(0, 4) + " for all users」",
                "   - 「Customize install location」改成：" + target,
                "   - 按 Install，等它跑完按 Close。",
                "5. 回到這個視窗，按「我已裝好，指定 python.exe…」，選 " + Path.Combine(target, "python.exe") + "。",
                "   之後第一次產生簡報或找對照圖說時，會自動安裝需要的套件。",
                "",
                "還是不行：請把畫面截圖給專案負責人，或請 IT 協助把 Python 裝到 C 槽以外的位置。" });
        }

        static string DefaultTarget()
        {
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    if (d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)) continue;
                    return Path.Combine(d.RootDirectory.FullName, "Python312");
                }
            }
            catch { }
            return @"D:\Python312";
        }

        static string FindInstaller()
        {
            try
            {
                string d = Config.DrawingDir;
                if (d.Length > 0)
                {
                    string dir = Path.Combine(d, "Python安裝檔");
                    if (Directory.Exists(dir))
                    {
                        string f = Directory.GetFiles(dir, "python-3*.exe").OrderByDescending(x => x).FirstOrDefault();
                        if (f != null) return f;
                    }
                }
                string dl = Path.Combine(DtStorage.TempDir, "python-" + PythonVersion + "-amd64.exe");
                if (File.Exists(dl)) return dl;
            }
            catch { }
            return "";
        }

        void Status(string s, bool error)
        {
            lblStatus.ForeColor = error ? Color.FromArgb(217, 58, 58) : Color.FromArgb(31, 95, 191);
            lblStatus.Text = s;
            lblStatus.Refresh();
            Application.DoEvents();
        }

        void Download()
        {
            if (busy) return;
            busy = true;
            string dst = Path.Combine(DtStorage.TempDir, "python-" + PythonVersion + "-amd64.exe");
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (WebClient wc = new WebClient())
                {
                    bool done = false;
                    Exception err = null;
                    wc.DownloadProgressChanged += (s, e) => Status("下載中… " + e.ProgressPercentage + "%", false);
                    wc.DownloadFileCompleted += (s, e) => { err = e.Error; done = true; };
                    wc.DownloadFileAsync(new Uri(DownloadUrl), dst + ".part");
                    while (!done) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
                    if (err != null) throw err;
                }
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(dst + ".part", dst);
                txtInstaller.Text = dst;
                Status("✔ 已下載安裝檔，可以按「③ 自動安裝」。", false);
            }
            catch (Exception ex)
            {
                Status("✖ 下載失敗：" + ex.Message + "\n可以請專案負責人提供附安裝檔的圖說包，或自行到 python.org 下載 " + PythonVersion + "（Windows 64-bit）。", true);
            }
            finally { busy = false; }
        }

        void AutoInstall()
        {
            if (busy) return;
            string exe = txtInstaller.Text.Trim().Trim('"');
            string target = txtTarget.Text.Trim().Trim('"');
            if (!File.Exists(exe)) { Status("請先指定安裝檔（或按「從 python.org 下載」）。", true); return; }
            if (target.Length < 3) { Status("請指定安裝位置。", true); return; }
            if (target.StartsWith(@"C:", StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this, "安裝位置在 C 槽，公司電腦可能不允許。確定要裝在 C 槽？", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            busy = true;
            btnAuto.Enabled = false;
            try
            {
                Status("安裝 Python 中…（會出現安裝進度視窗，請稍候）", false);
                ProcessStartInfo psi = new ProcessStartInfo(exe,
                    "/passive InstallAllUsers=0 TargetDir=\"" + target + "\" PrependPath=0 Include_launcher=0 InstallLauncherAllUsers=0 " +
                    "Include_test=0 Include_doc=0 Include_tcltk=0 AssociateFiles=0 Shortcuts=0 Include_pip=1");
                psi.UseShellExecute = true;
                using (Process p = Process.Start(psi))
                {
                    while (!p.HasExited) { Application.DoEvents(); System.Threading.Thread.Sleep(200); }
                }
                string py = Path.Combine(target, "python.exe");
                if (!File.Exists(py))
                {
                    Status("✖ 沒有裝好（可能被防毒或公司政策擋下）。請照下方說明手動安裝。", true);
                    return;
                }
                PyRunner.SetPython(py);
                Status("✔ Python 已裝好。接著安裝套件（" + Packages + "）…", false);
                string err = PyRunner.InstallPackages(Packages, s => Status(s, false));
                if (err != null)
                {
                    Status("Python 已裝好，但套件沒有裝成功（第一次使用時會再試一次）。\n" + Short(err), true);
                    return;
                }
                Status("✔ 全部完成，可以關閉這個視窗。", false);
                MessageBox.Show(this, "Python 與套件已安裝完成：\n" + py, Text);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Status("✖ 安裝失敗：" + ex.Message + "\n請照下方說明手動安裝。", true);
            }
            finally
            {
                busy = false;
                btnAuto.Enabled = true;
            }
        }

        void PickExisting()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "python.exe|python.exe";
                dlg.FileName = "python.exe";
                try { string d = txtTarget.Text.Trim(); if (Directory.Exists(d)) dlg.InitialDirectory = d; } catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                PyRunner.SetPython(dlg.FileName);
                Status("✔ 已指定 " + dlg.FileName + "。第一次使用時會自動安裝需要的套件。", false);
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        static string Short(string s) { s = s ?? ""; return s.Length > 400 ? s.Substring(s.Length - 400) : s; }
    }
}
