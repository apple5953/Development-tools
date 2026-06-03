using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.UI;
using DevelopmentTools.Modules.TileElevationGenerator;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DT_TileElevationGeneratorCommand : IExternalCommand
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
                    // 驗證使用者是否具有 Tiling (磁磚鋪設與相關模組) 的存取權限
                    if (GoogleAuthManager.IsAuthEnabled())
                    {
                        bool isAuthorized = await GoogleAuthManager.VerifyAccessAsync("Tiling", "磁磚展開圖生成器");
                        if (!isAuthorized) return;
                    }

                    uiDispatcher.Invoke(() =>
                    {
                        DoShowElevationDialog(commandData);
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

        private void DoShowElevationDialog(ExternalCommandData commandData)
        {
            try
            {
                var viewModel = new DT_TileElevationGeneratorViewModel(commandData);
                var window = new DT_TileElevationGeneratorWindow(viewModel);

                // 設定 Revit 主視窗為 Owner，避免 WPF 視窗掉到 Revit 後台
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = commandData.Application.MainWindowHandle;

                // 註冊關閉委派
                viewModel.RequestClose += () =>
                {
                    window.DialogResult = true;
                    window.Close();
                };

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", $"無法啟動磁磚展開圖生成器視窗：{ex.Message}");
            }
        }
    }
}
