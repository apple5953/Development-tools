using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using DT_WordPdfLink.Utils;

namespace DT_WordPdfLink.Services
{
    public static class PdfPreviewService
    {
        #region COM Shell Thumbnail Interfaces

        [Guid("bcc18b79-5580-4c25-8371-313bf701e4e6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            void GetImage(
                [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
                [In] SIIGBF flags,
                [Out] out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
        }

        [Flags]
        private enum SIIGBF
        {
            SIIGBF_RESIZEROTATE = 0x00,
            SIIGBF_FIRSTPAGEONLY = 0x01,
            SIIGBF_SCALEUP = 0x02,
            SIIGBF_ICONONLY = 0x04,
            SIIGBF_THUMBNAILONLY = 0x08,
            SIIGBF_INCACHEONLY = 0x10
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            [In] IntPtr pbc,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        #endregion

        /// <summary>
        /// 使用 Windows Shell (IShellItemImageFactory) 渲染 100% 真實 PDF 頁面圖像。
        /// </summary>
        public static Bitmap RenderPageToBitmap(string pdfPath, uint targetWidthPx = 600, uint targetHeightPx = 800)
        {
            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
            {
                return null;
            }

            try
            {
                Guid uuid = new Guid("bcc18b79-5580-4c25-8371-313bf701e4e6");
                IShellItemImageFactory factory;
                SHCreateItemFromParsingName(pdfPath, IntPtr.Zero, uuid, out factory);

                if (factory != null)
                {
                    IntPtr hBitmap;
                    factory.GetImage(new SIZE((int)targetWidthPx, (int)targetHeightPx), SIIGBF.SIIGBF_RESIZEROTATE | SIIGBF.SIIGBF_FIRSTPAGEONLY, out hBitmap);

                    if (hBitmap != IntPtr.Zero)
                    {
                        using (Bitmap tempBmp = Bitmap.FromHbitmap(hBitmap))
                        {
                            Bitmap resultBmp = new Bitmap(tempBmp);
                            DeleteObject(hBitmap);
                            return resultBmp;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Shell thumbnail extraction exception", ex);
            }

            return null;
        }
    }
}
