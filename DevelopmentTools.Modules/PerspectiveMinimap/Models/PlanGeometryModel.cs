using System.Collections.Generic;

namespace RevitPerspectiveMinimap.Models
{
    public class Point2D
    {
        public double X { get; set; }
        public double Y { get; set; }

        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    public class LineSegment2D
    {
        public Point2D Start { get; set; }
        public Point2D End { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }

        public LineSegment2D(Point2D start, Point2D end, string category, string name)
        {
            Start = start;
            End = end;
            Category = category;
            Name = name;
        }

        public LineSegment2D(Point2D start, Point2D end)
            : this(start, end, "Wall", "")
        {
        }
    }

    public class PlanGeometryModel
    {
        private List<LineSegment2D> _lines = new List<LineSegment2D>();
        public List<LineSegment2D> Lines
        {
            get { return _lines; }
            set { _lines = value; }
        }

        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }

        public PlanGeometryModel()
        {
            MinX = -100;
            MaxX = 100;
            MinY = -100;
            MaxY = 100;
        }

        public double Width
        {
            get { return MaxX - MinX; }
        }

        public double Height
        {
            get { return MaxY - MinY; }
        }

        public void UpdateBounds(double x, double y)
        {
            if (Lines.Count == 0 && MinX == -100)
            {
                MinX = MaxX = x;
                MinY = MaxY = y;
                return;
            }

            if (x < MinX) MinX = x;
            if (x > MaxX) MaxX = x;
            if (y < MinY) MinY = y;
            if (y > MaxY) MaxY = y;
        }
    }
}