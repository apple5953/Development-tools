using System;
using System.Linq;
using Autodesk.Revit.DB;
using OpeningDetailAddin.Models;

namespace OpeningDetailAddin.Services
{
    /// <summary>
    /// 牆面開口剖面圖生成服務
    /// </summary>
    public static class SectionService
    {
        public static ViewFamilyType GetSectionViewFamilyType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Section);
        }

        public static ViewSection CreateWallOpeningSection(
            Document doc, 
            Wall wall, 
            FamilyInstance opening, 
            int scale, 
            double offsetMm,
            ViewTemplateModel templateModel)
        {
            return CreateWallOpeningSection(doc, wall, opening, scale, offsetMm, templateModel, null);
        }

        public static ViewSection CreateWallOpeningSection(
            Document doc, 
            Wall wall, 
            FamilyInstance opening, 
            int scale, 
            double offsetMm,
            ViewTemplateModel templateModel,
            ExistingItemCache cache)
        {
            // 0. 先檢查是否已有現成之剖面視圖 (避免重複建立帶有 _1 的視圖)
            ViewSection existingSection = ExistingItemDetectorService.FindExistingSectionView(doc, opening != null ? opening.Id : null, cache);
            if (existingSection != null)
            {
                existingSection.Scale = scale;
                ViewTemplateService.ApplySectionTemplate(doc, existingSection, templateModel, scale);
                return existingSection;
            }

            ViewFamilyType sectionType = GetSectionViewFamilyType(doc);
            if (sectionType == null)
            {
                throw new InvalidOperationException("專案中未找到剖面視圖類型 (Section ViewFamilyType)。");
            }

            // 1. 計算以開口為中心之自適應剖面裁切框 (動態適應比例，確保 100% 納入 A3 左側工作區，絕不溢出)
            BoundingBoxXYZ sectionBox = GeometryService.CalculateOpeningWallSectionBoundingBox(wall, opening, scale, offsetMm);

            // 2. 建立剖面視圖
            ViewSection viewSection = ViewSection.CreateSection(doc, sectionType.Id, sectionBox);

            // 3. 設定基本視圖參數
            viewSection.Scale = scale;
            viewSection.CropBoxActive = true;
            viewSection.CropBoxVisible = false;
            try
            {
                Parameter pAnnotCrop = viewSection.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                if (pAnnotCrop != null && !pAnnotCrop.IsReadOnly)
                {
                    pAnnotCrop.Set(1);
                }
            }
            catch { }

            // 4. 設定唯一視圖名稱
            string mark = "OP";
            Parameter pMark = opening.get_Parameter(BuiltInParameter.DOOR_NUMBER) 
                              ?? opening.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (pMark != null && !string.IsNullOrEmpty(pMark.AsString()))
            {
                mark = pMark.AsString();
            }

            string baseName = string.Format("牆開口剖面_{0}_{1}_{2}", wall.Name, mark, opening.Id.Value);
            viewSection.Name = GetUniqueViewName(doc, baseName);

            // 5. 套用剖面視圖樣板 (若選取自動設定則依照開口出圖標準自動配置樣板)
            ViewTemplateService.ApplySectionTemplate(doc, viewSection, templateModel, scale);

            // 6. 自動進行開口尺寸、台度與牆體標註
            AutoDimensionService.CreateOpeningDimensions(doc, viewSection, wall, opening);

            return viewSection;
        }

        private static string GetUniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int counter = 1;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                name = string.Format("{0}_{1}", baseName, counter++);
            }
            return name;
        }
    }
}
