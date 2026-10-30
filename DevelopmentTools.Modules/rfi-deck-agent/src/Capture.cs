// 截圖：任何專案瀏覽器裡的視圖都能擷取（3D、平面、剖面、立面、圖紙、圖例、製圖視圖、明細表）。
// 目前視圖：優先擷取螢幕上的畫面（所見即所得，含選取高亮）；失敗時改用 Revit 匯出影像。
// 專案瀏覽器勾選的其他視圖：不用打開，直接匯出影像；明細表無法匯出影像，改讀表格內容畫成圖片。
// 注意：在專案瀏覽器點過之後，ActiveView 會變成「專案瀏覽器」本身，所以一律用 ActiveGraphicalView。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using Rectangle = System.Drawing.Rectangle;
using View = Autodesk.Revit.DB.View;

namespace RfiDeckAgent.Core
{
    public static class Capture
    {
        /// <summary>要擷取的「目前視圖」：專案瀏覽器有焦點時取最後一個繪圖視窗（不是瀏覽器本身）</summary>
        public static View TargetView(UIDocument uidoc)
        {
            View v = null;
            try { v = uidoc.ActiveGraphicalView; } catch { }
            if (v == null) v = uidoc.ActiveView;
            if (v != null && (v.ViewType == ViewType.ProjectBrowser || v.ViewType == ViewType.SystemBrowser)) v = null;
            return v;
        }

        /// <summary>專案瀏覽器裡選取的視圖／圖紙／明細表（樣板除外）</summary>
        public static List<View> SelectedViews(UIDocument uidoc)
        {
            List<View> list = new List<View>();
            try
            {
                foreach (ElementId id in uidoc.Selection.GetElementIds())
                {
                    View v = uidoc.Document.GetElement(id) as View;
                    if (v == null || v.IsTemplate) continue;
                    if (v.ViewType == ViewType.ProjectBrowser || v.ViewType == ViewType.SystemBrowser || v.ViewType == ViewType.Internal) continue;
                    if (v is ViewSchedule && ((ViewSchedule)v).IsTitleblockRevisionSchedule) continue;
                    list.Add(v);
                }
            }
            catch { }
            return list;
        }

        public static string ActiveView(UIDocument uidoc, string targetFile)
        {
            View v = TargetView(uidoc);
            if (v == null) return null;
            return CaptureView(uidoc, v, targetFile, true);
        }

        /// <summary>擷取指定視圖。screen=true 且視圖開在畫面上時抓螢幕畫面，否則匯出影像（明細表畫成表格圖）</summary>
        public static string CaptureView(UIDocument uidoc, View v, string targetFile, bool screen)
        {
            string r = null;
            if (screen)
            {
                try { r = ScreenGrab(uidoc, v, targetFile); } catch { r = null; }
                if (r != null && File.Exists(r)) return r;
            }
            ViewSchedule vs = v as ViewSchedule;
            if (vs != null) return RenderSchedule(vs, targetFile);
            return ExportImage(uidoc.Document, v, targetFile);
        }

        static string ScreenGrab(UIDocument uidoc, View v, string targetFile)
        {
            UIView uiv = uidoc.GetOpenUIViews().FirstOrDefault(uv => uv.ViewId == v.Id);
            if (uiv == null) return null;
            Autodesk.Revit.DB.Rectangle rc = uiv.GetWindowRectangle();
            int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
            if (w < 50 || h < 50) return null;
            // 內縮 2px 避開視窗邊框
            int gx = rc.Left + 2, gy = rc.Top + 2;
            w -= 4; h -= 4;
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(gx, gy, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                }
                if (IsBlank(bmp)) return null;
                bmp.Save(targetFile, ImageFormat.Png);
            }
            return targetFile;
        }

        static bool IsBlank(Bitmap b)
        {
            // 取樣檢查是否整張同色（例如被其他視窗擋住或未繪製）
            Color c0 = b.GetPixel(b.Width / 2, b.Height / 2);
            int same = 0, total = 0;
            for (int i = 1; i < 10; i++)
                for (int j = 1; j < 10; j++)
                {
                    Color c = b.GetPixel(b.Width * i / 10, b.Height * j / 10);
                    total++;
                    if (Math.Abs(c.R - c0.R) + Math.Abs(c.G - c0.G) + Math.Abs(c.B - c0.B) < 6) same++;
                }
            return same == total;
        }

        public static string ExportImage(Document doc, View v, string targetFile)
        {
            string dir = Path.GetDirectoryName(targetFile);
            string prefix = Path.Combine(dir, "export_" + DateTime.Now.ToString("HHmmssfff"));
            ImageExportOptions o = new ImageExportOptions();
            o.ExportRange = ExportRange.SetOfViews;
            o.SetViewsAndSheets(new List<ElementId> { v.Id });
            o.FilePath = prefix;
            o.FitDirection = FitDirectionType.Horizontal;
            o.PixelSize = v is ViewSheet ? 2800 : 1920;      // 圖紙要看得清楚圖框文字
            o.ImageResolution = ImageResolution.DPI_150;
            o.HLRandWFViewsFileType = ImageFileType.PNG;
            o.ShadowViewsFileType = ImageFileType.PNG;
            doc.ExportImage(o);
            string found = Directory.GetFiles(dir, Path.GetFileName(prefix) + "*.png")
                .OrderByDescending(f => File.GetLastWriteTime(f)).FirstOrDefault();
            if (found == null) return null;
            if (File.Exists(targetFile)) File.Delete(targetFile);
            File.Move(found, targetFile);
            return targetFile;
        }

