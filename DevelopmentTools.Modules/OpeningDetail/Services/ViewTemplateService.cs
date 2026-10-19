using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using OpeningDetailAddin.Models;

namespace OpeningDetailAddin.Services
{
    /// <summary>
    /// 視圖樣板服務
    /// 負責：
    /// 1. 讀取專案內既有之剖面與平面視圖樣板供使用者選擇
    /// 2. 依照開口圖面之呈現需求，自動建立並配置專屬標準樣板 (隱藏雜亂設備與家具，強化牆體開口)
    /// </summary>
    public static class ViewTemplateService
    {
        public const string DefaultSectionTemplateName = "【開口詳圖】標準剖面樣板";
        public const string DefaultKeyPlanTemplateName = "【開口詳圖】樓層索引樣板";
        public const string Default3DTemplateName = "【開口詳圖】3D立體樣板";

        /// <summary>
        /// 取得專案內所有可用於剖面圖的視圖樣板清單
        /// </summary>
        public static List<ViewTemplateModel> GetSectionViewTemplates(Document doc)
        {
            List<ViewTemplateModel> list = new List<ViewTemplateModel>();

            // 1. 預設自動建立選項
            list.Add(new ViewTemplateModel
            {
                Id = ElementId.InvalidElementId,
                Name = "✨ 自動依開口需求設定標準樣板 (推薦)",
                IsAutoCreate = true,
                IsNone = false
            });

            // 2. 專案中已有的剖面視圖樣板
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(delegate(View v) { return v.IsTemplate && v.ViewType == ViewType.Section; })
                .OrderBy(delegate(View v) { return v.Name; });

            foreach (var t in templates)
            {
                list.Add(new ViewTemplateModel
                {
                    Id = t.Id,
                    Name = t.Name,
                    IsAutoCreate = false,
                    IsNone = false
                });
            }

            // 3. 不套用樣板選項
            list.Add(new ViewTemplateModel
            {
                Id = ElementId.InvalidElementId,
                Name = "(不套用樣板)",
                IsAutoCreate = false,
                IsNone = true
            });

            return list;
        }

        /// <summary>
        /// 取得專案內所有可用於樓層索引圖 (平面) 的視圖樣板清單
        /// </summary>
        public static List<ViewTemplateModel> GetPlanViewTemplates(Document doc)
        {
            List<ViewTemplateModel> list = new List<ViewTemplateModel>();

            // 1. 預設自動建立選項
            list.Add(new ViewTemplateModel
            {
                Id = ElementId.InvalidElementId,
                Name = "✨ 自動依索引需求設定標準樣板 (推薦)",
                IsAutoCreate = true,
                IsNone = false
            });

            // 2. 專案中已有的平面視圖樣板
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(delegate(View v) { return v.IsTemplate && v.ViewType == ViewType.FloorPlan; })
                .OrderBy(delegate(View v) { return v.Name; });

            foreach (var t in templates)
            {
                list.Add(new ViewTemplateModel
                {
                    Id = t.Id,
                    Name = t.Name,
                    IsAutoCreate = false,
                    IsNone = false
                });
            }

            // 3. 不套用樣板選項
            list.Add(new ViewTemplateModel
            {
                Id = ElementId.InvalidElementId,
                Name = "(不套用樣板)",
                IsAutoCreate = false,
                IsNone = true
            });

            return list;
        }

        /// <summary>
        /// 取得專案內所有可用於 3D 視圖的視圖樣板清單
        /// </summary>
        public static List<ViewTemplateModel> Get3DViewTemplates(Document doc)
        {
            List<ViewTemplateModel> list = new List<ViewTemplateModel>();

            // 1. 預設自動建立選項
            list.Add(new ViewTemplateModel
            {
                Id = ElementId.InvalidElementId,
                Name = "✨ 自動依開口需求設定標準樣板 (推薦)",
                IsAutoCreate = true,
                IsNone = false
            });

            // 2. 專案中已有的 3D 視圖樣板
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(delegate(View v) { return v.IsTemplate && v.ViewType == ViewType.ThreeD; })
                .OrderBy(delegate(View v) { return v.Name; });

            foreach (var t in templates)
            {
                list.Add(new ViewTemplateModel
                {
                    Id = t.Id,
                    Name = t.Name,
                    IsAutoCreate = false,
                    IsNone = false
                });
            }

            // 3. 不套用樣板選項
            list.Add(new ViewTemplateModel
            {
                Id = ElementId.InvalidElementId,
                Name = "(不套用樣板)",
                IsAutoCreate = false,
                IsNone = true
            });

            return list;
        }

        /// <summary>
        /// 套用或自動建立剖面視圖樣板
        /// </summary>
        public static void ApplySectionTemplate(Document doc, ViewSection viewSection, ViewTemplateModel templateModel, int scale)
        {
            if (viewSection == null || templateModel == null || templateModel.IsNone) return;

            if (templateModel.IsAutoCreate)
            {
                View templateView = EnsureSectionTemplate(doc, viewSection, scale);
                if (templateView != null)
                {
                    viewSection.ViewTemplateId = templateView.Id;
                }
            }
            else if (templateModel.Id != null && templateModel.Id != ElementId.InvalidElementId)
            {
                viewSection.ViewTemplateId = templateModel.Id;
            }
        }

