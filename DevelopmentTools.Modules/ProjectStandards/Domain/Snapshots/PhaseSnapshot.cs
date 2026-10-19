using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class PhaseSnapshot
    {
        public string Name { get; set; }

        public int SequenceNumber { get; set; }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForNamedStandard("Phase", Name); }
        }
    }
}
