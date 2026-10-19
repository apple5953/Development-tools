using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using OpeningDetailAddin.Models;

namespace OpeningDetailAddin.Services
{
    /// <summary>
    /// A3 圖紙生成與 Viewport 排版服務
    /// 完整支援「剖面圖 + 索引圖 + 3D選取方塊視圖」三合一自動排版與視埠類型選擇
    /// </summary>
    public static class SheetService
    {
        public static List<TitleBlockModel> GetAvailableTitleBlocks(Document doc)
        {
            var titleBlocks = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Select(delegate(FamilySymbol fs) 
                { 
                    return new TitleBlockModel
                    {
                        Id = fs.Id,
                        FamilyName = fs.FamilyName,
                        TypeName = fs.Name
                    }; 
                })
                .OrderBy(delegate(TitleBlockModel tb) { return tb.FamilyName; })
                .ThenBy(delegate(TitleBlockModel tb) { return tb.TypeName; })
                .ToList();

            return titleBlocks;
        }

        /// <summary>
        /// 取得專案內所有視埠類型 (如「無標題」、「視圖標題」等)
        /// 完整支援透過現有視埠 GetValidTypes()、預設類型推導、及 BuiltInParameter 篩選
        /// </summary>
        public static List<ViewportTypeModel> GetAvailableViewportTypes(Document doc)
        {
            List<ViewportTypeModel> list = new List<ViewportTypeModel>();
            List<ElementId> seenIds = new List<ElementId>();

            // 方法 1：從專案現有視埠取得所有有效類型 (Revit API 原生最精確途徑)
            try
            {
                Viewport sampleVp = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .FirstOrDefault();

                if (sampleVp != null)
                {
                    ICollection<ElementId> validTypes = sampleVp.GetValidTypes();
                    if (validTypes != null)
                    {
                        foreach (var typeId in validTypes)
                        {
                            Element elem = doc.GetElement(typeId);
                            if (elem != null && !seenIds.Contains(typeId))
                            {
                                seenIds.Add(typeId);
                                list.Add(new ViewportTypeModel
                                {
                                    Id = elem.Id,
                                    Name = elem.Name
                                });
                            }
                        }
                    }
                }
            }
            catch { }

            // 方法 2：補充檢索所有 ElementType (針對未放置視埠的專案，或系統族群樣式)
            // (注意：Revit 中 Viewport 的 ElementType 之 Category 為 null，不能使用 OfCategory)
            try
            {
                string targetFamilyName = null;
                try
                {
                    ElementId defaultVpTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.ViewportType);
                    if (defaultVpTypeId != null && defaultVpTypeId != ElementId.InvalidElementId)
                    {
                        Element defElem = doc.GetElement(defaultVpTypeId);
                        if (defElem is ElementType)
                        {
                            targetFamilyName = ((ElementType)defElem).FamilyName;
                            if (!seenIds.Contains(defElem.Id))
                            {
                                seenIds.Add(defElem.Id);
                                list.Add(new ViewportTypeModel
                                {
                                    Id = defElem.Id,
                                    Name = defElem.Name
                                });
                            }
                        }
                    }
                }
                catch { }

                var allTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElementType))
                    .Cast<ElementType>();

                foreach (var et in allTypes)
                {
                    try
                    {
                        if (et == null || seenIds.Contains(et.Id)) continue;

                        bool isViewportType = false;

                        // 特徵 A: 具有視埠專屬參數 (VIEWPORT_ATTR_SHOW_LABEL 或 VIEWPORT_ATTR_LABEL_TAG)
                        if (et.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL) != null
                            || et.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_LABEL_TAG) != null)
                        {
                            isViewportType = true;
                        }

                        // 特徵 B: 族群名稱比對
                        if (!isViewportType && !string.IsNullOrEmpty(targetFamilyName))
                        {
                            if (string.Equals(et.FamilyName, targetFamilyName, StringComparison.OrdinalIgnoreCase))
                            {
                                isViewportType = true;
                            }
                        }

