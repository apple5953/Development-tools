using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;

namespace DevelopmentTools.Modules.SheetTools.AutoBoundary
{
    public partial class AutoBoundaryWindow : Window
    {
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        public AutoBoundaryWindow(UIDocument uiDoc)
        {
            InitializeComponent();
            _uiDoc = uiDoc;
            _doc = uiDoc.Document;
            
            // Link window owner to Revit window to keep focus correctly
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.Owner = uiDoc.Application.MainWindowHandle;

            TextStatus.Text = L("AutoBoundary_StatusReady");
        }

        private static string L(string key)
        {
            return LanguageManager.Instance[key];
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            string title = L("Tut_AutoBoundary_Title");
            TaskDialog td = new TaskDialog(title)
            {
                TitleAutoPrefix = false,
                MainInstruction = title,
                MainContent = L("Tut_AutoBoundary_Content")
            };
            td.Show();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            bool isRoomMode = RadioRoom.IsChecked == true;
            bool isSpaceMode = RadioSpace.IsChecked == true;
            bool isAreaMode = RadioArea.IsChecked == true;

            View activeView = _doc.ActiveView;

            Level level = activeView.GenLevel;
            if (level == null)
            {
                MessageBox.Show(L("AutoBoundary_LevelError"), L("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (isAreaMode && activeView.ViewType != ViewType.AreaPlan)
            {
                MessageBox.Show(L("AutoBoundary_AreaViewError"), L("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            double elevation = level.Elevation;

            Phase activePhase = null;
            Parameter phaseParam = activeView.get_Parameter(BuiltInParameter.VIEW_PHASE);
            if (phaseParam != null)
            {
                activePhase = _doc.GetElement(phaseParam.AsElementId()) as Phase;
            }

            // Get all walls visible in the active view
            var walls = new FilteredElementCollector(_doc, activeView.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            if (walls.Count == 0)
            {
                MessageBox.Show(L("AutoBoundary_NoWalls"), L("Common_Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Get sketch plane
            SketchPlane sketchPlane = activeView.SketchPlane;

            // Step 1: Draw boundaries along wall centerlines
            int boundaryLinesCount = 0;
            using (Transaction t1 = new Transaction(_doc, "自動繪製牆中心線邊界"))
            {
                t1.Start();

                // Create a sketch plane if null
                if (sketchPlane == null)
                {
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevation));
                    sketchPlane = SketchPlane.Create(_doc, plane);
                }

                // Delete existing boundary lines in this view to avoid overlapping issues
                if (isRoomMode)
                {
                    var existingSeparators = new FilteredElementCollector(_doc, activeView.Id)
                        .OfCategory(BuiltInCategory.OST_RoomSeparationLines)
                        .WhereElementIsNotElementType()
                        .ToElementIds();
                    if (existingSeparators.Count > 0)
                    {
                        _doc.Delete(existingSeparators);
                    }
                }
                else if (isSpaceMode)
                {
                    var existingSeparators = new FilteredElementCollector(_doc, activeView.Id)
                        .OfCategory(BuiltInCategory.OST_MEPSpaceSeparationLines)
                        .WhereElementIsNotElementType()
                        .ToElementIds();
                    if (existingSeparators.Count > 0)
                    {
                        _doc.Delete(existingSeparators);
                    }
                }
                else if (isAreaMode)
                {
                    var existingSeparators = new FilteredElementCollector(_doc, activeView.Id)
                        .OfCategory(BuiltInCategory.OST_AreaSchemeLines)
                        .WhereElementIsNotElementType()
                        .ToElementIds();
                    if (existingSeparators.Count > 0)
                    {
                        _doc.Delete(existingSeparators);
                    }
                }

                // Temporary Room Boundary Extraction Algorithm to ensure 100% closed loops
                System.Collections.Generic.List<Curve> roomBoundaryCurves = new System.Collections.Generic.List<Curve>();
                
                SpatialElementBoundaryOptions boundaryOptions = new SpatialElementBoundaryOptions();
                boundaryOptions.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center;

                // 1. Extract boundary curves from existing placed Rooms on this level
                var existingRooms = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.LevelId == level.Id && r.Location != null)
                    .ToList();

                foreach (Room room in existingRooms)
                {
                    try
                    {
                        var loops = room.GetBoundarySegments(boundaryOptions);
                        if (loops != null)
                        {
                            foreach (var loop in loops)
                            {
                                foreach (var segment in loop)
                                {
                                    Curve curve = segment.GetCurve();
                                    if (curve != null)
                                    {
                                        XYZ p1 = curve.GetEndPoint(0);
                                        XYZ p2 = curve.GetEndPoint(1);
                                        XYZ p1Flat = new XYZ(p1.X, p1.Y, elevation);
                                        XYZ p2Flat = new XYZ(p2.X, p2.Y, elevation);

                                        if (p1Flat.DistanceTo(p2Flat) > _doc.Application.ShortCurveTolerance)
                                        {
                                            Line line = curve as Line;
                                            Arc arc = curve as Arc;
                                            if (line != null)
                                                roomBoundaryCurves.Add(Line.CreateBound(p1Flat, p2Flat));
                                            else if (arc != null)
                                            {
                                                XYZ mid = arc.Evaluate(0.5, true);
                                                XYZ midFlat = new XYZ(mid.X, mid.Y, elevation);
                                                roomBoundaryCurves.Add(Arc.Create(p1Flat, p2Flat, midFlat));
                                            }
                                            else
                                                roomBoundaryCurves.Add(curve);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                // 2. Extract boundaries from unoccupied circuits by placing temporary rooms
                using (SubTransaction sub = new SubTransaction(_doc))
                {
                    sub.Start();

                    // Temporarily disable Room Bounding for walls in the "精裝修" workset
                    System.Collections.Generic.List<Wall> modifiedWalls = new System.Collections.Generic.List<Wall>();
                    foreach (Wall wall in walls)
                    {
                        if (_doc.IsWorkshared && wall.WorksetId != WorksetId.InvalidWorksetId)
                        {
                            WorksetTable worksetTable = _doc.GetWorksetTable();
                            Workset workset = worksetTable.GetWorkset(wall.WorksetId);
                            if (workset != null && workset.Name.Contains("精裝修"))
                            {
                                Parameter rbParam = wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                                if (rbParam != null && rbParam.AsInteger() == 1)
                                {
                                    rbParam.Set(0);
                                    modifiedWalls.Add(wall);
                                }
                            }
                        }
                    }

                    // Bulk-create rooms in all remaining unoccupied circuits on this level/phase
                    System.Collections.Generic.ICollection<ElementId> roomIds = null;
                    if (activePhase != null)
                    {
                        roomIds = _doc.Create.NewRooms2(level, activePhase);
                    }
                    else
                    {
                        roomIds = _doc.Create.NewRooms2(level);
                    }

                    foreach (ElementId roomId in roomIds)
                    {
                        try
                        {
                            Room tempRoom = _doc.GetElement(roomId) as Room;
                            if (tempRoom != null)
                            {
                                var loops = tempRoom.GetBoundarySegments(boundaryOptions);
                                if (loops != null)
                                {
                                    foreach (var loop in loops)
                                    {
                                        foreach (var segment in loop)
                                        {
                                            Curve curve = segment.GetCurve();
                                            if (curve != null)
                                            {
                                                XYZ p1 = curve.GetEndPoint(0);
                                                XYZ p2 = curve.GetEndPoint(1);
                                                XYZ p1Flat = new XYZ(p1.X, p1.Y, elevation);
                                                XYZ p2Flat = new XYZ(p2.X, p2.Y, elevation);

                                                if (p1Flat.DistanceTo(p2Flat) > _doc.Application.ShortCurveTolerance)
                                                {
                                                    Line line = curve as Line;
                                                    Arc arc = curve as Arc;
                                                    if (line != null)
                                                    {
                                                        roomBoundaryCurves.Add(Line.CreateBound(p1Flat, p2Flat));
                                                    }
                                                    else if (arc != null)
                                                    {
                                                        XYZ mid = arc.Evaluate(0.5, true);
                                                        XYZ midFlat = new XYZ(mid.X, mid.Y, elevation);
                                                        roomBoundaryCurves.Add(Arc.Create(p1Flat, p2Flat, midFlat));
                                                    }
                                                    else
                                                    {
                                                        roomBoundaryCurves.Add(curve);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Skip invalid rooms
                        }
                    }

                    // Roll back to discard temporary rooms and restore wall room bounding property
                    sub.RollBack();
                }

                // Filter out duplicate overlapping curves to keep the boundary lines clean and perfectly orthogonal
                System.Collections.Generic.List<Curve> uniqueCurves = new System.Collections.Generic.List<Curve>();
                foreach (Curve c in roomBoundaryCurves)
                {
                    bool isDuplicate = false;
                    foreach (Curve existing in uniqueCurves)
                    {
                        XYZ e1 = c.GetEndPoint(0);
                        XYZ e2 = c.GetEndPoint(1);
                        XYZ o1 = existing.GetEndPoint(0);
                        XYZ o2 = existing.GetEndPoint(1);

                        if ((e1.DistanceTo(o1) < 0.01 && e2.DistanceTo(o2) < 0.01) ||
                            (e1.DistanceTo(o2) < 0.01 && e2.DistanceTo(o1) < 0.01))
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                    if (!isDuplicate)
                    {
                        uniqueCurves.Add(c);
                    }
                }

                CurveArray curves = new CurveArray();
                foreach (Curve c in uniqueCurves)
                {
                    curves.Append(c);
                    boundaryLinesCount++;
                }

                // Draw boundaries
                if (curves.Size > 0)
                {
                    if (isRoomMode)
                    {
                        _doc.Create.NewRoomBoundaryLines(sketchPlane, curves, activeView);
                    }
                    else if (isSpaceMode)
                    {
                        _doc.Create.NewSpaceBoundaryLines(sketchPlane, curves, activeView);
                    }
                    else if (isAreaMode)
                    {
                        ViewPlan areaView = activeView as ViewPlan;
                        for (int i = 0; i < curves.Size; i++)
                        {
                            _doc.Create.NewAreaBoundaryLine(sketchPlane, curves.get_Item(i), areaView);
                        }
                    }
                }

                t1.Commit();
            }

            // Step 2: Auto-place Rooms/Spaces/Areas
            int placedCount = 0;

            using (Transaction t2 = new Transaction(_doc, "自動放置要素"))
            {
                t2.Start();

                if (isRoomMode)
                {
                    try
                    {
                        System.Collections.Generic.ICollection<ElementId> createdRooms = null;
                        if (activePhase != null)
                        {
                            createdRooms = _doc.Create.NewRooms2(level, activePhase);
                        }
                        else
                        {
                            createdRooms = _doc.Create.NewRooms2(level);
                        }
                        placedCount = createdRooms.Count;
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                {
                    // Get PlanTopology for the level
                    PlanTopology topology = _doc.get_PlanTopology(level);
                    
                    // Get active phase for spaces
                    Phase phase = activePhase;

                    // For Area Mode, collect existing areas in the view
                    List<Area> areasInView = null;
                    if (isAreaMode)
                    {
                        areasInView = new FilteredElementCollector(_doc, activeView.Id)
                            .OfCategory(BuiltInCategory.OST_Areas)
                            .Cast<Area>()
                            .ToList();
                    }

                    foreach (PlanCircuit circuit in topology.Circuits)
                    {
                        try
                        {
                            UV ptInside = circuit.GetPointInside();
                            if (ptInside == null) continue;
                            XYZ ptInside3D = new XYZ(ptInside.U, ptInside.V, elevation);

                            if (isSpaceMode)
                            {
                                Space existingSpace = _doc.GetSpaceAtPoint(ptInside3D);
                                if (existingSpace == null)
                                {
                                    Space space = _doc.Create.NewSpace(level, phase, ptInside);
                                    if (space != null)
                                    {
                                        placedCount++;
                                    }
                                }
                            }
                            else if (isAreaMode)
                            {
                                bool areaExists = false;
                                if (areasInView != null)
                                {
                                    foreach (Area a in areasInView)
                                    {
                                        // 1. Try point-in-polygon containment check (handles any shape/size)
                                        if (IsPointInArea(ptInside3D, a))
                                        {
                                            areaExists = true;
                                            break;
                                        }

                                        // 2. Fallback to location point proximity (covers unenclosed areas within 6.0 ft)
                                        LocationPoint lp = a.Location as LocationPoint;
                                        if (lp != null && lp.Point.DistanceTo(ptInside3D) < 6.0)
                                        {
                                            areaExists = true;
                                            break;
                                        }
                                    }
                                }

                                if (!areaExists)
                                {
                                    ViewPlan areaView = activeView as ViewPlan;
                                    Area area = _doc.Create.NewArea(areaView, ptInside);
                                    if (area != null)
                                    {
                                        placedCount++;
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Skip circuits that cannot get interior point or fail to place elements
                        }
                    }
                }

                t2.Commit();
            }

            string modeText = isRoomMode ? L("AutoBoundary_ModeRoom") : (isSpaceMode ? L("AutoBoundary_ModeSpace") : L("AutoBoundary_ModeArea"));
            TextStatus.Text = string.Format(L("AutoBoundary_DoneStatus"), boundaryLinesCount, placedCount, modeText);
            MessageBox.Show(string.Format(L("AutoBoundary_DoneMessage"), boundaryLinesCount, modeText, placedCount), L("Common_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            
            this.Close();
        }

        private bool IsPointInArea(XYZ point, Area area)
        {
            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();
            var loops = area.GetBoundarySegments(options);
            if (loops == null || loops.Count == 0) return false;

            // Collect vertices of the outer loop
            var vertices = new System.Collections.Generic.List<XYZ>();
            foreach (var segment in loops[0])
            {
                Curve curve = segment.GetCurve();
                if (curve != null)
                {
                    vertices.Add(curve.GetEndPoint(0));
                }
            }

            if (vertices.Count < 3) return false;
            return IsPointInPolygon2D(point, vertices);
        }

        private bool IsPointInPolygon2D(XYZ point, System.Collections.Generic.List<XYZ> polygon)
        {
            bool isInside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    isInside = !isInside;
                }
            }
            return isInside;
        }

        private XYZ GetSpatialElementCentroid(SpatialElement element)
        {
            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();
            options.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center;

            var boundaryLoops = element.GetBoundarySegments(options);
            if (boundaryLoops == null || boundaryLoops.Count == 0)
            {
                LocationPoint lp = element.Location as LocationPoint;
                return lp != null ? lp.Point : XYZ.Zero;
            }

            var outerLoop = boundaryLoops[0];
            if (outerLoop.Count == 0)
            {
                LocationPoint lp = element.Location as LocationPoint;
                return lp != null ? lp.Point : XYZ.Zero;
            }

            // Collect vertices in order
            var vertices = new System.Collections.Generic.List<XYZ>();
            foreach (var segment in outerLoop)
            {
                Curve curve = segment.GetCurve();
                if (curve == null) continue;
                vertices.Add(curve.GetEndPoint(0));
            }

            int n = vertices.Count;
            if (n < 3)
            {
                // Fallback if not a polygon
                LocationPoint lp = element.Location as LocationPoint;
                return lp != null ? lp.Point : XYZ.Zero;
            }

            // Calculate Area and Centroid using Polygon formula (area-weighted)
            double area = 0;
            double cx = 0;
            double cy = 0;
            double cz = vertices[0].Z;

            for (int i = 0; i < n; i++)
            {
                XYZ p1 = vertices[i];
                XYZ p2 = vertices[(i + 1) % n];

                double factor = (p1.X * p2.Y) - (p2.X * p1.Y);
                area += factor;
                cx += (p1.X + p2.X) * factor;
                cy += (p1.Y + p2.Y) * factor;
            }

            area *= 0.5;

            if (Math.Abs(area) > 0.001)
            {
                cx /= (6.0 * area);
                cy /= (6.0 * area);
                return new XYZ(cx, cy, cz);
            }
            else
            {
                // Fallback to vertex average
                double sumX = 0;
                double sumY = 0;
                foreach (var v in vertices)
                {
                    sumX += v.X;
                    sumY += v.Y;
                }
                return new XYZ(sumX / n, sumY / n, cz);
            }
        }

        private System.Collections.Generic.List<Curve> SnapAndTrimCurves(System.Collections.Generic.List<Curve> origCurves, double tolerance)
        {
            System.Collections.Generic.List<Line> lines = new System.Collections.Generic.List<Line>();
            System.Collections.Generic.List<Curve> nonLines = new System.Collections.Generic.List<Curve>();

            foreach (Curve c in origCurves)
            {
                Line line = c as Line;
                if (line != null)
                {
                    lines.Add(line);
                }
                else
                {
                    nonLines.Add(c);
                }
            }

            int n = lines.Count;
            XYZ[] p1s = new XYZ[n];
            XYZ[] p2s = new XYZ[n];
            for (int i = 0; i < n; i++)
            {
                p1s[i] = lines[i].GetEndPoint(0);
                p2s[i] = lines[i].GetEndPoint(1);
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    XYZ a1 = p1s[i];
                    XYZ a2 = p2s[i];
                    XYZ b1 = p1s[j];
                    XYZ b2 = p2s[j];

                    double d11 = a1.DistanceTo(b1);
                    double d12 = a1.DistanceTo(b2);
                    double d21 = a2.DistanceTo(b1);
                    double d22 = a2.DistanceTo(b2);

                    double minD = Math.Min(Math.Min(d11, d12), Math.Min(d21, d22));

                    if (minD < tolerance)
                    {
                        XYZ dirA = (a2 - a1).Normalize();
                        XYZ dirB = (b2 - b1).Normalize();

                        double det = dirB.X * dirA.Y - dirA.X * dirB.Y;
                        if (Math.Abs(det) > 0.01)
                        {
                            double t = ((b1.X - a1.X) * (-dirB.Y) - (b1.Y - a1.Y) * (-dirB.X)) / det;
                            XYZ intersect = a1 + t * dirA;

                            double distToA = DistanceToSegment(intersect, a1, a2);
                            double distToB = DistanceToSegment(intersect, b1, b2);

                            if (distToA < tolerance && distToB < tolerance)
                            {
                                if (a1.DistanceTo(intersect) < a2.DistanceTo(intersect))
                                {
                                    p1s[i] = intersect;
                                }
                                else
                                {
                                    p2s[i] = intersect;
                                }

                                if (b1.DistanceTo(intersect) < b2.DistanceTo(intersect))
                                {
                                    p1s[j] = intersect;
                                }
                                else
                                {
                                    p2s[j] = intersect;
                                }
                            }
                        }
                        else
                        {
                            if (d11 == minD) { p1s[i] = b1; p1s[j] = b1; }
                            else if (d12 == minD) { p1s[i] = b2; p2s[j] = b2; }
                            else if (d21 == minD) { p2s[i] = b1; p1s[j] = b1; }
                            else if (d22 == minD) { p2s[i] = b2; p2s[j] = b2; }
                        }
                    }
                }
            }

            System.Collections.Generic.List<Curve> result = new System.Collections.Generic.List<Curve>();
            for (int i = 0; i < n; i++)
            {
                if (p1s[i].DistanceTo(p2s[i]) > 0.001)
                {
                    result.Add(Line.CreateBound(p1s[i], p2s[i]));
                }
            }
            result.AddRange(nonLines);
            return result;
        }

        private double DistanceToSegment(XYZ pt, XYZ s1, XYZ s2)
        {
            XYZ v = s2 - s1;
            XYZ w = pt - s1;
            double c1 = w.DotProduct(v);
            if (c1 <= 0) return pt.DistanceTo(s1);
            double c2 = v.DotProduct(v);
            if (c2 <= c1) return pt.DistanceTo(s2);
            double b = c1 / c2;
            XYZ pb = s1 + b * v;
            return pt.DistanceTo(pb);
        }

        private void OptimizeButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            bool isRoomMode = RadioRoom.IsChecked == true;
            bool isSpaceMode = RadioSpace.IsChecked == true;
            bool isAreaMode = RadioArea.IsChecked == true;

            View activeView = _doc.ActiveView;
            Level level = activeView.GenLevel;
            if (level == null)
            {
                System.Windows.MessageBox.Show(L("AutoBoundary_LevelError"), L("Common_Error"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            if (isAreaMode && activeView.ViewType != ViewType.AreaPlan)
            {
                System.Windows.MessageBox.Show(L("AutoBoundary_AreaViewError"), L("Common_Error"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            // Get sketch plane
            SketchPlane sketchPlane = activeView.SketchPlane;
            double elevation = level.Elevation;

            BuiltInCategory lineCategory;
            if (isRoomMode) lineCategory = BuiltInCategory.OST_RoomSeparationLines;
            else if (isSpaceMode) lineCategory = BuiltInCategory.OST_MEPSpaceSeparationLines;
            else lineCategory = BuiltInCategory.OST_AreaSchemeLines;

            // Collect existing boundary lines in this view
            var boundaryLines = new FilteredElementCollector(_doc, activeView.Id)
                .OfCategory(lineCategory)
                .WhereElementIsNotElementType()
                .Cast<ModelCurve>()
                .ToList();

            if (boundaryLines.Count == 0)
            {
                System.Windows.MessageBox.Show(L("AutoBoundary_NoBoundaryLines"), L("Common_Info"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // Extract curves
            System.Collections.Generic.List<Curve> rawCurves = new System.Collections.Generic.List<Curve>();
            foreach (ModelCurve mc in boundaryLines)
            {
                try
                {
                    Curve curve = mc.GeometryCurve;
                    if (curve != null)
                    {
                        rawCurves.Add(curve);
                    }
                }
                catch (Exception)
                {
                    // Skip invalid geometry
                }
            }

            if (rawCurves.Count == 0)
            {
                System.Windows.MessageBox.Show(L("AutoBoundary_CurveError"), L("Common_Error"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            // Snap and trim curves (tolerance = 3.0 feet)
            System.Collections.Generic.List<Curve> optimizedCurves = SnapAndTrimCurves(rawCurves, 3.0);

            int optimizedCount = 0;
            using (Transaction t = new Transaction(_doc, "優化現有邊界線"))
            {
                t.Start();

                // Create sketch plane if null
                if (sketchPlane == null)
                {
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevation));
                    sketchPlane = SketchPlane.Create(_doc, plane);
                }

                // Delete old lines
                var idsToDelete = new System.Collections.Generic.List<ElementId>();
                foreach (ModelCurve mc in boundaryLines)
                {
                    idsToDelete.Add(mc.Id);
                }
                _doc.Delete(idsToDelete);

                // Draw optimized lines
                if (isRoomMode)
                {
                    CurveArray arr = new CurveArray();
                    foreach (Curve c in optimizedCurves)
                    {
                        arr.Append(c);
                    }
                    _doc.Create.NewRoomBoundaryLines(sketchPlane, arr, activeView);
                }
                else if (isSpaceMode)
                {
                    CurveArray arr = new CurveArray();
                    foreach (Curve c in optimizedCurves)
                    {
                        arr.Append(c);
                    }
                    _doc.Create.NewSpaceBoundaryLines(sketchPlane, arr, activeView);
                }
                else if (isAreaMode)
                {
                    ViewPlan areaView = activeView as ViewPlan;
                    foreach (Curve c in optimizedCurves)
                    {
                        _doc.Create.NewAreaBoundaryLine(sketchPlane, c, areaView);
                    }
                }

                optimizedCount = optimizedCurves.Count;
                t.Commit();
            }

            TextStatus.Text = string.Format(L("AutoBoundary_OptimizeDoneStatus"), boundaryLines.Count, optimizedCount);
            System.Windows.MessageBox.Show(string.Format(L("AutoBoundary_OptimizeDoneMessage"), boundaryLines.Count, optimizedCount), L("Common_Success"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            
            this.Close();
        }
    }
}
