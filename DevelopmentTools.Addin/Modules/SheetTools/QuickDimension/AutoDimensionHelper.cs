using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DevelopmentTools.Core;

namespace DevelopmentTools.Modules.SheetTools.QuickDimension
{
    public static class AutoDimensionHelper
    {
        public static void ApplyStoredAnnotations(Document doc, View view)
        {
            try
            {
                string json = ViewAnnotationStorageManager.LoadConfig(view);
                
                // 支援從視圖套用的視圖樣板 (View Template) 中讀取邏輯以利複用
                if (string.IsNullOrEmpty(json) && view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    View templateView = doc.GetElement(view.ViewTemplateId) as View;
                    if (templateView != null)
                    {
                        json = ViewAnnotationStorageManager.LoadConfig(templateView);
                    }
                }

                if (string.IsNullOrEmpty(json)) return;

                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var config = System.Text.Json.JsonSerializer.Deserialize<ViewAnnotationConfig>(json, options);
                if (config == null || !config.AutoApplyOnTransfer) return;

                // 若是舊版格式，沒有 Logics 則相容舊格式
                if (config.Logics == null || !config.Logics.Any())
                {
                    ApplyLegacyConfig(doc, view, config);
                    return;
                }

                foreach (var logic in config.Logics)
                {
                    if (!logic.IsEnabled) continue;

                    // 尋找目標 DimensionType
                    DimensionType dimType = new FilteredElementCollector(doc)
                        .OfClass(typeof(DimensionType))
                        .Cast<DimensionType>()
                        .FirstOrDefault(t => t.Name == logic.DimensionTypeName && t.StyleType == DimensionStyleType.Linear);

                    if (dimType == null)
                    {
                        dimType = new FilteredElementCollector(doc)
                            .OfClass(typeof(DimensionType))
                            .Cast<DimensionType>()
                            .FirstOrDefault(t => t.StyleType == DimensionStyleType.Linear);
                    }

                    if (dimType == null) continue;

                    var tempConfig = new ViewAnnotationConfig
                    {
                        TaggingMode = logic.TaggingMode,
                        DimensionTypeName = logic.DimensionTypeName,
                        OffsetDistance = logic.OffsetDistance,
                        PrefixText = logic.PrefixText,
                        SuffixText = logic.SuffixText
                    };

                    switch (logic.TaggingMode)
                    {
                        case 0:
                            AutoTagColumns(doc, view, dimType, tempConfig);
                            break;
                        case 1:
                            AutoTagWalls(doc, view, dimType, tempConfig);
                            break;
                        case 2:
                            AutoTagWallEdges(doc, view, dimType, tempConfig);
                            break;
                        case 3:
                            AutoTagWallOpenings(doc, view, dimType, tempConfig);
                            break;
                        case 4:
                            AutoTagBeams(doc, view, dimType, tempConfig);
                            break;
                    }
                }
            }
            catch
            {
                // 安靜失敗，不干擾轉移主流程
            }
        }

        private static void ApplyLegacyConfig(Document doc, View view, ViewAnnotationConfig config)
        {
            DimensionType dimType = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .FirstOrDefault(t => t.Name == config.DimensionTypeName && t.StyleType == DimensionStyleType.Linear)
                ?? new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .FirstOrDefault(t => t.StyleType == DimensionStyleType.Linear);

            if (dimType == null) return;

            switch (config.TaggingMode)
            {
                case 0:
                    AutoTagColumns(doc, view, dimType, config);
                    break;
                case 1:
                    AutoTagWalls(doc, view, dimType, config);
                    break;
                case 2:
                    AutoTagWallEdges(doc, view, dimType, config);
                    break;
                case 3:
                    AutoTagWallOpenings(doc, view, dimType, config);
                    break;
                case 4:
                    AutoTagBeams(doc, view, dimType, config);
                    break;
            }
        }

        private static void RunActionInTransaction(Document doc, string transactionName, Action action)
        {
            if (doc.IsModifiable)
            {
                using (SubTransaction sub = new SubTransaction(doc))
                {
                    sub.Start();
                    try
                    {
                        action();
                        sub.Commit();
                    }
                    catch
                    {
                        sub.RollBack();
                        throw;
                    }
                }
            }
            else
            {
                using (Transaction trans = new Transaction(doc, transactionName))
                {
                    trans.Start();
                    try
                    {
                        action();
                        trans.Commit();
                    }
                    catch
                    {
                        trans.RollBack();
                        throw;
                    }
                }
            }
        }