        /// <summary>
        /// 套用或自動建立樓層索引平面視圖樣板
        /// </summary>
        public static void ApplyKeyPlanTemplate(Document doc, ViewPlan keyPlan, ViewTemplateModel templateModel, int scale)
        {
            if (keyPlan == null || templateModel == null || templateModel.IsNone) return;

            if (templateModel.IsAutoCreate)
            {
                View templateView = EnsureKeyPlanTemplate(doc, keyPlan, scale);
                if (templateView != null)
                {
                    keyPlan.ViewTemplateId = templateView.Id;
                }
            }
            else if (templateModel.Id != null && templateModel.Id != ElementId.InvalidElementId)
            {
                keyPlan.ViewTemplateId = templateModel.Id;
            }
        }

        /// <summary>
        /// 套用或自動建立 3D 視圖樣板
        /// </summary>
        public static void Apply3DViewTemplate(Document doc, View3D view3D, ViewTemplateModel templateModel, int scale)
        {
            if (view3D == null || templateModel == null || templateModel.IsNone) return;

            if (templateModel.IsAutoCreate)
            {
                View templateView = Ensure3DViewTemplate(doc, view3D, scale);
                if (templateView != null)
                {
                    view3D.ViewTemplateId = templateView.Id;
                }
            }
            else if (templateModel.Id != null && templateModel.Id != ElementId.InvalidElementId)
            {
                view3D.ViewTemplateId = templateModel.Id;
            }
        }

        /// <summary>
        /// 確保存在「【開口詳圖】標準剖面樣板」；若不存在則以 viewSection 之設定自動建立
        /// </summary>
        private static View EnsureSectionTemplate(Document doc, ViewSection sampleView, int scale)
        {
            View existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(delegate(View v) 
                { 
                    return v.IsTemplate && v.Name.Equals(DefaultSectionTemplateName, StringComparison.OrdinalIgnoreCase); 
                });

            if (existing != null)
            {
                return existing;
            }

            // 1. 配置符合牆面開口剖面出圖要求的標準視覺設定
            ConfigureSectionDisplay(doc, sampleView, scale);

            // 2. 建立新視圖樣板
            View newTemplate = sampleView.CreateViewTemplate();
            newTemplate.Name = DefaultSectionTemplateName;
            return newTemplate;
        }

        /// <summary>
        /// 確保存在「【開口詳圖】樓層索引樣板」；若不存在則以 sampleView 之設定自動建立
        /// </summary>
        private static View EnsureKeyPlanTemplate(Document doc, ViewPlan sampleView, int scale)
        {
            View existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(delegate(View v) 
                { 
                    return v.IsTemplate && v.Name.Equals(DefaultKeyPlanTemplateName, StringComparison.OrdinalIgnoreCase); 
                });

            if (existing != null)
            {
                return existing;
            }

            // 1. 配置乾淨清爽之全區索引圖視覺設定
            ConfigureKeyPlanDisplay(doc, sampleView, scale);

            // 2. 建立新視圖樣板
            View newTemplate = sampleView.CreateViewTemplate();
            newTemplate.Name = DefaultKeyPlanTemplateName;
            return newTemplate;
        }

