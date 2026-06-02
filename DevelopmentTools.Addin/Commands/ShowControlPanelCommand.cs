using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ShowControlPanelCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                // 更新 ExternalEvent Handler 的 UIApplication（每次 Execute 都拿到最新的）
                App.EventHandler.ViewModel = null; // 先清空，等視窗建立後再設定

                // 開啟或激活 Modeless 視窗（不阻塞，立刻 return Succeeded）
                App.ShowOrActivateWindow(commandData);

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
