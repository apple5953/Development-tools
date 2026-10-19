using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;
using BEAI.QueryHost.Commands;
using BEAI.QueryHost.Core;
using BEAI.QueryHost.UI;

namespace BEAI.QueryHost
{
    public class App : IExternalApplication
    {
        public static string AssemblyPath { get { return Assembly.GetExecutingAssembly().Location; } }
        public static string AssemblyDirectory { get { return Path.GetDirectoryName(AssemblyPath); } }

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                Result startResult = EmbeddedBridge.Start(application, AssemblyDirectory);
                if (startResult != Result.Succeeded) return startResult;

                CoreUpdateManager.Instance.Initialize(AssemblyPath);
                CreateRibbon(application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal error during BEAI Query Bridge OnStartup", ex);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                Logger.Info("BEAI Personal Revit Query Bridge V0.1 Shutting down...");

                EmbeddedBridge.Stop();
                CoreUpdateManager.Instance.Dispose();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("Error during BEAI Query Bridge OnShutdown", ex);
                return Result.Failed;
            }
        }

        private void CreateRibbon(UIControlledApplication application)
        {
            const string tabName = "BEAI";
            const string panelName = "模型查詢";

            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
            }

            RibbonPanel panel = null;
            var panels = application.GetRibbonPanels(tabName);
            foreach (var p in panels)
            {
                if (p.Name == panelName)
                {
                    panel = p;
                    break;
                }
            }
            if (panel == null)
            {
                panel = application.CreateRibbonPanel(tabName, panelName);
            }

            string asm = AssemblyPath;

            var btnStatusData = new PushButtonData("btnShowStatus", "外掛\n狀態", asm, typeof(ShowStatusCommand).FullName);
            btnStatusData.ToolTip = "確認 BEAI 外掛、查詢服務及目前模型是否可用";
            btnStatusData.LongDescription = "外掛啟動後會自動開啟查詢服務。一般使用時只需按此按鈕確認狀態。";
            btnStatusData.Image = RibbonIconFactory.CreateStatusIcon(16);
            btnStatusData.LargeImage = RibbonIconFactory.CreateStatusIcon(32);
            panel.AddItem(btnStatusData);

            var serviceMenuData = new PulldownButtonData("btnServiceTools", "服務");
            serviceMenuData.ToolTip = "查詢服務的啟動與停止工具";
            serviceMenuData.Image = RibbonIconFactory.CreatePowerIcon(16);

            var maintenanceMenuData = new PulldownButtonData("btnMaintenanceTools", "維護");
            maintenanceMenuData.ToolTip = "規則、背景處理器及開發模式工具";
            maintenanceMenuData.Image = RibbonIconFactory.CreateToolsIcon(16);

            var compactItems = panel.AddStackedItems(serviceMenuData, maintenanceMenuData);
            var serviceMenu = compactItems[0] as PulldownButton;
            var maintenanceMenu = compactItems[1] as PulldownButton;

            var btnStartData = new PushButtonData("btnStartQuery", "啟動查詢服務", asm, typeof(StartServerCommand).FullName);
            btnStartData.ToolTip = "啟動本機查詢服務 (http://127.0.0.1:8765/)";
            btnStartData.Image = RibbonIconFactory.CreatePowerIcon(16);

            var btnStopData = new PushButtonData("btnStopQuery", "停止查詢服務", asm, typeof(StopServerCommand).FullName);
            btnStopData.ToolTip = "停止本機查詢服務";
            btnStopData.Image = RibbonIconFactory.CreateStopIcon(16);

            var btnReloadData = new PushButtonData("btnReloadRules", "重新載入規則", asm, typeof(ReloadRulesCommand).FullName);
            btnReloadData.ToolTip = "重新載入 Runtime/Rules 中的規則，不必關閉 Revit";
            btnReloadData.Image = RibbonIconFactory.CreateReloadIcon(16);

            var btnRestartWorkerData = new PushButtonData("btnRestartWorker", "重新啟動背景處理器", asm, typeof(RestartWorkerCommand).FullName);
            btnRestartWorkerData.ToolTip = "重新啟動背景查詢處理器，不必關閉 Revit";
            btnRestartWorkerData.Image = RibbonIconFactory.CreateWorkerIcon(16);

            var btnDevModeData = new PushButtonData("btnToggleDevMode", "切換開發模式", asm, typeof(ToggleDevModeCommand).FullName);
            btnDevModeData.ToolTip = "切換規則自動重載與詳細記錄";
            btnDevModeData.Image = RibbonIconFactory.CreateToolsIcon(16);

            serviceMenu.AddPushButton(btnStartData);
            serviceMenu.AddPushButton(btnStopData);
            maintenanceMenu.AddPushButton(btnReloadData);
            maintenanceMenu.AddPushButton(btnRestartWorkerData);
            maintenanceMenu.AddPushButton(btnDevModeData);
        }
    }
}
