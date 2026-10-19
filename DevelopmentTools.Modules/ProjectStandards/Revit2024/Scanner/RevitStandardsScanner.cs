using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DevelopmentTools.ProjectStandards.Domain.Models;
using DevelopmentTools.ProjectStandards.Domain.Snapshots;

namespace DevelopmentTools.ProjectStandards.Revit2024.Scanner
{
    public class RevitStandardsScanner
    {
        public ProjectStandardsSnapshot Scan(Document doc, ModelDescriptor descriptor)
        {
            if (doc == null) throw new ArgumentNullException("doc");

            var snapshot = new ProjectStandardsSnapshot();

            // 1. Metadata
            snapshot.Metadata = new SnapshotMetadata
            {
                ModelName = doc.Title,
                SourceType = descriptor != null ? descriptor.SourceType : (doc.IsModelInCloud ? ModelSourceType.RevitCloudModel : ModelSourceType.LocalFile),
                RevitVersion = doc.Application.VersionNumber,
                ScanTime = DateTime.UtcNow,
                PluginVersion = "1.0.0",
                LocalPath = doc.PathName,
                CloudVersionId = descriptor != null ? descriptor.VersionId : null
            };

            if (doc.IsModelInCloud)
            {
                try
                {
                    var cloudPath = doc.GetCloudModelPath();
                    if (cloudPath != null)
                    {
                        snapshot.Metadata.ModelGuid = cloudPath.GetModelGUID();
                        snapshot.Metadata.ProjectGuid = cloudPath.GetProjectGUID();
                    }
                }
                catch { }
            }

            // 2. Worksets (if workshared)
            if (doc.IsWorkshared)
            {
                try
                {
                    var worksets = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets();
                    var defaultVisibility = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
                    foreach (var w in worksets)
                    {
                        bool isVisible = true;
                        if (defaultVisibility != null)
                        {
                            isVisible = defaultVisibility.IsWorksetVisible(w.Id);
                        }

                        snapshot.Worksets.Add(new WorksetSnapshot
                        {
                            Name = w.Name,
                            UniqueGuid = w.UniqueId,
                            IsOpenByDefault = w.IsOpen,
                            IsVisibleByDefault = isVisible,
                            Kind = "UserWorkset"
                        });
                    }
                }
                catch { }
            }

            // 3. Project Parameters & Shared Parameters
            try
            {
                var iterator = doc.ParameterBindings.ForwardIterator();
                while (iterator.MoveNext())
                {
                    var def = iterator.Key;
                    var binding = iterator.Current as ElementBinding;
                    if (def != null && binding != null)
                    {
                        var paramSnap = new ProjectParameterSnapshot
                        {
                            Name = def.Name,
                            IsShared = def is ExternalDefinition,
                            DataType = def.GetDataType() != null ? def.GetDataType().TypeId : "Unknown",
                            ParameterGroup = def.GetGroupTypeId() != null ? def.GetGroupTypeId().TypeId : "General",
                            IsInstance = binding is InstanceBinding,
                            UserModifiable = true
                        };

                        if (def is ExternalDefinition)
                        {
                            paramSnap.SharedParameterGuid = ((ExternalDefinition)def).GUID;
                        }

                        if (binding.Categories != null)
                        {
                            foreach (Category cat in binding.Categories)
                            {
                                if (cat != null)
                                {
                                    paramSnap.BoundCategories.Add(cat.Name);
                                }
                            }
                        }

                        snapshot.Parameters.Add(paramSnap);
                    }
                }
            }
            catch { }

            // 4. Levels
            try
            {
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>();
                foreach (var lvl in levels)
                {
                    snapshot.Levels.Add(new LevelSnapshot
                    {
                        Name = lvl.Name,
                        Elevation = lvl.Elevation,
                        UseStory = lvl.LookupParameter("Building Story") != null ? lvl.LookupParameter("Building Story").AsInteger() == 1 : true
                    });
                }
            }
            catch { }

            // 5. Line Patterns
            try
            {
                var patterns = new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>();
                foreach (var lp in patterns)
                {
                    var patternSnap = new LinePatternSnapshot { Name = lp.Name };
                    var lpDef = lp.GetLinePattern();
                    if (lpDef != null)
                    {
                        var segments = lpDef.GetSegments();
                        if (segments != null)
                        {
                            foreach (var seg in segments)
                            {
                                patternSnap.Segments.Add(new LinePatternSegmentSnapshot
                                {
                                    SegmentType = seg.Type.ToString(),
                                    Length = seg.Length
                                });
                            }
                        }
                    }
                    snapshot.LinePatterns.Add(patternSnap);
                }
            }
            catch { }

            // 6. Line Styles (Subcategories of Lines)
            try
            {
                var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (linesCategory != null && linesCategory.SubCategories != null)
                {
                    foreach (Category subCat in linesCategory.SubCategories)
                    {
                        var styleSnap = new LineStyleSnapshot
                        {
                            Name = subCat.Name,
                            LineWeight = subCat.GetLineWeight(GraphicsStyleType.Projection) ?? 1,
                            ColorRgb = string.Format("{0},{1},{2}", subCat.LineColor.Red, subCat.LineColor.Green, subCat.LineColor.Blue)
                        };

                        var patId = subCat.GetLinePatternId(GraphicsStyleType.Projection);
                        if (patId != ElementId.InvalidElementId)
                        {
                            var patElem = doc.GetElement(patId) as LinePatternElement;
                            if (patElem != null)
                            {
                                styleSnap.LinePatternName = patElem.Name;
                            }
                        }

                        snapshot.LineStyles.Add(styleSnap);
                    }
                }
            }
            catch { }

            // 7. View Filters
            try
            {
                var filters = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>();
                foreach (var f in filters)
                {
                    var filterSnap = new ViewFilterSnapshot { Name = f.Name };
                    var cats = f.GetCategories();
                    if (cats != null)
                    {
                        foreach (var catId in cats)
                        {
                            var c = Category.GetCategory(doc, catId);
                            if (c != null) filterSnap.TargetCategories.Add(c.Name);
                        }
                    }

                    var rules = f.GetElementFilter();
                    if (rules != null)
                    {
                        filterSnap.Rules.Add(new FilterRuleSnapshot
                        {
                            ParameterName = "RuleFilter",
                            ParameterStableKey = rules.GetType().Name,
                            RuleEvaluator = "Evaluated",
                            RuleValue = "Active"
                        });
                    }

                    snapshot.ViewFilters.Add(filterSnap);
                }
            }
            catch { }

            // 8. View Templates
            try
            {
                var templates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate);
                foreach (var vt in templates)
                {
                    var vtSnap = new ViewTemplateSnapshot
                    {
                        Name = vt.Name,
                        ViewType = vt.ViewType.ToString(),
                        Scale = vt.Scale,
                        Discipline = vt.Discipline.ToString(),
                        DetailLevel = vt.DetailLevel.ToString()
                    };

                    var filters = vt.GetFilters();
                    if (filters != null)
                    {
                        foreach (var filterId in filters)
                        {
                            var filterElem = doc.GetElement(filterId) as ParameterFilterElement;
                            if (filterElem != null)
                            {
                                var overrides = vt.GetFilterOverrides(filterId);
                                vtSnap.FilterOverrides.Add(new TemplateFilterOverrideSnapshot
                                {
                                    FilterName = filterElem.Name,
                                    FilterStableKey = string.Format("ViewFilter::{0}", filterElem.Name),
                                    IsVisible = vt.GetFilterVisibility(filterId),
                                    Halftone = overrides != null ? overrides.Halftone : false,
                                    ProjectionLineWeight = overrides != null ? overrides.ProjectionLineWeight : 1
                                });
                            }
                        }
                    }

                    snapshot.ViewTemplates.Add(vtSnap);
                }
            }
            catch { }

