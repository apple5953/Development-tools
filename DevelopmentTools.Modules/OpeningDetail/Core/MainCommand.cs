using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OpeningDetailAddin.ViewModels;
using OpeningDetailAddin.Views;

namespace OpeningDetailAddin.Core
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MainCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;

                if (uiDoc == null || uiDoc.Document == null)
                {
                    TaskDialog.Show("提示", "請先開啟一個 Revit 專案模型！");
                    return Result.Cancelled;
                }

                MainViewModel vm = new MainViewModel(uiApp);
                MainWindow window = new MainWindow(vm);
                
                System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = uiApp.MainWindowHandle;

                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("執行錯誤", string.Format("開口詳圖工具發生異常：\n{0}\n\n{1}", ex.Message, ex.StackTrace));
                return Result.Failed;
            }
        }
    }
}
