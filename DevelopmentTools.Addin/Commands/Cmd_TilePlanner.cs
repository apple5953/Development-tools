using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DevelopmentTools.Algorithms;
using DevelopmentTools.Core;
using System;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_TilePlanner : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                if (!CommandAccessGuard.EnsureAuthorized("TilePlannerParts", "TilePlanner Parts"))
                {
                    return Result.Cancelled;
                }

                Reference picked = uidoc.Selection.PickObject(
                    ObjectType.Face,
                    new TilePlannerSelectionFilter(),
                    "請選取要分割的磁磚面。");

                Element selected = doc.GetElement(picked.ElementId);
                PlanarFace face = selected.GetGeometryObjectFromReference(picked) as PlanarFace;
                if (face == null)
                {
                    throw new InvalidOperationException("請選取平面牆面、樓板面、屋頂面、天花板面或 Part 面。");
                }

                TilePlannerEngine engine = new TilePlannerEngine();
                Element host = engine.ResolveHostOriginal(doc, selected);

                double tileWidthMm = 600.0;
                double tileHeightMm = 600.0;
                if (!TryApplyFaceMaterialDimensions(doc, selected, host, face, ref tileWidthMm, ref tileHeightMm))
                {
                    TileDimensionDetector.DetectDimensions(host ?? selected, doc, out tileWidthMm, out tileHeightMm);
                }

                TilePlannerSettings settings = new TilePlannerSettings
                {
                    TileWidthMm = tileWidthMm,
                    TileHeightMm = tileHeightMm,
                    HGroutGapMm = 3.0,
                    VGroutGapMm = 3.0
                };

                TilePlannerResult result = engine.Execute(doc, doc.ActiveView, selected, face, settings);

                TaskDialog.Show(
                    "TilePlanner",
                    string.Format(
                        "磁磚計畫完成。\n\n規格: {0:0.#}x{1:0.#} mm\n宿主 Id: {2}\nReferencePlane: {3}\nParts: {4}\n排除碎片: {5}\nPartMaker Id: {6}",
                        settings.TileWidthMm,
                        settings.TileHeightMm,
                        result.HostId.IntegerValue,
                        result.ReferencePlaneCount,
                        result.DividedPartCount,
                        result.ExcludedPartCount,
                        result.PartMakerId == ElementId.InvalidElementId ? "-" : result.PartMakerId.IntegerValue.ToString()));

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("TilePlanner 錯誤", ex.ToString());
                return Result.Failed;
            }
        }

        private static bool TryApplyFaceMaterialDimensions(Document doc, Element selected, Element host, PlanarFace face, ref double widthMm, ref double heightMm)
        {
            foreach (Element element in new[] { selected, host })
            {
                if (element == null) continue;

                Material material = null;
                try
                {
                    ElementId paintedId = doc.GetPaintedMaterial(element.Id, face);
                    if (paintedId != ElementId.InvalidElementId)
                    {
                        material = doc.GetElement(paintedId) as Material;
                    }
                }
                catch { }

                if (material == null && face.MaterialElementId != ElementId.InvalidElementId)
                {
                    material = doc.GetElement(face.MaterialElementId) as Material;
                }

                if (material != null && TileDimensionDetector.ParseDimensions(material.Name, out double w, out double h))
                {
                    widthMm = w;
                    heightMm = h;
                    return true;
                }
            }

            return false;
        }
    }

    internal class TilePlannerSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            if (elem is Part) return true;
            if (elem is Wall) return true;
            if (elem is Floor) return true;
            if (elem is RoofBase) return true;
            if (elem.Category != null && elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Ceilings) return true;
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
