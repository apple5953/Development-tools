using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitPerspectiveMinimap.Core
{
    public enum SectionDirectionMode
    {
        AlongSight = 0,
        AcrossSight = 1
    }

    public static class SectionIndexManager
    {
        private static readonly Dictionary<ElementId, SectionDirectionMode> _viewSectionModes = new Dictionary<ElementId, SectionDirectionMode>();

        public static SectionDirectionMode GetDirectionMode(ElementId viewId)
        {
            SectionDirectionMode mode;
            if (viewId != null && _viewSectionModes.TryGetValue(viewId, out mode))
            {
                return mode;
            }
            return SectionDirectionMode.AlongSight;
        }

        public static void ToggleDirectionMode(ElementId viewId)
        {
            if (viewId == null) return;
            SectionDirectionMode current = GetDirectionMode(viewId);
            _viewSectionModes[viewId] = (current == SectionDirectionMode.AlongSight)
                ? SectionDirectionMode.AcrossSight
                : SectionDirectionMode.AlongSight;
        }

        public static ViewSection GetOrCreateSectionView(Document doc, View3D perspectiveView)
        {
            if (doc == null || perspectiveView == null) return null;

            string expectedName = string.Format("[索引剖面] {0}", perspectiveView.Name);

            ViewSection existingSection = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSection))
                .Cast<ViewSection>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));

            SectionDirectionMode mode = GetDirectionMode(perspectiveView.Id);
            BoundingBoxXYZ bbox = CalculateSectionBoundingBox(perspectiveView, mode);

            if (existingSection != null)
            {
                using (Transaction tx = new Transaction(doc, "更新索引剖面位置"))
                {
                    tx.Start();
                    existingSection.CropBox = bbox;
                    existingSection.CropBoxActive = true;
                    existingSection.CropBoxVisible = true;
                    tx.Commit();
                }
                return existingSection;
            }

            ViewFamilyType sectionType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Section);

            if (sectionType == null) return null;

            ViewSection newSection = null;
            using (Transaction tx = new Transaction(doc, "建立索引剖面圖"))
            {
                tx.Start();
                newSection = ViewSection.CreateSection(doc, sectionType.Id, bbox);
                newSection.Name = expectedName;
                newSection.CropBoxActive = true;
                newSection.CropBoxVisible = true;
                tx.Commit();
            }

            return newSection;
        }

        public static BoundingBoxXYZ CalculateSectionBoundingBox(View3D perspectiveView, SectionDirectionMode mode)
        {
            ViewOrientation3D orient = perspectiveView.GetOrientation();
            XYZ eye = orient.EyePosition;
            XYZ forward = orient.ForwardDirection;
            XYZ forward2D = new XYZ(forward.X, forward.Y, 0).Normalize();
            if (forward2D.IsAlmostEqualTo(XYZ.Zero)) forward2D = XYZ.BasisX;

            XYZ right2D = forward2D.CrossProduct(XYZ.BasisZ).Normalize();

            BoundingBoxXYZ bbox = new BoundingBoxXYZ();
            Transform transform = Transform.Identity;
            transform.Origin = eye;

            if (mode == SectionDirectionMode.AlongSight)
            {
                transform.BasisX = forward2D;
                transform.BasisY = XYZ.BasisZ;
                transform.BasisZ = right2D;

                bbox.Min = new XYZ(-10.0, -10.0, -15.0);
                bbox.Max = new XYZ(40.0, 20.0, 15.0);
            }
            else
            {
                transform.BasisX = right2D;
                transform.BasisY = XYZ.BasisZ;
                transform.BasisZ = forward2D;

                bbox.Min = new XYZ(-25.0, -10.0, -5.0);
                bbox.Max = new XYZ(25.0, 20.0, 45.0);
            }

            bbox.Transform = transform;
            return bbox;
        }
    }
}