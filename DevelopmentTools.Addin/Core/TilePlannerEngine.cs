using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DevelopmentTools.Core
{
    public class TilePlannerSettings
    {
        public double TileWidthMm { get; set; } = 600.0;
        public double TileHeightMm { get; set; } = 600.0;
        public double HGroutGapMm { get; set; } = 3.0;
        public double VGroutGapMm { get; set; } = 3.0;
    }

    public class TilePlannerResult
    {
        public ElementId HostId { get; set; } = ElementId.InvalidElementId;
        public int ReferencePlaneCount { get; set; }
        public int DividedPartCount { get; set; }
        public int ExcludedPartCount { get; set; }
        public ElementId GroupId { get; set; } = ElementId.InvalidElementId;
        public ElementId PartMakerId { get; set; } = ElementId.InvalidElementId;
    }

    public class ElevationFrame
    {
        public XYZ Origin { get; set; }
        public XYZ UDir { get; set; }
        public XYZ VDir { get; set; }
        public XYZ Normal { get; set; }
        public double MinU { get; set; }
        public double MaxU { get; set; }
        public double MinV { get; set; }
        public double MaxV { get; set; }
        public PlanarFace Face { get; set; }

        public XYZ PointAt(double u, double v)
        {
            return Origin + UDir * u + VDir * v;
        }

        public UV Project(XYZ point)
        {
            XYZ rel = point - Origin;
            return new UV(rel.DotProduct(UDir), rel.DotProduct(VDir));
        }
    }

    public class TilePlannerEngine
    {
        private const string GridPrefix = "TileGrid_";
        private const double FootToMm = 304.8;
        private static readonly Guid StorageSchemaGuid = new Guid("7A76C663-F534-4D3C-B273-79E4DC5A7E31");

        public TilePlannerResult Execute(Document doc, View view, Element selected, TilePlannerSettings settings)
        {
            return Execute(doc, view, selected, null, settings);
        }

        public TilePlannerResult Execute(Document doc, View view, Element selected, PlanarFace targetFace, TilePlannerSettings settings)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (selected == null) throw new ArgumentNullException(nameof(selected));
            if (settings == null) settings = new TilePlannerSettings();

            Element host = ResolveHostOriginal(doc, selected);
            if (host == null) throw new InvalidOperationException("無法解析原始宿主。");

            TilePlannerResult result = new TilePlannerResult { HostId = host.Id };

            using (Transaction tx = new Transaction(doc, "TilePlanner - Create Reference Grid and Divide Parts"))
            {
                tx.Start();

                CleanOldGrids(doc, host.Id);
                EnsureHostParts(doc, host);

                ElevationFrame frame = BuildElevationFrame(host, targetFace);
                List<ElementId> planeIds = CreateGridWeb(doc, view, host.Id, frame, settings);
                result.ReferencePlaneCount = planeIds.Count;

                ElementId groupId = GroupPlanes(doc, host.Id, planeIds);
                result.GroupId = groupId;

                PartMaker maker = PerformDivision(doc, host, frame, planeIds);
                result.PartMakerId = maker == null ? ElementId.InvalidElementId : maker.Id;

                doc.Regenerate();

                result.DividedPartCount = CountLeafParts(doc, host);
                result.ExcludedPartCount = ExcludeOpeningsAndGapStrips(doc, host, frame, settings);

                SaveConfiguration(doc, host, settings, groupId);

                tx.Commit();
            }

            return result;
        }

        public Element ResolveHostOriginal(Document doc, Element element)
        {
            Element current = element;
            HashSet<ElementId> visited = new HashSet<ElementId>();

            while (current is Part part && part != null)
            {
                if (!visited.Add(current.Id)) break;

                ICollection<LinkElementId> sources = part.GetSourceElementIds();
                LinkElementId source = sources == null ? null : sources.FirstOrDefault();
                if (source == null || source.HostElementId == ElementId.InvalidElementId) break;

                Element next = doc.GetElement(source.HostElementId);
                if (next == null) break;
                current = next;
            }

            return current;
        }

        public void CleanOldGrids(Document doc, ElementId hostId)
        {
            string hostKey = hostId.IntegerValue.ToString();
            List<ElementId> toDelete = new FilteredElementCollector(doc)
                .OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>()
                .Where(rp => !string.IsNullOrEmpty(rp.Name)
                    && rp.Name.Contains(GridPrefix)
                    && rp.Name.Contains("_H" + hostKey + "_"))
                .Select(rp => rp.Id)
                .ToList();

            if (toDelete.Count > 0)
            {
                doc.Delete(toDelete);
            }
        }

        public void EnsureHostParts(Document doc, Element host)
        {
            if (PartUtils.HasAssociatedParts(doc, host.Id)) return;

            ICollection<ElementId> hostIds = new List<ElementId> { host.Id };
            if (!PartUtils.AreElementsValidForCreateParts(doc, hostIds))
            {
                throw new InvalidOperationException("所選宿主無法建立 Revit Parts。");
            }

            PartUtils.CreateParts(doc, hostIds);
            doc.Regenerate();
        }

        public ElevationFrame BuildElevationFrame(Element host, PlanarFace targetFace = null)
        {
            PlanarFace face = targetFace ?? FindLargestPlanarFace(host);
            if (face == null) throw new InvalidOperationException("找不到可投影的最大平面。");

            XYZ normal = face.FaceNormal.Normalize();
            XYZ uDir = face.XVector.Normalize();
            XYZ vDir = face.YVector.Normalize();

            if (host is Wall)
            {
                XYZ horizontal = XYZ.BasisZ.CrossProduct(normal);
                if (horizontal.GetLength() > 0.001)
                {
                    uDir = horizontal.Normalize();
                    vDir = normal.CrossProduct(uDir).Normalize();
                    if (vDir.DotProduct(XYZ.BasisZ) < 0) vDir = vDir.Negate();
                }
            }

            ElevationFrame frame = new ElevationFrame
            {
                Origin = face.Origin,
                UDir = uDir,
                VDir = vDir,
                Normal = normal,
                Face = face,
                MinU = double.MaxValue,
                MaxU = double.MinValue,
                MinV = double.MaxValue,
                MaxV = double.MinValue
            };

            foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
            {
                foreach (Curve curve in loop)
                {
                    foreach (XYZ point in curve.Tessellate())
                    {
                        UV uv = frame.Project(point);
                        frame.MinU = Math.Min(frame.MinU, uv.U);
                        frame.MaxU = Math.Max(frame.MaxU, uv.U);
                        frame.MinV = Math.Min(frame.MinV, uv.V);
                        frame.MaxV = Math.Max(frame.MaxV, uv.V);
                    }
                }
            }

            return frame;
        }

        public List<ElementId> CreateGridWeb(Document doc, View view, ElementId hostId, ElevationFrame frame, TilePlannerSettings settings)
        {
            List<ElementId> ids = new List<ElementId>();
            double pitchU = (settings.TileWidthMm + settings.VGroutGapMm) / FootToMm;
            double pitchV = (settings.TileHeightMm + settings.HGroutGapMm) / FootToMm;
            double pad = Math.Max(pitchU, pitchV) * 2.0;

            int index = 0;
            for (double u = frame.MinU + pitchU; u < frame.MaxU - 0.001; u += pitchU)
            {
                ReferencePlane rp = doc.Create.NewReferencePlane(
                    frame.PointAt(u, frame.MinV - pad),
                    frame.PointAt(u, frame.MaxV + pad),
                    frame.Normal,
                    view);
                rp.Name = string.Format("TileGrid_V_H{0}_{1:0000}", hostId.IntegerValue, index++);
                ids.Add(rp.Id);
            }

            index = 0;
            for (double v = frame.MinV + pitchV; v < frame.MaxV - 0.001; v += pitchV)
            {
                ReferencePlane rp = doc.Create.NewReferencePlane(
                    frame.PointAt(frame.MinU - pad, v),
                    frame.PointAt(frame.MaxU + pad, v),
                    frame.Normal,
                    view);
                rp.Name = string.Format("TileGrid_H_H{0}_{1:0000}", hostId.IntegerValue, index++);
                ids.Add(rp.Id);
            }

            return ids;
        }

        public ElementId GroupPlanes(Document doc, ElementId hostId, IList<ElementId> planeIds)
        {
            if (planeIds == null || planeIds.Count == 0) return ElementId.InvalidElementId;

            Group group = doc.Create.NewGroup(planeIds);
            try
            {
                group.GroupType.Name = "TileGrid_Group_H" + hostId.IntegerValue.ToString();
            }
            catch { }
            return group.Id;
        }

        public PartMaker PerformDivision(Document doc, Element host, ElevationFrame frame, IList<ElementId> planeIds)
        {
            ICollection<ElementId> parts = PartUtils.GetAssociatedParts(doc, host.Id, true, true);
            if (parts == null || parts.Count == 0)
            {
                throw new InvalidOperationException("找不到宿主關聯 Parts。");
            }

            if (!PartUtils.ArePartsValidForDivide(doc, parts))
            {
                throw new InvalidOperationException("目前 Parts 無法執行 Divide Parts。");
            }

            SketchPlane sketchPlane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(frame.Normal, frame.Origin));
            List<Curve> noCurves = new List<Curve>();
            ICollection<ElementId> intersections = planeIds == null ? new List<ElementId>() : planeIds.ToList();
            return PartUtils.DivideParts(doc, parts, intersections, noCurves, sketchPlane.Id);
        }

        public int ExcludeOpeningsAndGapStrips(Document doc, Element host, ElevationFrame frame, TilePlannerSettings settings)
        {
            List<BoundingBoxXYZ> openings = OpeningService.GetHostOpeningBoxes(doc, host);
            ICollection<ElementId> partIds = PartUtils.GetAssociatedParts(doc, host.Id, true, true);
            int excluded = 0;

            double pitchU = (settings.TileWidthMm + settings.VGroutGapMm) / FootToMm;
            double pitchV = (settings.TileHeightMm + settings.HGroutGapMm) / FootToMm;
            double halfVGap = Math.Max(settings.VGroutGapMm / FootToMm, 0.001) * 0.5;
            double halfHGap = Math.Max(settings.HGroutGapMm / FootToMm, 0.001) * 0.5;

            foreach (ElementId partId in partIds)
            {
                Part part = doc.GetElement(partId) as Part;
                if (part == null) continue;

                XYZ center = GetElementCenter(part);
                if (center == null) continue;

                bool shouldExclude = openings.Any(box => Contains(box, center)) || IsInGapStrip(frame, center, pitchU, pitchV, halfVGap, halfHGap);
                if (!shouldExclude) continue;

                Parameter p = GetExcludedByMakerParameter(part);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(1);
                    excluded++;
                }
            }

            return excluded;
        }

        private Parameter GetExcludedByMakerParameter(Part part)
        {
            const int dpartExcludedByMaker = -1152344;

            Parameter parameter = part.get_Parameter((BuiltInParameter)dpartExcludedByMaker);
            if (parameter != null)
            {
                return parameter;
            }

            foreach (Parameter candidate in part.Parameters)
            {
                if (candidate != null && candidate.Id.IntegerValue == dpartExcludedByMaker)
                {
                    return candidate;
                }
            }

            return null;
        }

        public int CountLeafParts(Document doc, Element host)
        {
            ICollection<ElementId> ids = PartUtils.GetAssociatedParts(doc, host.Id, true, true);
            return ids == null ? 0 : ids.Count;
        }

        private bool IsInGapStrip(ElevationFrame frame, XYZ point, double pitchU, double pitchV, double halfVGap, double halfHGap)
        {
            UV uv = frame.Project(point);
            double uFromAnchor = uv.U - frame.MinU;
            double vFromAnchor = uv.V - frame.MinV;
            return DistanceToNearestGrid(uFromAnchor, pitchU) <= halfVGap
                || DistanceToNearestGrid(vFromAnchor, pitchV) <= halfHGap;
        }

        private double DistanceToNearestGrid(double value, double pitch)
        {
            if (pitch <= 0.0001) return double.MaxValue;
            double mod = value - Math.Floor(value / pitch) * pitch;
            return Math.Min(mod, pitch - mod);
        }

        private XYZ GetElementCenter(Element element)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null) return null;
            return (box.Min + box.Max) * 0.5;
        }

        private bool Contains(BoundingBoxXYZ box, XYZ point)
        {
            if (box == null || point == null) return false;
            return point.X >= box.Min.X && point.X <= box.Max.X
                && point.Y >= box.Min.Y && point.Y <= box.Max.Y
                && point.Z >= box.Min.Z && point.Z <= box.Max.Z;
        }

        private PlanarFace FindLargestPlanarFace(Element element)
        {
            Options options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geometry = element.get_Geometry(options);
            if (geometry == null) return null;

            PlanarFace best = null;
            double bestArea = 0.0;
            foreach (PlanarFace face in EnumerateFaces(geometry))
            {
                if (face.Area > bestArea)
                {
                    bestArea = face.Area;
                    best = face;
                }
            }

            return best;
        }

        private IEnumerable<PlanarFace> EnumerateFaces(GeometryElement geometry)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace planar) yield return planar;
                    }
                }
                else if (obj is GeometryInstance instance)
                {
                    GeometryElement instGeometry = instance.GetInstanceGeometry();
                    foreach (PlanarFace face in EnumerateFaces(instGeometry)) yield return face;
                }
            }
        }

        private void SaveConfiguration(Document doc, Element host, TilePlannerSettings settings, ElementId groupId)
        {
            Schema schema = Schema.Lookup(StorageSchemaGuid);
            if (schema == null)
            {
                SchemaBuilder builder = new SchemaBuilder(StorageSchemaGuid);
                builder.SetSchemaName("TilePlannerConfiguration");
                builder.SetReadAccessLevel(AccessLevel.Public);
                builder.SetWriteAccessLevel(AccessLevel.Public);
                builder.AddSimpleField("TileWidthMm", typeof(double));
                builder.AddSimpleField("TileHeightMm", typeof(double));
                builder.AddSimpleField("HGroutGapMm", typeof(double));
                builder.AddSimpleField("VGroutGapMm", typeof(double));
                builder.AddSimpleField("GridGroupId", typeof(int));
                schema = builder.Finish();
            }

            Entity entity = new Entity(schema);
            entity.Set("TileWidthMm", settings.TileWidthMm);
            entity.Set("TileHeightMm", settings.TileHeightMm);
            entity.Set("HGroutGapMm", settings.HGroutGapMm);
            entity.Set("VGroutGapMm", settings.VGroutGapMm);
            entity.Set("GridGroupId", groupId == ElementId.InvalidElementId ? -1 : groupId.IntegerValue);
            host.SetEntity(entity);
        }
    }

    public static class OpeningService
    {
        public static List<BoundingBoxXYZ> GetHostOpeningBoxes(Document doc, Element host)
        {
            List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();
            if (doc == null || host == null) return boxes;

            BuiltInCategory[] categories =
            {
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows
            };

            foreach (BuiltInCategory category in categories)
            {
                foreach (FamilyInstance fi in new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                {
                    if (fi.Host == null || fi.Host.Id != host.Id) continue;
                    BoundingBoxXYZ box = fi.get_BoundingBox(null);
                    if (box != null) boxes.Add(box);
                }
            }

            return boxes;
        }
    }
}
