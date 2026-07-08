using System;
using System.IO;
using System.Text;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.DesignReview.Models;

namespace DevelopmentTools.Modules.DesignReview.Services
{
    public class ReportEngineService
    {
        public void ExportToCsv(ReviewProject project)
        {
            if (project == null) return;

            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV檔案 (*.csv)|*.csv",
                FileName = $"Revit法規檢討報告_{project.ProjectName}_{DateTime.Now:yyyyMMdd_HHmmss}",
                Title = "儲存建築技術規則檢討報告"
            };

            if (saveFileDialog.ShowDialog() != true) return;

            string fileName = saveFileDialog.FileName;
            try
            {
                using (var sw = new StreamWriter(fileName, false, Encoding.UTF8))
                {
                    sw.Write('\ufeff');

                    sw.WriteLine("Revit BCR 建築法規檢討與追蹤統計報表");
                    sw.WriteLine($"專案名稱: {project.ProjectName}");
                    sw.WriteLine($"建物類型: {project.BuildingType}");
                    sw.WriteLine($"載入模板: {project.ReviewTemplate}");
                    sw.WriteLine($"產出時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sw.WriteLine();

                    int totalItems = project.Items.Count;
                    int approvedItems = 0;
                    int failedItems = 0;
                    int uncheckedItems = 0;

                    foreach (var item in project.Items)
                    {
                        if (item.Status == TrackingStatus.Approved) approvedItems++;
                        else if (item.Status == TrackingStatus.Created || item.Status == TrackingStatus.Rejected) failedItems++;
                        else uncheckedItems++;
                    }

                    sw.WriteLine("檢討項目彙總");
                    sw.WriteLine($"\"總檢討項\",\"{totalItems}\"");
                    sw.WriteLine($"\"通過項 (Approved)\",\"{approvedItems}\"");
                    sw.WriteLine($"\"不符/未通過項\",\"{failedItems}\"");
                    sw.WriteLine($"\"處理中/其它\",\"{uncheckedItems}\"");
                    sw.WriteLine();

                    sw.WriteLine("法規檢核明細清單");
                    sw.WriteLine("\"法規編號\",\"法規項目\",\"分類\",\"審查級別\",\"當前狀態\",\"負責人\",\"位置\",\"元件ID\",\"異常說明\",\"改善建議/備註\"");

                    foreach (var item in project.Items)
                    {
                        if (item.Results == null || item.Results.Count == 0)
                        {
                            sw.WriteLine($"\"{item.RuleCode}\",\"{item.RuleName}\",\"{item.Category}\",\"{item.Type}\",\"{item.StatusDisplay}\",\"{item.Assignee ?? "無"}\",\"無\",\"無\",\"檢核通過或無異常\",\"{item.Comment ?? "無"}\"");
                        }
                        else
                        {
                            foreach (var res in item.Results)
                            {
                                sw.WriteLine($"\"{item.RuleCode}\",\"{item.RuleName}\",\"{item.Category}\",\"{item.Type}\",\"{item.StatusDisplay}\",\"{item.Assignee ?? "無"}\",\"{res.LevelName}\",\"{res.ElementId}\",\"{res.Message}\",\"{item.Comment ?? "無"}\"");
                            }
                        }
                    }
                }

                TaskDialog.Show("DT Code Review", $"已成功匯出報表至：\n{Path.GetFileName(fileName)}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("匯出報表錯誤", $"匯出失敗：{ex.Message}");
            }
        }
    }
}
