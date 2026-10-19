using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace OpeningDetailAddin.Services
{
    /// <summary>
    /// 開口對應之既有圖說與視圖快取模型
    /// </summary>
    public class ExistingItemCache
    {
        public List<ViewSheet> Sheets { get; set; }
        public List<ViewSection> SectionViews { get; set; }
        public List<ViewPlan> KeyPlans { get; set; }
        public List<View3D> Views3D { get; set; }

        public ExistingItemCache()
        {
            Sheets = new List<ViewSheet>();
            SectionViews = new List<ViewSection>();
            KeyPlans = new List<ViewPlan>();
            Views3D = new List<View3D>();
        }
    }

    /// <summary>
    /// 既有視圖與圖紙偵測服務 (避免重複生成)
    /// </summary>
    public static class ExistingItemDetectorService
    {
        /// <summary>
        /// 一次性預加載專案中所有相關圖紙與視圖，建立高速記憶體檢索快取
        /// </summary>
        public static ExistingItemCache BuildCache(Document doc)
        {
            ExistingItemCache cache = new ExistingItemCache();
            if (doc == null) return cache;

            try
            {
                // 1. 收集所有圖紙
                cache.Sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(delegate(ViewSheet s) { return !s.IsTemplate; })
                    .ToList();
            }
            catch { }

            try
            {
                // 2. 收集所有剖面視圖
                cache.SectionViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSection))
                    .Cast<ViewSection>()
                    .Where(delegate(ViewSection vs) { return !vs.IsTemplate && vs.ViewType == ViewType.Section; })
                    .ToList();
            }
            catch { }

            try
            {
                // 3. 收集所有樓層平面圖 (尋找索引圖)
                cache.KeyPlans = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(delegate(ViewPlan vp) { return !vp.IsTemplate && vp.ViewType == ViewType.FloorPlan; })
                    .ToList();
            }
            catch { }

            try
            {
                // 4. 收集所有 3D 視圖
                cache.Views3D = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .Where(delegate(View3D v) { return !v.IsTemplate && !v.IsPerspective; })
                    .ToList();
            }
            catch { }

            return cache;
        }

        /// <summary>
        /// 尋找開口是否已存在對應的 A3 圖紙
        /// </summary>
        public static ViewSheet FindExistingSheet(Document doc, ElementId openingId, ExistingItemCache cache)
        {
            if (openingId == null) return null;
            string opIdStr = openingId.Value.ToString();

            List<ViewSheet> sheets = (cache != null) ? cache.Sheets : null;
            if (sheets == null)
            {
                sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(delegate(ViewSheet s) { return !s.IsTemplate; })
                    .ToList();
            }

            foreach (var s in sheets)
            {
                // 檢查 A: 圖紙編號結尾是否為 -{opId} 或包含 -{opId}_
                string sNum = s.SheetNumber;
                if (!string.IsNullOrEmpty(sNum))
                {
                    if (sNum.EndsWith("-" + opIdStr, StringComparison.OrdinalIgnoreCase)
                        || sNum.Contains("-" + opIdStr + "_")
                        || sNum.Contains("-W" + opIdStr))
                    {
                        return s;
                    }
                }

                // 檢查 B: 圖紙上放置的視埠名稱是否包含開口 ID
                try
                {
                    var placedIds = s.GetAllPlacedViews();
                    foreach (var vId in placedIds)
                    {
                        Element vElem = doc.GetElement(vId);
                        if (vElem != null && !string.IsNullOrEmpty(vElem.Name))
                        {
                            if (vElem.Name.Contains(opIdStr) &&
                                (vElem.Name.IndexOf("開口", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 vElem.Name.IndexOf("剖面", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 vElem.Name.IndexOf("索引", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 vElem.Name.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                return s;
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// 尋找開口是否已存在專屬剖面視圖
        /// </summary>
        public static ViewSection FindExistingSectionView(Document doc, ElementId openingId, ExistingItemCache cache)
        {
            if (openingId == null) return null;
            string opIdStr = openingId.Value.ToString();

            List<ViewSection> sections = (cache != null) ? cache.SectionViews : null;
            if (sections == null)
            {
                sections = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSection))
                    .Cast<ViewSection>()
                    .Where(delegate(ViewSection vs) { return !vs.IsTemplate; })
                    .ToList();
            }

            return sections.FirstOrDefault(delegate(ViewSection vs)
            {
                return vs.Name != null
                       && vs.Name.Contains(opIdStr)
                       && (vs.Name.IndexOf("剖面", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           vs.Name.IndexOf("開口", StringComparison.OrdinalIgnoreCase) >= 0);
            });
        }

        /// <summary>
        /// 尋找開口是否已存在專屬平面索引圖
        /// </summary>
        public static ViewPlan FindExistingKeyPlan(Document doc, ElementId openingId, ExistingItemCache cache)
        {
            if (openingId == null) return null;
            string opIdStr = openingId.Value.ToString();

            List<ViewPlan> plans = (cache != null) ? cache.KeyPlans : null;
            if (plans == null)
            {
                plans = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(delegate(ViewPlan vp) { return !vp.IsTemplate && vp.ViewType == ViewType.FloorPlan; })
                    .ToList();
            }

            return plans.FirstOrDefault(delegate(ViewPlan vp)
            {
                return vp.Name != null
                       && vp.Name.Contains(opIdStr)
                       && (vp.Name.IndexOf("索引", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           vp.Name.IndexOf("KeyPlan", StringComparison.OrdinalIgnoreCase) >= 0);
            });
        }

        /// <summary>
        /// 尋找開口是否已存在專屬 3D 選取方塊視圖
        /// </summary>
        public static View3D FindExisting3DView(Document doc, ElementId openingId, ExistingItemCache cache)
        {
            if (openingId == null) return null;
            string opIdStr = openingId.Value.ToString();

            List<View3D> views3D = (cache != null) ? cache.Views3D : null;
            if (views3D == null)
            {
                views3D = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .Where(delegate(View3D v) { return !v.IsTemplate; })
                    .ToList();
            }

            return views3D.FirstOrDefault(delegate(View3D v)
            {
                return v.Name != null
                       && v.Name.Contains(opIdStr)
                       && (v.Name.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           v.Name.IndexOf("開口", StringComparison.OrdinalIgnoreCase) >= 0);
            });
        }
    }
}
