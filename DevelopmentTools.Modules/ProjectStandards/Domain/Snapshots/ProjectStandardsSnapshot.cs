using System.Collections.Generic;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class ProjectStandardsSnapshot
    {
        public SnapshotMetadata Metadata { get; set; }

        public List<WorksetSnapshot> Worksets { get; set; }

        public List<ProjectParameterSnapshot> Parameters { get; set; }

        public List<LevelSnapshot> Levels { get; set; }

        public List<LinePatternSnapshot> LinePatterns { get; set; }

        public List<LineStyleSnapshot> LineStyles { get; set; }

        public List<FillPatternSnapshot> FillPatterns { get; set; }

        public List<ObjectStyleSnapshot> ObjectStyles { get; set; }

        public List<ViewFilterSnapshot> ViewFilters { get; set; }

        public List<ViewTemplateSnapshot> ViewTemplates { get; set; }

        public List<ScheduleSnapshot> Schedules { get; set; }

        public List<TextTypeSnapshot> TextTypes { get; set; }

        public List<DimensionTypeSnapshot> DimensionTypes { get; set; }

        public List<FamilyTypeSnapshot> FamilyTypes { get; set; }

        public List<LegendSnapshot> Legends { get; set; }

        public List<SheetSnapshot> Sheets { get; set; }

        public List<BrowserOrganizationSnapshot> BrowserOrganizations { get; set; }

        public List<PhaseSnapshot> Phases { get; set; }

        public List<ScopeBoxSnapshot> ScopeBoxes { get; set; }

        public SharedCoordinateSnapshot Coordinates { get; set; }

        public string Signature { get; set; }

        public ProjectStandardsSnapshot()
        {
            Metadata = new SnapshotMetadata();
            Worksets = new List<WorksetSnapshot>();
            Parameters = new List<ProjectParameterSnapshot>();
            Levels = new List<LevelSnapshot>();
            LinePatterns = new List<LinePatternSnapshot>();
            LineStyles = new List<LineStyleSnapshot>();
            FillPatterns = new List<FillPatternSnapshot>();
            ObjectStyles = new List<ObjectStyleSnapshot>();
            ViewFilters = new List<ViewFilterSnapshot>();
            ViewTemplates = new List<ViewTemplateSnapshot>();
            Schedules = new List<ScheduleSnapshot>();
            TextTypes = new List<TextTypeSnapshot>();
            DimensionTypes = new List<DimensionTypeSnapshot>();
            FamilyTypes = new List<FamilyTypeSnapshot>();
            Legends = new List<LegendSnapshot>();
            Sheets = new List<SheetSnapshot>();
            BrowserOrganizations = new List<BrowserOrganizationSnapshot>();
            Phases = new List<PhaseSnapshot>();
            ScopeBoxes = new List<ScopeBoxSnapshot>();
        }
    }
}
