using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ExampleAgent.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class OpenCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            TaskDialog.Show("Example Agent", "Template command is installed.");
            return Result.Succeeded;
        }
    }
}
