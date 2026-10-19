using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitPerspectiveMinimap.Core
{
    public static class MultiViewManager
    {
        public static void OpenAndTileFourViews(UIApplication uiApp, ElementId perspectiveViewId)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null) return;
            Document doc = uiDoc.Document;

            View3D perspectiveView = doc.GetElement(perspectiveViewId) as View3D;
            if (perspectiveView == null || !perspectiveView.IsPerspective) return;

            DetectedLevelResult detected = SmartLevelDetector.DetectLevel(doc, perspectiveView.GetOrientation().EyePosition, null);
            ViewPlan planView = null;
            if (detected.Level != null)
            {
                planView = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == detected.Level.Id && v.ViewType == ViewType.FloorPlan);
            }

            if (planView == null)
            {
                planView = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);
            }

            ViewSection sectionView = SectionIndexManager.GetOrCreateSectionView(doc, perspectiveView);

            View3D iso3DView = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && !v.IsPerspective);

            if (iso3DView == null)
            {
                ViewFamilyType viewFamilyType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                if (viewFamilyType != null)
                {
                    using (Transaction tx = new Transaction(doc, "建立等角 3D 視圖"))
                    {
                        tx.Start();
                        iso3DView = View3D.CreateIsometric(doc, viewFamilyType.Id);
                        iso3DView.Name = "[索引等角] 3D 全景";
                        tx.Commit();
                    }
                }
            }

            List<View> viewsToOpen = new List<View>();
            viewsToOpen.Add(planView);
            viewsToOpen.Add(sectionView);
            viewsToOpen.Add(iso3DView);
            viewsToOpen.Add(perspectiveView);

            foreach (View v in viewsToOpen)
            {
                if (v != null)
                {
                    try
                    {
                        uiDoc.ActiveView = v;
                    }
                    catch { }
                }
            }

            try
            {
                RevitCommandId tileCmdId = RevitCommandId.LookupPostableCommandId(PostableCommand.TileViews);
                if (tileCmdId != null && uiApp.CanPostCommand(tileCmdId))
                {
                    uiApp.PostCommand(tileCmdId);
                }
            }
            catch { }
        }
    }
}