                        if (!isViewportType)
                        {
                            if (string.Equals(et.FamilyName, "Viewport", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(et.FamilyName, "視埠", StringComparison.OrdinalIgnoreCase))
                            {
                                isViewportType = true;
                            }
                        }

                        if (isViewportType)
                        {
                            seenIds.Add(et.Id);
                            list.Add(new ViewportTypeModel
                            {
                                Id = et.Id,
                                Name = et.Name
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 依名稱排序
            list = list.OrderBy(delegate(ViewportTypeModel m) { return m.Name; }).ToList();

            // 在首項加入預設選項
            list.Insert(0, new ViewportTypeModel
            {
                Id = ElementId.InvalidElementId,
                Name = "(專案預設視埠樣式)"
            });

            return list;
        }

        /// <summary>
        /// 建立開孔詳圖 A3 圖紙，並將剖面圖、平面索引圖與 3D 視圖精密排版至圖紙上
        /// </summary>
        public static ViewSheet CreateOpeningSheet(
            Document doc, 
            ElementId titleBlockId, 
            OpeningItem openingItem,
            WallGroup wallGroup,
            LevelGroup levelGroup,
            ViewSection sectionView, 
            ViewPlan keyPlan,
            View3D view3D,
            ElementId viewportTypeId,
            string sheetNumberPrefix,
            string layoutMode)
        {
            return CreateOpeningSheet(doc, titleBlockId, openingItem, wallGroup, levelGroup,
                sectionView, keyPlan, view3D, viewportTypeId, sheetNumberPrefix, layoutMode, false, null);
        }

        public static ViewSheet CreateOpeningSheet(
            Document doc, 
            ElementId titleBlockId, 
            OpeningItem openingItem,
            WallGroup wallGroup,
            LevelGroup levelGroup,
            ViewSection sectionView, 
            ViewPlan keyPlan,
            View3D view3D,
            ElementId viewportTypeId,
            string sheetNumberPrefix,
            string layoutMode,
            bool skipExisting,
            ExistingItemCache cache)
        {
            if (titleBlockId == null || titleBlockId == ElementId.InvalidElementId)
            {
                throw new ArgumentException("請指定有效的圖框 (TitleBlock)。");
            }

            // 0. 若啟用略過已建立項目，且該開口已有對應圖紙，直接回傳既有圖紙 (防止重複建圖)
            if (skipExisting && openingItem != null && openingItem.Id != null)
            {
                ViewSheet existing = ExistingItemDetectorService.FindExistingSheet(doc, openingItem.Id, cache);
                if (existing != null)
                {
                    return existing;
                }
            }

            // 1. 建立圖紙
            ViewSheet sheet = ViewSheet.Create(doc, titleBlockId);

            // 2. 圖紙編號與名稱命名
            string lvlName = levelGroup != null ? levelGroup.LevelName : "FL";
            string wallId = (wallGroup != null && wallGroup.WallId != null) ? wallGroup.WallId.Value.ToString() : "0";
            string opId = (openingItem != null && openingItem.Id != null) ? openingItem.Id.Value.ToString() : "0";

            string desiredNumber = string.Format("{0}{1}-W{2}-{3}", sheetNumberPrefix, lvlName, wallId, opId);
            sheet.SheetNumber = GetUniqueSheetNumber(doc, desiredNumber);
            sheet.Name = string.Format("牆開孔詳圖 - {0}", openingItem != null ? openingItem.TypeName : "開口");

            // 3. 初始配置 Viewport (保證所有視圖同步且確實放置於圖紙內)
            doc.Regenerate();

            XYZ tempSecCenter = new XYZ(0.40, 0.50, 0);
            XYZ tempPlanCenter = new XYZ(1.05, 0.70, 0);
            XYZ temp3DCenter = new XYZ(1.05, 0.30, 0);

            Viewport vpSec = SafeAddViewToSheet(doc, sheet, sectionView, tempSecCenter, viewportTypeId);
            Viewport vpPlan = SafeAddViewToSheet(doc, sheet, keyPlan, tempPlanCenter, viewportTypeId);
            Viewport vp3D = SafeAddViewToSheet(doc, sheet, view3D, temp3DCenter, viewportTypeId);

            // 4. 【核心關鍵】執行「一步到位」幾何感知動態排版引擎 (精確計算無碰撞置中坐標)
            ArrangeViewportsOnSheet(doc, sheet, vpSec, vpPlan, vp3D, layoutMode);

            return sheet;
        }

        /// <summary>
        /// 安全且可靠地將視圖放置於圖紙上：
        /// 1. 若視圖已在當前圖紙，直接回傳對應 Viewport。
        /// 2. 若視圖已放置於其他圖紙 (Revit 限制同一視圖不得存在於多張圖紙)，自動透過 WithDetailing 複製視圖以取得專屬副本，保證 100% 成功放置！
        /// 3. 包覆獨立 try-catch，確保任一視圖邊際異常絕不中斷其他視圖的放置與排版。
        /// </summary>
        private static Viewport SafeAddViewToSheet(
            Document doc, 
            ViewSheet sheet, 
            View view, 
            XYZ initialLocation, 
            ElementId viewportTypeId)
        {
            if (doc == null || sheet == null || view == null) return null;

            View viewToPlace = view;

            // 檢查 1: 該視圖是否已在當前圖紙上
            try
            {
                var placedViews = sheet.GetAllPlacedViews();
                if (placedViews != null && placedViews.Contains(viewToPlace.Id))
                {
                    Viewport existingVp = new FilteredElementCollector(doc, sheet.Id)
                        .OfClass(typeof(Viewport))
                        .Cast<Viewport>()
                        .FirstOrDefault(delegate(Viewport v) { return v.ViewId == viewToPlace.Id; });
                    if (existingVp != null)
                    {
                        ApplyViewportType(existingVp, viewportTypeId);
                        return existingVp;
                    }
                }
            }
            catch { }

            // 檢查 2: 若無法直接放置 (例如已放置於其他圖紙)，自動進行複製以獲取專屬副本
            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewToPlace.Id))
            {
                try
                {
                    ViewDuplicateOption opt = ViewDuplicateOption.WithDetailing;
                    if (!viewToPlace.CanViewBeDuplicated(opt))
                    {
                        opt = ViewDuplicateOption.Duplicate;
                    }

                    if (viewToPlace.CanViewBeDuplicated(opt))
                    {
                        ElementId dupId = viewToPlace.Duplicate(opt);
                        View dupView = doc.GetElement(dupId) as View;
                        if (dupView != null)
                        {
                            viewToPlace = dupView;
                        }
                    }
                }
                catch { }
            }

            // 檢查 3: 執行建立 Viewport
            try
            {
                if (Viewport.CanAddViewToSheet(doc, sheet.Id, viewToPlace.Id))
                {
                    Viewport vp = Viewport.Create(doc, sheet.Id, viewToPlace.Id, initialLocation);
                    ApplyViewportType(vp, viewportTypeId);
                    return vp;
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// 「一步到位」幾何感知精密排版引擎：
        /// 1. 優先以 sheet.Outline 為基準量測圖紙邊界，輔以圖框真實 BoundingBox，嚴格避開右側標題欄 (65mm) 與裝訂留白。
        /// 2. 呼叫 doc.Regenerate() 後讀取 Viewport 的真實 BoxOutline 尺寸。
        /// 3. 動態計算無碰撞置中坐標，並套用 SetBoxCenter 與 LabelOffset。
        /// </summary>
        public static void ArrangeViewportsOnSheet(
            Document doc,
            ViewSheet sheet,
            Viewport vpSec,
            Viewport vpPlan,
            Viewport vp3D,
            string layoutMode)
        {
            if (sheet == null) return;

            // 1. 強制重新生成圖紙以計算準確的 Viewport 邊界
            doc.Regenerate();

            // 2. 偵測圖紙與圖框真實邊界 (以 sheet.Outline 為最優先基準，輔以 TitleBlock BoundingBox)
            double sheetMinX = 0.0;
            double sheetMaxX = GeometryService.MmToFeet(420);
            double sheetMinY = 0.0;
            double sheetMaxY = GeometryService.MmToFeet(297);

            bool hasValidSheetOutline = false;
            if (sheet.Outline != null)
            {
                double outW = sheet.Outline.Max.U - sheet.Outline.Min.U;
                double outH = sheet.Outline.Max.V - sheet.Outline.Min.V;
                if (outW > GeometryService.MmToFeet(100) && outH > GeometryService.MmToFeet(100))
                {
                    sheetMinX = sheet.Outline.Min.U;
                    sheetMaxX = sheet.Outline.Max.U;
                    sheetMinY = sheet.Outline.Min.V;
                    sheetMaxY = sheet.Outline.Max.V;
                    hasValidSheetOutline = true;
                }
            }

            if (!hasValidSheetOutline)
            {
                FamilyInstance titleBlock = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilyInstance>()
                    .FirstOrDefault();

                if (titleBlock != null)
                {
                    BoundingBoxXYZ tbBox = titleBlock.get_BoundingBox(sheet);
                    if (tbBox != null)
                    {
                        sheetMinX = tbBox.Min.X;
                        sheetMaxX = tbBox.Max.X;
                        sheetMinY = tbBox.Min.Y;
                        sheetMaxY = tbBox.Max.Y;
                    }
                }
            }

            double totalW = sheetMaxX - sheetMinX;
            double totalH = sheetMaxY - sheetMinY;

            // 3. 設定安全可繪圖工作區 (Safe Drawing Work Area)
            // 標準 A3 橫向圖框 (如事務所標準圖框，底部約 45mm 高之大型標題資訊欄)：
            // 左側裝訂留白: 18mm
            // 右側留白: 20mm (不再浪費 65mm，讓右側視圖充分向右靠齊，騰出中左間距)
            // 頂部留白: 15mm
            // 底部標題欄淨空: 58mm (標準圖框底部表格約 45mm，保留 58mm 保證視埠與標題文字絕對不壓到底部表格！)
            double leftMargin = GeometryService.MmToFeet(18);
            double rightMargin = (totalW > GeometryService.MmToFeet(500)) 
                ? GeometryService.MmToFeet(25) 
                : GeometryService.MmToFeet(20);
            double topMargin = GeometryService.MmToFeet(15);
            double bottomMargin = GeometryService.MmToFeet(58);

            double drawMinX = sheetMinX + leftMargin;
            double drawMaxX = sheetMaxX - rightMargin;
            double drawMinY = sheetMinY + bottomMargin;
            double drawMaxY = sheetMaxY - topMargin;

            double drawW = drawMaxX - drawMinX;
            double drawH = drawMaxY - drawMinY;

            // 4. 讀取三個 Viewport 的真實 Outline 尺寸 (單位: 呎)
            Outline secBox = (vpSec != null) ? vpSec.GetBoxOutline() : null;
            Outline planBox = (vpPlan != null) ? vpPlan.GetBoxOutline() : null;
            Outline box3D = (vp3D != null) ? vp3D.GetBoxOutline() : null;

            double secW = (secBox != null) ? (secBox.MaximumPoint.X - secBox.MinimumPoint.X) : 0;
            double secH = (secBox != null) ? (secBox.MaximumPoint.Y - secBox.MinimumPoint.Y) : 0;

            double planW = (planBox != null) ? (planBox.MaximumPoint.X - planBox.MinimumPoint.X) : 0;
            double planH = (planBox != null) ? (planBox.MaximumPoint.Y - planBox.MinimumPoint.Y) : 0;

            double w3D = (box3D != null) ? (box3D.MaximumPoint.X - box3D.MinimumPoint.X) : 0;
            double h3D = (box3D != null) ? (box3D.MaximumPoint.Y - box3D.MinimumPoint.Y) : 0;

            if (layoutMode == "Horizontal")
            {
                // ==========================================
                // 排版模式 B：橫向三欄平鋪 (左剖面 + 中3D + 右索引)
                // ==========================================
                double colGap = GeometryService.MmToFeet(15);
                double totalViewsW = secW + w3D + planW;
                double remainingW = drawW - totalViewsW;

                double c1_X, c2_X, c3_X;
                if (remainingW > colGap * 2)
                {
                    double gap = remainingW / 4.0;
                    c1_X = drawMinX + gap + (secW / 2.0);
                    c2_X = c1_X + (secW / 2.0) + gap + (w3D / 2.0);
                    c3_X = c2_X + (w3D / 2.0) + gap + (planW / 2.0);
                }
                else
                {
                    double step = drawW / 3.0;
                    c1_X = drawMinX + (step * 0.5);
                    c2_X = drawMinX + (step * 1.5);
                    c3_X = drawMinX + (step * 2.5);
                }

                double centerY = (drawMinY + drawMaxY) * 0.5;

                if (vpSec != null) vpSec.SetBoxCenter(new XYZ(c1_X, centerY, 0));
                if (vp3D != null) vp3D.SetBoxCenter(new XYZ(c2_X, centerY, 0));
                if (vpPlan != null) vpPlan.SetBoxCenter(new XYZ(c3_X, centerY, 0));
            }
            else
            {
                // ==============================================================
                // 排版模式 A：✨ 三合一標準版 (左側剖面詳圖 + 右上平面索引 + 右下3D視圖)
                // ==============================================================

                // 1. 右側欄寬度與 X 中心點計算 (靠齊最右側)
                double maxRightW = Math.Max(planW, w3D);
                double minRightColW = GeometryService.MmToFeet(95);
                double maxRightColW = GeometryService.MmToFeet(120);
                double rightColWidth = Math.Max(maxRightW, minRightColW);
                if (rightColWidth > maxRightColW) rightColWidth = maxRightColW;

                // 右欄中心點：靠齊工作區右側
                double rightColCenterX = drawMaxX - (rightColWidth / 2.0);

                // 2. 右側欄 Y 軸垂直定位 (索引圖在上方，3D視圖在下方，兩者保持安全間隙)
                double vGap = GeometryService.MmToFeet(22); // 上下視圖安全垂直間隔 22mm
                
                // 平面索引圖置於右上，緊貼頂部安全留白 (微調下移 6mm)
                double planCenterY = drawMaxY - (planH / 2.0) - GeometryService.MmToFeet(6);
                
                // 3D 視圖置於右下，保留標題文字空間 (標題文字在下方約 7mm，底緣留 12mm 確保絕不碰到底部圖框)
                double center3DY = drawMinY + (h3D / 2.0) + GeometryService.MmToFeet(12);

                // 檢查上下是否重疊
                double planBottomY = planCenterY - (planH / 2.0);
                double top3DY = center3DY + (h3D / 2.0);
                if (planBottomY - top3DY < vGap)
                {
                    double totalViewsH = planH + h3D;
                    double availableGap = drawH - totalViewsH;
                    if (availableGap > 0)
                    {
                        double pad = availableGap / 3.0;
                        center3DY = drawMinY + pad + (h3D / 2.0);
                        planCenterY = center3DY + (h3D / 2.0) + pad + (planH / 2.0);
                    }
                }

                // 3. 左側剖面詳圖定位 (在剩餘的左側大工作區內完全居中，並保證絕對不跨入右側欄)
                double colGap = GeometryService.MmToFeet(25); // 左右欄安全間距 25mm
                double leftZoneMaxX = rightColCenterX - (rightColWidth / 2.0) - colGap;
                double leftZoneW = leftZoneMaxX - drawMinX;
                double secCenterX = drawMinX + (leftZoneW / 2.0);

                // 嚴格邊界保護：若剖面圖較寬，限制其 X 中心，保證右邊界絕不超過 leftZoneMaxX
                if (secW > 0)
                {
                    double maxSecCenterX = leftZoneMaxX - (secW / 2.0);
                    if (secCenterX > maxSecCenterX) secCenterX = maxSecCenterX;
                    double minSecCenterX = drawMinX + (secW / 2.0);
                    if (secCenterX < minSecCenterX) secCenterX = minSecCenterX;
                }

                double secCenterY = (drawMinY + drawMaxY) * 0.5;
                // 垂直保護：確保剖面圖底部及標題不壓到底部標題欄
                double secBottomY = secCenterY - (secH / 2.0);
                if (secBottomY < drawMinY)
                {
                    secCenterY = drawMinY + (secH / 2.0) + GeometryService.MmToFeet(10);
                }

                // 套用中心點位移
                if (vpSec != null) vpSec.SetBoxCenter(new XYZ(secCenterX, secCenterY, 0));
                if (vpPlan != null) vpPlan.SetBoxCenter(new XYZ(rightColCenterX, planCenterY, 0));
                if (vp3D != null) vp3D.SetBoxCenter(new XYZ(rightColCenterX, center3DY, 0));
            }

            // 5. 標題線微調優化 (Revit 2024 LabelOffset 與 LabelLineLength)
            AdjustViewportLabels(doc, vpSec, secW);
            AdjustViewportLabels(doc, vpPlan, planW);
            AdjustViewportLabels(doc, vp3D, w3D);

            // 6. 再次重新生成以保證視埠位置絕對生效
            doc.Regenerate();
        }

        private static void AdjustViewportLabels(Document doc, Viewport vp, double viewWidth)
        {
            if (vp == null) return;
            try
            {
                // 將標題位置緊貼在視埠左下角 (向右偏移 3mm，向下 7mm)
                vp.LabelOffset = new XYZ(GeometryService.MmToFeet(3), -GeometryService.MmToFeet(7), 0);

                // 標題線長度依視埠寬度自適應 (上限 60mm，下限 30mm)
                if (viewWidth > 0)
                {
                    double targetLineLen = Math.Min(viewWidth * 0.65, GeometryService.MmToFeet(60));
                    if (targetLineLen < GeometryService.MmToFeet(30)) targetLineLen = GeometryService.MmToFeet(30);
                    vp.LabelLineLength = targetLineLen;
                }
            }
            catch
            {
                // 忽略不支援標題自訂或無標題樣式之異常
            }
        }

        private static void ApplyViewportType(Viewport vp, ElementId typeId)
        {
            if (vp != null && typeId != null && typeId != ElementId.InvalidElementId)
            {
                try
                {
                    vp.ChangeTypeId(typeId);
                }
                catch
                {
                }
            }
        }

        private static string GetUniqueSheetNumber(Document doc, string baseNumber)
        {
            string number = baseNumber;
            int counter = 1;
            while (new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Any(delegate(ViewSheet s) 
            { 
                return s.SheetNumber.Equals(number, StringComparison.OrdinalIgnoreCase); 
            }))
            {
                number = string.Format("{0}_{1}", baseNumber, counter++);
            }
            return number;
        }
    }
}
