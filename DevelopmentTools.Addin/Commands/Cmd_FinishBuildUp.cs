using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.FinishBuildUp;

namespace DevelopmentTools.Commands
{
    /// <summary>降梁降板增築：依裝修表比對房間面層底與結構頂，建立板上／梁上增築，或修正降板降梁。</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_FinishBuildUp : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 與「裝修配置與粉刷」同一授權
                if (!CommandAccessGuard.EnsureAuthorized("DT_RoomFinishConfigurator", "降梁降板增築"))
                    return Result.Cancelled;

                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    TaskDialog.Show("降梁降板增築", "請在專案檔中使用。");
                    return Result.Cancelled;
                }

                string key = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
                BuildUpSettings s = BuildUpSettings.Load(key);
                if (s.Finishes.Count == 0) s.Finishes = FinishTable.FromModel(doc);
                else s.Finishes = FinishTable.Merge(s.Finishes, FinishTable.FromModel(doc));

                var win = new BuildUpWindow(uidoc, s, key);
                new System.Windows.Interop.WindowInteropHelper(win).Owner = commandData.Application.MainWindowHandle;
                win.ShowDialog();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("降梁降板增築", "執行失敗：" + ex.Message);
                return Result.Failed;
            }
        }
    }
}
