using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitPerspectiveMinimap.Core
{
    public static class SheetComposer
    {
        public static ViewSheet ComposePerspectiveSheet(UIApplication uiApp, ElementId perspectiveViewId)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null) return null;
            Document doc = uiDoc.Document;

            View3D perspectiveView = doc.GetElement(perspectiveViewId) as View3D;
            if (perspectiveView == null || !perspectiveView.IsPerspective)
            {
                TaskDialog.Show("提示", "請先選擇一張透視 3D 視圖再執行出圖排版。");
                return null;
            }

            // 尋找 TitleBlock 圖框
            FamilySymbol titleBlockSymbol = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            ElementId titleBlockId = (titleBlockSymbol != null) ? titleBlockSymbol.Id : ElementId.InvalidElementId;

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

            ViewSheet newSheet = null;

            try
            {
                using (Transaction tx = new Transaction(doc, "一鍵透視空間索引出圖"))
                {
                    tx.Start();

                    try
                    {
                        newSheet = ViewSheet.Create(doc, titleBlockId);
                    }
                    catch
                    {
                        newSheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                    }

                    if (newSheet != null)
                    {
                        newSheet.Name = string.Format("透視索引 - {0}", perspectiveView.Name);
                        newSheet.SheetNumber = GetNextSheetNumber(doc, "P-");

                        double sheetWidth = 2.8;
                        double sheetHeight = 1.95;

                        XYZ ptTopLeft = new XYZ(sheetWidth * 0.28, sheetHeight * 0.72, 0);
                        XYZ ptTopRight = new XYZ(sheetWidth * 0.72, sheetHeight * 0.72, 0);
                        XYZ ptBottomLeft = new XYZ(sheetWidth * 0.28, sheetHeight * 0.28, 0);
                        XYZ ptBottomRight = new XYZ(sheetWidth * 0.72, sheetHeight * 0.28, 0);

                        PlaceOrDuplicateViewport(doc, newSheet, planView, ptTopLeft);
                        PlaceOrDuplicateViewport(doc, newSheet, sectionView, ptTopRight);
                        if (iso3DView != null) PlaceOrDuplicateViewport(doc, newSheet, iso3DView, ptBottomLeft);
                        PlaceOrDuplicateViewport(doc, newSheet, perspectiveView, ptBottomRight);
                    }

                    tx.Commit();
                }

                if (newSheet != null)
                {
                    uiDoc.ActiveView = newSheet;
                    TaskDialog.Show("一鍵出圖成功", string.Format("✅ 已成功建立圖紙：【{0} - {1}】！\n已自動完成 4 宮格排版 (平面、剖面、等角、透視)。", newSheet.SheetNumber, newSheet.Name));
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("出圖排版提示", "建立圖紙時發生錯誤: " + ex.Message);
            }

            return newSheet;
        }

        private static void PlaceOrDuplicateViewport(Document doc, ViewSheet sheet, View view, XYZ center)
        {
            if (view == null || sheet == null) return;
            try
            {
                if (Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                {
                    Viewport.Create(doc, sheet.Id, view.Id, center);
                }
                else
                {
                    ViewDuplicateOption dupOpt = (view is View3D) ? ViewDuplicateOption.Duplicate : ViewDuplicateOption.WithDetailing;
                    ElementId dupId = view.Duplicate(dupOpt);
                    if (dupId != null && Viewport.CanAddViewToSheet(doc, sheet.Id, dupId))
                    {
                        Viewport.Create(doc, sheet.Id, dupId, center);
                    }
                }
            }
            catch { }
        }

        private static string GetNextSheetNumber(Document doc, string prefix)
        {
            int index = 101;
            while (true)
            {
                string num = string.Format("{0}{1}", prefix, index);
                bool exists = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Any(s => s.SheetNumber.Equals(num, StringComparison.OrdinalIgnoreCase));
                if (!exists) return num;
                index++;
            }
        }
    }
}