            // 9. Schedules
            try
            {
                var schedules = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                    .Where(s => !s.IsTemplate && !s.IsInternalKeynoteSchedule);

                foreach (var s in schedules)
                {
                    var schedSnap = new ScheduleSnapshot
                    {
                        Name = s.Name,
                        ScheduleType = "ViewSchedule"
                    };

                    var def = s.Definition;
                    if (def != null)
                    {
                        var cat = Category.GetCategory(doc, def.CategoryId);
                        if (cat != null) schedSnap.Category = cat.Name;

                        schedSnap.ItemizeEveryInstance = def.IsItemized;
                        schedSnap.ShowGrandTotal = def.ShowGrandTotal;
                        schedSnap.GrandTotalTitle = def.GrandTotalTitle;

                        int fieldCount = def.GetFieldCount();
                        for (int i = 0; i < fieldCount; i++)
                        {
                            var field = def.GetField(i);
                            if (field != null)
                            {
                                schedSnap.Fields.Add(new ScheduleFieldSnapshot
                                {
                                    Heading = field.ColumnHeading,
                                    IsHidden = field.IsHidden,
                                    IsCalculated = field.IsCalculatedField,
                                    ColumnAlignment = field.HorizontalAlignment.ToString()
                                });
                            }
                        }
                    }

                    snapshot.Schedules.Add(schedSnap);
                }
            }
            catch { }

