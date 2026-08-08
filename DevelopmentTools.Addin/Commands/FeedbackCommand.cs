using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.UI;

namespace DevelopmentTools.Commands
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
            try
            {
                if (!CommandAccessGuard.EnsureAuthorized("Feedback", "問題與反饋"))
                {
                    return Result.Cancelled;
                }

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
