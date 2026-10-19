using System;
using System.Runtime.InteropServices;

namespace DT_WordPdfLink.Utils
{
    public static class ComUtils
    {
        public static void ReleaseComObject(object obj)
        {
            if (obj != null)
            {
                try
                {
                    if (Marshal.IsComObject(obj))
                    {
                        Marshal.FinalReleaseComObject(obj);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to release COM object", ex);
                }
            }
        }
    }
}