            // 10. Text Types
            try
            {
                var textTypes = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>();
                foreach (var tt in textTypes)
                {
                    snapshot.TextTypes.Add(new TextTypeSnapshot
                    {
                        Name = tt.Name,
                        FontName = tt.get_Parameter(BuiltInParameter.TEXT_FONT) != null ? tt.get_Parameter(BuiltInParameter.TEXT_FONT).AsString() : "Arial",
                        TextSize = tt.get_Parameter(BuiltInParameter.TEXT_SIZE) != null ? tt.get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() : 0.0025
                    });
                }
            }
            catch { }

            // 11. Dimension Types
            try
            {
                var dimTypes = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>();
                foreach (var dt in dimTypes)
                {
                    snapshot.DimensionTypes.Add(new DimensionTypeSnapshot
                    {
                        Name = dt.Name,
                        DimensionTypeStyle = dt.StyleType.ToString()
                    });
                }
            }
            catch { }

            // 12. Shared Coordinates
            try
            {
                var projLoc = doc.ActiveProjectLocation;
                if (projLoc != null)
                {
                    var siteLoc = projLoc.GetSiteLocation();
                    var projPos = projLoc.GetProjectPosition(XYZ.Zero);

                    snapshot.Coordinates = new SharedCoordinateSnapshot
                    {
                        ActiveSiteLocationName = projLoc.Name,
                        OriginX = projPos.EastWest,
                        OriginY = projPos.NorthSouth,
                        OriginZ = projPos.Elevation,
                        TrueNorthAngleRadians = projPos.Angle,
                        Latitude = siteLoc != null ? siteLoc.Latitude : 0.0,
                        Longitude = siteLoc != null ? siteLoc.Longitude : 0.0
                    };
                }
            }
            catch { }

            // 13. Loadable Family Types & Annotation Symbols (Doors, Windows, Columns, Framing, Furniture, Equipment, Generic Models, Annotation Symbols, Detail Items, Tags, etc.)
            try
            {
                var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>();
                foreach (var sym in symbols)
                {
                    if (sym.Family == null) continue;
                    string categoryPrefix = "族群元件";
                    string catName = "一般族群";

                    if (sym.Category != null)
                    {
                        catName = sym.Category.Name;
                        if (sym.Category.Id == new ElementId(BuiltInCategory.OST_GenericAnnotation) ||
                            sym.Category.CategoryType == CategoryType.Annotation ||
                            sym.Category.Id == new ElementId(BuiltInCategory.OST_DetailComponents) ||
                            catName.Contains("註解") || catName.Contains("符號") || catName.Contains("標記") || catName.Contains("標頭"))
                        {
                            categoryPrefix = "符號";
                        }
                    }

                    string categoryDisplayName = categoryPrefix == "符號"
                        ? string.Format("符號: {0}", catName)
                        : string.Format("族群元件: {0}", catName);

                    var snap = new FamilyTypeSnapshot
                    {
                        FamilyName = sym.FamilyName,
                        TypeName = sym.Name,
                        CategoryName = categoryDisplayName
                    };

                    try
                    {
                        foreach (Parameter p in sym.Parameters)
                        {
                            if (p != null && p.Definition != null && p.HasValue && snap.Parameters.Count < 12)
                            {
                                string pName = p.Definition.Name;
                                string valStr = p.AsValueString();
                                if (string.IsNullOrEmpty(valStr)) valStr = p.AsString();
                                if (string.IsNullOrEmpty(valStr) && p.StorageType == StorageType.Double) valStr = p.AsDouble().ToString("F3");
                                if (string.IsNullOrEmpty(valStr) && p.StorageType == StorageType.Integer) valStr = p.AsInteger().ToString();

                                if (!string.IsNullOrEmpty(valStr))
                                {
                                    snap.Parameters.Add(new FamilyTypeParameterSnapshot
                                    {
                                        Name = pName,
                                        ParameterStableKey = pName,
                                        Value = valStr
                                    });
                                }
                            }
                        }
                    }
                    catch { }

                    snapshot.FamilyTypes.Add(snap);
                }
            }
            catch { }

