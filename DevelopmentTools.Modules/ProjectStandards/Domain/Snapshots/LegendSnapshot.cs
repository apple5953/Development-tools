using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class LegendSnapshot
    {
        public string Name { get; set; }

        public int Scale { get; set; }

        public int ElementCount { get; set; }

        public List<string> ContainedComponentNames { get; set; }

        public LegendSnapshot()
        {
            Scale = 100;
            ContainedComponentNames = new List<string>();
        }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForNamedStandard("Legend", Name); }
        }
    }
}
