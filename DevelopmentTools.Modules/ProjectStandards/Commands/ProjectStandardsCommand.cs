using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.ProjectStandards.Revit2024.ExternalEvents;
using DevelopmentTools.ProjectStandards.UI.ViewModels;
using DevelopmentTools.ProjectStandards.UI.Views;

namespace DevelopmentTools.ProjectStandards.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProjectStandardsCommand : IExternalCommand
    {
        private static ProjectStandardsWindow _windowInstance;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (_windowInstance != null && _windowInstance.IsLoaded)
                {
                    _windowInstance.Activate();
                    return Result.Succeeded;
                }

                var uiApp = commandData.Application;
                var handler = new SimpleExternalEventHandler();
                var externalEvent = ExternalEvent.Create(handler);

                var viewModel = new ProjectStandardsViewModel(uiApp, externalEvent, handler);
                _windowInstance = new ProjectStandardsWindow(viewModel);
                _windowInstance.Closed += (s, e) => { _windowInstance = null; };
                _windowInstance.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }
    }
}