            // 13.1 Generic Annotation Symbols (符號: 一般註解)
            try
            {
                var genericAnnotationTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                    .WhereElementIsElementType()
                    .Cast<ElementType>();

                foreach (var gt in genericAnnotationTypes)
                {
                    if (snapshot.FamilyTypes.Any(f => f.TypeName == gt.Name && f.CategoryName.StartsWith("符號")))
                        continue;

                    var snap = new FamilyTypeSnapshot
                    {
                        FamilyName = gt.FamilyName ?? "一般註解",
                        TypeName = gt.Name,
                        CategoryName = "符號: 一般註解 (Generic Annotations)"
                    };
                    snapshot.FamilyTypes.Add(snap);
                }
            }
            catch { }

            // 14. System Family Types (Wall, Floor, Ceiling, Roof, Railing)
            try
            {
                var wallTypes = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>();
                foreach (var wt in wallTypes)
                {
                    snapshot.FamilyTypes.Add(new FamilyTypeSnapshot
                    {
                        FamilyName = wt.Kind.ToString(),
                        TypeName = wt.Name,
                        CategoryName = "系統類型: 牆 (Wall Types)"
                    });
                }
            }
            catch { }

            try
            {
                var floorTypes = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>();
                foreach (var ft in floorTypes)
                {
                    snapshot.FamilyTypes.Add(new FamilyTypeSnapshot
                    {
                        FamilyName = "樓板",
                        TypeName = ft.Name,
                        CategoryName = "系統類型: 樓板 (Floor Types)"
                    });
                }
            }
            catch { }

            try
            {
                var ceilingTypes = new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<CeilingType>();
                foreach (var ct in ceilingTypes)
                {
                    snapshot.FamilyTypes.Add(new FamilyTypeSnapshot
                    {
                        FamilyName = "天花板",
                        TypeName = ct.Name,
                        CategoryName = "系統類型: 天花板 (Ceiling Types)"
                    });
                }
            }
            catch { }

            try
            {
                var roofTypes = new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>();
                foreach (var rt in roofTypes)
                {
                    snapshot.FamilyTypes.Add(new FamilyTypeSnapshot
                    {
                        FamilyName = "屋頂",
                        TypeName = rt.Name,
                        CategoryName = "系統類型: 屋頂 (Roof Types)"
                    });
                }
            }
            catch { }

            try
            {
                var railingTypes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Railings).WhereElementIsElementType().Cast<ElementType>();
                foreach (var rt in railingTypes)
                {
                    snapshot.FamilyTypes.Add(new FamilyTypeSnapshot
                    {
                        FamilyName = "欄杆",
                        TypeName = rt.Name,
                        CategoryName = "系統類型: 欄杆 (Railing Types)"
                    });
                }
            }
            catch { }

            // 15. Legend Views (圖例視圖)
            try
            {
                var legends = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate);

