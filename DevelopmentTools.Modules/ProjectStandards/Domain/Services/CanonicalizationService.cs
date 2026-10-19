using System;
using System.Collections.Generic;
using System.Linq;
using DevelopmentTools.ProjectStandards.Domain.Snapshots;
using DevelopmentTools.ProjectStandards.Infrastructure.Json;

namespace DevelopmentTools.ProjectStandards.Domain.Services
{
    public class CanonicalizationService : ICanonicalizationService
    {
        public double NormalizeDouble(double value)
        {
            return Math.Round(value, 6);
        }

        public string NormalizeColor(string colorRgb)
        {
            if (string.IsNullOrWhiteSpace(colorRgb)) return "0,0,0";
            var parts = colorRgb.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int r, g, b;
            if (parts.Length >= 3 &&
                int.TryParse(parts[0], out r) &&
                int.TryParse(parts[1], out g) &&
                int.TryParse(parts[2], out b))
            {
                return string.Format("{0},{1},{2}", r, g, b);
            }
            return colorRgb.Trim();
        }

        public string ToCanonicalJson<T>(T obj)
        {
            return SimpleJson.SerializeCanonical(obj);
        }

        public ProjectStandardsSnapshot Canonicalize(ProjectStandardsSnapshot snapshot)
        {
            if (snapshot == null) return null;

            var canonical = new ProjectStandardsSnapshot();
            canonical.Metadata = snapshot.Metadata;

            if (snapshot.Worksets != null)
            {
                canonical.Worksets = snapshot.Worksets
                    .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(w => new WorksetSnapshot
                    {
                        Name = w.Name != null ? w.Name.Trim() : null,
                        UniqueGuid = w.UniqueGuid,
                        IsVisibleByDefault = w.IsVisibleByDefault,
                        IsOpenByDefault = w.IsOpenByDefault,
                        Kind = w.Kind
                    }).ToList();
            }

            if (snapshot.Parameters != null)
            {
                canonical.Parameters = snapshot.Parameters
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.SharedParameterGuid.HasValue ? p.SharedParameterGuid.Value.ToString() : string.Empty)
                    .Select(p => new ProjectParameterSnapshot
                    {
                        Name = p.Name != null ? p.Name.Trim() : null,
                        IsShared = p.IsShared,
                        SharedParameterGuid = p.SharedParameterGuid,
                        DataType = p.DataType != null ? p.DataType.Trim() : null,
                        ParameterGroup = p.ParameterGroup != null ? p.ParameterGroup.Trim() : null,
                        IsInstance = p.IsInstance,
                        UserModifiable = p.UserModifiable,
                        BoundCategories = p.BoundCategories != null
                            ? p.BoundCategories.Select(c => c.Trim()).OrderBy(c => c).ToList()
                            : new List<string>(),
                        VariesAcrossGroups = p.VariesAcrossGroups
                    }).ToList();
            }

            if (snapshot.Levels != null)
            {
                canonical.Levels = snapshot.Levels
                    .OrderBy(l => NormalizeDouble(l.Elevation))
                    .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(l => new LevelSnapshot
                    {
                        Name = l.Name != null ? l.Name.Trim() : null,
                        Elevation = NormalizeDouble(l.Elevation),
                        UseStory = l.UseStory,
                        StoryAboveElevation = NormalizeDouble(l.StoryAboveElevation)
                    }).ToList();
            }

