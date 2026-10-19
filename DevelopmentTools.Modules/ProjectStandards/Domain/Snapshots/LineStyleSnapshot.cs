using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class LineStyleSnapshot
    {
        public string Name { get; set; }

        public int LineWeight { get; set; }

        public string ColorRgb { get; set; }

        public string LinePatternName { get; set; }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForNamedStandard("LineStyle", Name); }
        }
    }
}
