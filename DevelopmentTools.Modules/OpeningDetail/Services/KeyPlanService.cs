using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using OpeningDetailAddin.Models;

namespace OpeningDetailAddin.Services
{
    /// <summary>
    /// 樓層平面索引圖 (Key Plan) 服務
    /// 負責在樓層全區平面圖上高亮標示該面牆體與開口位置，並支援精確視圖範圍裁剪控制
    /// </summary>
    public static class KeyPlanService
    {
        public static ViewPlan FindFloorPlanForLevel(Document doc, Level level)
        {
            if (level == null) return null;

            var plans = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(delegate(ViewPlan vp) 
                { 
                    return !vp.IsTemplate 
                           && vp.ViewType == ViewType.FloorPlan 
                           && vp.GenLevel != null 
                           && vp.GenLevel.Id == level.Id; 
                })
                .ToList();

            if (plans.Count == 0) return null;

            ViewPlan bestPlan = plans.FirstOrDefault(delegate(ViewPlan p) 
            { 
                return p.Name.IndexOf("建築", StringComparison.OrdinalIgnoreCase) >= 0 
                       || p.Name.IndexOf("平面", StringComparison.OrdinalIgnoreCase) >= 0; 
            });

            if (bestPlan == null)
            {
                bestPlan = plans.FirstOrDefault(delegate(ViewPlan p) 
                { 
                    return p.Discipline == ViewDiscipline.Architectural; 
                });
            }

            return bestPlan ?? plans.FirstOrDefault();
        }

        public static ViewPlan CreateKeyPlan(
            Document doc, 
            Level level, 
            Wall wall, 
            FamilyInstance opening, 
            int scale,
            ViewTemplateModel templateModel,
            string cropMode)
        {
            return CreateKeyPlan(doc, level, wall, opening, scale, templateModel, cropMode, null);
        }

        public static ViewPlan CreateKeyPlan(
            Document doc, 
            Level level, 
            Wall wall, 
            FamilyInstance opening, 
            int scale,
            ViewTemplateModel templateModel,
            string cropMode,
            ExistingItemCache cache)
        {
            // 0. 先檢查是否已有現成之平面索引圖 (避免重複 Duplicate 與產生 _1 視圖)
            ViewPlan existingPlan = ExistingItemDetectorService.FindExistingKeyPlan(doc, opening != null ? opening.Id : null, cache);
            if (existingPlan != null)
            {
                existingPlan.Scale = scale;
                ViewTemplateService.ApplyKeyPlanTemplate(doc, existingPlan, templateModel, scale);
                return existingPlan;
            }

            ViewPlan basePlan = FindFloorPlanForLevel(doc, level);
            if (basePlan == null)
            {
                basePlan = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(delegate(ViewPlan vp) { return !vp.IsTemplate && vp.ViewType == ViewType.FloorPlan; });
            }

            if (basePlan == null)
            {
                throw new InvalidOperationException("專案中未找到可用於建立索引圖的樓層平面圖。");
            }

            // 1. 複製樓層平面圖
            ElementId newViewId = basePlan.Duplicate(ViewDuplicateOption.Duplicate);
            ViewPlan keyPlan = doc.GetElement(newViewId) as ViewPlan;

            if (keyPlan == null)
            {
                throw new InvalidOperationException("複製索引平面圖失敗。");
            }

            // 2. 設定視圖比例
            keyPlan.Scale = scale;

            // 3. 【核心修正】精確設定視圖裁剪框 (CropBox)，保證能塞入 A3 圖紙
            SetKeyPlanCropBox(doc, keyPlan, level, wall, opening, cropMode);

            string levelName = level != null ? level.Name : "FL";
            string baseName = string.Format("索引圖_{0}_牆{1}_開口{2}", levelName, wall.Id.Value, opening.Id.Value);
            keyPlan.Name = GetUniqueViewName(doc, baseName);

            // 4. 套用索引圖視圖樣板
            ViewTemplateService.ApplyKeyPlanTemplate(doc, keyPlan, templateModel, scale);

            // 5. 超高對比著色標記 (鮮紅粗邊框 + 實心填充)
            HighlightWallAndOpening(doc, keyPlan, wall, opening);

            // 6. 自動放置位置引線標註
            AddKeyPlanLocationMarker(doc, keyPlan, wall, opening);

            return keyPlan;
        }

