using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class FillPatternSnapshot
    {
        public string Name { get; set; }

        public string TargetType { get; set; }

        public double Angle { get; set; }

        public double Spacing1 { get; set; }

        public double Spacing2 { get; set; }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForNamedStandard(string.Format("FillPattern::{0}", TargetType), Name); }
        }
    }
}
