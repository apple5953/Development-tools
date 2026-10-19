using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPerspectiveMinimap.Core;

namespace RevitPerspectiveMinimap.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TileFourViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                TaskDialog.Show("提示", "請先開啟一個專案檔案。");
                return Result.Cancelled;
            }

            View activeView = uiDoc.ActiveView;
            View3D view3D = activeView as View3D;
            if (view3D != null && view3D.IsPerspective)
            {
                MultiViewManager.OpenAndTileFourViews(commandData.Application, view3D.Id);
                return Result.Succeeded;
            }
            else
            {
                TaskDialog.Show("提示", "請在透視 3D 視圖中執行此命令，或在透視圖索引面板中選取透視圖！");
                return Result.Cancelled;
            }
        }
    }
}