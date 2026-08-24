using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.SheetTools.AutoBoundary;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_AutoBoundary : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!CommandAccessGuard.EnsureAuthorized("AutoBoundary", "自動邊界與標籤"))
                {
                    return Result.Cancelled;
                }

                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                Document doc = uiDoc?.Document;
                if (doc == null)
                {
                    message = LanguageManager.Instance["Common_NoOpenProject"];
                    return Result.Failed;
                }

                View activeView = doc.ActiveView;
                if (activeView.ViewType != ViewType.FloorPlan &&
                    activeView.ViewType != ViewType.CeilingPlan &&
                    activeView.ViewType != ViewType.AreaPlan)
                {
                    TaskDialog.Show(LanguageManager.Instance["Ribbon_Btn_AutoBoundary"], LanguageManager.Instance["AutoBoundary_ViewError"]);
                    return Result.Cancelled;
                }

                var window = new AutoBoundaryWindow(uiDoc);
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = commandData.Application.MainWindowHandle;
                window.ShowDialog();

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
