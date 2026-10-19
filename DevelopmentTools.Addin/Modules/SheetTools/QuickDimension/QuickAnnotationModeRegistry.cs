using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Modules.SheetTools.QuickDimension
{
    public sealed class QuickAnnotationModeDefinition
    {
        public string ModeKey { get; set; }
        public int TaggingMode { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsDimensionMode { get; set; } = true;
        public Func<View, bool> SupportsView { get; set; }
        public Action<QuickDimensionViewModel, UIDocument, View> ManualExecute { get; set; }
        public Action<Document, View, DimensionType, ViewAnnotationConfig> AutomaticApply { get; set; }

        public bool CanRunManually => ManualExecute != null;
        public bool CanRunAutomatically => AutomaticApply != null;
        public bool AppliesTo(View view) => SupportsView == null || SupportsView(view);
    }

    public sealed class QuickAnnotationProfileDefinition
    {
        public string ProfileKey { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Func<View, bool> MatchesView { get; set; }
        public IReadOnlyList<string> RecommendedModeKeys { get; set; } = Array.Empty<string>();
    }

    public static class QuickAnnotationModeRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, QuickAnnotationModeDefinition> ModesByKey =
            new Dictionary<string, QuickAnnotationModeDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> ModeKeyByLegacyId = new Dictionary<int, string>();
        private static readonly List<QuickAnnotationProfileDefinition> Profiles = new List<QuickAnnotationProfileDefinition>();
        private static bool _initialized;

        public static void EnsureInitialized()
        {
            lock (SyncRoot)
            {
                if (_initialized) return;
                _initialized = true;
                RegisterBuiltInModes();
                RegisterBuiltInProfiles();
            }
        }

        public static void RegisterOrReplaceMode(QuickAnnotationModeDefinition mode)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (string.IsNullOrWhiteSpace(mode.ModeKey)) throw new ArgumentException("ModeKey is required.", nameof(mode));

            lock (SyncRoot)
            {
                ModesByKey[mode.ModeKey] = mode;
                ModeKeyByLegacyId[mode.TaggingMode] = mode.ModeKey;
            }
        }

        public static bool UnregisterMode(string modeKey)
        {
            if (string.IsNullOrWhiteSpace(modeKey)) return false;

            lock (SyncRoot)
            {
                if (!ModesByKey.TryGetValue(modeKey, out var mode)) return false;
                ModesByKey.Remove(modeKey);
                if (ModeKeyByLegacyId.TryGetValue(mode.TaggingMode, out var key) &&
                    string.Equals(key, modeKey, StringComparison.OrdinalIgnoreCase))
                {
                    ModeKeyByLegacyId.Remove(mode.TaggingMode);
                }
                return true;
            }
        }

        public static void RegisterOrReplaceProfile(QuickAnnotationProfileDefinition profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrWhiteSpace(profile.ProfileKey)) throw new ArgumentException("ProfileKey is required.", nameof(profile));

            lock (SyncRoot)
            {
                Profiles.RemoveAll(p => string.Equals(p.ProfileKey, profile.ProfileKey, StringComparison.OrdinalIgnoreCase));
                Profiles.Add(profile);
            }
        }

        public static IReadOnlyList<QuickAnnotationProfileDefinition> GetProfiles()
        {
            EnsureInitialized();
            lock (SyncRoot) return Profiles.ToList();
        }

        public static QuickAnnotationProfileDefinition GetProfileForView(View view)
        {
            EnsureInitialized();
            lock (SyncRoot)
            {
                return Profiles.FirstOrDefault(p => p.MatchesView != null && p.MatchesView(view))
                    ?? Profiles.FirstOrDefault(p => string.Equals(p.ProfileKey, "generic", StringComparison.OrdinalIgnoreCase));
            }
        }

        public static IReadOnlyList<QuickAnnotationModeDefinition> GetModesForProfile(QuickAnnotationProfileDefinition profile, View view, bool manualOnly)
        {
            EnsureInitialized();
            lock (SyncRoot)
            {
                IEnumerable<QuickAnnotationModeDefinition> modes = ModesByKey.Values.Where(m => m.AppliesTo(view));
                if (manualOnly) modes = modes.Where(m => m.CanRunManually);

                var modeList = modes.ToList();
                var recommendedKeys = profile?.RecommendedModeKeys ?? Array.Empty<string>();
                return modeList
                    .OrderBy(m =>
                    {
                        int index = recommendedKeys
                            .Select((key, i) => new { key, i })
                            .FirstOrDefault(x => string.Equals(x.key, m.ModeKey, StringComparison.OrdinalIgnoreCase))?.i ?? int.MaxValue;
                        return index;
                    })
                    .ThenBy(m => m.TaggingMode)
                    .ToList();
            }
        }

        public static QuickAnnotationModeDefinition GetMode(string modeKey, int legacyTaggingMode)
        {
            EnsureInitialized();
            lock (SyncRoot)
            {
                if (!string.IsNullOrWhiteSpace(modeKey) && ModesByKey.TryGetValue(modeKey, out var byKey))
                {
                    return byKey;
                }

                if (ModeKeyByLegacyId.TryGetValue(legacyTaggingMode, out var mappedKey) &&
                    ModesByKey.TryGetValue(mappedKey, out var byLegacy))
                {
                    return byLegacy;
                }

                return null;
            }
        }

        public static QuickAnnotationModeDefinition GetModeByLegacyId(int legacyTaggingMode)
        {
            return GetMode(null, legacyTaggingMode);
        }

        private static void RegisterBuiltInModes()
        {
            RegisterOrReplaceMode(new QuickAnnotationModeDefinition
            {
                ModeKey = "columns.center",
                TaggingMode = 0,
                Name = "柱中心到中心標註",
                Description = "適合結構平面或建築平面：依柱列方向建立柱中心尺寸。",
                ManualExecute = (vm, uidoc, view) => vm.TagColumns(uidoc, view),
                AutomaticApply = AutoDimensionHelper.AutoTagColumns
            });

            RegisterOrReplaceMode(new QuickAnnotationModeDefinition
            {
                ModeKey = "walls.center",
                TaggingMode = 1,
                Name = "平行牆體中心標註",
                Description = "適合建築平面：選取平行牆體並建立牆中心尺寸。",
                ManualExecute = (vm, uidoc, view) => vm.TagWalls(uidoc, view),
                AutomaticApply = AutoDimensionHelper.AutoTagWalls
            });

            RegisterOrReplaceMode(new QuickAnnotationModeDefinition
            {
                ModeKey = "walls.edges",
                TaggingMode = 2,
                Name = "平行牆邊淨距標註",
                Description = "適合隔間、裝修或放樣圖：標註牆邊到牆邊淨距與厚度。",
                ManualExecute = (vm, uidoc, view) => vm.TagWallEdges(uidoc, view),
                AutomaticApply = AutoDimensionHelper.AutoTagWallEdges
            });

            RegisterOrReplaceMode(new QuickAnnotationModeDefinition
            {
                ModeKey = "walls.openings",
                TaggingMode = 3,
                Name = "牆面門窗開口標註",
                Description = "適合建築平面與立面前置圖：選一道牆，自動抓門窗左右邊界。",
                ManualExecute = (vm, uidoc, view) => vm.TagWallOpenings(uidoc, view),
                AutomaticApply = AutoDimensionHelper.AutoTagWallOpenings
            });

            RegisterOrReplaceMode(new QuickAnnotationModeDefinition
            {
                ModeKey = "beams.edges",
                TaggingMode = 4,
                Name = "梁邊到梁邊標註",
                Description = "適合結構平面：依平行梁群建立梁邊尺寸。",
                ManualExecute = (vm, uidoc, view) => vm.TagBeams(uidoc, view),
                AutomaticApply = AutoDimensionHelper.AutoTagBeams,
                SupportsView = view => view == null || view.ViewType == ViewType.EngineeringPlan || view.ViewType == ViewType.FloorPlan
            });

            RegisterOrReplaceMode(new QuickAnnotationModeDefinition
            {
                ModeKey = "rooms.tags",
                TaggingMode = 100,
                Name = "房間標籤自動放置",
                Description = "適合建築、裝修平面：在尚未放置房間標籤的房間中心補上標籤。",
                IsDimensionMode = false,
                ManualExecute = (vm, uidoc, view) => vm.PlaceRoomTags(uidoc, view),
                AutomaticApply = AutoDimensionHelper.AutoPlaceRoomTags,
                SupportsView = view => view == null || view.ViewType == ViewType.FloorPlan
            });
        }

        private static void RegisterBuiltInProfiles()
        {
            RegisterOrReplaceProfile(new QuickAnnotationProfileDefinition
            {
                ProfileKey = "architectural",
                Name = "建築平面圖",
                Description = "牆中心、牆邊、開口與房間標籤優先。",
                MatchesView = view => MatchesName(view, "建築", "architect", "floor") && !MatchesName(view, "結構", "struct"),
                RecommendedModeKeys = new[] { "walls.center", "walls.edges", "walls.openings", "rooms.tags", "columns.center" }
            });

            RegisterOrReplaceProfile(new QuickAnnotationProfileDefinition
            {
                ProfileKey = "structural",
                Name = "結構平面圖",
                Description = "柱中心與梁邊尺寸優先。",
                MatchesView = view => view != null && (view.ViewType == ViewType.EngineeringPlan || MatchesName(view, "結構", "struct")),
                RecommendedModeKeys = new[] { "columns.center", "beams.edges", "walls.center" }
            });

            RegisterOrReplaceProfile(new QuickAnnotationProfileDefinition
            {
                ProfileKey = "ceiling",
                Name = "天花板平面圖",
                Description = "天花板圖說以牆體與開口定位輔助為主。",
                MatchesView = view => view != null && (view.ViewType == ViewType.CeilingPlan || MatchesName(view, "天花", "ceiling", "rcp")),
                RecommendedModeKeys = new[] { "walls.center", "walls.edges", "walls.openings" }
            });

            RegisterOrReplaceProfile(new QuickAnnotationProfileDefinition
            {
                ProfileKey = "finish",
                Name = "裝修/材料平面圖",
                Description = "牆邊淨距、開口與房間標籤優先。",
                MatchesView = view => MatchesName(view, "裝修", "材料", "finish", "interior"),
                RecommendedModeKeys = new[] { "walls.edges", "walls.openings", "rooms.tags", "walls.center" }
            });

            RegisterOrReplaceProfile(new QuickAnnotationProfileDefinition
            {
                ProfileKey = "generic",
                Name = "通用平面圖",
                Description = "顯示所有可用的快速標註與標籤放置模式。",
                MatchesView = view => true,
                RecommendedModeKeys = new[] { "columns.center", "walls.center", "walls.edges", "walls.openings", "rooms.tags", "beams.edges" }
            });
        }

        private static bool MatchesName(View view, params string[] tokens)
        {
            if (view == null || tokens == null || tokens.Length == 0) return false;
            string text = view.Name ?? string.Empty;
            try
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    View template = view.Document.GetElement(view.ViewTemplateId) as View;
                    if (template != null) text += " " + template.Name;
                }
            }
            catch { }

            return tokens.Any(t => text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}

