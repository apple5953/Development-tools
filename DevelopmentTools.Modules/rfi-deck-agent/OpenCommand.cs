// 釋疑簡報模組的唯一入口（module.json 的 entryClass）。
// 主外掛產生的 Generated_RfiDeckAgent 先經 CommandAccessGuard 授權，再呼叫這裡。
// 本命令不開 Transaction、不修改模型；功能本體在 src/Entry.cs。
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RfiDeckAgent.Core;

namespace RfiDeckAgent.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class OpenCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            TaskDialog main = new TaskDialog("釋疑簡報");
            main.MainInstruction = "要做什麼？";
            main.MainContent = Status();
            main.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "釋疑擷取",
                "截下目前視圖，帶入樓層、柱線與選取元件，填寫問題後存入收件匣。");
            main.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "產生簡報",
                "勾選收件匣的擷取項目，在本機產出釋疑簡報 PPT。");
            main.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "共用簡報",
                "開啟多人同步的 Google 共用簡報。");
            main.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "收件匣、成果資料夾與設定");
            main.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (main.Show())
            {
                case TaskDialogResult.CommandLink1: return Entry.Run("capture", data, ref message);
                case TaskDialogResult.CommandLink2: return Entry.Run("handoff", data, ref message);
                case TaskDialogResult.CommandLink3: return Entry.Run("slides", data, ref message);
                case TaskDialogResult.CommandLink4: return More(data, ref message);
                default: return Result.Cancelled;
            }
        }

        static Result More(ExternalCommandData data, ref string message)
        {
            TaskDialog more = new TaskDialog("釋疑簡報");
            more.MainInstruction = "收件匣、成果資料夾與設定";
            more.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "開啟收件匣", "尚未做成簡報的擷取項目。");
            more.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "開啟成果資料夾", "簡報 PPT 與 Word 文件的輸出位置。");
            more.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "設定", "AI AGENT 資料夾、Python 與 Google 共用簡報連線。");
            more.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (more.Show())
            {
                case TaskDialogResult.CommandLink1: return Entry.Run("inbox", data, ref message);
                case TaskDialogResult.CommandLink2: return Entry.Run("output", data, ref message);
                case TaskDialogResult.CommandLink3: return Entry.Run("settings", data, ref message);
                default: return Result.Cancelled;
            }
        }

        static string Status()
        {
            try
            {
                if (!Config.IsConfigured) return "尚未設定 AI AGENT 資料夾，第一次使用會先開啟設定。";
                return string.Format("AI AGENT 資料夾：{0}\n收件匣待處理：{1} 筆", Config.Root, Inbox.ListPending().Count);
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
