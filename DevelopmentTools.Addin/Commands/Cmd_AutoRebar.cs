using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DevelopmentTools.Core;
using DevelopmentTools.UI;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_AutoRebar : IExternalCommand
    {
        private const double MmPerFoot = 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!CommandAccessGuard.EnsureAuthorized("AutoRebar", "可調式鋼筋配置"))
                {
                    return Result.Cancelled;
                }

                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                Document doc = uiDoc?.Document;
                if (doc == null)
                {
                    message = LanguageManager.Instance["Common_NoOpenProject"];
                    return Result.Failed;
                }

                Reference pickedRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new StructuralElementFilter(),
                    "選取一支混凝土樑或柱，工具會先產生可調式 3D 鋼筋預覽");

                Element element = doc.GetElement(pickedRef);
                PlacementFrame frame = PlacementFrame.FromElement(element);
                if (frame == null)
                {
                    TaskDialog.Show("鋼筋工具", "無法讀取選取構件的幾何或定位線。");
                    return Result.Cancelled;
                }

                var context = new RebarPreviewContext
                {
                    Kind = frame.Kind,
                    ElementName = element.Name,
                    LengthMm = ToMm(frame.LengthFt),
                    WidthMm = ToMm(frame.WidthFt),
                    HeightMm = ToMm(frame.HeightFt)
                };

                var configWindow = new RebarConfigWindow(context);
                var helper = new System.Windows.Interop.WindowInteropHelper(configWindow);
                helper.Owner = commandData.Application.MainWindowHandle;
                configWindow.ShowDialog();

                if (!configWindow.IsConfirmed || configWindow.Options == null)
                {
                    return Result.Cancelled;
                }

                RebarPlacementOptions options = configWindow.Options;
                RebarBarType topType = FindBarType(doc, options.TopBarDbMm);
                RebarBarType bottomType = FindBarType(doc, options.BottomBarDbMm) ?? topType;
                RebarBarType stirrupType = FindBarType(doc, options.StirrupDbMm) ?? topType;

                if (topType == null)
                {
                    TaskDialog.Show("鋼筋工具", "專案中找不到任何鋼筋型號，請先載入 Rebar Bar Type。");
                    return Result.Cancelled;
                }

                int createdCount;
                using (Transaction trans = new Transaction(doc, "可調式鋼筋配置"))
                {
                    trans.Start();
                    createdCount = frame.Kind == RebarElementKind.Column
                        ? CreateColumnRebar(doc, element, frame, options, topType, stirrupType)
                        : CreateBeamRebar(doc, element, frame, options, topType, bottomType, stirrupType);
                    trans.Commit();
                }

                TaskDialog.Show(
                    "鋼筋工具",
                    $"鋼筋已建立完成。\n\n建立數量：約 {createdCount} 組 Rebar\n接頭模式：{GetConnectionModeText(options.ConnectionMode)}\n\n提醒：續接器目前先以分段避讓表示；若要放置公司標準續接器族，下一版可加入族型選擇與自動放置。");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("鋼筋工具錯誤", ex.Message);
                return Result.Failed;
            }
        }

        private static int CreateBeamRebar(Document doc, Element host, PlacementFrame frame, RebarPlacementOptions options, RebarBarType topType, RebarBarType bottomType, RebarBarType stirrupType)
        {
            int count = 0;
            double cover = ToFt(options.CoverMm);
            double topDb = ToFt(options.TopBarDbMm);
            double botDb = ToFt(options.BottomBarDbMm);
            double stirrupDb = ToFt(options.StirrupDbMm);
            double halfL = frame.LengthFt / 2;
            double halfW = frame.WidthFt / 2;
            double halfH = frame.HeightFt / 2;
            double yMin = -halfW + cover + stirrupDb + topDb / 2;
            double yMax = halfW - cover - stirrupDb - topDb / 2;
            double zTop = halfH - cover - stirrupDb - topDb / 2;
            double zBot = -halfH + cover + stirrupDb + botDb / 2;
            double xStart = -halfL - ToFt(options.AnchorageMm);
            double xEnd = halfL + ToFt(options.AnchorageMm);

            foreach (double y in EvenPositions(yMin, yMax, options.TopBarCount))
            {
                count += CreateSegmentedStraightBars(doc, host, topType, frame, xStart, xEnd, y, zTop, frame.ZAxis, options, false);
            }

            foreach (double y in EvenPositions(yMin, yMax, options.BottomBarCount))
            {
                count += CreateSegmentedStraightBars(doc, host, bottomType, frame, xStart, xEnd, y, zBot, frame.ZAxis, options, false);
            }

            if (options.SideBarCount > 0)
            {
                double zMidMin = zBot + (zTop - zBot) * 0.35;
                double zMidMax = zBot + (zTop - zBot) * 0.65;
                foreach (double z in EvenPositions(zMidMin, zMidMax, options.SideBarCount))
                {
                    count += CreateSegmentedStraightBars(doc, host, bottomType, frame, xStart, xEnd, yMin, z, frame.ZAxis, options, false);
                    count += CreateSegmentedStraightBars(doc, host, bottomType, frame, xStart, xEnd, yMax, z, frame.ZAxis, options, false);
                }
            }

            double y1 = -halfW + cover;
            double y2 = halfW - cover;
            double z1 = -halfH + cover;
            double z2 = halfH - cover;
            foreach (double xMm in GetStirrupPositions(ToMm(frame.LengthFt), options))
            {
                double x = -halfL + ToFt(xMm);
                var curves = new List<Curve>
                {
                    Line.CreateBound(frame.ToWorld(x, y1, z1), frame.ToWorld(x, y2, z1)),
                    Line.CreateBound(frame.ToWorld(x, y2, z1), frame.ToWorld(x, y2, z2)),
                    Line.CreateBound(frame.ToWorld(x, y2, z2), frame.ToWorld(x, y1, z2)),
                    Line.CreateBound(frame.ToWorld(x, y1, z2), frame.ToWorld(x, y1, z1))
                };
                Rebar.CreateFromCurves(doc, RebarStyle.StirrupTie, stirrupType, null, null, host, frame.XAxis, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                count++;
            }

            return count;
        }

        private static int CreateColumnRebar(Document doc, Element host, PlacementFrame frame, RebarPlacementOptions options, RebarBarType mainType, RebarBarType stirrupType)
        {
            int count = 0;
            double cover = ToFt(options.CoverMm);
            double db = ToFt(options.TopBarDbMm);
            double stirrupDb = ToFt(options.StirrupDbMm);
            double halfL = frame.LengthFt / 2;
            double halfW = frame.WidthFt / 2;
            double halfD = frame.HeightFt / 2;
            double xMin = -halfW + cover + stirrupDb + db / 2;
            double xMax = halfW - cover - stirrupDb - db / 2;
            double yMin = -halfD + cover + stirrupDb + db / 2;
            double yMax = halfD - cover - stirrupDb - db / 2;
            double zStart = -halfL;
            double zEnd = halfL + ToFt(options.AnchorageMm);

            foreach (var pt in GetColumnBarPoints(xMin, xMax, yMin, yMax, options.TopBarCount, options.BottomBarCount))
            {
                count += CreateSegmentedStraightBars(doc, host, mainType, frame, zStart, zEnd, pt.X, pt.Y, frame.XAxis, options, true);
            }

            double tieX1 = -halfW + cover;
            double tieX2 = halfW - cover;
            double tieY1 = -halfD + cover;
            double tieY2 = halfD - cover;
            foreach (double zMm in GetStirrupPositions(ToMm(frame.LengthFt), options))
            {
                double z = -halfL + ToFt(zMm);
                var curves = new List<Curve>
                {
                    Line.CreateBound(frame.ToWorld(tieX1, tieY1, z), frame.ToWorld(tieX2, tieY1, z)),
                    Line.CreateBound(frame.ToWorld(tieX2, tieY1, z), frame.ToWorld(tieX2, tieY2, z)),
                    Line.CreateBound(frame.ToWorld(tieX2, tieY2, z), frame.ToWorld(tieX1, tieY2, z)),
                    Line.CreateBound(frame.ToWorld(tieX1, tieY2, z), frame.ToWorld(tieX1, tieY1, z))
                };
                Rebar.CreateFromCurves(doc, RebarStyle.StirrupTie, stirrupType, null, null, host, frame.ZAxis, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                count++;
            }

            return count;
        }

        private static int CreateSegmentedStraightBars(Document doc, Element host, RebarBarType type, PlacementFrame frame, double start, double end, double a, double b, XYZ normal, RebarPlacementOptions options, bool vertical)
        {
            int count = 0;
            double total = Math.Abs(end - start);
            double maxSegment = ToFt(options.MaxSegmentLengthMm);
            if (options.ConnectionMode == RebarConnectionMode.Continuous || total <= maxSegment)
            {
                CreateStraightBar(doc, host, type, frame, start, end, a, b, normal, vertical);
                return 1;
            }

            double direction = end >= start ? 1.0 : -1.0;
            double cursor = start;
            int guard = 0;
            while ((end - cursor) * direction > 1e-6 && guard++ < 100)
            {
                double rawEnd = cursor + direction * Math.Min(maxSegment, Math.Abs(end - cursor));
                bool isLast = Math.Abs(end - rawEnd) < 1e-6;
                double segmentEnd = rawEnd;
                double nextStart = rawEnd;

                if (!isLast)
                {
                    if (options.ConnectionMode == RebarConnectionMode.LapSplice)
                    {
                        double lap = ToFt(options.LapLengthMm);
                        segmentEnd = rawEnd + direction * lap / 2;
                        nextStart = rawEnd - direction * lap / 2;
                    }
                    else if (options.ConnectionMode == RebarConnectionMode.Coupler)
                    {
                        double gap = ToFt(options.CouplerGapMm);
                        segmentEnd = rawEnd - direction * gap / 2;
                        nextStart = rawEnd + direction * gap / 2;
                    }
                }

                CreateStraightBar(doc, host, type, frame, cursor, segmentEnd, a, b, normal, vertical);
                count++;
                cursor = nextStart;
            }

            return count;
        }

        private static void CreateStraightBar(Document doc, Element host, RebarBarType type, PlacementFrame frame, double start, double end, double a, double b, XYZ normal, bool vertical)
        {
            XYZ p1 = vertical ? frame.ToWorld(a, b, start) : frame.ToWorld(start, a, b);
            XYZ p2 = vertical ? frame.ToWorld(a, b, end) : frame.ToWorld(end, a, b);
            var curves = new List<Curve> { Line.CreateBound(p1, p2) };
            Rebar.CreateFromCurves(doc, RebarStyle.Standard, type, null, null, host, normal, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
        }

        private static RebarBarType FindBarType(Document doc, double dbMm)
        {
            string label = GetBarLabel(dbMm);
            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .ToList();

            RebarBarType exact = types.FirstOrDefault(t =>
                t.Name.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.Name.IndexOf(label.Replace("D", "#"), StringComparison.OrdinalIgnoreCase) >= 0);
            if (exact != null)
            {
                return exact;
            }

            string numeric = Math.Round(dbMm, 1).ToString("0.#");
            return types.FirstOrDefault(t => t.Name.IndexOf(numeric, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? types.FirstOrDefault();
        }

        private static string GetBarLabel(double dbMm)
        {
            if (Math.Abs(dbMm - 9.53) < 0.2) return "D10";
            if (Math.Abs(dbMm - 12.7) < 0.2) return "D13";
            if (Math.Abs(dbMm - 15.9) < 0.2) return "D16";
            if (Math.Abs(dbMm - 19.1) < 0.2) return "D19";
            if (Math.Abs(dbMm - 22.2) < 0.2) return "D22";
            if (Math.Abs(dbMm - 25.4) < 0.2) return "D25";
            if (Math.Abs(dbMm - 28.7) < 0.2) return "D29";
            if (Math.Abs(dbMm - 32.2) < 0.2) return "D32";
            if (Math.Abs(dbMm - 35.8) < 0.2) return "D36";
            return $"D{dbMm:0}";
        }

        private static List<double> GetStirrupPositions(double lengthMm, RebarPlacementOptions options)
        {
            var values = new List<double>();
            double pos = Math.Max(0, options.StartOffsetMm);
            double end = Math.Max(0, lengthMm - options.StartOffsetMm);
            int guard = 0;
            while (pos <= end && guard++ < 2000)
            {
                values.Add(pos);
                bool dense = pos <= options.DenseLengthMm || pos >= lengthMm - options.DenseLengthMm;
                pos += dense ? options.DenseSpacingMm : options.NormalSpacingMm;
            }

            return values;
        }

        private static IEnumerable<double> EvenPositions(double min, double max, int count)
        {
            if (count <= 1)
            {
                yield return (min + max) / 2;
                yield break;
            }

            for (int i = 0; i < count; i++)
            {
                yield return min + (max - min) * i / (count - 1);
            }
        }

        private static List<LocalPoint> GetColumnBarPoints(double xMin, double xMax, double yMin, double yMax, int xFaceCount, int yFaceCount)
        {
            var points = new List<LocalPoint>();
            foreach (double x in EvenPositions(xMin, xMax, Math.Max(2, xFaceCount)))
            {
                points.Add(new LocalPoint(x, yMin));
                points.Add(new LocalPoint(x, yMax));
            }

            foreach (double y in EvenPositions(yMin, yMax, Math.Max(2, yFaceCount)).Skip(1).Take(Math.Max(0, yFaceCount - 2)))
            {
                points.Add(new LocalPoint(xMin, y));
                points.Add(new LocalPoint(xMax, y));
            }

            return points
                .GroupBy(p => $"{p.X:0.000000},{p.Y:0.000000}")
                .Select(g => g.First())
                .ToList();
        }

        private static string GetConnectionModeText(RebarConnectionMode mode)
        {
            switch (mode)
            {
                case RebarConnectionMode.LapSplice:
                    return "搭接分段";
                case RebarConnectionMode.Coupler:
                    return "續接器分段";
                default:
                    return "連續鋼筋";
            }
        }

        private static double ToFt(double mm) => mm / MmPerFoot;
        private static double ToMm(double ft) => ft * MmPerFoot;

        private class LocalPoint
        {
            public LocalPoint(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double X { get; }
            public double Y { get; }
        }

        private class PlacementFrame
        {
            public RebarElementKind Kind { get; private set; }
            public XYZ Origin { get; private set; }
            public XYZ XAxis { get; private set; }
            public XYZ YAxis { get; private set; }
            public XYZ ZAxis { get; private set; }
            public double LengthFt { get; private set; }
            public double WidthFt { get; private set; }
            public double HeightFt { get; private set; }

            public XYZ ToWorld(double x, double y, double z)
            {
                return Origin + XAxis.Multiply(x) + YAxis.Multiply(y) + ZAxis.Multiply(z);
            }

            public static PlacementFrame FromElement(Element element)
            {
                if (element?.Category == null)
                {
                    return null;
                }

                int categoryId = element.Category.Id.IntegerValue;
                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox == null)
                {
                    return null;
                }

                if (categoryId == (int)BuiltInCategory.OST_StructuralFraming)
                {
                    return CreateBeamFrame(element, bbox);
                }

                if (categoryId == (int)BuiltInCategory.OST_StructuralColumns)
                {
                    return new PlacementFrame
                    {
                        Kind = RebarElementKind.Column,
                        Origin = (bbox.Min + bbox.Max).Multiply(0.5),
                        XAxis = XYZ.BasisX,
                        YAxis = XYZ.BasisY,
                        ZAxis = XYZ.BasisZ,
                        LengthFt = Math.Abs(bbox.Max.Z - bbox.Min.Z),
                        WidthFt = Math.Abs(bbox.Max.X - bbox.Min.X),
                        HeightFt = Math.Abs(bbox.Max.Y - bbox.Min.Y)
                    };
                }

                return null;
            }

            private static PlacementFrame CreateBeamFrame(Element element, BoundingBoxXYZ bbox)
            {
                XYZ start = null;
                XYZ end = null;
                if (element.Location is LocationCurve locationCurve)
                {
                    Curve curve = locationCurve.Curve;
                    start = curve.GetEndPoint(0);
                    end = curve.GetEndPoint(1);
                }

                if (start == null || end == null || start.DistanceTo(end) < 1e-6)
                {
                    start = new XYZ(bbox.Min.X, (bbox.Min.Y + bbox.Max.Y) / 2, (bbox.Min.Z + bbox.Max.Z) / 2);
                    end = new XYZ(bbox.Max.X, (bbox.Min.Y + bbox.Max.Y) / 2, (bbox.Min.Z + bbox.Max.Z) / 2);
                }

                XYZ xAxis = (end - start).Normalize();
                XYZ zAxis = XYZ.BasisZ;
                if (Math.Abs(xAxis.DotProduct(zAxis)) > 0.95)
                {
                    zAxis = XYZ.BasisY;
                }

                XYZ yAxis = zAxis.CrossProduct(xAxis).Normalize();
                zAxis = xAxis.CrossProduct(yAxis).Normalize();

                XYZ centerLineMid = (start + end).Multiply(0.5);
                var corners = GetBoundingBoxCorners(bbox);
                double minY = double.PositiveInfinity;
                double maxY = double.NegativeInfinity;
                double minZ = double.PositiveInfinity;
                double maxZ = double.NegativeInfinity;

                foreach (XYZ corner in corners)
                {
                    XYZ v = corner - centerLineMid;
                    double y = v.DotProduct(yAxis);
                    double z = v.DotProduct(zAxis);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                    minZ = Math.Min(minZ, z);
                    maxZ = Math.Max(maxZ, z);
                }

                XYZ origin = centerLineMid + yAxis.Multiply((minY + maxY) / 2) + zAxis.Multiply((minZ + maxZ) / 2);

                return new PlacementFrame
                {
                    Kind = RebarElementKind.Beam,
                    Origin = origin,
                    XAxis = xAxis,
                    YAxis = yAxis,
                    ZAxis = zAxis,
                    LengthFt = start.DistanceTo(end),
                    WidthFt = Math.Abs(maxY - minY),
                    HeightFt = Math.Abs(maxZ - minZ)
                };
            }

            private static IEnumerable<XYZ> GetBoundingBoxCorners(BoundingBoxXYZ bbox)
            {
                yield return new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z);
                yield return new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z);
                yield return new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z);
                yield return new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z);
                yield return new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z);
                yield return new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z);
                yield return new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z);
                yield return new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z);
            }
        }
    }

    public class StructuralElementFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem.Category == null) return false;
            return elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns ||
                   elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