                foreach (var leg in legends)
                {
                    var legSnap = new LegendSnapshot
                    {
                        Name = leg.Name,
                        Scale = leg.Scale
                    };

                    try
                    {
                        var elemCount = new FilteredElementCollector(doc, leg.Id).WhereElementIsNotElementType().GetElementCount();
                        legSnap.ElementCount = elemCount;
                    }
                    catch { }

                    snapshot.Legends.Add(legSnap);
                }
            }
            catch { }

            // 16. Sheets (圖紙與視圖埠)
            try
            {
                var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(s => !s.IsTemplate);

                foreach (var sh in sheets)
                {
                    var sheetSnap = new SheetSnapshot
                    {
                        SheetNumber = sh.SheetNumber,
                        SheetName = sh.Name
                    };

                    // Titleblock
                    try
                    {
                        var tb = new FilteredElementCollector(doc, sh.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).FirstOrDefault();
                        if (tb != null)
                        {
                            var tbType = doc.GetElement(tb.GetTypeId()) as FamilySymbol;
                            if (tbType != null)
                            {
                                sheetSnap.TitleBlockFamilyName = tbType.FamilyName;
                                sheetSnap.TitleBlockTypeName = tbType.Name;
                            }
                        }
                    }
                    catch { }

                    // Viewports
                    try
                    {
                        var vpIds = sh.GetAllViewports();
                        if (vpIds != null)
                        {
                            foreach (var vpId in vpIds)
                            {
                                var vp = doc.GetElement(vpId) as Viewport;
                                if (vp != null)
                                {
                                    var v = doc.GetElement(vp.ViewId) as View;
                                    if (v != null)
                                    {
                                        var center = vp.GetBoxCenter();
                                        sheetSnap.Viewports.Add(new PlacedViewportSnapshot
                                        {
                                            ViewName = v.Name,
                                            ViewType = v.ViewType.ToString(),
                                            CenterX = center.X,
                                            CenterY = center.Y,
                                            CenterZ = center.Z
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    // Placed Schedules
                    try
                    {
                        var schedInstances = new FilteredElementCollector(doc, sh.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>();
                        foreach (var ssi in schedInstances)
                        {
                            var sched = doc.GetElement(ssi.ScheduleId) as ViewSchedule;
                            if (sched != null)
                            {
                                var pt = ssi.Point;
                                sheetSnap.PlacedSchedules.Add(new PlacedScheduleSnapshot
                                {
                                    ScheduleName = sched.Name,
                                    PointX = pt.X,
                                    PointY = pt.Y
                                });
                            }
                        }
                    }
                    catch { }

                    snapshot.Sheets.Add(sheetSnap);
                }
            }
            catch { }

            // 17. Browser Organization (專案瀏覽器組織)
            try
            {
                var curViewOrg = BrowserOrganization.GetCurrentBrowserOrganizationForViews(doc);
                var curSheetOrg = BrowserOrganization.GetCurrentBrowserOrganizationForSheets(doc);
                var curSchedOrg = BrowserOrganization.GetCurrentBrowserOrganizationForSchedules(doc);

                var browserOrgs = new FilteredElementCollector(doc).OfClass(typeof(BrowserOrganization)).Cast<BrowserOrganization>();
                foreach (var bo in browserOrgs)
                {
                    bool isCurrent = (curViewOrg != null && curViewOrg.Id == bo.Id) ||
                                     (curSheetOrg != null && curSheetOrg.Id == bo.Id) ||
                                     (curSchedOrg != null && curSchedOrg.Id == bo.Id);

                    string orgType = "Views";
                    if (curSheetOrg != null && curSheetOrg.Id == bo.Id) orgType = "Sheets";
                    else if (curSchedOrg != null && curSchedOrg.Id == bo.Id) orgType = "Schedules";
                    else if (curViewOrg != null && curViewOrg.Id == bo.Id) orgType = "Views";

                    var boSnap = new BrowserOrganizationSnapshot
                    {
                        Name = bo.Name,
                        OrganizationType = orgType,
                        IsCurrentActive = isCurrent
                    };

                    try
                    {
                        if (bo.SortingParameterId != ElementId.InvalidElementId)
                        {
                            var paramElem = doc.GetElement(bo.SortingParameterId);
                            if (paramElem != null)
                            {
                                boSnap.FolderGroupingParameters.Add(paramElem.Name);
                            }
                            else
                            {
                                boSnap.FolderGroupingParameters.Add(bo.SortingParameterId.ToString());
                            }
                        }
                    }
                    catch { }

                    snapshot.BrowserOrganizations.Add(boSnap);
                }
            }
            catch { }

            return snapshot;
        }
    }
}
