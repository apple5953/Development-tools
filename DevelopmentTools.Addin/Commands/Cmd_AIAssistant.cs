using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.AIAssistant;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_AIAssistant : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                App.ShowOrActivateAIAssistant(commandData);
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
