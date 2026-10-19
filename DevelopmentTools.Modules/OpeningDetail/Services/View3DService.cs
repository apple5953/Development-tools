using System;
using System.Linq;
using Autodesk.Revit.DB;
using OpeningDetailAddin.Models;

namespace OpeningDetailAddin.Services
{
    /// <summary>
    /// 開口 3D 視圖生成服務
    /// 運用「選取方塊 (Section Box)」功能，自動聚焦開口元件與周邊牆體，生成高精緻度 3D 視圖
    /// </summary>
    public static class View3DService
    {
        public static ViewFamilyType Get3DViewFamilyType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(delegate(ViewFamilyType vft) { return vft.ViewFamily == ViewFamily.ThreeDimensional; });
        }

        public static View3D CreateOpening3DView(
            Document doc, 
            Wall wall, 
            FamilyInstance opening, 
            int scale, 
            double marginMm)
        {
            return CreateOpening3DView(doc, wall, opening, scale, marginMm, null, null);
        }

        public static View3D CreateOpening3DView(
            Document doc, 
            Wall wall, 
            FamilyInstance opening, 
            int scale, 
            double marginMm,
            ExistingItemCache cache)
        {
            return CreateOpening3DView(doc, wall, opening, scale, marginMm, null, cache);
        }

        public static View3D CreateOpening3DView(
            Document doc, 
            Wall wall, 
            FamilyInstance opening, 
            int scale, 
            double marginMm,
            ViewTemplateModel templateModel,
            ExistingItemCache cache)
        {
            // 0. 先檢查是否已有現成之 3D 開口選取方塊視圖 (避免重複建立帶有 _1 的視圖)
            View3D existingView = ExistingItemDetectorService.FindExisting3DView(doc, opening != null ? opening.Id : null, cache);
            if (existingView != null)
            {
                existingView.Scale = scale;
                ViewTemplateService.Apply3DViewTemplate(doc, existingView, templateModel, scale);
                return existingView;
            }

            ViewFamilyType vft = Get3DViewFamilyType(doc);
            if (vft == null)
            {
                throw new InvalidOperationException("專案中未找到 3D 視圖類型 (3D ViewFamilyType)。");
            }

            // 1. 建立正交 3D 視圖 (Isometric 3D View)
            View3D view3D = View3D.CreateIsometric(doc, vft.Id);

            // 2. 計算開口元件之選取方塊範圍 (Section Box)
            BoundingBoxXYZ opBBox = opening.get_BoundingBox(null);
            if (opBBox == null)
            {
                opBBox = wall.get_BoundingBox(null);
            }

            if (opBBox != null)
            {
                double marginFt = GeometryService.MmToFeet(marginMm > 100 ? marginMm : 800);
                
                BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                sectionBox.Min = new XYZ(opBBox.Min.X - marginFt, opBBox.Min.Y - marginFt, opBBox.Min.Z - marginFt);
                sectionBox.Max = new XYZ(opBBox.Max.X + marginFt, opBBox.Max.Y + marginFt, opBBox.Max.Z + marginFt);

                // 套用選取方塊 (Section Box)
                view3D.SetSectionBox(sectionBox);
                view3D.IsSectionBoxActive = true;

                // 3. 設定 3D 最佳觀看視角 (朝向牆面與開口之 3D 俯視視角)
                XYZ center = (opBBox.Min + opBBox.Max) * 0.5;
                XYZ wallNormal = wall.Orientation.Normalize();
                
                // 計算視線方向 forward (朝向牆面開口並帶有適當俯視角)
                XYZ forward = (-wallNormal + new XYZ(0.35, 0.35, -0.5)).Normalize();
                // 透過外積嚴格計算垂直於 forward 之 up 方向，保證 100% 符合 Revit API 要求之正交性
                XYZ right = forward.CrossProduct(XYZ.BasisZ).Normalize();
                XYZ up = right.CrossProduct(forward).Normalize();
                XYZ eye = center - forward * GeometryService.MmToFeet(3000);

                try
                {
                    view3D.SetOrientation(new ViewOrientation3D(eye, up, forward));
                }
                catch
                {
                }
            }

            // 4. 設定視圖參數
            view3D.Scale = scale;
            view3D.DetailLevel = ViewDetailLevel.Fine;
            view3D.DisplayStyle = DisplayStyle.ShadingWithEdges; // 著色邊框，展示立體感

            // 5. 設定唯一視圖名稱
            string mark = "OP";
            Parameter pMark = opening.get_Parameter(BuiltInParameter.DOOR_NUMBER) 
                              ?? opening.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (pMark != null && !string.IsNullOrEmpty(pMark.AsString()))
            {
                mark = pMark.AsString();
            }

            string baseName = string.Format("3D開口_{0}_{1}_{2}", wall.Name, mark, opening.Id.Value);
            view3D.Name = GetUniqueViewName(doc, baseName);

            // 6. 套用 3D 視圖樣板
            ViewTemplateService.Apply3DViewTemplate(doc, view3D, templateModel, scale);

            return view3D;
        }

        private static string GetUniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int counter = 1;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(delegate(View v) 
            { 
                return v.Name.Equals(name, StringComparison.OrdinalIgnoreCase); 
            }))
            {
                name = string.Format("{0}_{1}", baseName, counter++);
            }
            return name;
        }
    }
}