        private static void ApplyDimensionTexts(Dimension dim, ViewAnnotationConfig config)
        {
            if (dim == null || config == null) return;
            if (!string.IsNullOrEmpty(config.PrefixText) || !string.IsNullOrEmpty(config.SuffixText))
            {
                if (dim.NumberOfSegments > 0)
                {
                    foreach (DimensionSegment seg in dim.Segments)
                    {
                        if (!string.IsNullOrEmpty(config.PrefixText)) seg.Prefix = config.PrefixText;
                        if (!string.IsNullOrEmpty(config.SuffixText)) seg.Suffix = config.SuffixText;
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(config.PrefixText)) dim.Prefix = config.PrefixText;
                    if (!string.IsNullOrEmpty(config.SuffixText)) dim.Suffix = config.SuffixText;
                }
            }
        }

        private static void AutoTagColumns(Document doc, View view, DimensionType dimType, ViewAnnotationConfig config)
        {
            var cols = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(c => c.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns || 
                            c.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Columns)
                .ToList();

            if (cols.Count < 2) return;

            double tolerance = 1.0;
            var horizontalGroups = new List<List<FamilyInstance>>();
            var verticalGroups = new List<List<FamilyInstance>>();

            foreach (var col in cols)
            {
                XYZ pt = (col.Location as LocationPoint)?.Point;
                if (pt == null) continue;

                var hGroup = horizontalGroups.FirstOrDefault(g => Math.Abs((g[0].Location as LocationPoint).Point.Y - pt.Y) < tolerance);
                if (hGroup != null) hGroup.Add(col);
                else horizontalGroups.Add(new List<FamilyInstance> { col });

                var vGroup = verticalGroups.FirstOrDefault(g => Math.Abs((g[0].Location as LocationPoint).Point.X - pt.X) < tolerance);
                if (vGroup != null) vGroup.Add(col);
                else verticalGroups.Add(new List<FamilyInstance> { col });
            }

            foreach (var g in horizontalGroups.Where(g => g.Count >= 2))
            {
                CreateColumnDimension(doc, view, g, true, dimType, config);
            }
            foreach (var g in verticalGroups.Where(g => g.Count >= 2))
            {
                CreateColumnDimension(doc, view, g, false, dimType, config);
            }
        }

        private static void CreateColumnDimension(Document doc, View view, List<FamilyInstance> cols, bool isHorizontal, DimensionType dimType, ViewAnnotationConfig config)
        {
            var pts = cols.Select(c => (c.Location as LocationPoint).Point).OrderBy(p => isHorizontal ? p.X : p.Y).ToList();
            
            ReferenceArray refArray = new ReferenceArray();
            foreach (var col in cols)
            {
                Reference colRef = GetColumnCenterReference(col, isHorizontal);
                if (colRef != null) refArray.Append(colRef);
            }

            if (refArray.Size < 2) return;

            XYZ normal = isHorizontal ? XYZ.BasisY : XYZ.BasisX;
            double offsetFeet = config.OffsetDistance / 304.8;
            XYZ offsetVec = normal * offsetFeet;

            XYZ p1 = pts.First();
            XYZ p2 = pts.Last();
            Line dimLine = Line.CreateBound(p1 + offsetVec, p2 + offsetVec);

            RunActionInTransaction(doc, "自動標註-柱中心", () =>
            {
                Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);
                if (dim != null)
                {
                    dim.DimensionType = dimType;
                    ApplyDimensionTexts(dim, config);
                }
            });
        }

        private static Reference GetColumnCenterReference(FamilyInstance col, bool isHorizontal)
        {
            IList<Reference> refs = col.GetReferences(isHorizontal 
                ? FamilyInstanceReferenceType.CenterLeftRight 
                : FamilyInstanceReferenceType.CenterFrontBack);

            if (refs != null && refs.Count > 0) return refs[0];

            foreach (FamilyInstanceReferenceType type in Enum.GetValues(typeof(FamilyInstanceReferenceType)))
            {
                if (type.ToString().Contains("Center"))
                {
                    var tempRefs = col.GetReferences(type);
                    if (tempRefs.Count > 0) return tempRefs[0];
                }
            }
            return null;
        }

