using System;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Models
{
    public class AdaptiveRegion
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public PolygonPrism Geometry { get; set; }

        public AdaptiveRegion()
        {
            Id = Guid.NewGuid();
        }

        public AdaptiveRegion(string name, PolygonPrism geometry) : this()
        {
            Name = name;
            Geometry = geometry;
        }

        public AdaptiveRegion Clone()
        {
            return new AdaptiveRegion
            {
                Id = this.Id,
                Name = this.Name,
                Geometry = this.Geometry?.Clone()
            };
        }
    }
}
