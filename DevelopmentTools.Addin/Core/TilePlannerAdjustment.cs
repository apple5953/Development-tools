using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DevelopmentTools.Core
{
    public class TilePlannerAdjustmentRequest
    {
        public ElementId HostId { get; set; } = ElementId.InvalidElementId;
        public TilePlannerSettings Settings { get; set; } = new TilePlannerSettings();
    }

    public class GroutUpdateHandler : IExternalEventHandler
    {
        private TilePlannerAdjustmentRequest _pending;

        public void Request(TilePlannerAdjustmentRequest request)
        {
            _pending = request;
        }

        public void Execute(UIApplication app)
        {
            TilePlannerAdjustmentRequest request = _pending;
            _pending = null;
            if (request == null || request.HostId == ElementId.InvalidElementId) return;

            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;
            Element host = doc.GetElement(request.HostId);
            if (host == null) return;

            using (Transaction tx = new Transaction(doc, "TilePlanner - Adjust grout"))
            {
                tx.Start();
                TilePlannerEngine engine = new TilePlannerEngine();
                ElevationFrame frame = engine.BuildElevationFrame(host);
                GridAdjustmentService.RedistributePlanes(doc, host.Id, frame, request.Settings);
                tx.Commit();
            }
        }

        public string GetName()
        {
            return "TilePlanner Grout Update Handler";
        }
    }

    public static class GridAdjustmentService
    {
        private const double FootToMm = 304.8;

        public static void RedistributePlanes(Document doc, ElementId hostId, ElevationFrame frame, TilePlannerSettings settings)
        {
            List<ReferencePlane> planes = GetHostGridPlanes(doc, hostId);
            if (planes.Count == 0) return;

            UngroupTileGrid(doc, hostId);

            double pitchU = (settings.TileWidthMm + settings.VGroutGapMm) / FootToMm;
            double pitchV = (settings.TileHeightMm + settings.HGroutGapMm) / FootToMm;

            List<ReferencePlane> vertical = planes
                .Where(p => p.Name.Contains("TileGrid_V_"))
                .OrderBy(p => p.Name)
                .ToList();
            List<ReferencePlane> horizontal = planes
                .Where(p => p.Name.Contains("TileGrid_H_"))
                .OrderBy(p => p.Name)
                .ToList();

            for (int i = 0; i < vertical.Count; i++)
            {
                double targetU = frame.MinU + pitchU * (i + 1);
                MovePlaneToOffset(doc, vertical[i], frame, targetU, true);
            }

            for (int i = 0; i < horizontal.Count; i++)
            {
                double targetV = frame.MinV + pitchV * (i + 1);
                MovePlaneToOffset(doc, horizontal[i], frame, targetV, false);
            }

            List<ElementId> ids = planes.Select(p => p.Id).ToList();
            if (ids.Count > 0)
            {
                Group group = doc.Create.NewGroup(ids);
                try { group.GroupType.Name = "TileGrid_Group_H" + hostId.IntegerValue.ToString(); } catch { }
            }
        }

        private static List<ReferencePlane> GetHostGridPlanes(Document doc, ElementId hostId)
        {
            string key = "_H" + hostId.IntegerValue.ToString() + "_";
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>()
                .Where(p => !string.IsNullOrEmpty(p.Name) && p.Name.Contains("TileGrid_") && p.Name.Contains(key))
                .ToList();
        }

        private static void UngroupTileGrid(Document doc, ElementId hostId)
        {
            string groupName = "TileGrid_Group_H" + hostId.IntegerValue.ToString();
            foreach (Group group in new FilteredElementCollector(doc).OfClass(typeof(Group)).Cast<Group>())
            {
                try
                {
                    if (group.GroupType != null && group.GroupType.Name == groupName)
                    {
                        group.UngroupMembers();
                    }
                }
                catch { }
            }
        }

        private static void MovePlaneToOffset(Document doc, ReferencePlane plane, ElevationFrame frame, double target, bool vertical)
        {
            XYZ currentPoint = plane.BubbleEnd;
            UV uv = frame.Project(currentPoint);
            double delta = target - (vertical ? uv.U : uv.V);
            XYZ move = (vertical ? frame.UDir : frame.VDir) * delta;
            if (move.GetLength() > 0.000001)
            {
                ElementTransformUtils.MoveElement(doc, plane.Id, move);
            }
        }
    }
}
