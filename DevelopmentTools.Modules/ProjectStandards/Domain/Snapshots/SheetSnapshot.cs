using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class PlacedViewportSnapshot
    {
        public string ViewName { get; set; }

        public string ViewType { get; set; }

        public double CenterX { get; set; }

        public double CenterY { get; set; }

        public double CenterZ { get; set; }
    }

    public class PlacedScheduleSnapshot
    {
        public string ScheduleName { get; set; }

        public double PointX { get; set; }

        public double PointY { get; set; }
    }

    public class SheetSnapshot
    {
        public string SheetNumber { get; set; }

        public string SheetName { get; set; }

        public string TitleBlockFamilyName { get; set; }

        public string TitleBlockTypeName { get; set; }

        public List<PlacedViewportSnapshot> Viewports { get; set; }

        public List<PlacedScheduleSnapshot> PlacedSchedules { get; set; }

        public SheetSnapshot()
        {
            Viewports = new List<PlacedViewportSnapshot>();
            PlacedSchedules = new List<PlacedScheduleSnapshot>();
        }

        public StableIdentity Identity
        {
            get
            {
                string key = string.Format("{0} - {1}", SheetNumber, SheetName);
                return StableIdentity.ForNamedStandard("Sheet", key);
            }
        }
    }
}