        /// <summary>
        /// 精確控制平面圖裁剪範圍，避免因全區模型過大或外部 CAD 導致塞不進 A3 圖紙
        /// </summary>
        public static void SetKeyPlanCropBox(
            Document doc, 
            ViewPlan keyPlan, 
            Level level, 
            Wall wall, 
            FamilyInstance opening, 
            string cropMode)
        {
            try
            {
                Transform viewTransform = keyPlan.CropBox.Transform;
                BoundingBoxXYZ origBox = keyPlan.CropBox;

                if (cropMode == "Building")
                {
                    // 全棟外廓裁剪：抓取該樓層所有牆體之邊界
                    var wallsOnLevel = new FilteredElementCollector(doc)
                        .OfClass(typeof(Wall))
                        .Cast<Wall>()
                        .Where(delegate(Wall w) { return w.LevelId == level.Id; })
                        .ToList();

                    if (wallsOnLevel.Count > 0)
                    {
                        double minX = double.MaxValue, minY = double.MaxValue;
                        double maxX = double.MinValue, maxY = double.MinValue;

                        foreach (var w in wallsOnLevel)
                        {
                            LocationCurve lc = w.Location as LocationCurve;
                            if (lc != null && lc.Curve != null)
                            {
                                XYZ p0 = viewTransform.Inverse.OfPoint(lc.Curve.GetEndPoint(0));
                                XYZ p1 = viewTransform.Inverse.OfPoint(lc.Curve.GetEndPoint(1));

                                minX = Math.Min(minX, Math.Min(p0.X, p1.X));
                                maxX = Math.Max(maxX, Math.Max(p0.X, p1.X));
                                minY = Math.Min(minY, Math.Min(p0.Y, p1.Y));
                                maxY = Math.Max(maxY, Math.Max(p0.Y, p1.Y));
                            }
                        }

                        double marginFt = GeometryService.MmToFeet(3000);
                        BoundingBoxXYZ bCrop = new BoundingBoxXYZ();
                        bCrop.Min = new XYZ(minX - marginFt, minY - marginFt, origBox.Min.Z);
                        bCrop.Max = new XYZ(maxX + marginFt, maxY + marginFt, origBox.Max.Z);

                        keyPlan.CropBox = bCrop;
                        keyPlan.CropBoxActive = true;
                        keyPlan.CropBoxVisible = false;
                        return;
                    }
                }
                else if (cropMode == "Original")
                {
                    // 繼承原始視圖裁切
                    keyPlan.CropBoxActive = true;
                    keyPlan.CropBoxVisible = false;
                    return;
                }

                // 預設模式【Zone 局部開口區域】：以開口為中心，裁切寬 16m × 高 12m
                // 在 1:200 比例下，圖紙尺寸僅約 80mm × 60mm，保證完美放入 A3 圖紙且周圍隔間清晰！
                XYZ opPoint = XYZ.Zero;
                LocationPoint lp = opening.Location as LocationPoint;
                if (lp != null) opPoint = lp.Point;
                else
                {
                    BoundingBoxXYZ bbox = opening.get_BoundingBox(keyPlan);
                    if (bbox != null) opPoint = (bbox.Min + bbox.Max) * 0.5;
                }

                if (opPoint != XYZ.Zero)
                {
                    XYZ localCenter = viewTransform.Inverse.OfPoint(opPoint);
                    double halfW = GeometryService.MmToFeet(8000); // 寬度 16 米
                    double halfH = GeometryService.MmToFeet(6000); // 高度 12 米

                    BoundingBoxXYZ zoneCrop = new BoundingBoxXYZ();
                    zoneCrop.Min = new XYZ(localCenter.X - halfW, localCenter.Y - halfH, origBox.Min.Z);
                    zoneCrop.Max = new XYZ(localCenter.X + halfW, localCenter.Y + halfH, origBox.Max.Z);

                    keyPlan.CropBox = zoneCrop;
                    keyPlan.CropBoxActive = true;
                    keyPlan.CropBoxVisible = false;
                }
                else
                {
                    keyPlan.CropBoxActive = true;
                    keyPlan.CropBoxVisible = false;
                }
            }
            catch
            {
                keyPlan.CropBoxActive = true;
                keyPlan.CropBoxVisible = false;
            }

            try
            {
                Parameter pAnnotCrop = keyPlan.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                if (pAnnotCrop != null && !pAnnotCrop.IsReadOnly)
                {
                    pAnnotCrop.Set(1);
                }
            }
            catch { }
        }

