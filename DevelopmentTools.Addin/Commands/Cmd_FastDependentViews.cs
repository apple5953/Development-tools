using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.SheetTools.FastDependentViews;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_FastDependentViews : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!CommandAccessGuard.EnsureAuthorized("FastDependentViews", "快速建立從屬視圖"))
                {
                    return Result.Cancelled;
                }

                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;
                if (uiDoc == null)
                {
                    message = LanguageManager.Instance["Common_NoOpenProject"];
                    return Result.Failed;
                }

                View activeView = uiDoc.Document.ActiveView;
                if (activeView == null || !activeView.CanViewBeDuplicated(ViewDuplicateOption.AsDependent))
                {
                    TaskDialog.Show(
                        LanguageManager.Instance["FastDep_Title"],
                        LanguageManager.Instance["FastDep_UnsupportedView"]);
                    return Result.Cancelled;
                }

                var window = new FastDependentViewsWindow(uiApp);
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = uiApp.MainWindowHandle;
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(LanguageManager.Instance["FastDep_ErrorTitle"], string.Format(LanguageManager.Instance["FastDep_UnexpectedError"], ex.Message));
                return Result.Failed;
            }
        }
    }
}
