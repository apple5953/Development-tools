using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class DimensionTypeSnapshot
    {
        public string Name { get; set; }

        public string DimensionTypeStyle { get; set; }

        public string FontName { get; set; }

        public double TextSize { get; set; }

        public string TextColorRgb { get; set; }

        public string LineColorRgb { get; set; }

        public int LineWeight { get; set; }

        public string TickMarkName { get; set; }

        public double TickMarkSize { get; set; }

        public double WitnessLineGap { get; set; }

        public double WitnessLineExtension { get; set; }

        public string UnitsFormat { get; set; }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForNamedStandard(string.Format("DimensionType::{0}", DimensionTypeStyle), Name); }
        }
    }
}
