using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DevelopmentTools.AgentTools
{
    /// <summary>
    /// 跨文件比對工具用：只在 Revit 已開啟的專案文件中找，不替 agent 開啟或關閉檔案。
    /// </summary>
    public static class AgentToolDocuments
    {
        public static List<Document> OpenProjects(UIApplication app)
        {
            return app.Application.Documents.Cast<Document>().Where(d => !d.IsFamilyDocument && !d.IsLinked).ToList();
        }

        /// <summary>依完整路徑、標題或檔名（不含副檔名）找已開啟的文件；找不到回傳 null。</summary>
        public static Document Find(List<Document> open, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string n = name.Trim();
            return open.FirstOrDefault(d => string.Equals(d.PathName, n, StringComparison.OrdinalIgnoreCase))
                ?? open.FirstOrDefault(d => string.Equals(d.Title, n, StringComparison.OrdinalIgnoreCase))
                ?? open.FirstOrDefault(d => string.Equals(d.Title, Path.GetFileNameWithoutExtension(n), StringComparison.OrdinalIgnoreCase));
        }

        public static string Titles(List<Document> open)
        {
            return string.Join(", ", open.Select(d => d.Title));
        }
    }
}
