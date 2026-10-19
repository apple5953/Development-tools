using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class TextTypeSnapshot
    {
        public string Name { get; set; }

        public string FontName { get; set; }

        public double TextSize { get; set; }

        public double TabSize { get; set; }

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        public bool Underline { get; set; }

        public double WidthFactor { get; set; }

        public string ColorRgb { get; set; }

        public string Background { get; set; }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForNamedStandard("TextType", Name); }
        }
    }
}