        private static void HighlightWallAndOpening(Document doc, View view, Wall wall, FamilyInstance opening)
        {
            FillPatternElement solidPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(delegate(FillPatternElement fp) 
                { 
                    return fp.GetFillPattern() != null && fp.GetFillPattern().IsSolidFill; 
                });

            ElementId solidPatternId = solidPattern != null ? solidPattern.Id : ElementId.InvalidElementId;
            Color brightRed = new Color(255, 0, 0);

            // 1. 開口元件高亮
            OverrideGraphicSettings openingOverride = new OverrideGraphicSettings();
            openingOverride.SetProjectionLineColor(brightRed);
            openingOverride.SetProjectionLineWeight(10);
            openingOverride.SetCutLineColor(brightRed);
            openingOverride.SetCutLineWeight(10);

            if (solidPatternId != ElementId.InvalidElementId)
            {
                openingOverride.SetSurfaceForegroundPatternId(solidPatternId);
                openingOverride.SetSurfaceForegroundPatternColor(brightRed);
                openingOverride.SetCutForegroundPatternId(solidPatternId);
                openingOverride.SetCutForegroundPatternColor(brightRed);
            }
            view.SetElementOverrides(opening.Id, openingOverride);

            // 2. 宿主牆高亮
            if (wall != null)
            {
                OverrideGraphicSettings wallOverride = new OverrideGraphicSettings();
                Color wallColor = new Color(220, 20, 20);
                wallOverride.SetProjectionLineColor(wallColor);
                wallOverride.SetProjectionLineWeight(8);
                wallOverride.SetCutLineColor(wallColor);
                wallOverride.SetCutLineWeight(8);
                
                if (solidPatternId != ElementId.InvalidElementId)
                {
                    wallOverride.SetCutForegroundPatternId(solidPatternId);
                    wallOverride.SetCutForegroundPatternColor(new Color(255, 180, 180));
                    wallOverride.SetSurfaceForegroundPatternId(solidPatternId);
                    wallOverride.SetSurfaceForegroundPatternColor(new Color(255, 200, 200));
                }
                view.SetElementOverrides(wall.Id, wallOverride);
            }
        }

        private static void AddKeyPlanLocationMarker(Document doc, ViewPlan view, Wall wall, FamilyInstance opening)
        {
            try
            {
                XYZ opPos = XYZ.Zero;
                LocationPoint lp = opening.Location as LocationPoint;
                if (lp != null) opPos = lp.Point;
                else
                {
                    BoundingBoxXYZ obox = opening.get_BoundingBox(view);
                    if (obox != null) opPos = (obox.Min + obox.Max) * 0.5;
                }

                if (opPos == XYZ.Zero) return;

                XYZ wallNormal = wall.Orientation.Normalize();
                XYZ textPos = new XYZ(
                    opPos.X + wallNormal.X * GeometryService.MmToFeet(1800),
                    opPos.Y + wallNormal.Y * GeometryService.MmToFeet(1800),
                    opPos.Z);

                string opTypeName = opening.Symbol != null ? opening.Symbol.Name : "開孔";
                string markerText = string.Format("🎯 此詳圖開孔牆面\n規格：{0}\n牆體：{1} (ID: {2})",
                    opTypeName, wall.Name, wall.Id.Value);

                TextNoteOptions options = new TextNoteOptions();
                options.TypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                options.HorizontalAlignment = HorizontalTextAlignment.Left;

                TextNote note = TextNote.Create(doc, view.Id, textPos, markerText, options);
                if (note != null)
                {
                    Leader leader = note.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                    if (leader != null)
                    {
                        leader.End = opPos;
                    }
                }
            }
            catch
            {
            }
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
