using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DevelopmentTools.Modules.DesignReview.Models;

namespace DevelopmentTools.Modules.DesignReview.Services
{
    public class DesignReviewService
    {
        private readonly Document _doc;

        public DesignReviewService(Document doc)
        {
            _doc = doc;
        }

        #region 1. 避難步行距離檢討 (A* 演算法)
        public List<EscapeDistanceIssue> CheckEscapeDistance()
        {
            var issues = new List<EscapeDistanceIssue>();

            var rooms = new FilteredElementCollector(_doc)
                .OfClass(typeof(SpatialElement))
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            var doors = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_Doors)
                .Cast<FamilyInstance>()
                .ToList();

            foreach (var room in rooms)
            {
                var exitDoors = doors.Where(d => d.ToRoom?.Id == room.Id || d.FromRoom?.Id == room.Id).ToList();
                if (!exitDoors.Any()) continue;

                XYZ exitPoint = exitDoors.First().Location is LocationPoint lp ? lp.Point : null;
                if (exitPoint == null) continue;

                BoundingBoxXYZ bbox = room.get_BoundingBox(null);
                if (bbox == null) continue;

                double gridSpacing = 0.8;
                XYZ min = bbox.Min;
                XYZ max = bbox.Max;

                XYZ farthestPoint = null;
                double maxDist = 0;
                
                List<XYZ> roomPoints = new List<XYZ>();
                for (double x = min.X; x <= max.X; x += gridSpacing)
                {
                    for (double y = min.Y; y <= max.Y; y += gridSpacing)
                    {
                        XYZ testPt = new XYZ(x, y, min.Z + 0.1);
                        if (room.IsPointInRoom(testPt))
                        {
                            roomPoints.Add(testPt);
                            double d = testPt.DistanceTo(exitPoint);
                            if (d > maxDist)
                            {
                                maxDist = d;
                                farthestPoint = testPt;
                            }
                        }
                    }
                }

                if (farthestPoint == null) continue;

                double actualPathLength = CalculateAStarDistance(room, farthestPoint, exitPoint, roomPoints, gridSpacing);

                double limitFeet = 30.0 / 0.3048; 
                if (actualPathLength > limitFeet)
                {
                    double lengthMeter = actualPathLength * 0.3048;
                    issues.Add(new EscapeDistanceIssue
                    {
                        IssueId = Guid.NewGuid().ToString(),
                        RuleName = "避難步行距離檢討",
                        Severity = Severity.Error,
                        Description = $"房間避難步行距離超標！實際長度為 {lengthMeter:F1}m (法規限制: 30m)。",
                        ElementId = room.Id,
                        ElementName = room.Name,
                        LevelName = room.Level?.Name,
                        Location = farthestPoint,
                        ActualDistanceMeter = lengthMeter,
                        LimitDistanceMeter = 30.0
                    });
                }
            }

            return issues;
        }

        private double CalculateAStarDistance(Room room, XYZ start, XYZ goal, List<XYZ> validPoints, double spacing)
        {
            var openSet = new List<AStarNode> { new AStarNode(start, 0, start.DistanceTo(goal)) };
            var closedSet = new HashSet<string>();

            while (openSet.Any())
            {
                var current = openSet.OrderBy(n => n.F).First();
                if (current.Point.DistanceTo(goal) < spacing * 1.5)
                {
                    return current.G + current.Point.DistanceTo(goal);
                }

                openSet.Remove(current);
                closedSet.Add(current.Key);

                foreach (var dir in GetDirections())
                {
                    XYZ neighborPt = current.Point + dir * spacing;
                    string key = $"{Math.Round(neighborPt.X, 1)}_{Math.Round(neighborPt.Y, 1)}";
                    
                    if (closedSet.Contains(key)) continue;
                    if (!room.IsPointInRoom(neighborPt)) continue;

                    double tentativeG = current.G + dir.GetLength();
                    var existing = openSet.FirstOrDefault(n => n.Key == key);

                    if (existing == null)
                    {
                        openSet.Add(new AStarNode(neighborPt, tentativeG, neighborPt.DistanceTo(goal)));
                    }
                    else if (tentativeG < existing.G)
                    {
                        existing.G = tentativeG;
                    }
                }
            }

            return start.DistanceTo(goal);
        }

