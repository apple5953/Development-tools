using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;

namespace DevelopmentTools.Commands
{
    // 內建模組的 Ribbon 進入點。
    // 每個按鈕先經過授權檢查（CommandAccessGuard），通過後才交給模組原本的命令執行。
    // 模組程式碼位於 DevelopmentTools.Modules\*，以 ProjectReference 編入主外掛。

    internal static class IntegratedModuleRunner
    {
        public static Result Run(string toolId, string toolName, IExternalCommand inner,
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!CommandAccessGuard.EnsureAuthorized(toolId, toolName))
            {
                return Result.Cancelled;
            }

            try
            {
                return inner.Execute(commandData, ref message, elements);
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(toolName, "執行時發生錯誤：\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_PerspectiveMinimap : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return IntegratedModuleRunner.Run("DT_PerspectiveMinimap", "透視圖索引面板",
                new global::RevitPerspectiveMinimap.Commands.ShowMinimapPanelCommand(),
                commandData, ref message, elements);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_TileFourViews : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return IntegratedModuleRunner.Run("DT_PerspectiveMinimap", "一鍵 4 視圖",
                new global::RevitPerspectiveMinimap.Commands.TileFourViewsCommand(),
                commandData, ref message, elements);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_OpeningDetail : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return IntegratedModuleRunner.Run("DT_OpeningDetail", "開口剖面圖",
                new global::OpeningDetailAddin.Core.MainCommand(),
                commandData, ref message, elements);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_DrawingFactory : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return IntegratedModuleRunner.Run("DT_DrawingFactory", "量化開圖",
                new global::BIMDrawingFactory.UI.Revit.OpenDrawingFactoryCommand(),
                commandData, ref message, elements);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_ProjectStandards : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return IntegratedModuleRunner.Run("DT_ProjectStandards", "專案標準比對",
                new global::DevelopmentTools.ProjectStandards.Commands.ProjectStandardsCommand(),
                commandData, ref message, elements);
        }
    }
}
