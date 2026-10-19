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
                // 在主執行緒等待檢查結果，TaskDialog 必須在 Revit UI 執行緒顯示。
                var result = Task.Run(async () =>
                {
                    var checkTask = UpdateManager.CheckForUpdatesAsync();
                    var timeoutTask = Task.Delay(15000);
                    var completedTask = await Task.WhenAny(checkTask, timeoutTask);
                    if (completedTask == checkTask)
                    {
                        return await checkTask;
                    }
                    return null; // 超時
                }).GetAwaiter().GetResult();

                if (result == null)
                {
                    TaskDialog.Show("遠端更新提示", "連線至更新伺服器超時，請檢查網路連線後重試。");
                    return Result.Succeeded;
                }

                if (string.IsNullOrWhiteSpace(result.LatestVersion))
                {
                    TaskDialog.Show("遠端更新提示", "無法讀取更新清單，請檢查網路連線後重試。若持續失敗，請使用 GitHub Release 的安裝程式更新。");
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
                TaskDialog.Show("更新錯誤", $"無法連線至更新伺服器。\n詳細原因：{ex.Message}");
            }
            return Result.Succeeded;
        }
    }
}