        private List<XYZ> GetDirections()
        {
            return new List<XYZ>
            {
                new XYZ(1, 0, 0), new XYZ(-1, 0, 0), new XYZ(0, 1, 0), new XYZ(0, -1, 0),
                new XYZ(0.707, 0.707, 0), new XYZ(-0.707, 0.707, 0), new XYZ(0.707, -0.707, 0), new XYZ(-0.707, -0.707, 0)
            };
        }

        private class AStarNode
        {
            public XYZ Point { get; }
            public double G { get; set; }
            public double H { get; }
            public double F => G + H;
            public string Key => $"{Math.Round(Point.X, 1)}_{Math.Round(Point.Y, 1)}";

            public AStarNode(XYZ pt, double g, double h)
            {
                Point = pt;
                G = g;
                H = h;
            }
        }
        #endregion

        #region 2. 樓梯級高與級深安全標準檢討
        public List<ReviewIssue> CheckStairDimensions()
        {
            var issues = new List<ReviewIssue>();

            var stairs = new FilteredElementCollector(_doc)
                .OfClass(typeof(Stairs))
                .Cast<Stairs>()
                .ToList();

            foreach (var stair in stairs)
            {
                double actualRiser = stair.ActualRiserHeight * 30.48;
                double actualTread = stair.ActualTreadDepth * 30.48;

                if (actualRiser > 16.0 || actualTread < 26.0)
                {
                    issues.Add(new ReviewIssue
                    {
                        IssueId = Guid.NewGuid().ToString(),
                        RuleName = "樓梯級高級深安全檢核",
                        Severity = Severity.Error,
                        Description = $"樓梯尺寸不符安全標準！實際級高: {actualRiser:F1}cm (法規要求 <= 16cm)，實際級深: {actualTread:F1}cm (法規要求 >= 26cm)。",
                        ElementId = stair.Id,
                        ElementName = stair.Name,
                        LevelName = stair.Document.GetElement(stair.GroupId)?.Name ?? "未指定樓層"
                    });
                }
            }

            return issues;
        }
        #endregion

        #region 3. 無障礙迴轉圓空間檢核
        public List<ReviewIssue> CheckWheelchairSpace()
        {
            var issues = new List<ReviewIssue>();

            var rooms = new FilteredElementCollector(_doc)
                .OfClass(typeof(SpatialElement))
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            foreach (var room in rooms)
            {
                string name = room.Name;
                if (!(name.Contains("無障礙") || name.Contains("多功能") || name.Contains("殘障") || name.Contains("Barrier-Free"))) continue;

                var options = new SpatialElementBoundaryOptions();
                var boundaryList = room.GetBoundarySegments(options);
                if (boundaryList == null || !boundaryList.Any()) continue;

                List<Line> boundaryLines = new List<Line>();
                foreach (var loop in boundaryList)
                {
                    foreach (var seg in loop)
                    {
                        Curve curve = seg.GetCurve();
                        if (curve is Line line)
                        {
                            boundaryLines.Add(line);
                        }
                    }
                }

                BoundingBoxXYZ bbox = room.get_BoundingBox(null);
                if (bbox == null || !boundaryLines.Any()) continue;

                double gridSpacing = 0.5;
                double maxDiameterFeet = 0;
                XYZ bestCenter = null;

                for (double x = bbox.Min.X; x <= bbox.Max.X; x += gridSpacing)
                {
                    for (double y = bbox.Min.Y; y <= bbox.Max.Y; y += gridSpacing)
                    {
                        XYZ pt = new XYZ(x, y, bbox.Min.Z + 0.1);
                        if (room.IsPointInRoom(pt))
                        {
                            double minDist = double.MaxValue;
                            foreach (var line in boundaryLines)
                            {
                                double dist = line.Distance(pt);
                                if (dist < minDist) minDist = dist;
                            }

                            double diameter = minDist * 2;
                            if (diameter > maxDiameterFeet)
                            {
                                maxDiameterFeet = diameter;
                                bestCenter = pt;
                            }
                        }
                    }
                }

                double maxDiameterMeter = maxDiameterFeet * 0.3048;
                if (maxDiameterMeter < 1.5)
                {
                    issues.Add(new ReviewIssue
                    {
                        IssueId = Guid.NewGuid().ToString(),
                        RuleName = "無障礙迴轉圓空間檢核",
                        Severity = Severity.Error,
                        Description = $"無障礙空間無法容納直徑 1.5m 的輪椅迴轉圓！最大內切圓直徑僅 {maxDiameterMeter:F2}m。",
                        ElementId = room.Id,
                        ElementName = room.Name,
                        LevelName = room.Level?.Name,
                        Location = bestCenter ?? new XYZ((bbox.Min.X + bbox.Max.X)/2, (bbox.Min.Y + bbox.Max.Y)/2, bbox.Min.Z)
                    });
                }
            }

            return issues;
        }
        #endregion

