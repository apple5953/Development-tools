using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BEAI.QueryHost.Core;

namespace BEAI.QueryHost.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StartServerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (RevitServerHost.Instance.IsRunning)
            {
                TaskDialog.Show("BEAI 模型查詢", "查詢服務已在運作。\n位址：http://127.0.0.1:8765/");
                return Result.Succeeded;
            }

            bool ok = RevitServerHost.Instance.Start();
            if (ok)
            {
                TaskDialog.Show("BEAI 模型查詢", "查詢服務已成功啟動。\n位址：http://127.0.0.1:8765/\n現在可以進行本機查詢。");
            }
            else
            {
                TaskDialog.Show("BEAI 模型查詢", string.Format("查詢服務啟動失敗：\n{0}", RuntimeState.Instance.LastError));
            }

            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StopServerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!RevitServerHost.Instance.IsRunning)
            {
                TaskDialog.Show("BEAI 模型查詢", "查詢服務目前已停止。");
                return Result.Succeeded;
            }

            RevitServerHost.Instance.Stop();
            TaskDialog.Show("BEAI 模型查詢", "查詢服務已停止。");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReloadRulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            bool ok = HotReloadManager.Instance.ReloadAllRules(false);
            if (ok)
            {
                TaskDialog.Show("BEAI 模型查詢", string.Format("規則已重新載入。\n目前規則版本：{0}", RuntimeState.Instance.RuleGeneration));
            }
            else
            {
                TaskDialog.Show("BEAI 模型查詢", string.Format("規則重新載入失敗：\n{0}\n系統已保留上一份可用規則。", RuntimeState.Instance.LastError));
            }

            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RestartWorkerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            bool ok = WorkerManager.Instance.RestartWorker();
            if (ok)
            {
                TaskDialog.Show("BEAI 模型查詢", string.Format("背景處理器已重新啟動。\n狀態：{0}\n版本：{1}", TranslateWorkerStatus(RuntimeState.Instance.WorkerStatus), RuntimeState.Instance.WorkerVersion));
            }
            else
            {
                TaskDialog.Show("BEAI 模型查詢", string.Format("背景處理器重新啟動失敗，或已回復上一版本。\n狀態：{0}\n版本：{1}\n錯誤：{2}", TranslateWorkerStatus(RuntimeState.Instance.WorkerStatus), RuntimeState.Instance.WorkerVersion, RuntimeState.Instance.LastError));
            }

            return Result.Succeeded;
        }

        private static string TranslateWorkerStatus(string status)
        {
            if (string.Equals(status, "online", System.StringComparison.OrdinalIgnoreCase)) return "運作中";
            if (string.Equals(status, "offline", System.StringComparison.OrdinalIgnoreCase)) return "離線";
            if (string.Equals(status, "error", System.StringComparison.OrdinalIgnoreCase)) return "錯誤";
            return status;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ShowStatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            CoreUpdateManager.Instance.RefreshStatus();
            Document doc = null;
            if (commandData != null && commandData.Application != null && commandData.Application.ActiveUIDocument != null)
            {
                doc = commandData.Application.ActiveUIDocument.Document;
            }

            if (doc != null)
            {
                RuntimeState.Instance.DocumentOpen = true;
                RuntimeState.Instance.DocumentTitle = doc.Title;
                RuntimeState.Instance.ActiveView = doc.ActiveView != null ? doc.ActiveView.Name : "無";
            }
            else
            {
                RuntimeState.Instance.DocumentOpen = false;
                RuntimeState.Instance.DocumentTitle = "無";
                RuntimeState.Instance.ActiveView = "無";
            }

            bool serverRunning = RevitServerHost.Instance.IsRunning;
            bool workerOnline = string.Equals(RuntimeState.Instance.WorkerStatus, "online", System.StringComparison.OrdinalIgnoreCase);

            string instruction;
            TaskDialogIcon icon;
            if (RuntimeState.Instance.CoreUpdatePending)
            {
                instruction = "新版外掛已就緒，重新啟動 Revit 後生效";
                icon = TaskDialogIcon.TaskDialogIconWarning;
            }
            else if (serverRunning && doc != null)
            {
                instruction = "BEAI 外掛已啟用，可以查詢目前模型";
                icon = TaskDialogIcon.TaskDialogIconInformation;
            }
            else if (serverRunning)
            {
                instruction = "BEAI 外掛已啟用，尚未開啟模型";
                icon = TaskDialogIcon.TaskDialogIconWarning;
            }
            else
            {
                instruction = "BEAI 外掛已載入，但查詢服務未啟動";
                icon = TaskDialogIcon.TaskDialogIconWarning;
            }

            var dialog = new TaskDialog("BEAI 外掛狀態");
            dialog.MainInstruction = instruction;
            dialog.MainContent = string.Format(
                "外掛：已啟用\n查詢服務：{0}\n模型：{1}",
                serverRunning ? "運作中" : "未啟動",
                doc != null ? doc.Title : "尚未開啟");
            dialog.ExpandedContent = string.Format(
                "本機位址：{0}\n目前視圖：{1}\n背景處理器：{2}\n查詢次數：{3}\n最後查詢：{4}\n核心版本：{5}\n核心更新：{6}\n最後錯誤：{7}",
                RuntimeState.Instance.ServerAddress,
                RuntimeState.Instance.ActiveView,
                workerOnline ? "運作中" : "離線",
                RuntimeState.Instance.QueryCount,
                DisplayValue(RuntimeState.Instance.LastQueryTime),
                RuntimeState.Instance.CoreBuild,
                RuntimeState.Instance.CoreUpdatePending ? "等待重新啟動 Revit" : "已是最新版",
                DisplayValue(RuntimeState.Instance.LastError));
            dialog.MainIcon = icon;
            dialog.CommonButtons = TaskDialogCommonButtons.Close;
            dialog.Show();
            return Result.Succeeded;
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, "None", System.StringComparison.OrdinalIgnoreCase)
                ? "無"
                : value;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleDevModeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            RuntimeState.Instance.DevelopmentMode = !RuntimeState.Instance.DevelopmentMode;
            HotReloadManager.Instance.SetWatcherEnabled(RuntimeState.Instance.DevelopmentMode);

            TaskDialog.Show(
                "BEAI 模型查詢",
                string.Format("開發模式目前為：{0}", RuntimeState.Instance.DevelopmentMode ? "開啟（規則自動重載）" : "關閉"));
            return Result.Succeeded;
        }
    }
}
