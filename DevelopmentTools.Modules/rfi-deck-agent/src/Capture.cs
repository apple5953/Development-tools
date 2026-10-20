// 截圖：優先擷取目前視圖在螢幕上的畫面（所見即所得，含選取高亮）；失敗時改用 Revit 匯出影像
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Color = System.Drawing.Color;
using View = Autodesk.Revit.DB.View;

namespace RfiDeckAgent.Core
{
    public static class Capture
    {
        public static string ActiveView(UIDocument uidoc, string targetFile)
        {
            string r = null;
            try { r = ScreenGrab(uidoc, targetFile); } catch { r = null; }
            if (r == null || !File.Exists(r))
            {
                r = ExportImage(uidoc.Document, uidoc.ActiveView, targetFile);
            }
            return r;
        }

        static string ScreenGrab(UIDocument uidoc, string targetFile)
        {
            View v = uidoc.ActiveView;
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
            o.SetViewsAndSheets(new System.Collections.Generic.List<ElementId> { v.Id });
            o.FilePath = prefix;
            o.FitDirection = FitDirectionType.Horizontal;
            o.PixelSize = 1920;
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
    }
}
