// Ribbon 子工具命令。授權由主外掛產生的 Generated_* 包裝命令統一檢查。
// 本模組只讀取 Revit 模型，不開啟 Transaction。
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RfiDeckAgent.Core;

namespace RfiDeckAgent.Commands
{
    internal static class ToolCommandRunner
    {
        internal static Result Run(string action, ExternalCommandData data, ref string message)
        {
            return Entry.Run(action, data, ref message);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CaptureToolCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ToolCommandRunner.Run("capture", data, ref message);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class BuildDeckToolCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ToolCommandRunner.Run("handoff", data, ref message);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class SharedSlidesToolCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ToolCommandRunner.Run("slides", data, ref message);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class InboxToolCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ToolCommandRunner.Run("inbox", data, ref message);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class OutputToolCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ToolCommandRunner.Run("output", data, ref message);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class SettingsToolCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ToolCommandRunner.Run("settings", data, ref message);
    }
}