        private static void AutoTagWalls(Document doc, View view, DimensionType dimType, ViewAnnotationConfig config)
        {
            var walls = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            if (walls.Count < 2) return;

            var groups = new List<List<Wall>>();
            foreach (var wall in walls)
            {
                if (!(wall.Location is LocationCurve lc && lc.Curve is Line line)) continue;
                XYZ dir = line.Direction.Normalize();

                var matchedGroup = groups.FirstOrDefault(g => {
                    if (!(g[0].Location is LocationCurve lcG && lcG.Curve is Line lineG)) return false;
                    XYZ gDir = lineG.Direction.Normalize();
                    return Math.Abs(Math.Abs(dir.DotProduct(gDir)) - 1.0) < 1e-3;
                });

                if (matchedGroup != null) matchedGroup.Add(wall);
                else groups.Add(new List<Wall> { wall });
            }

            foreach (var g in groups.Where(g => g.Count >= 2))
            {
                ReferenceArray refArray = new ReferenceArray();
                List<XYZ> pts = new List<XYZ>();

                foreach (var w in g)
                {
                    refArray.Append(new Reference(w));
                    if (w.Location is LocationCurve lc && lc.Curve is Line line)
                    {
                        pts.Add(line.Evaluate(0.5, true));
                    }
                }

                if (refArray.Size < 2 || pts.Count < 2) continue;

                XYZ avgPt = new XYZ(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));
                
                XYZ wallDir = XYZ.BasisX;
                if (g[0].Location is LocationCurve lc0 && lc0.Curve is Line line0)
                {
                    wallDir = line0.Direction.Normalize();
                }

                XYZ normal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();
                double offsetFeet = config.OffsetDistance / 304.8;
                XYZ offsetVec = normal * offsetFeet;

                XYZ lineStart = avgPt + offsetVec;
                XYZ lineEnd = lineStart + wallDir * 10.0;
                Line dimLine = Line.CreateBound(lineStart, lineEnd);

                RunActionInTransaction(doc, "自動標註-牆中心", () =>
                {
                    Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);
                    if (dim != null)
                    {
                        dim.DimensionType = dimType;
                        ApplyDimensionTexts(dim, config);
                    }
                });
            }
        }

        private static void AutoTagWallEdges(Document doc, View view, DimensionType dimType, ViewAnnotationConfig config)
        {
            var walls = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            if (walls.Count < 2) return;

            var groups = new List<List<Wall>>();
            foreach (var wall in walls)
            {
                if (!(wall.Location is LocationCurve lc && lc.Curve is Line line)) continue;
                XYZ dir = line.Direction.Normalize();

                var matchedGroup = groups.FirstOrDefault(g => {
                    if (!(g[0].Location is LocationCurve lcG && lcG.Curve is Line lineG)) return false;
                    XYZ gDir = lineG.Direction.Normalize();
                    return Math.Abs(Math.Abs(dir.DotProduct(gDir)) - 1.0) < 1e-3;
                });

                if (matchedGroup != null) matchedGroup.Add(wall);
                else groups.Add(new List<Wall> { wall });
            }

            Options geomOptions = new Options { ComputeReferences = true, View = view };

            foreach (var g in groups.Where(g => g.Count >= 2))
            {
                ReferenceArray refArray = new ReferenceArray();
                List<XYZ> pts = new List<XYZ>();

                foreach (var w in g.Take(2))
                {
                    GeometryElement geomElem = w.get_Geometry(geomOptions);
                    if (geomElem == null) continue;

                    foreach (var obj in geomElem)
                    {
                        if (obj is Solid solid)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                if (face.Reference != null)
                                {
                                    refArray.Append(face.Reference);
                                }
                            }
                        }
                    }

                    if (w.Location is LocationCurve lc && lc.Curve is Line line)
                    {
                        pts.Add(line.Evaluate(0.5, true));
                    }
                }

                if (refArray.Size < 2 || pts.Count < 2) continue;

                XYZ avgPt = new XYZ(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));
                XYZ wallDir = XYZ.BasisX;
                if (g[0].Location is LocationCurve lc0 && lc0.Curve is Line line0)
                {
                    wallDir = line0.Direction.Normalize();
                }

                XYZ normal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();
                double offsetFeet = config.OffsetDistance / 304.8;
                XYZ offsetVec = normal * offsetFeet;

                XYZ lineStart = avgPt + offsetVec;
                XYZ lineEnd = lineStart + wallDir * 10.0;
                Line dimLine = Line.CreateBound(lineStart, lineEnd);

                RunActionInTransaction(doc, "自動標註-牆邊到牆邊", () =>
                {
                    Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);
                    if (dim != null)
                    {
                        dim.DimensionType = dimType;
                        ApplyDimensionTexts(dim, config);
                    }
                });
            }
        }

        private static void AutoTagWallOpenings(Document doc, View view, DimensionType dimType, ViewAnnotationConfig config)
        {
            var doorsAndWindows = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors || 
                            fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows)
                .ToList();

            if (!doorsAndWindows.Any()) return;

            var groups = doorsAndWindows
                .Where(fi => fi.Host != null)
                .GroupBy(fi => fi.Host.Id.IntegerValue)
                .ToList();

            foreach (var g in groups)
            {
                ReferenceArray refArray = new ReferenceArray();
                List<XYZ> pts = new List<XYZ>();

                foreach (var item in g)
                {
                    var leftRefs = item.GetReferences(FamilyInstanceReferenceType.Left);
                    var rightRefs = item.GetReferences(FamilyInstanceReferenceType.Right);

                    if (leftRefs.Count > 0 && rightRefs.Count > 0)
                    {
                        refArray.Append(leftRefs[0]);
                        refArray.Append(rightRefs[0]);
                    }

                    if (item.Location is LocationPoint lp)
                    {
                        pts.Add(lp.Point);
                    }
                }

                if (refArray.Size < 2) continue;

                XYZ avgPt = pts.Count > 0 ? new XYZ(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z)) : XYZ.Zero;
                
                XYZ wallDir = XYZ.BasisX;
                FamilyInstance firstItem = g.First();
                if (firstItem.Host is Wall hostWall && hostWall.Location is LocationCurve lc && lc.Curve is Line line)
                {
                    wallDir = line.Direction.Normalize();
                }

                XYZ normal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();
                double offsetFeet = config.OffsetDistance / 304.8;
                XYZ offsetVec = normal * offsetFeet;

                XYZ lineStart = avgPt + offsetVec;
                XYZ lineEnd = lineStart + wallDir * 10.0;
                Line dimLine = Line.CreateBound(lineStart, lineEnd);

                RunActionInTransaction(doc, "自動標註-開口邊", () =>
                {
                    Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);
                    if (dim != null)
                    {
                        dim.DimensionType = dimType;
                        ApplyDimensionTexts(dim, config);
                    }
                });
            }
        }

        private static void AutoTagBeams(Document doc, View view, DimensionType dimType, ViewAnnotationConfig config)
        {
            var beams = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();

            if (beams.Count < 2) return;

            var groups = new List<List<FamilyInstance>>();
            foreach (var beam in beams)
            {
                if (!(beam.Location is LocationCurve lc && lc.Curve is Line line)) continue;
                XYZ dir = line.Direction.Normalize();

                var matchedGroup = groups.FirstOrDefault(g => {
                    if (!(g[0].Location is LocationCurve lcG && lcG.Curve is Line lineG)) return false;
                    XYZ gDir = lineG.Direction.Normalize();
                    return Math.Abs(Math.Abs(dir.DotProduct(gDir)) - 1.0) < 1e-3;
                });

                if (matchedGroup != null) matchedGroup.Add(beam);
                else groups.Add(new List<FamilyInstance> { beam });
            }

            Options geomOptions = new Options { ComputeReferences = true, View = view };

            foreach (var g in groups.Where(g => g.Count >= 2))
            {
                ReferenceArray refArray = new ReferenceArray();
                List<XYZ> pts = new List<XYZ>();

                foreach (var b in g.Take(2))
                {
                    GeometryElement geomElem = b.get_Geometry(geomOptions);
                    if (geomElem == null) continue;

                    foreach (var obj in geomElem)
                    {
                        if (obj is Solid solid)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                if (face.Reference != null)
                                {
                                    refArray.Append(face.Reference);
                                }
                            }
                        }
                    }

                    if (b.Location is LocationCurve lc && lc.Curve is Line line)
                    {
                        pts.Add(line.Evaluate(0.5, true));
                    }
                }

                if (refArray.Size < 2 || pts.Count < 2) continue;

                XYZ avgPt = new XYZ(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));
                XYZ beamDir = XYZ.BasisX;
                if (g[0].Location is LocationCurve lc0 && lc0.Curve is Line line0)
                {
                    beamDir = line0.Direction.Normalize();
                }

                XYZ normal = new XYZ(-beamDir.Y, beamDir.X, 0).Normalize();
                double offsetFeet = config.OffsetDistance / 304.8;
                XYZ offsetVec = normal * offsetFeet;

                XYZ lineStart = avgPt + offsetVec;
                XYZ lineEnd = lineStart + beamDir * 10.0;
                Line dimLine = Line.CreateBound(lineStart, lineEnd);

                RunActionInTransaction(doc, "自動標註-梁邊到梁邊", () =>
                {
                    Dimension dim = doc.Create.NewDimension(view, dimLine, refArray);
                    if (dim != null)
                    {
                        dim.DimensionType = dimType;
                        ApplyDimensionTexts(dim, config);
                    }
                });
            }
        }
    }
}
