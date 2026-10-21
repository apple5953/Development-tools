using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DT_WordPdfLink.Services;
using DT_WordPdfLink.Utils;

namespace DT_WordPdfLink.Forms
{
    public class PageSelectionForm : Form
    {
        private readonly string _wordPath;
        private readonly string _fullPdfPath;
        private readonly int _pageCount;
        private ComboBox _cboPage;
        private PictureBox _picPreview;
        private Label _lblPageInfo;
        private Button _btnOk;
        private Button _btnCancel;

        public int SelectedPageNumber { get; private set; }

        public PageSelectionForm(string wordPath, string fullPdfPath, int pageCount)
        {
            SelectedPageNumber = 1;
            _wordPath = wordPath;
            _fullPdfPath = fullPdfPath;
            _pageCount = pageCount;

            InitializeUI();
            LoadPagePreview(1);
        }

        private void InitializeUI()
        {
            Text = "選擇要連結的 Word 頁面";
            Width = 520;
            Height = 620;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            Label lblHeader = new Label
            {
                Left = 20,
                Top = 15,
                Width = 460,
                Height = 22,
                Text = string.Format("此文件共有 {0} 頁，請選擇欲連結至圖紙的頁面：", _pageCount),
                Font = new Font("Microsoft JhengHei", 9.5f, FontStyle.Bold)
            };

            Label lblSelect = new Label
            {
                Left = 20,
                Top = 45,
                Width = 80,
                Height = 25,
                Text = "選擇頁面：",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft JhengHei", 9f)
            };

            _cboPage = new ComboBox
            {
                Left = 105,
                Top = 45,
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Microsoft JhengHei", 9f)
            };

            for (int i = 1; i <= _pageCount; i++)
            {
                _cboPage.Items.Add(string.Format("第 {0} 頁 / 共 {1} 頁", i, _pageCount));
            }
            _cboPage.SelectedIndex = 0;
            _cboPage.SelectedIndexChanged += OnPageSelectedIndexChanged;

            _lblPageInfo = new Label
            {
                Left = 280,
                Top = 45,
                Width = 200,
                Height = 25,
                Text = "即時真實預覽：第 1 頁",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft JhengHei", 9f, FontStyle.Bold),
                ForeColor = Color.DarkSlateBlue
            };

            GroupBox grpPreview = new GroupBox
            {
                Left = 20,
                Top = 80,
                Width = 465,
                Height = 440,
                Text = " 頁面真實內容預覽 ",
                Font = new Font("Microsoft JhengHei", 9f)
            };

            _picPreview = new PictureBox
            {
                Left = 12,
                Top = 22,
                Width = 441,
                Height = 406,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };

            grpPreview.Controls.Add(_picPreview);

            _btnOk = new Button
            {
                Text = "確定連結",
                Left = 285,
                Top = 535,
                Width = 95,
                Height = 32,
                DialogResult = DialogResult.OK,
                Font = new Font("Microsoft JhengHei", 9f)
            };
            _btnOk.Click += (s, e) => { SelectedPageNumber = _cboPage.SelectedIndex + 1; };

            _btnCancel = new Button
            {
                Text = "取消",
                Left = 390,
                Top = 535,
                Width = 95,
                Height = 32,
                DialogResult = DialogResult.Cancel,
                Font = new Font("Microsoft JhengHei", 9f)
            };

            Controls.Add(lblHeader);
            Controls.Add(lblSelect);
            Controls.Add(_cboPage);
            Controls.Add(_lblPageInfo);
            Controls.Add(grpPreview);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void OnPageSelectedIndexChanged(object sender, EventArgs e)
        {
            int pageNum = _cboPage.SelectedIndex + 1;
            _lblPageInfo.Text = string.Format("即時真實預覽：第 {0} 頁", pageNum);
            LoadPagePreview(pageNum);
        }

        private void LoadPagePreview(int pageNumber)
        {
            try
            {
                string targetPdfToRender = _fullPdfPath;
                string tempSinglePdf = Path.Combine(global::DevelopmentTools.Core.InstallLocation.TempDir, string.Format("word_page_preview_{0}_{1}.pdf", Path.GetFileNameWithoutExtension(_wordPath), pageNumber));

                // If multi-page, export single page PDF for exact page N rendering
                if (_pageCount > 1 && File.Exists(_wordPath))
                {
                    try
                    {
                        WordPdfExporter.ExportSinglePagePdf(_wordPath, tempSinglePdf, pageNumber);
                        if (File.Exists(tempSinglePdf))
                        {
                            targetPdfToRender = tempSinglePdf;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(string.Format("Single page export for preview failed for page {0}", pageNumber), ex);
                    }
                }

                Bitmap bmp = PdfPreviewService.RenderPageToBitmap(targetPdfToRender, 600, 800);
                if (bmp != null)
                {
                    Image oldImg = _picPreview.Image;
                    _picPreview.Image = bmp;
                    if (oldImg != null)
                    {
                        oldImg.Dispose();
                    }
                }

                // Cleanup temporary single-page preview PDF
                if (targetPdfToRender != _fullPdfPath && File.Exists(targetPdfToRender))
                {
                    try { File.Delete(targetPdfToRender); } catch {}
                }
            }
            catch (Exception ex)
            {
                Logger.Error("LoadPagePreview failed", ex);
            }
        }
    }
}