            if (snapshot.LinePatterns != null)
            {
                canonical.LinePatterns = snapshot.LinePatterns
                    .OrderBy(lp => lp.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(lp => new LinePatternSnapshot
                    {
                        Name = lp.Name != null ? lp.Name.Trim() : null,
                        Segments = lp.Segments != null
                            ? lp.Segments.Select(s => new LinePatternSegmentSnapshot
                            {
                                SegmentType = s.SegmentType != null ? s.SegmentType.Trim() : null,
                                Length = NormalizeDouble(s.Length)
                            }).ToList()
                            : new List<LinePatternSegmentSnapshot>()
                    }).ToList();
            }

            if (snapshot.LineStyles != null)
            {
                canonical.LineStyles = snapshot.LineStyles
                    .OrderBy(ls => ls.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(ls => new LineStyleSnapshot
                    {
                        Name = ls.Name != null ? ls.Name.Trim() : null,
                        LineWeight = ls.LineWeight,
                        ColorRgb = NormalizeColor(ls.ColorRgb),
                        LinePatternName = ls.LinePatternName != null ? ls.LinePatternName.Trim() : null
                    }).ToList();
            }

            if (snapshot.FillPatterns != null)
            {
                canonical.FillPatterns = snapshot.FillPatterns
                    .OrderBy(fp => fp.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(fp => new FillPatternSnapshot
                    {
                        Name = fp.Name != null ? fp.Name.Trim() : null,
                        TargetType = fp.TargetType != null ? fp.TargetType.Trim() : null,
                        Angle = NormalizeDouble(fp.Angle),
                        Spacing1 = NormalizeDouble(fp.Spacing1),
                        Spacing2 = NormalizeDouble(fp.Spacing2)
                    }).ToList();
            }

            if (snapshot.ObjectStyles != null)
            {
                canonical.ObjectStyles = snapshot.ObjectStyles
                    .OrderBy(os => os.BuiltInCategory ?? os.CategoryName, StringComparer.OrdinalIgnoreCase)
                    .Select(os => new ObjectStyleSnapshot
                    {
                        CategoryName = os.CategoryName != null ? os.CategoryName.Trim() : null,
                        BuiltInCategory = os.BuiltInCategory != null ? os.BuiltInCategory.Trim() : null,
                        ProjectionLineWeight = os.ProjectionLineWeight,
                        CutLineWeight = os.CutLineWeight,
                        LineColorRgb = NormalizeColor(os.LineColorRgb),
                        LinePatternName = os.LinePatternName != null ? os.LinePatternName.Trim() : null,
                        MaterialName = os.MaterialName != null ? os.MaterialName.Trim() : null
                    }).ToList();
            }

            if (snapshot.ViewFilters != null)
            {
                canonical.ViewFilters = snapshot.ViewFilters
                    .OrderBy(vf => vf.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(vf => new ViewFilterSnapshot
                    {
                        Name = vf.Name != null ? vf.Name.Trim() : null,
                        TargetCategories = vf.TargetCategories != null
                            ? vf.TargetCategories.Select(c => c.Trim()).OrderBy(c => c).ToList()
                            : new List<string>(),
                        Rules = vf.Rules != null
                            ? vf.Rules.OrderBy(r => r.ParameterStableKey ?? r.ParameterName).Select(r => new FilterRuleSnapshot
                            {
                                ParameterName = r.ParameterName != null ? r.ParameterName.Trim() : null,
                                ParameterStableKey = r.ParameterStableKey != null ? r.ParameterStableKey.Trim() : null,
                                RuleEvaluator = r.RuleEvaluator != null ? r.RuleEvaluator.Trim() : null,
                                RuleValue = r.RuleValue != null ? r.RuleValue.Trim() : null,
                                Tolerance = r.Tolerance.HasValue ? (double?)NormalizeDouble(r.Tolerance.Value) : null
                            }).ToList()
                            : new List<FilterRuleSnapshot>()
                    }).ToList();
            }

            if (snapshot.ViewTemplates != null)
            {
                canonical.ViewTemplates = snapshot.ViewTemplates
                    .OrderBy(vt => vt.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(vt => new ViewTemplateSnapshot
                    {
                        Name = vt.Name != null ? vt.Name.Trim() : null,
                        ViewType = vt.ViewType != null ? vt.ViewType.Trim() : null,
                        Scale = vt.Scale,
                        Discipline = vt.Discipline != null ? vt.Discipline.Trim() : null,
                        DetailLevel = vt.DetailLevel != null ? vt.DetailLevel.Trim() : null,
                        Phase = vt.Phase != null ? vt.Phase.Trim() : null,
                        PhaseFilter = vt.PhaseFilter != null ? vt.PhaseFilter.Trim() : null,
                        ViewRangeTopLevel = vt.ViewRangeTopLevel != null ? vt.ViewRangeTopLevel.Trim() : null,
                        ViewRangeTopOffset = NormalizeDouble(vt.ViewRangeTopOffset),
                        ViewRangeCutLevel = vt.ViewRangeCutLevel != null ? vt.ViewRangeCutLevel.Trim() : null,
                        ViewRangeCutOffset = NormalizeDouble(vt.ViewRangeCutOffset),
                        ViewRangeBottomLevel = vt.ViewRangeBottomLevel != null ? vt.ViewRangeBottomLevel.Trim() : null,
                        ViewRangeBottomOffset = NormalizeDouble(vt.ViewRangeBottomOffset),
                        ControlledParameterNames = vt.ControlledParameterNames != null
                            ? vt.ControlledParameterNames.Select(cp => cp.Trim()).OrderBy(cp => cp).ToList()
                            : new List<string>(),
                        CategoryOverrides = vt.CategoryOverrides != null
                            ? vt.CategoryOverrides.OrderBy(co => co.BuiltInCategory ?? co.CategoryName).Select(co => new CategoryOverrideSnapshot
                            {
                                CategoryName = co.CategoryName != null ? co.CategoryName.Trim() : null,
                                BuiltInCategory = co.BuiltInCategory != null ? co.BuiltInCategory.Trim() : null,
                                IsVisible = co.IsVisible,
                                Halftone = co.Halftone,
                                ProjectionLineWeight = co.ProjectionLineWeight,
                                ProjectionLineColorRgb = NormalizeColor(co.ProjectionLineColorRgb),
                                CutLineWeight = co.CutLineWeight,
                                CutLineColorRgb = NormalizeColor(co.CutLineColorRgb)
                            }).ToList()
                            : new List<CategoryOverrideSnapshot>(),
                        FilterOverrides = vt.FilterOverrides != null
                            ? vt.FilterOverrides.OrderBy(fo => fo.FilterName).Select(fo => new TemplateFilterOverrideSnapshot
                            {
                                FilterName = fo.FilterName != null ? fo.FilterName.Trim() : null,
                                FilterStableKey = fo.FilterStableKey != null ? fo.FilterStableKey.Trim() : null,
                                IsVisible = fo.IsVisible,
                                Halftone = fo.Halftone,
                                ProjectionLineWeight = fo.ProjectionLineWeight,
                                ProjectionLineColorRgb = NormalizeColor(fo.ProjectionLineColorRgb),
                                ProjectionLinePatternName = fo.ProjectionLinePatternName != null ? fo.ProjectionLinePatternName.Trim() : null,
                                CutLineWeight = fo.CutLineWeight,
                                CutLineColorRgb = NormalizeColor(fo.CutLineColorRgb),
                                CutLinePatternName = fo.CutLinePatternName != null ? fo.CutLinePatternName.Trim() : null,
                                SurfacePatternName = fo.SurfacePatternName != null ? fo.SurfacePatternName.Trim() : null,
                                CutPatternName = fo.CutPatternName != null ? fo.CutPatternName.Trim() : null
                            }).ToList()
                            : new List<TemplateFilterOverrideSnapshot>()
                    }).ToList();
            }

            if (snapshot.Schedules != null)
            {
                canonical.Schedules = snapshot.Schedules
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(s => new ScheduleSnapshot
                    {
                        Name = s.Name != null ? s.Name.Trim() : null,
                        Category = s.Category != null ? s.Category.Trim() : null,
                        ScheduleType = s.ScheduleType != null ? s.ScheduleType.Trim() : null,
                        ItemizeEveryInstance = s.ItemizeEveryInstance,
                        ShowGrandTotal = s.ShowGrandTotal,
                        GrandTotalTitle = s.GrandTotalTitle != null ? s.GrandTotalTitle.Trim() : null,
                        Fields = s.Fields != null
                            ? s.Fields.Select(f => new ScheduleFieldSnapshot
                            {
                                ParameterName = f.ParameterName != null ? f.ParameterName.Trim() : null,
                                ParameterStableKey = f.ParameterStableKey != null ? f.ParameterStableKey.Trim() : null,
                                Heading = f.Heading != null ? f.Heading.Trim() : null,
                                IsHidden = f.IsHidden,
                                IsCalculated = f.IsCalculated,
                                ColumnAlignment = f.ColumnAlignment != null ? f.ColumnAlignment.Trim() : null,
                                DisplayUnitType = f.DisplayUnitType != null ? f.DisplayUnitType.Trim() : null
                            }).ToList()
                            : new List<ScheduleFieldSnapshot>(),
                        SortGroupings = s.SortGroupings != null
                            ? s.SortGroupings.Select(sg => new ScheduleSortGroupSnapshot
                            {
                                FieldHeading = sg.FieldHeading != null ? sg.FieldHeading.Trim() : null,
                                SortDescending = sg.SortDescending,
                                ShowHeader = sg.ShowHeader,
                                ShowFooter = sg.ShowFooter,
                                BlankLine = sg.BlankLine
                            }).ToList()
                            : new List<ScheduleSortGroupSnapshot>(),
                        Filters = s.Filters != null
                            ? s.Filters.Select(sf => new ScheduleFilterSnapshot
                            {
                                FieldHeading = sf.FieldHeading != null ? sf.FieldHeading.Trim() : null,
                                FilterType = sf.FilterType != null ? sf.FilterType.Trim() : null,
                                Value = sf.Value != null ? sf.Value.Trim() : null
                            }).ToList()
                            : new List<ScheduleFilterSnapshot>()
                    }).ToList();
            }

            if (snapshot.TextTypes != null)
            {
                canonical.TextTypes = snapshot.TextTypes
                    .OrderBy(tt => tt.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(tt => new TextTypeSnapshot
                    {
                        Name = tt.Name != null ? tt.Name.Trim() : null,
                        FontName = tt.FontName != null ? tt.FontName.Trim() : null,
                        TextSize = NormalizeDouble(tt.TextSize),
                        TabSize = NormalizeDouble(tt.TabSize),
                        Bold = tt.Bold,
                        Italic = tt.Italic,
                        Underline = tt.Underline,
                        WidthFactor = NormalizeDouble(tt.WidthFactor),
                        ColorRgb = NormalizeColor(tt.ColorRgb),
                        Background = tt.Background != null ? tt.Background.Trim() : null
                    }).ToList();
            }

            if (snapshot.DimensionTypes != null)
            {
                canonical.DimensionTypes = snapshot.DimensionTypes
                    .OrderBy(dt => dt.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(dt => new DimensionTypeSnapshot
                    {
                        Name = dt.Name != null ? dt.Name.Trim() : null,
                        DimensionTypeStyle = dt.DimensionTypeStyle != null ? dt.DimensionTypeStyle.Trim() : null,
                        FontName = dt.FontName != null ? dt.FontName.Trim() : null,
                        TextSize = NormalizeDouble(dt.TextSize),
                        TextColorRgb = NormalizeColor(dt.TextColorRgb),
                        LineColorRgb = NormalizeColor(dt.LineColorRgb),
                        LineWeight = dt.LineWeight,
                        TickMarkName = dt.TickMarkName != null ? dt.TickMarkName.Trim() : null,
                        TickMarkSize = NormalizeDouble(dt.TickMarkSize),
                        WitnessLineGap = NormalizeDouble(dt.WitnessLineGap),
                        WitnessLineExtension = NormalizeDouble(dt.WitnessLineExtension),
                        UnitsFormat = dt.UnitsFormat != null ? dt.UnitsFormat.Trim() : null
                    }).ToList();
            }

            if (snapshot.Coordinates != null)
            {
                canonical.Coordinates = new SharedCoordinateSnapshot
                {
                    ActiveSiteLocationName = snapshot.Coordinates.ActiveSiteLocationName != null ? snapshot.Coordinates.ActiveSiteLocationName.Trim() : null,
                    OriginX = NormalizeDouble(snapshot.Coordinates.OriginX),
                    OriginY = NormalizeDouble(snapshot.Coordinates.OriginY),
                    OriginZ = NormalizeDouble(snapshot.Coordinates.OriginZ),
                    TrueNorthAngleRadians = NormalizeDouble(snapshot.Coordinates.TrueNorthAngleRadians),
                    Latitude = NormalizeDouble(snapshot.Coordinates.Latitude),
                    Longitude = NormalizeDouble(snapshot.Coordinates.Longitude)
                };
            }

            if (snapshot.FamilyTypes != null)
            {
                canonical.FamilyTypes = snapshot.FamilyTypes
                    .OrderBy(f => f.CategoryName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.FamilyName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.TypeName, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new FamilyTypeSnapshot
                    {
                        CategoryName = f.CategoryName != null ? f.CategoryName.Trim() : null,
                        FamilyName = f.FamilyName != null ? f.FamilyName.Trim() : null,
                        TypeName = f.TypeName != null ? f.TypeName.Trim() : null,
                        Parameters = f.Parameters != null
                            ? f.Parameters.OrderBy(p => p.Name).Select(p => new FamilyTypeParameterSnapshot
                            {
                                Name = p.Name != null ? p.Name.Trim() : null,
                                ParameterStableKey = p.ParameterStableKey != null ? p.ParameterStableKey.Trim() : null,
                                Value = p.Value != null ? p.Value.Trim() : null
                            }).ToList()
                            : new List<FamilyTypeParameterSnapshot>()
                    }).ToList();
            }

            if (snapshot.Legends != null)
            {
                canonical.Legends = snapshot.Legends
                    .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(l => new LegendSnapshot
                    {
                        Name = l.Name != null ? l.Name.Trim() : null,
                        Scale = l.Scale,
                        ElementCount = l.ElementCount,
                        ContainedComponentNames = l.ContainedComponentNames != null
                            ? l.ContainedComponentNames.Select(c => c.Trim()).OrderBy(c => c).ToList()
                            : new List<string>()
                    }).ToList();
            }

            if (snapshot.Sheets != null)
            {
                canonical.Sheets = snapshot.Sheets
                    .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                    .Select(s => new SheetSnapshot
                    {
                        SheetNumber = s.SheetNumber != null ? s.SheetNumber.Trim() : null,
                        SheetName = s.SheetName != null ? s.SheetName.Trim() : null,
                        TitleBlockFamilyName = s.TitleBlockFamilyName != null ? s.TitleBlockFamilyName.Trim() : null,
                        TitleBlockTypeName = s.TitleBlockTypeName != null ? s.TitleBlockTypeName.Trim() : null,
                        Viewports = s.Viewports != null
                            ? s.Viewports.OrderBy(vp => vp.ViewName).Select(vp => new PlacedViewportSnapshot
                            {
                                ViewName = vp.ViewName != null ? vp.ViewName.Trim() : null,
                                ViewType = vp.ViewType != null ? vp.ViewType.Trim() : null,
                                CenterX = NormalizeDouble(vp.CenterX),
                                CenterY = NormalizeDouble(vp.CenterY),
                                CenterZ = NormalizeDouble(vp.CenterZ)
                            }).ToList()
                            : new List<PlacedViewportSnapshot>(),
                        PlacedSchedules = s.PlacedSchedules != null
                            ? s.PlacedSchedules.OrderBy(ps => ps.ScheduleName).Select(ps => new PlacedScheduleSnapshot
                            {
                                ScheduleName = ps.ScheduleName != null ? ps.ScheduleName.Trim() : null,
                                PointX = NormalizeDouble(ps.PointX),
                                PointY = NormalizeDouble(ps.PointY)
                            }).ToList()
                            : new List<PlacedScheduleSnapshot>()
                    }).ToList();
            }

            if (snapshot.BrowserOrganizations != null)
            {
                canonical.BrowserOrganizations = snapshot.BrowserOrganizations
                    .OrderBy(bo => bo.OrganizationType ?? "Views", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(bo => bo.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(bo => new BrowserOrganizationSnapshot
                    {
                        Name = bo.Name != null ? bo.Name.Trim() : null,
                        OrganizationType = bo.OrganizationType != null ? bo.OrganizationType.Trim() : null,
                        IsCurrentActive = bo.IsCurrentActive,
                        FolderGroupingParameters = bo.FolderGroupingParameters != null
                            ? bo.FolderGroupingParameters.Select(p => p.Trim()).ToList()
                            : new List<string>(),
                        FilterRules = bo.FilterRules != null
                            ? bo.FilterRules.Select(r => r.Trim()).ToList()
                            : new List<string>()
                    }).ToList();
            }

            return canonical;
        }
    }
}
