using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.SheetTools.SheetDuplicator;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_SheetDuplicator : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                bool isAuthorized = true;
                if (GoogleAuthManager.IsAuthEnabled())
                {
                    isAuthorized = GoogleAuthManager.VerifyAccess("SheetTools", "圖紙逐層量化");
                }

                if (!isAuthorized)
                {
                    return Result.Failed;
                }

                DoShowDialog(commandData);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private void DoShowDialog(ExternalCommandData commandData)
        {
            try
            {
                var viewModel = new SheetDuplicatorViewModel(commandData);
                var window = new SheetDuplicatorWindow(viewModel);

                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = commandData.Application.MainWindowHandle;

                viewModel.RequestClose += () =>
                {
                    window.DialogResult = true;
                    window.Close();
                };

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", $"執行圖紙量化工具時發生異常：{ex.Message}");
            }
        }
    }
}
