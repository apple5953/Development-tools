using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class SharedCoordinateSnapshot
    {
        public string ActiveSiteLocationName { get; set; }

        public double OriginX { get; set; }
        public double OriginY { get; set; }
        public double OriginZ { get; set; }

        public double TrueNorthAngleRadians { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForNamedStandard("SharedCoordinate", ActiveSiteLocationName ?? "Default"); }
        }
    }
}
