using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class ScheduleFieldSnapshot
    {
        public string ParameterName { get; set; }

        public string ParameterStableKey { get; set; }

        public string Heading { get; set; }

        public bool IsHidden { get; set; }

        public bool IsCalculated { get; set; }

        public string ColumnAlignment { get; set; }

        public string DisplayUnitType { get; set; }
    }

    public class ScheduleSortGroupSnapshot
    {
        public string FieldHeading { get; set; }

        public bool SortDescending { get; set; }

        public bool ShowHeader { get; set; }

        public bool ShowFooter { get; set; }

        public bool BlankLine { get; set; }
    }

    public class ScheduleFilterSnapshot
    {
        public string FieldHeading { get; set; }

        public string FilterType { get; set; }

        public string Value { get; set; }
    }

    public class ScheduleSnapshot
    {
        public string Name { get; set; }

        public string Category { get; set; }

        public string ScheduleType { get; set; }

        public bool ItemizeEveryInstance { get; set; }

        public bool ShowGrandTotal { get; set; }

        public string GrandTotalTitle { get; set; }

        public List<ScheduleFieldSnapshot> Fields { get; set; }

        public List<ScheduleSortGroupSnapshot> SortGroupings { get; set; }

        public List<ScheduleFilterSnapshot> Filters { get; set; }

        public ScheduleSnapshot()
        {
            Fields = new List<ScheduleFieldSnapshot>();
            SortGroupings = new List<ScheduleSortGroupSnapshot>();
            Filters = new List<ScheduleFilterSnapshot>();
        }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForSchedule(Name, Category, ScheduleType); }
        }
    }
}