        // ------------------------------------------------------------ 明細表 → 表格圖片
        const int MaxRows = 60;

        /// <summary>明細表無法用 Revit 匯出影像：讀表頭與內容列，畫成表格 PNG（最多 60 列）</summary>
        public static string RenderSchedule(ViewSchedule vs, string targetFile)
        {
            List<string[]> rows = new List<string[]>();
            int headerRows = 0;
            TableData td = vs.GetTableData();
            foreach (SectionType st in new[] { SectionType.Header, SectionType.Body })
            {
                TableSectionData sd;
                try { sd = td.GetSectionData(st); } catch { continue; }
                if (sd == null || sd.NumberOfRows == 0 || sd.NumberOfColumns == 0) continue;
                for (int r = sd.FirstRowNumber; r <= sd.LastRowNumber; r++)
                {
                    List<string> cells = new List<string>();
                    for (int c = sd.FirstColumnNumber; c <= sd.LastColumnNumber; c++)
                    {
                        string t = "";
                        try { t = vs.GetCellText(st, r, c) ?? ""; } catch { }
                        cells.Add(t.Replace("\r", " ").Replace("\n", " "));
                    }
                    if (cells.All(x => x.Trim().Length == 0)) continue;
                    if (st == SectionType.Header) headerRows++;
                    rows.Add(cells.ToArray());
                }
            }
            int more = 0;
            if (rows.Count > MaxRows + headerRows) { more = rows.Count - MaxRows - headerRows; rows = rows.Take(MaxRows + headerRows).ToList(); }
            int cols = rows.Count == 0 ? 1 : rows.Max(x => x.Length);

            using (Font f = new Font("Microsoft JhengHei UI", 11f))
            using (Font fb = new Font("Microsoft JhengHei UI", 11f, FontStyle.Bold))
            using (Font ft = new Font("Microsoft JhengHei UI", 15f, FontStyle.Bold))
            using (Bitmap probe = new Bitmap(10, 10))
            using (Graphics gp = Graphics.FromImage(probe))
            {
                int[] w = new int[cols];
                for (int c = 0; c < cols; c++) w[c] = 60;
                foreach (string[] row in rows)
                    for (int c = 0; c < row.Length; c++)
                        w[c] = Math.Min(360, Math.Max(w[c], (int)gp.MeasureString(row[c], fb).Width + 16));
                int rowH = 26, titleH = 44, pad = 16;
                int width = Math.Max(400, w.Sum() + pad * 2);
                int height = titleH + rowH * Math.Max(1, rows.Count) + (more > 0 ? rowH : 0) + pad * 2;
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp))
                using (Pen grid = new Pen(Color.FromArgb(190, 190, 190)))
                using (SolidBrush headFill = new SolidBrush(Color.FromArgb(232, 238, 246)))
                using (StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    g.Clear(Color.White);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    g.DrawString("明細表：" + vs.Name, ft, Brushes.Black, pad, pad);
                    int y = pad + titleH;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        int x = pad;
                        bool head = i < headerRows;
                        for (int c = 0; c < cols; c++)
                        {
                            Rectangle cell = new Rectangle(x, y, w[c], rowH);
                            if (head) g.FillRectangle(headFill, cell);
                            g.DrawRectangle(grid, cell);
                            string t = c < rows[i].Length ? rows[i][c] : "";
                            g.DrawString(t, head ? fb : f, Brushes.Black, new RectangleF(x + 6, y, w[c] - 10, rowH), sf);
                            x += w[c];
                        }
                        y += rowH;
                    }
                    if (rows.Count == 0) g.DrawString("（明細表沒有內容）", f, Brushes.DimGray, pad, y + 4);
                    if (more > 0) g.DrawString("…還有 " + more + " 列未顯示", f, Brushes.DimGray, pad, y + 4);
                    bmp.Save(targetFile, ImageFormat.Png);
                }
            }
            return targetFile;
        }

        /// <summary>預設圖說文字（依視圖類型）</summary>
        public static string DefaultCaption(View v)
        {
            switch (v.ViewType)
            {
                case ViewType.ThreeD: return "3D 透視圖";
                case ViewType.FloorPlan: return "平面圖 - " + v.Name;
                case ViewType.EngineeringPlan: return "結構平面圖 - " + v.Name;
                case ViewType.CeilingPlan: return "天花平面圖 - " + v.Name;
                case ViewType.AreaPlan: return "面積平面圖 - " + v.Name;
                case ViewType.Section: return "剖面圖 - " + v.Name;
                case ViewType.Detail: return "詳圖 - " + v.Name;
                case ViewType.Elevation: return "立面圖 - " + v.Name;
                case ViewType.DraftingView: return "製圖視圖 - " + v.Name;
                case ViewType.Legend: return "圖例 - " + v.Name;
                case ViewType.Schedule: return "明細表 - " + v.Name;
                case ViewType.DrawingSheet:
                    ViewSheet s = v as ViewSheet;
                    return "圖紙 - " + (s != null ? (s.SheetNumber + " " + s.Name).Trim() : v.Name);
                default: return v.Name;
            }
        }
    }
}
