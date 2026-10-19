using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class TemplateFilterOverrideSnapshot
    {
        public string FilterName { get; set; }

        public string FilterStableKey { get; set; }

        public bool IsVisible { get; set; }

        public bool Halftone { get; set; }

        public int ProjectionLineWeight { get; set; }

        public string ProjectionLineColorRgb { get; set; }

        public string ProjectionLinePatternName { get; set; }

        public int CutLineWeight { get; set; }

        public string CutLineColorRgb { get; set; }

        public string CutLinePatternName { get; set; }

        public string SurfacePatternName { get; set; }

        public string CutPatternName { get; set; }
    }

    public class CategoryOverrideSnapshot
    {
        public string CategoryName { get; set; }

        public string BuiltInCategory { get; set; }

        public bool IsVisible { get; set; }

        public bool Halftone { get; set; }

        public int ProjectionLineWeight { get; set; }

        public string ProjectionLineColorRgb { get; set; }

        public int CutLineWeight { get; set; }

        public string CutLineColorRgb { get; set; }
    }

    public class ViewTemplateSnapshot
    {
        public string Name { get; set; }

        public string ViewType { get; set; }

        public int Scale { get; set; }

        public string Discipline { get; set; }

        public string DetailLevel { get; set; }

        public string Phase { get; set; }

        public string PhaseFilter { get; set; }

        public string ViewRangeTopLevel { get; set; }

        public double ViewRangeTopOffset { get; set; }

        public string ViewRangeCutLevel { get; set; }

        public double ViewRangeCutOffset { get; set; }

        public string ViewRangeBottomLevel { get; set; }

        public double ViewRangeBottomOffset { get; set; }

        public List<string> ControlledParameterNames { get; set; }

        public List<CategoryOverrideSnapshot> CategoryOverrides { get; set; }

        public List<TemplateFilterOverrideSnapshot> FilterOverrides { get; set; }

        public ViewTemplateSnapshot()
        {
            ControlledParameterNames = new List<string>();
            CategoryOverrides = new List<CategoryOverrideSnapshot>();
            FilterOverrides = new List<TemplateFilterOverrideSnapshot>();
        }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForViewTemplate(ViewType, Name); }
        }
    }
}
