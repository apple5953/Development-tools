using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPerspectiveMinimap.UI;

namespace RevitPerspectiveMinimap.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ShowMinimapPanelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;

                if (PerspectiveMinimapWindow.Instance == null || !PerspectiveMinimapWindow.Instance.IsLoaded)
                {
                    PerspectiveMinimapWindow window = new PerspectiveMinimapWindow();
                    PerspectiveMinimapWindow.Instance = window;

                    IntPtr revitHwnd = IntPtr.Zero;
                    try
                    {
                        revitHwnd = uiApp.MainWindowHandle;
                    }
                    catch { }

                    if (revitHwnd != IntPtr.Zero)
                    {
                        WindowInteropHelper helper = new WindowInteropHelper(window);
                        helper.Owner = revitHwnd;
                    }

                    window.Show();

                    if (uiDoc != null && uiDoc.Document != null)
                    {
                        View3D active3D = uiDoc.ActiveView as View3D;
                        window.ViewModel.RefreshData(uiDoc.Document, active3D);
                    }
                }
                else
                {
                    PerspectiveMinimapWindow.Instance.Activate();
                    if (PerspectiveMinimapWindow.Instance.WindowState == System.Windows.WindowState.Minimized)
                    {
                        PerspectiveMinimapWindow.Instance.WindowState = System.Windows.WindowState.Normal;
                    }

                    if (uiDoc != null && uiDoc.Document != null)
                    {
                        View3D active3D = uiDoc.ActiveView as View3D;
                        PerspectiveMinimapWindow.Instance.ViewModel.RefreshData(uiDoc.Document, active3D);
                    }
                }

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