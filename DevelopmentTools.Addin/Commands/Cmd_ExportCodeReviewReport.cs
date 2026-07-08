using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.DesignReview.Services;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_ExportCodeReviewReport : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                string projectName = doc.Title;

                string assemblyFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string rulesDir = System.IO.Path.Combine(assemblyFolder, "Resources", "Rules");
                if (!System.IO.Directory.Exists(rulesDir))
                {
                    rulesDir = System.IO.Path.Combine(assemblyFolder, "Rules");
                }

                var tracker = new IssueTrackerService();
                var project = tracker.LoadOrCreateProject(doc, projectName, "集合住宅", "Residential_TW", rulesDir);
                
                var reporter = new ReportEngineService();
                reporter.ExportToCsv(project);

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
