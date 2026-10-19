using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class LevelSnapshot
    {
        public string Name { get; set; }

        public double Elevation { get; set; }

        public bool UseStory { get; set; }

        public double StoryAboveElevation { get; set; }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForNamedStandard("Level", Name); }
        }
    }
}
