using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.DesignReview.Models;
using DevelopmentTools.Modules.DesignReview.Services;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_CreateCodeReviewProject : IExternalCommand
    {
        private static readonly Guid SchemaGuid = new Guid("F9A8A5E1-723A-4FBE-B388-B1F7623910A4");

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                if (uidoc == null)
                {
                    TaskDialog.Show("建立檢討案", "請先開啟一個 Revit 專案檔案。");
                    return Result.Cancelled;
                }

                Document doc = uidoc.Document;

                // 1. 取得法規庫 JSON 目錄
                string assemblyFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string rulesDir = Path.Combine(assemblyFolder, "Resources", "Rules");
                if (!Directory.Exists(rulesDir))
                {
                    rulesDir = Path.Combine(assemblyFolder, "Rules");
                }

                if (!Directory.Exists(rulesDir) || !Directory.GetFiles(rulesDir, "*.json").Any())
                {
                    TaskDialog.Show("建立檢討案錯誤", $"找不到法規規則庫 JSON 檔案！\n搜尋路徑: {rulesDir}");
                    return Result.Failed;
                }

                // 2. 檢查是否已有既有檢核案
                bool hasExistingProject = CheckIfProjectExists(doc);
                if (hasExistingProject)
                {
                    // 直接開啟法規檢核面板
                    App.ShowCodeReviewPane(uiapp);
                    return Result.Succeeded;
                }
                else
                {
                    // 全新專案，提示並引導初始化
                    TaskDialog td = new TaskDialog("法規檢討");
                    td.MainInstruction = "此專案尚未建立法規檢討案。";
                    td.MainContent = "是否要載入中華民國建築技術規則模板，並建立全新的檢核追蹤清單？";
                    td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                    td.DefaultButton = TaskDialogResult.Yes;

                    if (td.Show() != TaskDialogResult.Yes)
                    {
                        return Result.Cancelled;
                    }
                }

                // 3. 建立並載入新專案檢討條目
                var tracker = new IssueTrackerService();
                ReviewProject project = tracker.LoadOrCreateProject(doc, doc.Title, "集合住宅", "Residential_TW", rulesDir);

                if (project != null)
                {
                    // 4. 自動開啟常駐面板
                    App.ShowCodeReviewPane(uiapp);

                    TaskDialog.Show("建立檢討案成功", $"已成功為專案建立建築技術規則法規檢核案！\n\n專案名稱: {project.ProjectName}\n法規條目: 已載入 {project.Items.Count} 項規則\n\n您現在可以在右側「DT Code Review」面板中進行檢查與狀態追蹤。");
                    return Result.Succeeded;
                }

                return Result.Failed;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private bool CheckIfProjectExists(Document doc)
        {
            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(DataStorage))
                    .Cast<DataStorage>();

                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return false;

                var ds = collector.FirstOrDefault(x => x.GetEntity(schema).IsValid());
                if (ds == null) return false;

                Entity entity = ds.GetEntity(schema);
                string json = entity.Get<string>("JsonData");
                if (string.IsNullOrEmpty(json)) return false;

                // 簡單反序列化來確認 items 數量
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var project = System.Text.Json.JsonSerializer.Deserialize<ReviewProject>(json, options);
                return project != null && project.Items != null && project.Items.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private void DeleteExistingStorage(Document doc)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return;

            using (Transaction t = new Transaction(doc, "清理舊法規檢核資料"))
            {
                t.Start();
                foreach (var ds in collector)
                {
                    if (ds.GetEntity(schema).IsValid())
                    {
                        doc.Delete(ds.Id);
                    }
                }
                t.Commit();
            }
        }
    }
}
