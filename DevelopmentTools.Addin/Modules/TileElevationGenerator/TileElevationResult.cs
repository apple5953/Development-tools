using System.Collections.Generic;

namespace DevelopmentTools.Modules.TileElevationGenerator
{
    public class TileElevationResult
    {
        public bool Success { get; set; } = false;
        public int CreatedViewsCount { get; set; } = 0;
        public int SkippedWallsCount { get; set; } = 0;
        public List<string> CreatedViewNames { get; set; } = new List<string>();
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
