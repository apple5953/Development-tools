using System;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using RoomTileSystem.Core;

namespace RoomTileSystem.Commands
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class CheckUpdateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // 將執行異步呼叫移至背景執行緒，避免卡死 UI
            Task.Run(async () =>
            {
                try
                {
                    var result = await UpdateManager.CheckForUpdatesAsync();
                    if (result.HasUpdate)
                    {
                        TaskDialog td = new TaskDialog("遠端更新提示");
                        td.MainInstruction = $"發現新版本 v{result.LatestVersion}！";
                        td.MainContent = $"目前本機版本: v{result.CurrentVersion}\n\n更新內容:\n{result.ReleaseNotes}\n\n是否下載新版並準備安裝？";
                        td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                        td.DefaultButton = TaskDialogResult.Yes;

                        if (td.Show() == TaskDialogResult.Yes)
                        {
                            await UpdateManager.StartUpdateProcessAsync(result.Manifest);
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
            });
            return Result.Succeeded;
        }
    }
}
