using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DT_WordPdfLink.Models;
using DT_WordPdfLink.Services;
using DT_WordPdfLink.Utils;

namespace DT_WordPdfLink.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenWordCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Failed;
            Document doc = uidoc.Document;

            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds.Count == 0)
            {
                TaskDialog.Show("Word PDF Link", "請先選取圖紙上的 PDF 連結項目。");
                return Result.Failed;
            }

            ImageInstance selectedInst = null;
            foreach (ElementId id in selectedIds)
            {
                ImageInstance inst = doc.GetElement(id) as ImageInstance;
                if (inst != null)
                {
                    selectedInst = inst;
                    break;
                }
            }

            if (selectedInst == null)
            {
                TaskDialog.Show("Word PDF Link", "選取的項目並不是由 Word PDF Link 建立。");
                return Result.Failed;
            }

            ImageType imageType = doc.GetElement(selectedInst.GetTypeId()) as ImageType;
            if (imageType == null)
            {
                TaskDialog.Show("Word PDF Link", "選取的項目無效。");
                return Result.Failed;
            }

            WordLinkRecord record = WordLinkRegistryService.FindRecordByImageTypeUniqueId(doc, imageType.UniqueId);
            if (record == null)
            {
                TaskDialog.Show("Word PDF Link", "選取的 PDF 並不是由 Word PDF Link 建立。");
                return Result.Failed;
            }

            string wordPath = PathUtils.ResolveLocalPath(record.SourceWordPath);
            if (!File.Exists(wordPath))
            {
                TaskDialog td = new TaskDialog("Word PDF Link");
                td.MainInstruction = "找不到 Word 原始文件：";
                td.MainContent = string.Format("{0}\n\n目前無法開啟檔案，請重新指定來源。", wordPath);
                td.Show();
                return Result.Failed;
            }

            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = wordPath;
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
                Logger.Info(string.Format("Opened Word document via shell: {0}", wordPath));
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open Word file via shell", ex);
                TaskDialog.Show("Word PDF Link", string.Format("無法開啟 Word 檔案: {0}", ex.Message));
            }

            return Result.Succeeded;
        }
    }
}
