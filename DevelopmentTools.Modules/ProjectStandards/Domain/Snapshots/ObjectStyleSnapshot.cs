using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class ObjectStyleSnapshot
    {
        public string CategoryName { get; set; }

        public string BuiltInCategory { get; set; }

        public int ProjectionLineWeight { get; set; }

        public int CutLineWeight { get; set; }

        public string LineColorRgb { get; set; }

        public string LinePatternName { get; set; }

        public string MaterialName { get; set; }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForNamedStandard("ObjectStyle", BuiltInCategory ?? CategoryName); }
        }
    }
}
