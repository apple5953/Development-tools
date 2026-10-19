using System.Windows.Media;

namespace BEAI.QueryHost.UI
{
    internal static class RibbonIconFactory
    {
        private static readonly Color Green = Color.FromRgb(35, 145, 82);
        private static readonly Color Red = Color.FromRgb(196, 63, 63);
        private static readonly Color Blue = Color.FromRgb(45, 103, 160);
        private static readonly Color Gray = Color.FromRgb(88, 96, 105);
        private static readonly Color White = Colors.White;

        public static ImageSource CreateStatusIcon(double size)
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Green),
                null,
                new EllipseGeometry(new System.Windows.Point(8, 8), 7, 7)));
            group.Children.Add(Path("M4.4,8.2 L7,10.8 L11.8,5.6", null, White, 1.8));
            return CreateImage(group, size);
        }

        public static ImageSource CreatePowerIcon(double size)
        {
            var group = new DrawingGroup();
            group.Children.Add(Path("M8,1.8 L8,8", null, Green, 1.8));
            group.Children.Add(Path("M4.3,3.8 C1.5,6.2 2.2,11.1 5.4,13 C8.6,14.9 12.8,12.9 13.5,9.2 C13.9,7 13,5 11.7,3.8", null, Gray, 1.8));
            return CreateImage(group, size);
        }

        public static ImageSource CreateStopIcon(double size)
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Red),
                null,
                new RectangleGeometry(new System.Windows.Rect(3, 3, 10, 10), 1.5, 1.5)));
            return CreateImage(group, size);
        }

        public static ImageSource CreateReloadIcon(double size)
        {
            var group = new DrawingGroup();
            group.Children.Add(Path("M13,6 A5.5,5.5 0 1 0 13,10", null, Blue, 1.7));
            group.Children.Add(Path("M10.4,3.3 L13.3,6 L10.1,7.2", new SolidColorBrush(Blue), Gray, 0));
            return CreateImage(group, size);
        }

        public static ImageSource CreateWorkerIcon(double size)
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Blue),
                null,
                new RectangleGeometry(new System.Windows.Rect(3, 4, 10, 8), 1.5, 1.5)));
            group.Children.Add(Path("M5,7 L7,9 L5,11", null, White, 1.2));
            group.Children.Add(Path("M8.5,10.5 L11,10.5", null, White, 1.2));
            return CreateImage(group, size);
        }

        public static ImageSource CreateToolsIcon(double size)
        {
            var group = new DrawingGroup();
            group.Children.Add(Path("M3,12.8 L9.8,6 M8.6,3.1 C9.8,1.9 12,2 13.3,3.2 L10.8,5.7 L12.3,7.2 L14.7,4.8 C15.1,6.4 14.6,8.1 13.4,9.1 C12.2,10.1 10.5,10.2 9.2,9.5 L4.7,14 C4,14.7 2.8,14.7 2.1,14 C1.4,13.3 1.4,12.1 2.1,11.4 Z", null, Gray, 1.3));
            return CreateImage(group, size);
        }

        private static GeometryDrawing Path(string data, Brush fill, Color strokeColor, double thickness)
        {
            Pen pen = null;
            if (thickness > 0)
            {
                pen = new Pen(new SolidColorBrush(strokeColor), thickness);
                pen.StartLineCap = PenLineCap.Round;
                pen.EndLineCap = PenLineCap.Round;
                pen.LineJoin = PenLineJoin.Round;
            }
            return new GeometryDrawing(fill, pen, Geometry.Parse(data));
        }

        private static ImageSource CreateImage(DrawingGroup drawing, double size)
        {
            drawing.Transform = new ScaleTransform(size / 16.0, size / 16.0);
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
    }
}
