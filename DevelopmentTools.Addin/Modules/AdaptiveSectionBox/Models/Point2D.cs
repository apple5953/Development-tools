using System;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Models
{
    public class Point2D
    {
        public double X { get; set; }
        public double Y { get; set; }

        public Point2D() { }

        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public Point2D Clone()
        {
            return new Point2D(X, Y);
        }
    }
}
