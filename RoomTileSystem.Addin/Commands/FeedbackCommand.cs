using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RoomTileSystem.Core;
using RoomTileSystem.UI;

namespace RoomTileSystem.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FeedbackCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            if (GoogleAuthManager.IsAuthEnabled() && string.IsNullOrEmpty(GoogleAuthManager.CurrentUserEmail))
            {
                // 先引導登入
                TaskDialog.Show("請先登入", "您必須先登入 Google 帳號才能提交意見與反饋。");
                return Result.Cancelled;
            }

            try
            {
                FeedbackWindow feedbackWin = new FeedbackWindow();
                var helper = new System.Windows.Interop.WindowInteropHelper(feedbackWin);
                helper.Owner = commandData.Application.MainWindowHandle;
                feedbackWin.ShowDialog(); // 模態視窗，等待反饋提交
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
