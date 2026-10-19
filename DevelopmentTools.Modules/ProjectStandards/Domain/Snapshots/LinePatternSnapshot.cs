using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class LinePatternSegmentSnapshot
    {
        public string SegmentType { get; set; }

        public double Length { get; set; }
    }

    public class LinePatternSnapshot
    {
        public string Name { get; set; }

        public List<LinePatternSegmentSnapshot> Segments { get; set; }

        public LinePatternSnapshot()
        {
            Segments = new List<LinePatternSegmentSnapshot>();
        }

        public string PatternDefinition
        {
            get
            {
                if (Segments == null || Segments.Count == 0) return "Solid";
                var parts = new List<string>();
                foreach (var seg in Segments)
                {
                    parts.Add(string.Format("{0}:{1:F4}", seg.SegmentType, seg.Length));
                }
                return string.Join(";", parts.ToArray());
            }
        }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForLinePattern(Name, PatternDefinition); }
        }
    }
}
