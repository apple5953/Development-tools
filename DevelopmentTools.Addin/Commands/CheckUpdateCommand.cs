using System;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using DevelopmentTools.Core;

namespace DevelopmentTools.Commands
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class CheckUpdateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 在主執行緒同步等待（限制 3 秒超時），讓按鍵按下去立刻有視覺反饋，且 100% 確保 TaskDialog 線程安全
                var result = Task.Run(async () =>
                {
                    var checkTask = UpdateManager.CheckForUpdatesAsync();
                    var timeoutTask = Task.Delay(3000);
                    var completedTask = await Task.WhenAny(checkTask, timeoutTask);
                    if (completedTask == checkTask)
                    {
                        return await checkTask;
                    }
                    return null; // 超時
                }).GetAwaiter().GetResult();

                if (result == null)
                {
                    TaskDialog.Show("遠端更新提示", "連線至更新伺服器超時，請檢查網路連線。");
                    return Result.Succeeded;
                }

                if (result.HasUpdate)
                {
                    TaskDialog td = new TaskDialog("遠端更新提示");
                    td.MainInstruction = $"發現新版本 v{result.LatestVersion}！";
                    td.MainContent = $"目前本機版本: v{result.CurrentVersion}\n\n更新內容:\n{result.ReleaseNotes}\n\n是否下載新版並準備安裝？";
                    td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                    td.DefaultButton = TaskDialogResult.Yes;

                    if (td.Show() == TaskDialogResult.Yes)
                    {
                        // 下載更新包與開啟更新視窗依然放背景，以免關閉 Revit 前卡死
                        Task.Run(async () =>
                        {
                            await UpdateManager.StartUpdateProcessAsync(result.Manifest);
                        });
                    }
                }
                else
                {
                    TaskDialog.Show("遠端更新提示", $"目前已是最新版本 (v{result.CurrentVersion})。");
                }
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("檢查更新命令觸發失敗", ex);
                TaskDialog.Show("更新錯誤", "無法連線至更新伺服器或解析 Manifest。");
            }
            return Result.Succeeded;
        }
    }
}
