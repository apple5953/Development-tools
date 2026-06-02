using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WallFinishCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            // 啟動非同步驗證，防止卡死 Revit 主執行緒
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    bool isAuthorized = await GoogleAuthManager.VerifyAccessAsync("WallFinish", "自動粉刷系統");
                    if (!isAuthorized) return;

                    uiDispatcher.Invoke(() =>
                    {
                        TaskDialog.Show(
                            "自動粉刷系統 (Mock)", 
                            "驗證成功！您已獲得「自動粉刷系統」的完整使用權限。\n\n" +
                            "【功能展示】\n" +
                            "本功能正與 Revit 牆面粉刷幾何引擎（Wall Decoration Geometry Engine）進行最終對接，預計於下個小版本更新後正式啟用，敬請期待！"
                        );
                    });
                }
                catch (Exception ex)
                {
                    uiDispatcher.Invoke(() =>
                    {
                        TaskDialog.Show("驗證錯誤", $"驗證過程發生異常：{ex.Message}");
                    });
                }
            });

            return Result.Succeeded;
        }
    }
}
