using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class ScopeBoxSnapshot
    {
        public string Name { get; set; }

        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }

        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForNamedStandard("ScopeBox", Name); }
        }
    }
}
