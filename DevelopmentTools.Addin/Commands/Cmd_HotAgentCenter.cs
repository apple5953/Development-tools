using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Core.AgentHost;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class Cmd_HotAgentCenter : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                string root = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Agents");
                Version hostVersion = ReadHostVersion();
                while (true)
                {
                    IReadOnlyList<ApprovedAgent> agents = ApprovedAgentCatalog.Load(root, data.Application.Application.VersionNumber, hostVersion);
                    if (agents.Count == 0)
                    {
                        TaskDialog.Show("BEAI Agent 中心", "沒有可用的已核准 Agent。\n\n請由維護者安裝含 approved-agents.json 與 SHA-256 的 Agent 套件。");
                        return Result.Cancelled;
                    }
                    DialogResult action;
                    ApprovedAgent selected = AgentCenterDialog.Select(agents, out action);
                    if (action == DialogResult.Retry) continue;
                    if (selected == null) return Result.Cancelled;
                    if (!CommandAccessGuard.EnsureAuthorized(selected.ToolId, selected.DisplayName)) return Result.Cancelled;
                    AgentWorkerRequest request = AgentWorkerRunner.CaptureRequest(selected, data);
                    Task.Run(() =>
                    {
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            AgentWorkerResponse response = AgentWorkerRunner.Run(selected, request);
                            AgentAuditLog.Write(selected, "succeeded", timer.ElapsedMilliseconds, null);
                            ShowOnRevitUi(response.Title ?? selected.DisplayName, response.Message ?? "Agent 已完成。");
                        }
                        catch (Exception ex)
                        {
                            AgentAuditLog.Write(selected, "failed", timer.ElapsedMilliseconds, ex.Message);
                            ShowOnRevitUi(selected.DisplayName + " 執行失敗", ex.Message);
                        }
                    });
                    TaskDialog.Show("BEAI Agent 中心", selected.DisplayName + " 已在背景啟動。完成後會顯示結果，Revit 可繼續操作。");
                    return Result.Succeeded;
                }
            }
            catch (Exception ex) { message = ex.Message; TaskDialog.Show("BEAI Agent 中心", ex.Message); return Result.Failed; }
        }

        private static void ShowOnRevitUi(string title, string text)
        {
            try
            {
                var dispatcher = DevelopmentTools.App.UiDispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(new Action(() => TaskDialog.Show(title, text)));
            }
            catch { }
        }

        private static Version ReadHostVersion()
        {
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "version.json");
                using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(file)))
                {
                    Version version;
                    if (Version.TryParse(json.RootElement.GetProperty("current_version").GetString(), out version)) return version;
                }
            }
            catch { }
            return new Version(0, 0, 0);
        }
    }

    internal sealed class AgentCenterDialog : System.Windows.Forms.Form
    {
        private readonly ListBox _list = new ListBox();
        public ApprovedAgent SelectedAgent => _list.SelectedItem as ApprovedAgent;

        private AgentCenterDialog(IReadOnlyList<ApprovedAgent> agents)
        {
            Text = "BEAI Agent 中心"; Width = 560; Height = 390; StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            var title = new Label { Left = 16, Top = 14, Width = 510, Height = 40, Text = "選擇已核准 Agent。重新開啟或按重新載入，即會讀取新的版本目錄。" };
            _list.Left = 16; _list.Top = 58; _list.Width = 510; _list.Height = 230;
            foreach (ApprovedAgent agent in agents) _list.Items.Add(agent);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            var run = new Button { Text = "執行", Left = 326, Top = 305, Width = 95, DialogResult = DialogResult.OK };
            var reload = new Button { Text = "重新載入", Left = 220, Top = 305, Width = 95, DialogResult = DialogResult.Retry };
            var cancel = new Button { Text = "取消", Left = 431, Top = 305, Width = 95, DialogResult = DialogResult.Cancel };
            Controls.Add(title); Controls.Add(_list); Controls.Add(run); Controls.Add(reload); Controls.Add(cancel);
            AcceptButton = run; CancelButton = cancel;
        }

        public static ApprovedAgent Select(IReadOnlyList<ApprovedAgent> agents, out DialogResult result)
        {
            using (var dialog = new AgentCenterDialog(agents)) { result = dialog.ShowDialog(); return result == DialogResult.OK ? dialog.SelectedAgent : null; }
        }
    }
}
