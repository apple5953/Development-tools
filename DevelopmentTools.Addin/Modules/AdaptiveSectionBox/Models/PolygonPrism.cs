using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Models
{
    public class PolygonPrism
    {
        public List<Point2D> BasePoints { get; set; } = new List<Point2D>();
        public double ZMin { get; set; }
        public double ZMax { get; set; }

        public double Height => ZMax - ZMin;

        public PolygonPrism() { }

        public PolygonPrism(List<Point2D> basePoints, double zMin, double zMax)
        {
            BasePoints = basePoints.Select(p => p.Clone()).ToList();
            ZMin = zMin;
            ZMax = zMax;
        }

        public List<XYZ> GetVertices()
        {
            var list = new List<XYZ>();
            if (BasePoints == null) return list;

            // 1. 底面點 (n 個)
            foreach (var p in BasePoints)
            {
                list.Add(new XYZ(p.X, p.Y, ZMin));
            }
            // 2. 頂面點 (n 個)
            foreach (var p in BasePoints)
            {
                list.Add(new XYZ(p.X, p.Y, ZMax));
            }
            return list;
        }

        public BoundingBoxXYZ GetBoundingBox()
        {
            var bbox = new BoundingBoxXYZ();
            if (BasePoints == null || BasePoints.Count == 0)
            {
                bbox.Min = new XYZ(-10, -10, -10);
                bbox.Max = new XYZ(10, 10, 10);
                return bbox;
            }

            double minX = BasePoints.Min(p => p.X);
            double maxX = BasePoints.Max(p => p.X);
            double minY = BasePoints.Min(p => p.Y);
            double maxY = BasePoints.Max(p => p.Y);

            bbox.Min = new XYZ(minX, minY, ZMin);
            bbox.Max = new XYZ(maxX, maxY, ZMax);
            return bbox;
        }

        public PolygonPrism Clone()
        {
            return new PolygonPrism(BasePoints, ZMin, ZMax);
        }
    }
}