        /// <summary>
        /// 針對剖面圖設置專業 BIM 出圖參數：細緻 (Fine)、隱藏雜亂家具/機電/植栽，凸顯牆體開口
        /// </summary>
        private static void ConfigureSectionDisplay(Document doc, ViewSection view, int scale)
        {
            try
            {
                view.Scale = scale;
                view.DetailLevel = ViewDetailLevel.Fine;
                view.Discipline = ViewDiscipline.Architectural;
                view.DisplayStyle = DisplayStyle.HLR;

                // 隱藏非開口剖面所需之無關品類，保持剖面圖乾淨精準
                HideCategory(doc, view, BuiltInCategory.OST_Furniture);
                HideCategory(doc, view, BuiltInCategory.OST_FurnitureSystems);
                HideCategory(doc, view, BuiltInCategory.OST_Planting);
                HideCategory(doc, view, BuiltInCategory.OST_Site);
                HideCategory(doc, view, BuiltInCategory.OST_PlumbingFixtures);
                HideCategory(doc, view, BuiltInCategory.OST_DuctCurves);
                HideCategory(doc, view, BuiltInCategory.OST_DuctFitting);
                HideCategory(doc, view, BuiltInCategory.OST_PipeCurves);
                HideCategory(doc, view, BuiltInCategory.OST_PipeFitting);
                HideCategory(doc, view, BuiltInCategory.OST_ElectricalEquipment);
                HideCategory(doc, view, BuiltInCategory.OST_ElectricalFixtures);
                HideCategory(doc, view, BuiltInCategory.OST_LightingFixtures);
                HideCategory(doc, view, BuiltInCategory.OST_Conduit);
                HideCategory(doc, view, BuiltInCategory.OST_CableTray);
                HideCategory(doc, view, BuiltInCategory.OST_Entourage);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 針對索引圖設置全區索引參數：中等/粗略、隱藏房間與細部標籤、保留牆體與軸網
        /// </summary>
        private static void ConfigureKeyPlanDisplay(Document doc, ViewPlan view, int scale)
        {
            try
            {
                view.Scale = scale;
                view.DetailLevel = ViewDetailLevel.Medium;
                view.Discipline = ViewDiscipline.Architectural;
                view.DisplayStyle = DisplayStyle.HLR;

                // 隱藏雜亂細節與標籤，保留外廓牆與軸線以利定位
                HideCategory(doc, view, BuiltInCategory.OST_Furniture);
                HideCategory(doc, view, BuiltInCategory.OST_FurnitureSystems);
                HideCategory(doc, view, BuiltInCategory.OST_PlumbingFixtures);
                HideCategory(doc, view, BuiltInCategory.OST_LightingFixtures);
                HideCategory(doc, view, BuiltInCategory.OST_ElectricalFixtures);
                HideCategory(doc, view, BuiltInCategory.OST_DuctCurves);
                HideCategory(doc, view, BuiltInCategory.OST_PipeCurves);
                HideCategory(doc, view, BuiltInCategory.OST_Planting);
                HideCategory(doc, view, BuiltInCategory.OST_RoomTags);
                HideCategory(doc, view, BuiltInCategory.OST_DoorTags);
                HideCategory(doc, view, BuiltInCategory.OST_WindowTags);
                HideCategory(doc, view, BuiltInCategory.OST_Dimensions);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 確保存在「【開口詳圖】3D立體樣板」；若不存在則以 sampleView 之設定自動建立
        /// </summary>
        private static View Ensure3DViewTemplate(Document doc, View3D sampleView, int scale)
        {
            View existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(delegate(View v) 
                { 
                    return v.IsTemplate && v.ViewType == ViewType.ThreeD && v.Name.Equals(Default3DTemplateName, StringComparison.OrdinalIgnoreCase); 
                });

            if (existing != null)
            {
                return existing;
            }

            // 1. 配置乾淨清爽之開口 3D 選取方塊立體視覺設定
            Configure3DDisplay(doc, sampleView, scale);

            // 2. 建立新視圖樣板
            View newTemplate = sampleView.CreateViewTemplate();
            newTemplate.Name = Default3DTemplateName;
            return newTemplate;
        }

        /// <summary>
        /// 針對 3D 選取方塊視圖設置立體展示參數：細緻 (Fine)、著色 (Shading)、隱藏雜亂設備與家具
        /// </summary>
        private static void Configure3DDisplay(Document doc, View3D view, int scale)
        {
            try
            {
                view.Scale = scale;
                view.DetailLevel = ViewDetailLevel.Fine;
                view.Discipline = ViewDiscipline.Architectural;
                view.DisplayStyle = DisplayStyle.ShadingWithEdges;

                // 隱藏非開口 3D 所需之無關品類，確保立體選取方塊聚焦於牆體與開口
                HideCategory(doc, view, BuiltInCategory.OST_Furniture);
                HideCategory(doc, view, BuiltInCategory.OST_FurnitureSystems);
                HideCategory(doc, view, BuiltInCategory.OST_Planting);
                HideCategory(doc, view, BuiltInCategory.OST_Site);
                HideCategory(doc, view, BuiltInCategory.OST_PlumbingFixtures);
                HideCategory(doc, view, BuiltInCategory.OST_DuctCurves);
                HideCategory(doc, view, BuiltInCategory.OST_DuctFitting);
                HideCategory(doc, view, BuiltInCategory.OST_PipeCurves);
                HideCategory(doc, view, BuiltInCategory.OST_PipeFitting);
                HideCategory(doc, view, BuiltInCategory.OST_ElectricalEquipment);
                HideCategory(doc, view, BuiltInCategory.OST_ElectricalFixtures);
                HideCategory(doc, view, BuiltInCategory.OST_LightingFixtures);
                HideCategory(doc, view, BuiltInCategory.OST_Conduit);
                HideCategory(doc, view, BuiltInCategory.OST_CableTray);
                HideCategory(doc, view, BuiltInCategory.OST_Entourage);
            }
            catch
            {
            }
        }

        private static void HideCategory(Document doc, View view, BuiltInCategory bic)
        {
            try
            {
                Category cat = Category.GetCategory(doc, bic);
                if (cat != null && view.CanCategoryBeHidden(cat.Id))
                {
                    view.SetCategoryHidden(cat.Id, true);
                }
            }
            catch
            {
            }
        }
    }
}
