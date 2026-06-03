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
            try
            {
                bool isAuthorized = true;
                if (GoogleAuthManager.IsAuthEnabled())
                {
                    // 同步等待非同步驗證，保留 Revit API Context 鎖
                    isAuthorized = System.Threading.Tasks.Task.Run(async () =>
                    {
                        return await GoogleAuthManager.VerifyAccessAsync("Tiling", "磁磚展開圖生成器");
                    }).GetAwaiter().GetResult();
                }

                if (!isAuthorized)
                {
                    return Result.Failed;
                }

                // 驗證成功，此時 100% 處於 API Context 中
                DoShowElevationDialog(commandData);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
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

                bool? dialogResult = window.ShowDialog();

                if (dialogResult == true && viewModel.IsConfirmed)
                {
                    UIDocument uidoc = commandData.Application.ActiveUIDocument;
                    Document doc = uidoc.Document;

                    var service = new DT_TileElevationGeneratorService();
                    TileElevationResult result;

                    if (viewModel.IsFloorMode)
                    {
                        result = service.GenerateElevationsForFloor(doc, viewModel.SelectedFloor, viewModel.Settings);
                    }
                    else
                    {
                        result = service.GenerateElevationsForWalls(doc, viewModel.SelectedWalls, viewModel.Settings);
                    }

                    if (result.Success)
                    {
                        string msg = $"成功建立 {result.CreatedViewsCount} 個磁磚展開圖視圖：\n" +
                                     string.Join("\n", result.CreatedViewNames);
                        if (result.SkippedWallsCount > 0)
                        {
                            msg += $"\n\n跳過了 {result.SkippedWallsCount} 面牆。";
                        }
                        TaskDialog.Show("生成成功", msg);
                    }
                    else
                    {
                        TaskDialog.Show("生成失敗", $"無法生成展開圖：\n{result.ErrorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", $"執行展開圖生成時發生異常：{ex.Message}");
            }
        }
    }
}