        #region 4. 梁下與機電管線結構淨高檢核
        public List<NetHeightIssue> CheckNetHeight()
        {
            var issues = new List<NetHeightIssue>();

            var rooms = new FilteredElementCollector(_doc)
                .OfClass(typeof(SpatialElement))
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            View3D view3d = new FilteredElementCollector(_doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);

            if (view3d == null)
            {
                throw new InvalidOperationException("檢驗淨高需要專案中至少存在一個非樣板的 3D 視圖。");
            }

            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_CableTray
            };

            var catFilters = categories.Select(c => new ElementCategoryFilter(c)).ToList();
            var multiFilter = new LogicalOrFilter(catFilters.Cast<ElementFilter>().ToList());

            ReferenceIntersector intersector = new ReferenceIntersector(multiFilter, FindReferenceTarget.Element, view3d);
            intersector.FindReferencesInRevitLinks = false; 

            double limitHeightFeet = 2.1 / 0.3048;

            foreach (var room in rooms)
            {
                BoundingBoxXYZ bbox = room.get_BoundingBox(null);
                if (bbox == null) continue;

                double gridSpacing = 1.5; 
                
                for (double x = bbox.Min.X; x <= bbox.Max.X; x += gridSpacing)
                {
                    for (double y = bbox.Min.Y; y <= bbox.Max.Y; y += gridSpacing)
                    {
                        XYZ rayOrigin = new XYZ(x, y, bbox.Min.Z + 0.1);
                        if (room.IsPointInRoom(rayOrigin))
                        {
                            ReferenceWithContext hit = intersector.FindNearest(rayOrigin, new XYZ(0, 0, 1));
                            if (hit != null)
                            {
                                double netHeight = hit.Proximity + 0.1; 
                                if (netHeight < limitHeightFeet)
                                {
                                    double netHeightMeter = netHeight * 0.3048;
                                    Element hitElement = _doc.GetElement(hit.GetReference().ElementId);
                                    string hitName = hitElement != null ? $"{hitElement.Category?.Name}: {hitElement.Name}" : "結構/管線";

                                    issues.Add(new NetHeightIssue
                                    {
                                        IssueId = Guid.NewGuid().ToString(),
                                        RuleName = "淨高不足檢核",
                                        Severity = Severity.Warning,
                                        Description = $"淨高不足！此處梁下或管線淨高僅有 {netHeightMeter:F2}m (法規要求 >= 2.1m)。",
                                        ElementId = room.Id,
                                        ElementName = room.Name,
                                        LevelName = room.Level?.Name,
                                        Location = rayOrigin,
                                        RelatedElementIds = hitElement != null ? new List<ElementId> { hitElement.Id } : new List<ElementId>(),
                                        ActualHeightMeter = netHeightMeter,
                                        LimitHeightMeter = 2.1,
                                        HitElementName = hitName
                                    });

                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return issues;
        }
        #endregion

        #region 5. 機電穿梁/剪力牆套管遺漏比對
        public List<SleevePenetrationIssue> CheckSleevePenetrations()
        {
            var issues = new List<SleevePenetrationIssue>();

            var structuralWalls = new FilteredElementCollector(_doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w => w.StructuralUsage != Autodesk.Revit.DB.Structure.StructuralWallUsage.NonBearing)
                .ToList();

            var beams = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .Cast<FamilyInstance>()
                .ToList();

            var ducts = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType()
                .ToList();

            var pipes = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .ToList();

            var trays = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_CableTray)
                .WhereElementIsNotElementType()
                .ToList();

            List<Element> mepElements = new List<Element>();
            mepElements.AddRange(ducts);
            mepElements.AddRange(pipes);
            mepElements.AddRange(trays);

            List<Element> structElements = new List<Element>();
            structElements.AddRange(structuralWalls);
            structElements.AddRange(beams);

            foreach (var mep in mepElements)
            {
                BoundingBoxXYZ mepBbox = mep.get_BoundingBox(null);
                if (mepBbox == null) continue;

                var outline = new Outline(mepBbox.Min, mepBbox.Max);
                var filter = new BoundingBoxIntersectsFilter(outline);
                
                var collidingStructs = new FilteredElementCollector(_doc, structElements.Select(x => x.Id).ToList())
                    .WherePasses(filter)
                    .ToList();

                foreach (var structElem in collidingStructs)
                {
                    if (ElementIntersectsElement(mep, structElem))
                    {
                        XYZ intersectionPoint = GetIntersectionPoint(mep, structElem);
                        if (intersectionPoint == null) continue;

                        var sleeveFilter = new ElementCategoryFilter(BuiltInCategory.OST_MechanicalEquipment); 
                        var sphere = new Outline(intersectionPoint - new XYZ(0.8, 0.8, 0.8), intersectionPoint + new XYZ(0.8, 0.8, 0.8));
                        var boxFilter = new BoundingBoxIntersectsFilter(sphere);

                        var nearSleeves = new FilteredElementCollector(_doc)
                            .WherePasses(boxFilter)
                            .WherePasses(sleeveFilter)
                            .WhereElementIsNotElementType()
                            .Cast<FamilyInstance>()
                            .Where(x => x.Name.Contains("套管") || x.Symbol.Family.Name.Contains("套管") || x.Name.Contains("Sleeve"))
                            .ToList();

                        if (!nearSleeves.Any())
                        {
                            string mepSize = "";
                            Parameter diameterParam = mep.LookupParameter("Diameter") ?? mep.LookupParameter("管徑") ?? mep.LookupParameter("Size");
                            if (diameterParam != null && diameterParam.HasValue)
                            {
                                mepSize = diameterParam.AsValueString() ?? diameterParam.AsString() ?? "";
                            }

                            issues.Add(new SleevePenetrationIssue
                            {
                                IssueId = Guid.NewGuid().ToString(),
                                RuleName = "管線穿梁牆套管遺漏檢核",
                                Severity = Severity.Error,
                                Description = $"管線 ({mep.Category.Name}) 穿越結構體 ({structElem.Name}) 處，未配置穿孔套管 (Sleeve)！",
                                ElementId = mep.Id,
                                ElementName = mep.Name,
                                LevelName = mep.Document.GetElement(mep.LevelId)?.Name ?? "未指定樓層",
                                Location = intersectionPoint,
                                RelatedElementIds = new List<ElementId> { structElem.Id },
                                MepCategoryName = mep.Category.Name,
                                MepElementName = mep.Name,
                                MepSize = mepSize,
                                StructureElementName = structElem.Name
                            });
                        }
                    }
                }
            }

            return issues;
        }

        private bool ElementIntersectsElement(Element e1, Element e2)
        {
            var solid1 = GetElementSolid(e1);
            var solid2 = GetElementSolid(e2);
            if (solid1 == null || solid2 == null) return false;

            try
            {
                Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(solid1, solid2, BooleanOperationsType.Intersect);
                return intersection != null && intersection.Volume > 1e-4;
            }
            catch
            {
                return false;
            }
        }

        private XYZ GetIntersectionPoint(Element e1, Element e2)
        {
            var solid1 = GetElementSolid(e1);
            var solid2 = GetElementSolid(e2);
            if (solid1 == null || solid2 == null) return null;

            try
            {
                Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(solid1, solid2, BooleanOperationsType.Intersect);
                if (intersection != null && intersection.Volume > 1e-4)
                {
                    return intersection.ComputeCentroid();
                }
            }
            catch { }

            BoundingBoxXYZ b1 = e1.get_BoundingBox(null);
            BoundingBoxXYZ b2 = e2.get_BoundingBox(null);
            if (b1 != null && b2 != null)
            {
                double x = (Math.Max(b1.Min.X, b2.Min.X) + Math.Min(b1.Max.X, b2.Max.X)) / 2;
                double y = (Math.Max(b1.Min.Y, b2.Min.Y) + Math.Min(b1.Max.Y, b2.Max.Y)) / 2;
                double z = (Math.Max(b1.Min.Z, b2.Min.Z) + Math.Min(b1.Max.Z, b2.Max.Z)) / 2;
                return new XYZ(x, y, z);
            }
            return null;
        }

        private Solid GetElementSolid(Element elem)
        {
            Options geomOptions = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = true };
            GeometryElement geomElem = elem.get_Geometry(geomOptions);
            if (geomElem == null) return null;

            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid && solid.Volume > 1e-4) return solid;
                if (geomObj is GeometryInstance geomInst)
                {
                    GeometryElement instGeom = geomInst.GetInstanceGeometry();
                    foreach (GeometryObject instObj in instGeom)
                    {
                        if (instObj is Solid instSolid && instSolid.Volume > 1e-4) return instSolid;
                    }
                }
            }
            return null;
        }
        #endregion

        #region 6. 房間裝修參數與實際幾何對齊檢核
        public List<ReviewIssue> CheckRoomFinishes()
        {
            var issues = new List<ReviewIssue>();

            var rooms = new FilteredElementCollector(_doc)
                .OfClass(typeof(SpatialElement))
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            foreach (var room in rooms)
            {
                Parameter floorFinishParam = room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR);
                Parameter ceilingFinishParam = room.get_Parameter(BuiltInParameter.ROOM_FINISH_CEILING);

                string floorFinish = floorFinishParam?.AsString() ?? "";
                string ceilingFinish = ceilingFinishParam?.AsString() ?? "";

                if (string.IsNullOrEmpty(floorFinish) && string.IsNullOrEmpty(ceilingFinish)) continue;

                BoundingBoxXYZ bbox = room.get_BoundingBox(null);
                if (bbox == null) continue;

                if (!string.IsNullOrEmpty(floorFinish))
                {
                    var floorFilter = new ElementCategoryFilter(BuiltInCategory.OST_Floors);
                    var outline = new Outline(bbox.Min, bbox.Max);
                    var boxFilter = new BoundingBoxIntersectsFilter(outline);

                    var floorsInRoom = new FilteredElementCollector(_doc)
                        .WherePasses(boxFilter)
                        .WherePasses(floorFilter)
                        .WhereElementIsNotElementType()
                        .Cast<Floor>()
                        .ToList();

                    bool hasPhysicalFloor = floorsInRoom.Any(f => f.get_BoundingBox(null).Max.Z <= bbox.Max.Z + 0.1 && f.get_BoundingBox(null).Min.Z >= bbox.Min.Z - 0.5);
                    
                    if (!hasPhysicalFloor)
                    {
                        issues.Add(new ReviewIssue
                        {
                            IssueId = Guid.NewGuid().ToString(),
                            RuleName = "房間裝修幾何一致性檢核",
                            Severity = Severity.Warning,
                            Description = $"裝修參數衝突！房間設定地板裝修為「{floorFinish}」，但此房間範圍內未放置任何地板元件。",
                            ElementId = room.Id,
                            ElementName = room.Name,
                            LevelName = room.Level?.Name
                        });
                    }
                }

                if (!string.IsNullOrEmpty(ceilingFinish))
                {
                    var ceilingFilter = new ElementCategoryFilter(BuiltInCategory.OST_Ceilings);
                    var outline = new Outline(bbox.Min, bbox.Max);
                    var boxFilter = new BoundingBoxIntersectsFilter(outline);

                    var ceilingsInRoom = new FilteredElementCollector(_doc)
                        .WherePasses(boxFilter)
                        .WherePasses(ceilingFilter)
                        .WhereElementIsNotElementType()
                        .ToList();

                    bool hasPhysicalCeiling = ceilingsInRoom.Any();

                    if (!hasPhysicalCeiling)
                    {
                        issues.Add(new ReviewIssue
                        {
                            IssueId = Guid.NewGuid().ToString(),
                            RuleName = "房間裝修幾何一致性檢核",
                            Severity = Severity.Warning,
                            Description = $"裝修參數衝突！房間設定天花板裝修為「{ceilingFinish}」，但房間上方未繪製任何天花板元件。",
                            ElementId = room.Id,
                            ElementName = room.Name,
                            LevelName = room.Level?.Name
                        });
                    }
                }
            }

            return issues;
        }
        #endregion
    }
}
