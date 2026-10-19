using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.Revit
{
    public class RevitDataService : IRevitDataService
    {
        private sealed class UseDestinationDuplicateTypeHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }

        private sealed class GenerationBatchCache
        {
            public Dictionary<string, ElementId> SynchronizedTemplateIds { get; private set; }
            public Dictionary<string, List<TargetElementCandidate>> LinkedTagCandidates { get; private set; }
            public Dictionary<string, ElementId> ResolvedTagTypeIds { get; private set; }
            public Dictionary<string, ElementId> TargetPrimaryViewIds { get; private set; }
            public GenerationBatchCache()
            {
                SynchronizedTemplateIds = new Dictionary<string, ElementId>(StringComparer.Ordinal);
                LinkedTagCandidates = new Dictionary<string, List<TargetElementCandidate>>(StringComparer.Ordinal);
                ResolvedTagTypeIds = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
                TargetPrimaryViewIds = new Dictionary<string, ElementId>(StringComparer.Ordinal);
            }
        }

        private sealed class BatchWarningPreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                if (failuresAccessor == null) return FailureProcessingResult.Continue;
                foreach (var message in failuresAccessor.GetFailureMessages())
                {
                    try
                    {
                        // Non-fatal Revit warnings can otherwise open a modal
                        // dialog at Commit and make the modeless batch UI appear
                        // frozen. Errors are deliberately retained so the current
                        // sheet rolls back instead of silently corrupting output.
                        if (message.GetSeverity() == FailureSeverity.Warning)
                            failuresAccessor.DeleteWarning(message);
                    }
                    catch { }
                }
                return FailureProcessingResult.Continue;
            }
        }

        private sealed class DimensionFailurePreprocessor : IFailuresPreprocessor
        {
            private readonly IList<string> _warnings;

            public DimensionFailurePreprocessor(IList<string> warnings)
            {
                _warnings = warnings;
            }

            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                if (failuresAccessor == null) return FailureProcessingResult.Continue;
                bool removedInvalidDimensions = false;
                var document = failuresAccessor.GetDocument();
                foreach (var message in failuresAccessor.GetFailureMessages())
                {
                    try
                    {
                        if (message.GetSeverity() == FailureSeverity.Warning)
                        {
                            failuresAccessor.DeleteWarning(message);
                            continue;
                        }

                        // Invalid dimension references are reported only when the
                        // transaction commits. Delete only failing Dimension
                        // elements; never delete their referenced model objects.
                        var dimensionIds = message.GetFailingElementIds()
                            .Where(id => document.GetElement(id) is Dimension).ToList();
                        if (dimensionIds.Count == 0) continue;
                        failuresAccessor.DeleteElements(dimensionIds);
                        removedInvalidDimensions = true;
                        if (_warnings != null)
                            _warnings.Add(string.Format("尺寸參照失效：已自動移除 {0} 條無效尺寸，不需人工按『移除參考』",
                                dimensionIds.Count));
                    }
                    catch { }
                }
                return removedInvalidDimensions
                    ? FailureProcessingResult.ProceedWithCommit
                    : FailureProcessingResult.Continue;
            }
        }

        private static CopyPasteOptions CreateCopyPasteOptions()
        {
            var options = new CopyPasteOptions();
            options.SetDuplicateTypeNamesHandler(new UseDestinationDuplicateTypeHandler());
            return options;
        }

        private readonly UIApplication _uiApp;
        private readonly RevitExternalEventHandler _handler;
        private readonly MockRevitDataService _fallbackMock = new MockRevitDataService();
        private string _cachedSeriesDocumentKey;
        private int _cachedSeriesSheetCount = -1;
        private DateTime _cachedSeriesAt = DateTime.MinValue;
        private List<SheetSeriesDto> _cachedSeries;
        private string _cachedBuildingsDocumentKey;
        private int _cachedBuildingsSheetCount = -1;
        private List<BuildingCardDto> _cachedBuildings;

        public RevitDataService(RevitExternalEventHandler handler)
            : this(null, handler)
        {
        }

        public RevitDataService(UIApplication uiApp, RevitExternalEventHandler handler)
        {
            _uiApp = uiApp;
            _handler = handler;
        }

        public void InvalidateAnalysisCache()
        {
            _cachedSeries = null;
            _cachedSeriesDocumentKey = null;
            _cachedSeriesSheetCount = -1;
            _cachedSeriesAt = DateTime.MinValue;
            _cachedBuildings = null;
            _cachedBuildingsDocumentKey = null;
            _cachedBuildingsSheetCount = -1;
        }

        public async Task<List<string>> GetOpenProjectNamesAsync()
        {
            if (_handler != null && RevitApp.ExternalEvent == null)
                throw new InvalidOperationException("Revit ExternalEvent 初始化失敗，無法讀取真實專案；已停止使用測試資料。請關閉外掛視窗後重新開啟。");
            if (_handler == null)
            {
                return await _fallbackMock.GetOpenProjectNamesAsync();
            }

            return await _handler.RunAsync(app =>
            {
                string activeTitle = app.ActiveUIDocument != null ? app.ActiveUIDocument.Document.Title : null;
                return app.Application.Documents.Cast<Document>()
                    .Where(d => d != null && !d.IsFamilyDocument && !d.IsLinked)
                    .Select(d => d.Title)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(n => string.Equals(n, activeTitle, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(n => n)
                    .ToList();
            });
        }

        public async Task<List<string>> GetOpenSourceProjectNamesAsync()
        {
            if (_handler != null && RevitApp.ExternalEvent == null)
                throw new InvalidOperationException("Revit ExternalEvent 初始化失敗，無法讀取來源專案。");
            if (_handler == null)
                return await _fallbackMock.GetOpenSourceProjectNamesAsync();

            return await _handler.RunAsync(app =>
            {
                string activeTitle = app.ActiveUIDocument != null ? app.ActiveUIDocument.Document.Title : null;
                return app.Application.Documents.Cast<Document>()
                    .Where(d => d != null && !d.IsFamilyDocument)
                    .Select(d => new { Document = d, DisplayName = d.Title + (d.IsLinked ? " [Revit Link]" : string.Empty) })
                    .OrderByDescending(x => !x.Document.IsLinked && string.Equals(x.Document.Title, activeTitle, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(x => x.Document.IsLinked)
                    .ThenBy(x => x.DisplayName)
                    .Select(x => x.DisplayName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });
        }

        public async Task<ProjectProfileDto> GetProjectProfileAsync()
        {
            try
            {
                // 只有沒有 ExternalEvent 的同步命令情境才直接讀取 Document；
                // modeless WPF 一律走下方 ExternalEvent，避免跨 Revit API Context。
                var doc = _handler == null ? GetCurrentDocument() : null;
                if (doc != null)
                {
                    var levelsCount = new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount();
                    var sheetsCount = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
                    var scopeBoxesCount = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType().GetElementCount();

                    string projectName = !string.IsNullOrEmpty(doc.Title) ? doc.Title : "Revit Project";
                    string docPath = !string.IsNullOrEmpty(doc.PathName) ? doc.PathName : projectName;

                    return new ProjectProfileDto
                    {
                        ProjectName = projectName,
                        SourceDocumentName = docPath,
                        StandardDrawingGroup = "標準圖說組 (自動偵測)",
                        StatusSummary = string.Format("已連接專案 ({0} 個樓層 / {1} 張既有圖紙)", levelsCount, sheetsCount),
                        IsSetupComplete = true,
                        TotalRulesCount = Math.Max(12, sheetsCount),
                        BuildingCount = Math.Max(1, scopeBoxesCount),
                        LastAnalysisTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm")
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[GetProjectProfileAsync Warning] {0}", ex.Message));
            }

            if (_handler != null)
            {
                try
                {
                    return await _handler.RunAsync(app =>
                    {
                        var doc = app.ActiveUIDocument != null ? app.ActiveUIDocument.Document : null;
                        if (doc == null) return _fallbackMock.GetProjectProfileAsync().Result;

                        var levelsCount = new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount();
                        var sheetsCount = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
                        var scopeBoxesCount = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType().GetElementCount();

                        string projectName = !string.IsNullOrEmpty(doc.Title) ? doc.Title : "Revit Project";
                        string docPath = !string.IsNullOrEmpty(doc.PathName) ? doc.PathName : projectName;

                        return new ProjectProfileDto
                        {
                            ProjectName = projectName,
                            SourceDocumentName = docPath,
                            StandardDrawingGroup = "標準圖說組 (自動偵測)",
                            StatusSummary = string.Format("已連接專案 ({0} 個樓層 / {1} 張既有圖紙)", levelsCount, sheetsCount),
                            IsSetupComplete = true,
                            TotalRulesCount = Math.Max(12, sheetsCount),
                            BuildingCount = Math.Max(1, scopeBoxesCount),
                            LastAnalysisTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm")
                        };
                    });
                }
                catch
                {
                }
            }

            return await _fallbackMock.GetProjectProfileAsync();
        }

        public async Task<List<BuildingCardDto>> GetBuildingsAsync(string projectName = null)
        {
            if (_handler != null && RevitApp.ExternalEvent == null)
                throw new InvalidOperationException("Revit ExternalEvent 初始化失敗，無法讀取真實樓層資料。");
            if (_handler != null && RevitApp.ExternalEvent != null)
            {
                try
                {
                    var buildings = await _handler.RunAsync(app =>
                    {
                        var doc = FindDocument(app, projectName, true);
                        if (doc == null)
                            throw new InvalidOperationException("找不到指定的目標專案：" + projectName + "。請確認該檔案仍在 Revit 中開啟。");
                        return GetCachedBuildings(doc) ?? new List<BuildingCardDto>();
                    });
                    // A real project with no usable Levels is an empty result, not
                    // permission to show fixed mock buildings from another project.
                    return buildings ?? new List<BuildingCardDto>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("[GetBuildingsAsync Warning] {0}", ex.Message));
                    throw new InvalidOperationException("讀取 Revit 樓層資料失敗：" + ex.Message, ex);
                }
            }

            return await _fallbackMock.GetBuildingsAsync(projectName);
        }

        private List<BuildingCardDto> GetCachedBuildings(Document doc)
        {
            int sheetCount = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
            string key = string.Format("{0}|{1}", doc.Title, doc.GetHashCode());
            if (_cachedBuildings != null && _cachedBuildingsDocumentKey == key && _cachedBuildingsSheetCount == sheetCount)
                return _cachedBuildings;
            _cachedBuildings = ExtractBuildingsFromDoc(doc) ?? new List<BuildingCardDto>();
            _cachedBuildingsDocumentKey = key;
            _cachedBuildingsSheetCount = sheetCount;
            return _cachedBuildings;
        }

        private Document GetCurrentDocument()
        {
            try
            {
                if (_uiApp != null && _uiApp.ActiveUIDocument != null)
                {
                    return _uiApp.ActiveUIDocument.Document;
                }
            }
            catch
            {
            }
            return null;
        }

        private static Document FindDocument(UIApplication app, string projectName, bool useActiveAsFallback, bool allowLinked = false)
        {
            if (app == null) return null;
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                string requested = projectName.Trim();
                bool explicitlyLinked = requested.EndsWith(" [Revit Link]", StringComparison.OrdinalIgnoreCase);
                if (explicitlyLinked)
                    requested = requested.Substring(0, requested.Length - " [Revit Link]".Length).Trim();
                foreach (Document document in app.Application.Documents)
                {
                    if (document == null || document.IsFamilyDocument || (!allowLinked && document.IsLinked)) continue;
                    if (explicitlyLinked && !document.IsLinked) continue;
                    if (string.Equals(document.Title, requested, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(document.PathName) &&
                         string.Equals(System.IO.Path.GetFileNameWithoutExtension(document.PathName), requested, StringComparison.OrdinalIgnoreCase)))
                    {
                        return document;
                    }
                }
            }

            // An explicit A/B selection must never silently resolve to the active
            // document. That made a closed or renamed source look as though it had
            // refreshed while the service was actually reading B again.
            return useActiveAsFallback && string.IsNullOrWhiteSpace(projectName) && app.ActiveUIDocument != null
                ? app.ActiveUIDocument.Document
                : null;
        }

        private List<BuildingCardDto> ExtractBuildingsFromDoc(Document doc)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count == 0) return null;
            FactoryConventions.Current.RegisterLevelNames(levels.Select(l => l.Name));

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            // 尋找範圍框 (Scope Boxes)
            var scopeBoxes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .ToList();

            var buildingNames = new List<string>();
            foreach (var sb in scopeBoxes)
            {
                var name = sb.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    if (name.Contains("棟") || name.Contains("區") || name.Contains("Building") ||
                        FactoryConventions.Current.IsKnownBuildingName(name))
                    {
                        var clean = name.Replace("建築", "").Replace("結構", "").Trim();
                        if (!string.IsNullOrEmpty(clean) && !buildingNames.Contains(clean))
                        {
                            buildingNames.Add(clean);
                        }
                    }
                }
            }

            // 1. 將專案中所有 Level 依據標準樓層代碼 (B4, B3, B2, B1, 1F, 2F... RF) 進行分組整合
            var storyDict = new Dictionary<string, ParsedFloorGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var lvl in levels)
            {
                string floorCode;
                int sortOrder;
                ParseLevelStory(lvl, out floorCode, out sortOrder);

                if (!storyDict.ContainsKey(floorCode))
                {
                    storyDict[floorCode] = new ParsedFloorGroup
                    {
                        FloorCode = floorCode,
                        SortOrder = sortOrder,
                        PrimaryLevel = lvl,
                        PrimaryElevation = lvl.Elevation,
                        AssociatedLevels = new List<Level> { lvl }
                    };
                }
                else
                {
                    var group = storyDict[floorCode];
                    group.AssociatedLevels.Add(lvl);
                    // 挑選具備 IsBuildingStory 或高程較符合主樓層者作為 PrimaryLevel
                    try
                    {
                        var isStoryParam = lvl.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
                        if (isStoryParam != null && isStoryParam.AsInteger() == 1)
                        {
                            group.PrimaryLevel = lvl;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            // 2. 嚴格依照建築常規排序：B4 -> B3 -> B2 -> B1 -> 1F -> 2F -> 3F ... -> RF -> 屋突
            var sortedStories = storyDict.Values
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.PrimaryElevation)
                .ToList();

            // 3. 深入分析現有圖紙 (分析 ViewSheet 的 SheetNumber, Name, PlacedViews 與 GenLevel)
            var allChips = new List<FloorChipDto>();
            bool isFirstGroundFloorFound = false;

            for (int storyIndex = 0; storyIndex < sortedStories.Count; storyIndex++)
            {
                var story = sortedStories[storyIndex];
                var matchedSheets = FindMatchingSheetsForStory(doc, story, sheets);
                int count = matchedSheets.Count;
                bool hasCompletedSheets = count > 0;

                bool isMaster = false;
                if (!isFirstGroundFloorFound && (story.FloorCode == "1F" || story.SortOrder == 1 || (story.SortOrder > 0 && story.PrimaryElevation >= -0.001)))
                {
                    isMaster = true;
                    isFirstGroundFloorFound = true;
                }

                FloorStatus status;
                string statusText;

                if (hasCompletedSheets)
                {
                    status = FloorStatus.Completed;
                    statusText = string.Format("已完成 ({0}張)", count);
                }
                else if (isMaster)
                {
                    status = FloorStatus.Completed;
                    statusText = "標準圖說";
                }
                else
                {
                    status = FloorStatus.NotCreated;
                    statusText = "尚未建立";
                }

                var chip = new FloorChipDto
                {
                    FloorId = story.PrimaryLevel.Id.ToString(),
                    FloorName = story.FloorCode,
                    Status = status,
                    StatusText = statusText,
                    IsStandardMaster = isMaster,
                    CompletedSheetsCount = count,
                    TotalSheetsCount = Math.Max(13, count),
                    IsSelected = !hasCompletedSheets && !isMaster // 已完成的樓層預設不勾選，未建立的自動勾選
                };
                chip.RevitLevelName = story.PrimaryLevel.Name;
                chip.ElevationMm = story.PrimaryElevation * 304.8;
                chip.AssociatedLevelsSummary = string.Join(" / ", story.AssociatedLevels
                    .OrderBy(l => l.Elevation)
                    .Select(l => string.Format("{0} ({1:N0} mm)", l.Name, l.Elevation * 304.8)));
                chip.HeightToNextMm = storyIndex + 1 < sortedStories.Count
                    ? (sortedStories[storyIndex + 1].PrimaryElevation - story.PrimaryElevation) * 304.8
                    : 0;

                allChips.Add(chip);
            }

            var buildings = new List<BuildingCardDto>();

            if (buildingNames.Count > 0)
            {
                for (int i = 0; i < buildingNames.Count; i++)
                {
                    var bName = buildingNames[i];
                    var completedCount = allChips.Count(c => c.Status == FloorStatus.Completed);
                    buildings.Add(new BuildingCardDto
                    {
                        Id = string.Format("B_{0}", i + 1),
                        Name = bName,
                        CompletedFloors = completedCount,
                        TotalFloors = allChips.Count,
                        IsSelected = (i == 0),
                        Floors = allChips
                    });
                }
            }
            else
            {
                // 專案沒有可辨識的範圍框時，依樓層名稱前綴（或 conventions.json 指定的棟別）建立棟別卡片；
                // 都辨識不到就只提供一張「全棟」卡片。
                var conv = FactoryConventions.Current;
                var buildingKeys = conv.GetBuildingKeys();
                int completedCount = allChips.Count(c => c.Status == FloorStatus.Completed);
                if (buildingKeys.Count > 0)
                {
                    for (int i = 0; i < buildingKeys.Count; i++)
                    {
                        buildings.Add(new BuildingCardDto
                        {
                            Id = "B_" + buildingKeys[i],
                            Name = conv.GetDisplayName(buildingKeys[i]) + " (地上層目標建置)",
                            CompletedFloors = 0,
                            TotalFloors = allChips.Count,
                            IsSelected = (i == 0),
                            Floors = allChips
                        });
                    }

                    buildings.Add(new BuildingCardDto
                    {
                        Id = "B_BASEMENT",
                        Name = "地下層 (基準圖說來源)",
                        CompletedFloors = completedCount,
                        TotalFloors = allChips.Count,
                        IsSelected = false,
                        Floors = allChips
                    });
                }
                else
                {
                    buildings.Add(new BuildingCardDto
                    {
                        Id = "B_ALL",
                        Name = "全棟",
                        CompletedFloors = completedCount,
                        TotalFloors = allChips.Count,
                        IsSelected = true,
                        Floors = allChips
                    });
                }
            }

            return buildings;
        }

        public async Task<List<SheetSeriesDto>> GetAvailableSheetSeriesAsync(string projectName = null)
        {
            if (_handler != null && RevitApp.ExternalEvent == null)
                throw new InvalidOperationException("Revit ExternalEvent 初始化失敗，無法讀取真實圖紙系列。");
            if (_handler != null && RevitApp.ExternalEvent != null)
            {
                try
                {
                    var series = await _handler.RunAsync(app =>
                    {
                        var doc = FindDocument(app, projectName, true, true);
                        if (doc == null)
                            throw new InvalidOperationException("找不到指定的來源專案：" + projectName + "。請確認該檔案仍在 Revit 中開啟。");

                        // This is an explicit source scan. Always rebuild it from the
                        // selected document; sheet count alone cannot detect changes
                        // to views, templates, title blocks or viewport placement.
                        _cachedSeries = ExtractAllSeriesFromDoc(doc) ?? new List<SheetSeriesDto>();
                        _cachedSeriesDocumentKey = string.Format("{0}|{1}", doc.Title, doc.GetHashCode());
                        _cachedSeriesSheetCount = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
                        _cachedSeriesAt = DateTime.Now;
                        return _cachedSeries;
                    });
                    // A real, empty document is still a valid result. Never replace
                    // it with fixed mock standards from add-in startup.
                    return series ?? new List<SheetSeriesDto>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("[GetAvailableSheetSeriesAsync Warning] {0}", ex.Message));
                    throw new InvalidOperationException("讀取 Revit 圖紙系列失敗：" + ex.Message, ex);
                }
            }

            return await _fallbackMock.GetAvailableSheetSeriesAsync(projectName);
        }

        private List<SheetSeriesDto> GetCachedSeries(Document doc)
        {
            int sheetCount = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
            string key = string.Format("{0}|{1}", doc.Title, doc.GetHashCode());
            if (_cachedSeries != null && _cachedSeriesDocumentKey == key && _cachedSeriesSheetCount == sheetCount &&
                DateTime.Now - _cachedSeriesAt < TimeSpan.FromMinutes(5))
                return _cachedSeries;

            _cachedSeries = ExtractAllSeriesFromDoc(doc) ?? new List<SheetSeriesDto>();
            _cachedSeriesDocumentKey = key;
            _cachedSeriesSheetCount = sheetCount;
            _cachedSeriesAt = DateTime.Now;
            return _cachedSeries;
        }

        public async Task<List<DrawingRuleDto>> GetDrawingRulesAsync()
        {
            var seriesList = await GetAvailableSheetSeriesAsync();
            if (seriesList != null && seriesList.Count > 0)
            {
                return seriesList[0].Sheets;
            }
            return await _fallbackMock.GetDrawingRulesAsync();
        }

        private List<SheetSeriesDto> ExtractAllSeriesFromDoc(Document doc)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0) return null;

            var seriesDict = new Dictionary<string, List<DrawingRuleDto>>(StringComparer.OrdinalIgnoreCase);
            var sourceStories = GetStoryGroups(doc);
            var titleBlocksBySheet = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType()
                .Where(e => e.OwnerViewId != ElementId.InvalidElementId)
                .GroupBy(e => e.OwnerViewId.Value).ToDictionary(g => g.Key, g => g.First());
            var schedulesBySheet = new FilteredElementCollector(doc)
                .OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                .Where(x => !x.IsTitleblockRevisionSchedule && x.OwnerViewId != ElementId.InvalidElementId)
                .GroupBy(x => x.OwnerViewId.Value).ToDictionary(g => g.Key, g => g.ToList());
            int ruleIndex = 1;

            foreach (var sheet in sheets)
            {
                string sNumber = sheet.SheetNumber.Trim();
                string sName = sheet.Name.Trim();

                // 1. 抓取圖框 (TitleBlock)
                Element titleBlockInstance;
                titleBlocksBySheet.TryGetValue(sheet.Id.Value, out titleBlockInstance);

                var titleBlockFamilyInstance = titleBlockInstance as FamilyInstance;
                var titleBlockSymbol = titleBlockFamilyInstance != null ? titleBlockFamilyInstance.Symbol : null;
                string titleBlockName = titleBlockSymbol != null
                    ? GetTitleBlockDisplayName(titleBlockSymbol)
                    : titleBlockInstance != null ? titleBlockInstance.Name : "未偵測到圖框";
                string titleBlockTypeId = titleBlockSymbol != null ? titleBlockSymbol.Id.ToString() : string.Empty;

                // 2. 完整抓取圖紙上放置之全部視圖 (Viewports) 與各自的樣板、比例、範圍框
                var viewportIds = sheet.GetAllViewports();
                var placedViews = new List<SheetViewItemDto>();

                string primaryViewName = sName;
                string primaryTemplateName = "無樣板";
                string primaryScale = "1:100";
                string primaryCoordText = "X: 420.0 mm, Y: 297.0 mm (對齊圖框中心)";
                string primaryScopeBox = "-";

                foreach (var vpId in viewportIds)
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = doc.GetElement(vp.ViewId) as Autodesk.Revit.DB.View;
                    if (view == null) continue;
                    var viewportType = doc.GetElement(vp.GetTypeId()) as ElementType;
                    string viewportTypeName = viewportType != null ? GetViewportTypeName(viewportType) : "未偵測到視埠類型";

                    string vName = view.Name;
                    string vTemplateName = "無樣板 (依視圖設定)";
                    if (view.ViewTemplateId != null && view.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        var vt = doc.GetElement(view.ViewTemplateId);
                        if (vt != null) vTemplateName = vt.Name;
                    }
                    string vScale = string.Format("1:{0}", view.Scale);
                    string vScopeBox = "-";
                    try
                    {
                        var sbParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                        if (sbParam != null && sbParam.AsElementId() != ElementId.InvalidElementId)
                        {
                            var sbElem = doc.GetElement(sbParam.AsElementId());
                            if (sbElem != null) vScopeBox = sbElem.Name;
                        }
                    }
                    catch { }

                    string vCoord = "置中";
                    try
                    {
                        XYZ center = vp.GetBoxCenter();
                        vCoord = string.Format("X:{0:F0}, Y:{1:F0} mm", center.X * 304.8, center.Y * 304.8);
                    }
                    catch { }

                    string vType = view.ViewType.ToString();
                    if (vType == "FloorPlan") vType = "平面圖";
                    else if (vType == "EngineeringPlan") vType = "結構平面圖";
                    else if (vType == "CeilingPlan") vType = "天花板圖";
                    else if (vType == "Elevation") vType = "立面圖";
                    else if (vType == "Section") vType = "剖面圖";
                    else if (vType == "Detail") vType = "大樣詳圖";
                    else if (vType == "DraftingView") vType = "製圖視圖";
                    else if (vType == "Legend") vType = "圖例";

                    string sourceLevelName = "不依附樓層";
                    var planView = view as ViewPlan;
                    if (planView != null && planView.GenLevel != null)
                        sourceLevelName = planView.GenLevel.Name;

                    var placedView = new SheetViewItemDto
                    {
                        ViewId = view.Id.ToString(),
                        ViewName = vName,
                        ViewType = vType,
                        ViewTemplateName = vTemplateName,
                        Scale = vScale,
                        ScopeBoxName = vScopeBox,
                        PlacementCoord = vCoord,
                        SourceLevelName = sourceLevelName,
                        ViewportTypeName = viewportTypeName,
                        SourceViewportTypeId = vp.GetTypeId().ToString(),
                        ResolvedViewportTypeName = viewportTypeName
                    };
                    ElementId sourcePrimaryId = ElementId.InvalidElementId;
                    try { sourcePrimaryId = view.GetPrimaryViewId(); } catch { }
                    if (sourcePrimaryId != null && sourcePrimaryId != ElementId.InvalidElementId)
                    {
                        var sourcePrimary = doc.GetElement(sourcePrimaryId) as Autodesk.Revit.DB.View;
                        placedView.IsDependentView = true;
                        placedView.SourcePrimaryViewId = sourcePrimaryId.ToString();
                        placedView.SourcePrimaryViewName = sourcePrimary != null ? sourcePrimary.Name : string.Empty;
                    }
                    if (planView != null)
                    {
                        PopulatePlanViewMetadata(doc, planView, placedView, sourceStories);
                        var inferredBinding = InferSourceViewBinding(doc, planView, sName, vName, sourceStories);
                        placedView.LevelBindingRule = inferredBinding.ToString();
                        placedView.ViewDirectionDescription = inferredBinding == LevelBindingOption.PreviousLevel
                            ? "見上：由樓下一層往上看"
                            : "見下／當層：由當層往下看";
                    }
                    placedViews.Add(placedView);

                    if (placedViews.Count == 1)
                    {
                        primaryViewName = vName;
                        primaryTemplateName = vTemplateName;
                        primaryScale = vScale;
                        primaryCoordText = vCoord;
                        primaryScopeBox = vScopeBox;
                    }
                }

                // ScheduleSheetInstance 不會出現在 GetAllViewports()，必須獨立納入基準分析。
                List<ScheduleSheetInstance> sheetSchedules;
                if (!schedulesBySheet.TryGetValue(sheet.Id.Value, out sheetSchedules))
                    sheetSchedules = new List<ScheduleSheetInstance>();
                foreach (var scheduleInstance in sheetSchedules)
                {
                    var schedule = doc.GetElement(scheduleInstance.ScheduleId) as ViewSchedule;
                    if (schedule == null) continue;
                    string coord = string.Format("X:{0:F0}, Y:{1:F0} mm",
                        scheduleInstance.Point.X * 304.8, scheduleInstance.Point.Y * 304.8);
                    placedViews.Add(new SheetViewItemDto
                    {
                        ViewId = schedule.Id.ToString(),
                        ViewName = schedule.Name,
                        ViewType = "明細表",
                        ViewTemplateName = "不適用",
                        Scale = "不適用",
                        ScopeBoxName = "-",
                        PlacementCoord = coord,
                        SourceLevelName = "不依附樓層",
                        ViewportTypeName = "不適用",
                        ResolvedViewportTypeName = "不適用"
                    });

                    if (placedViews.Count == 1)
                    {
                        primaryViewName = schedule.Name;
                        primaryTemplateName = "不適用";
                        primaryScale = "不適用";
                        primaryCoordText = coord;
                        primaryScopeBox = "-";
                    }
                }

                // CAD 匯入、詳圖線、文字等可能直接放在 Sheet，不會出現在 GetAllViewports() 中。
                // 必須顯示在 A 的分析明細中，並由建立引擎另行複製到目標 Sheet。
                var directSheetElements = new FilteredElementCollector(doc, sheet.Id).WhereElementIsNotElementType()
                    .Where(e => e.ViewSpecific && !(e is Viewport) && !(e is ScheduleSheetInstance) &&
                        (e.Category == null || e.Category.Id.Value != (long)BuiltInCategory.OST_TitleBlocks)).ToList();
                if (directSheetElements.Count > 0)
                {
                    placedViews.Add(new SheetViewItemDto
                    {
                        ViewId = "SHEET-" + sheet.Id,
                        ViewName = string.Format("圖紙直接繪圖元件（{0} 個）", directSheetElements.Count),
                        ViewType = "圖紙直接繪圖元件",
                        ViewTemplateName = "不適用",
                        Scale = "不適用",
                        ScopeBoxName = "-",
                        PlacementCoord = "保留 A 圖紙座標",
                        SourceLevelName = "不依附樓層",
                        ViewportTypeName = "不適用",
                        ResolvedViewportTypeName = "不適用",
                        AnnotationCount = directSheetElements.Count(e => e.Category != null && e.Category.CategoryType == CategoryType.Annotation),
                        TextNoteCount = directSheetElements.Count(e => e is TextNote),
                        DetailElementCount = directSheetElements.Count(e => e.Category == null || e.Category.CategoryType != CategoryType.Annotation)
                    });
                }

                if (placedViews.Count == 0)
                {
                    placedViews.Add(new SheetViewItemDto
                    {
                        ViewId = string.Empty,
                        ViewName = string.Format("{0} 主視圖", sName),
                        ViewType = "平面圖",
                        ViewTemplateName = "無樣板",
                        Scale = "1:100",
                        ScopeBoxName = "-",
                        PlacementCoord = "置中"
                    });
                }

                // 3. 判斷樓層依附邏輯
                LevelBindingOption levelBinding = LevelBindingOption.CurrentLevel;
                if (ContainsLookUp(sName)) levelBinding = LevelBindingOption.PreviousLevel;
                if (ContainsLookDown(sName)) levelBinding = LevelBindingOption.CurrentLevel;

                var rule = new DrawingRuleDto
                {
                    RuleId = string.Format("R_AUTO_{0:D3}", ruleIndex++),
                    RuleName = sName,
                    BuildingName = "全棟通用",
                    TargetFloorScope = "所有目標樓層",
                    SheetNameFormula = string.Format("{{棟別}}{{樓層}}{0}", CleanRuleName(sName)),
                    SheetNumberFormula = GenerateSheetFormulaFromNumber(sNumber),
                    LevelBinding = levelBinding,
                    ViewTemplateName = primaryTemplateName,
                    Scale = primaryScale,
                    ScopeBoxRule = primaryScopeBox,
                    SourceViewName = primaryViewName,
                    SourceSheetNumber = sNumber,
                    SourceSheetName = sName,
                    TitleBlockName = titleBlockName,
                    TitleBlockTypeId = titleBlockTypeId,
                    ViewportPlacementCoord = primaryCoordText,
                    AssociatedSchedules = "自動對齊明細表",
                    AssociatedLegends = "自動對齊圖例清單",
                    ActiveUsageCount = placedViews.Count,
                    IsChecked = true,
                    PlacedViews = placedViews
                };

                // 4. 解析 SeriesKey：先用 conventions.json 的系列規則（例如 SB\d+），否則取圖號前段
                string seriesKey = "一般圖紙";
                string coreSeries = FactoryConventions.Current.MatchSeriesCore(sNumber);
                if (!string.IsNullOrEmpty(coreSeries))
                {
                    seriesKey = coreSeries;
                }
                else
                {
                    var matchPrefix = System.Text.RegularExpressions.Regex.Match(sNumber, @"^([A-Za-z0-9]+(?:-[A-Za-z0-9]+)?)");
                    if (matchPrefix.Success)
                    {
                        seriesKey = matchPrefix.Groups[1].Value;
                    }
                }

                if (!seriesDict.ContainsKey(seriesKey))
                {
                    seriesDict[seriesKey] = new List<DrawingRuleDto>();
                }
                seriesDict[seriesKey].Add(rule);
            }

            var result = new List<SheetSeriesDto>();

            // 依系列代碼結尾的數字排序（例如 S1、S2 … S10），沒有數字的排最後
            var orderedKeys = seriesDict.Keys
                .OrderBy(k => 
                {
                    var m = System.Text.RegularExpressions.Regex.Match(k, @"(\d+)$");
                    return m.Success ? int.Parse(m.Groups[1].Value) : 9999;
                })
                .ThenBy(k => k)
                .ToList();

            foreach (var key in orderedKeys)
            {
                var list = seriesDict[key];
                result.Add(new SheetSeriesDto
                {
                    SeriesId = key,
                    DisplayName = string.Format("{0} 系列標準圖說 (包含 {1} 張標準圖紙與視圖樣板)", key, list.Count),
                    SheetCount = list.Count,
                    Sheets = list
                });
            }

            return result;
        }

        private static string CleanRuleName(string sheetName)
        {
            if (string.IsNullOrEmpty(sheetName)) return "平面圖";
            string cleaned = sheetName.Replace("1FL", "").Replace("1F", "");
            foreach (var buildingName in FactoryConventions.Current.AllBuildingNames)
                cleaned = cleaned.Replace(buildingName, "");
            return cleaned.Trim();
        }

        private static string GenerateSheetFormulaFromNumber(string sheetNumber)
        {
            if (string.IsNullOrEmpty(sheetNumber)) return FactoryConventions.Current.DefaultSeriesCode + "-{樓層代碼}-01";
            if (sheetNumber.Contains("-"))
            {
                var parts = sheetNumber.Split('-');
                if (parts.Length >= 2)
                {
                    return string.Format("{0}-{{樓層代碼}}-{1}", parts[0], parts[parts.Length - 1]);
                }
            }
            return string.Format("{0}-{{樓層代碼}}", sheetNumber);
        }

        private class ParsedFloorGroup
        {
            public string FloorCode { get; set; }
            public int SortOrder { get; set; }
            public Level PrimaryLevel { get; set; }
            public double PrimaryElevation { get; set; }
            public List<Level> AssociatedLevels { get; set; }
        }

        private static void ParseLevelStory(Level lvl, out string floorCode, out int sortOrder)
        {
            // Revit projects in the field use several visually identical naming
            // variants (for example 2FL, 2F.L., ２ＦＬ and names separated by
            // underscores).  Normalize compatibility characters before matching;
            // otherwise an unrecognised 2F falls through to the elevation estimate
            // and can be incorrectly merged into the 3F group.
            string name = (lvl.Name ?? string.Empty).Trim()
                .Normalize(System.Text.NormalizationForm.FormKC);
            string normalizedName = name
                .Replace('．', '.')
                .Replace('＿', '_')
                .Replace('　', ' ');
            double elevFeet = lvl.Elevation;
            double elevMeters = elevFeet * 0.3048;

            // 1. 屋突層 (屋突1F, 屋突2F, PR, PH)
            var matchPent = System.Text.RegularExpressions.Regex.Match(normalizedName, @"(?i)(?:屋突|突出物|PR|PH)\s*([1-9]\d*)?");
            if (matchPent.Success)
            {
                string numStr = matchPent.Groups[1].Value;
                int num = string.IsNullOrEmpty(numStr) ? 1 : int.Parse(numStr);
                floorCode = string.Format("屋突{0}F", num);
                sortOrder = 1000 + num;
                return;
            }

            // 2. 屋頂層 (RF, RFL, ROOF, 屋頂)
            var matchRoof = System.Text.RegularExpressions.Regex.Match(normalizedName, @"(?i)\b(?:RF|RFL|ROOF|屋頂層?|頂層)\b");
            if (matchRoof.Success)
            {
                floorCode = "RF";
                sortOrder = 900;
                return;
            }

            // 3. 地下層 (B1~B10, 地下1層~地下10層)
            // 排除純粹高程數字干擾，精準提取 B4, B3, B2, B1
            var matchBasement = System.Text.RegularExpressions.Regex.Match(normalizedName, @"(?i)(?:地下|(?:^|[^A-Z0-9])B[_\-.\s]*)\s*([1-9]|10)(?![0-9])");
            if (matchBasement.Success)
            {
                int bNum = int.Parse(matchBasement.Groups[1].Value);
                floorCode = string.Format("B{0}", bNum);
                sortOrder = -bNum; // B4=-4, B3=-3, B2=-2, B1=-1
                return;
            }

            // 4. 地上層 (1F~99F, 1FL~99FL, 1樓~99樓)
            // Some coordination models use the reversed token order FL1/FL2,
            // for example ALL_FL1(+15).
            var matchGroundReversed = System.Text.RegularExpressions.Regex.Match(normalizedName,
                @"(?i)(?:^|[_\-\s])F\s*L\s*([1-9]\d?)(?=[^A-Z0-9]|$)");
            if (matchGroundReversed.Success)
            {
                int fNum = int.Parse(matchGroundReversed.Groups[1].Value);
                floorCode = string.Format("{0}F", fNum);
                sortOrder = fNum;
                return;
            }

            var matchGround = System.Text.RegularExpressions.Regex.Match(normalizedName,
                @"(?i)(?:Level\s*)?([1-9]\d?)\s*(?:F\s*[.\-_]?\s*L?|\u5c64|\u6a13)(?=[^A-Z0-9]|$)");
            if (matchGround.Success)
            {
                int fNum = int.Parse(matchGround.Groups[1].Value);
                floorCode = string.Format("{0}F", fNum);
                sortOrder = fNum;
                return;
            }

            // 5. 開頭帶樓層數字 (如 "1FL", "2F", "3-...")
            var matchPrefix = System.Text.RegularExpressions.Regex.Match(normalizedName,
                @"(?i)(?:^|[_\-\s])([1-9]\d?)\s*(?:F\s*[.\-_]?\s*L?)?(?=[_\-\s\(]|$)");
            if (matchPrefix.Success)
            {
                int fNum = int.Parse(matchPrefix.Groups[1].Value);
                floorCode = string.Format("{0}F", fNum);
                sortOrder = fNum;
                return;
            }

            // 6. 依高程自然推算保底
            if (elevMeters < -0.3)
            {
                int bNum = Math.Max(1, (int)Math.Round(Math.Abs(elevMeters) / 3.2));
                floorCode = string.Format("B{0}", bNum);
                sortOrder = -bNum;
            }
            else
            {
                int fNum = Math.Max(1, (int)Math.Round(elevMeters / 3.2) + 1);
                floorCode = string.Format("{0}F", fNum);
                sortOrder = fNum;
            }
        }

        private static bool ContainsLookUp(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                (text.Contains("見上") || text.Contains("仰視") || text.Contains("向上看"));
        }

        private static bool ContainsLookDown(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                (text.Contains("見下") || text.Contains("俯視") || text.Contains("向下看"));
        }

        private static List<ParsedFloorGroup> GetStoryGroups(Document doc)
        {
            var groups = new Dictionary<string, ParsedFloorGroup>(StringComparer.OrdinalIgnoreCase);
            var allLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            FactoryConventions.Current.RegisterLevelNames(allLevels.Select(l => l.Name));
            foreach (var level in allLevels)
            {
                string code;
                int order;
                ParseLevelStory(level, out code, out order);
                ParsedFloorGroup group;
                if (!groups.TryGetValue(code, out group))
                {
                    group = new ParsedFloorGroup
                    {
                        FloorCode = code,
                        SortOrder = order,
                        PrimaryLevel = level,
                        PrimaryElevation = level.Elevation,
                        AssociatedLevels = new List<Level>()
                    };
                    groups.Add(code, group);
                }
                group.AssociatedLevels.Add(level);
                try
                {
                    var isStory = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
                    if (isStory != null && isStory.AsInteger() == 1)
                    {
                        group.PrimaryLevel = level;
                        group.PrimaryElevation = level.Elevation;
                    }
                }
                catch { }
            }
            return groups.Values.OrderBy(g => g.SortOrder).ThenBy(g => g.PrimaryElevation).ToList();
        }

        private static Level ResolveNominalSourceLevel(List<ParsedFloorGroup> stories, params string[] texts)
        {
            if (stories == null || stories.Count == 0) return null;
            foreach (string text in texts ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                var matches = System.Text.RegularExpressions.Regex.Matches(text,
                    @"(?i)(B[1-9]\d*|[1-9]\d?F)(?:L)?");
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    string code = match.Groups[1].Value.ToUpperInvariant();
                    var story = stories.FirstOrDefault(g => string.Equals(g.FloorCode, code, StringComparison.OrdinalIgnoreCase));
                    if (story != null) return story.PrimaryLevel;
                }
            }
            return null;
        }

        private static int GetStoryDelta(Document doc, Level baseLevel, ElementId planeLevelId)
        {
            return GetStoryDelta(doc, baseLevel, planeLevelId, GetStoryGroups(doc));
        }

        private static int GetStoryDelta(Document doc, Level baseLevel, ElementId planeLevelId, List<ParsedFloorGroup> stories)
        {
            if (baseLevel == null || planeLevelId == null || planeLevelId == ElementId.InvalidElementId) return 0;
            var planeLevel = doc.GetElement(planeLevelId) as Level;
            if (planeLevel == null) return 0;
            int baseIndex = stories.FindIndex(g => g.AssociatedLevels.Any(l => l.Id == baseLevel.Id));
            int planeIndex = stories.FindIndex(g => g.AssociatedLevels.Any(l => l.Id == planeLevel.Id));
            return baseIndex >= 0 && planeIndex >= 0 ? planeIndex - baseIndex : 0;
        }

        private static Level ResolveStoryLevel(Document doc, Level selectedLevel, int storyDelta)
        {
            return ResolveStoryLevel(selectedLevel, storyDelta, GetStoryGroups(doc));
        }

        private static Level ResolveStoryLevel(Level selectedLevel, int storyDelta, List<ParsedFloorGroup> stories)
        {
            if (selectedLevel == null) return null;
            int index = stories.FindIndex(g => g.AssociatedLevels.Any(l => l.Id == selectedLevel.Id));
            if (index < 0) return selectedLevel;
            int targetIndex = index + storyDelta;
            if (targetIndex < 0 || targetIndex >= stories.Count) return null;

            // A floor group can contain several physical Levels, for example
            // OB_2FL, LB_2FL and ALL_2FL. Preserve the building identity while
            // resolving an adjacent storey instead of returning an arbitrary
            // PrimaryLevel from the group.
            return SelectLevelForBuilding(stories[targetIndex], null, selectedLevel);
        }

        private static string GetLevelBuildingKey(string text)
        {
            return FactoryConventions.Current.GetLevelBuildingKey(text);
        }

        private static string GetRequestedBuildingKey(string buildingName)
        {
            return FactoryConventions.Current.GetRequestedBuildingKey(buildingName);
        }

        private static Level SelectLevelForBuilding(ParsedFloorGroup story, string buildingName, Level preferredLevel)
        {
            if (story == null) return null;
            var candidates = story.AssociatedLevels ?? new List<Level>();
            if (candidates.Count == 0) return story.PrimaryLevel;

            // OB_R1FL/LB_R1FL are auxiliary raised/roof levels. They can be
            // parsed into the same 1F story group, but must never be used as the
            // physical previous floor for a look-up structural plan.
            var normalFloorCandidates = candidates.Where(l => !IsAuxiliaryRLevelName(l.Name)).ToList();
            if (normalFloorCandidates.Count > 0) candidates = normalFloorCandidates;
            else if (candidates.All(l => IsAuxiliaryRLevelName(l.Name))) return null;

            string requestedKey = GetRequestedBuildingKey(buildingName);
            if (string.IsNullOrEmpty(requestedKey) && preferredLevel != null)
                requestedKey = GetLevelBuildingKey(preferredLevel.Name);

            Level best = null;
            int bestScore = int.MinValue;
            foreach (var candidate in candidates)
            {
                int score = 0;
                string candidateKey = GetLevelBuildingKey(candidate.Name);
                if (!string.IsNullOrEmpty(requestedKey))
                    score += string.Equals(candidateKey, requestedKey, StringComparison.OrdinalIgnoreCase) ? 10000 : -10000;
                if (preferredLevel != null && candidate.Id == preferredLevel.Id) score += 500;
                try
                {
                    var isStory = candidate.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
                    if (isStory != null && isStory.AsInteger() == 1) score += 100;
                }
                catch { }
                score -= (int)Math.Min(99, Math.Round(Math.Abs(candidate.Elevation - story.PrimaryElevation) * 10.0));
                if (best == null || score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best ?? story.PrimaryLevel;
        }

        private static bool IsAuxiliaryRLevelName(string levelName)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(levelName ?? string.Empty,
                @"(?i)(^|[_\-\s])R\s*(?:[1-9]\d*\s*F(?:L)?|F(?:L)?)(?=[_\-\s\(\+]|$)");
        }

        private static Level ResolveBuildingStoryLevel(Level selectedLevel, int storyDelta,
            List<ParsedFloorGroup> stories, string buildingName, string expectedFloorCode)
        {
            if (stories == null || stories.Count == 0) return null;

            int index = -1;
            if (!string.IsNullOrWhiteSpace(expectedFloorCode))
            {
                index = stories.FindIndex(g =>
                    string.Equals(g.FloorCode, expectedFloorCode, StringComparison.OrdinalIgnoreCase));
            }
            if (index < 0 && selectedLevel != null)
                index = stories.FindIndex(g => g.AssociatedLevels.Any(l => l.Id == selectedLevel.Id));
            if (index < 0) return selectedLevel;

            int targetIndex = index + storyDelta;
            if (targetIndex < 0 || targetIndex >= stories.Count) return null;
            return SelectLevelForBuilding(stories[targetIndex], buildingName, selectedLevel);
        }

        private static double GetFloorHeightMm(Document doc, Level level)
        {
            return GetFloorHeightMm(level, GetStoryGroups(doc));
        }

        private static double GetFloorHeightMm(Level level, List<ParsedFloorGroup> stories)
        {
            if (level == null) return 0;
            var next = ResolveStoryLevel(level, 1, stories);
            return next != null ? (next.Elevation - level.Elevation) * 304.8 : 0;
        }

        private static void PopulatePlanViewMetadata(Document doc, ViewPlan plan, SheetViewItemDto dto, List<ParsedFloorGroup> stories)
        {
            if (plan == null || dto == null || plan.GenLevel == null) return;
            dto.SourceLevelElevationMm = plan.GenLevel.Elevation * 304.8;
            try
            {
                var range = plan.GetViewRange();
                dto.HasPlanViewRange = true;
                dto.TopPlaneStoryDelta = GetStoryDelta(doc, plan.GenLevel, range.GetLevelId(PlanViewPlane.TopClipPlane), stories);
                dto.CutPlaneStoryDelta = GetStoryDelta(doc, plan.GenLevel, range.GetLevelId(PlanViewPlane.CutPlane), stories);
                dto.BottomPlaneStoryDelta = GetStoryDelta(doc, plan.GenLevel, range.GetLevelId(PlanViewPlane.BottomClipPlane), stories);
                dto.ViewDepthPlaneStoryDelta = GetStoryDelta(doc, plan.GenLevel, range.GetLevelId(PlanViewPlane.ViewDepthPlane), stories);
                dto.TopPlaneOffsetMm = range.GetOffset(PlanViewPlane.TopClipPlane) * 304.8;
                dto.CutPlaneOffsetMm = range.GetOffset(PlanViewPlane.CutPlane) * 304.8;
                dto.BottomPlaneOffsetMm = range.GetOffset(PlanViewPlane.BottomClipPlane) * 304.8;
                dto.ViewDepthPlaneOffsetMm = range.GetOffset(PlanViewPlane.ViewDepthPlane) * 304.8;
            }
            catch { dto.HasPlanViewRange = false; }

            try
            {
                var bottom = plan.get_Parameter(BuiltInParameter.VIEW_UNDERLAY_BOTTOM_ID);
                var top = plan.get_Parameter(BuiltInParameter.VIEW_UNDERLAY_TOP_ID);
                var orientation = plan.get_Parameter(BuiltInParameter.VIEW_UNDERLAY_ORIENTATION);
                var bottomId = bottom != null ? bottom.AsElementId() : ElementId.InvalidElementId;
                var topId = top != null ? top.AsElementId() : ElementId.InvalidElementId;
                dto.HasUnderlay = bottomId != ElementId.InvalidElementId || topId != ElementId.InvalidElementId;
                dto.UnderlayBottomStoryDelta = GetStoryDelta(doc, plan.GenLevel, bottomId, stories);
                dto.UnderlayTopStoryDelta = GetStoryDelta(doc, plan.GenLevel, topId, stories);
                dto.UnderlayOrientation = orientation != null ? orientation.AsInteger() : 0;
            }
            catch { dto.HasUnderlay = false; }

            dto.SourcePhaseName = GetReferencedElementName(doc, plan.get_Parameter(BuiltInParameter.VIEW_PHASE));
            dto.SourcePhaseFilterName = GetReferencedElementName(doc, plan.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER));
        }

        private static string GetReferencedElementName(Document doc, Parameter parameter)
        {
            try
            {
                if (parameter == null) return string.Empty;
                var id = parameter.AsElementId();
                var element = id != ElementId.InvalidElementId ? doc.GetElement(id) : null;
                return element != null ? element.Name : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static LevelBindingOption InferSourceViewBinding(Document doc, ViewPlan plan, string sheetName, string viewName,
            List<ParsedFloorGroup> stories)
        {
            if (ContainsLookUp(viewName)) return LevelBindingOption.PreviousLevel;
            if (ContainsLookDown(viewName)) return LevelBindingOption.CurrentLevel;
            if (plan == null || plan.GenLevel == null) return LevelBindingOption.CurrentLevel;

            var match = System.Text.RegularExpressions.Regex.Match(sheetName ?? string.Empty,
                @"(?i)(B[1-9]\d*|[1-9]\d?F)L?");
            if (!match.Success) return LevelBindingOption.CurrentLevel;
            string nominalCode = match.Groups[1].Value.ToUpperInvariant();
            int nominalIndex = stories.FindIndex(g => string.Equals(g.FloorCode, nominalCode, StringComparison.OrdinalIgnoreCase));
            int viewIndex = stories.FindIndex(g => g.AssociatedLevels.Any(l => l.Id == plan.GenLevel.Id));
            if (nominalIndex >= 0 && viewIndex == nominalIndex - 1) return LevelBindingOption.PreviousLevel;
            if (nominalIndex >= 0 && viewIndex == nominalIndex + 1) return LevelBindingOption.NextLevel;
            return LevelBindingOption.CurrentLevel;
        }

        private static void PopulateViewSpecificCounts(Document doc, Autodesk.Revit.DB.View view, SheetViewItemDto dto)
        {
            var elements = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType()
                // Dimensions and hosted tags are intentionally outside this
                // add-in's scope. Dedicated add-ins own their analysis and
                // creation, so the drawing factory does not scan either one.
                .Where(e => e.ViewSpecific && !(e is Dimension) && !(e is IndependentTag)).ToList();
            dto.DimensionCount = 0;
            dto.TextNoteCount = elements.Count(e => e is TextNote);
            dto.AnnotationCount = elements.Count(e =>
                e.Category != null && e.Category.CategoryType == CategoryType.Annotation && !(e is TextNote));
            dto.DetailElementCount = Math.Max(0, elements.Count - dto.TextNoteCount - dto.AnnotationCount);
            dto.DimensionStyleNames = "交由尺寸專用外掛處理";
            dto.DimensionLayoutSummary = "本外掛不分析";
        }

        private static List<ViewSheet> FindMatchingSheetsForStory(Document doc, ParsedFloorGroup story, List<ViewSheet> sheets)
        {
            var matched = new List<ViewSheet>();
            string code = story.FloorCode.ToUpper(); // e.g. "B4", "B3", "1F", "RF"

            // 構建關鍵字正則 (例如 B4 -> 匹配 B4, B4F, B4FL, 地下4, 地下四, -B4-)
            string pattern;
            if (code.StartsWith("B"))
            {
                string bNum = code.Substring(1);
                string chDigit = ConvertDigitToChinese(bNum);
                pattern = string.Format(@"(?i)\bB{0}(?:F|FL)?\b|B{0}[-_ ]|地下[{0}{1}]", bNum, chDigit);
            }
            else if (code.EndsWith("F") && !code.StartsWith("屋"))
            {
                string fNum = code.Replace("F", "");
                string chDigit = ConvertDigitToChinese(fNum);
                pattern = string.Format(@"(?i)\b{0}(?:F|FL)\b|{0}F[-_ ]|(?i)\bLevel\s*{0}\b|[{0}{1}][樓層]", fNum, chDigit);
            }
            else
            {
                pattern = string.Format(@"(?i){0}", code);
            }

            var regex = new System.Text.RegularExpressions.Regex(pattern);

            // 快速純記憶體文字比對 (0.001 毫秒)，不執行沉重的視圖資料庫查詢
            foreach (var sheet in sheets)
            {
                string sheetNum = sheet.SheetNumber ?? string.Empty;
                string sheetName = sheet.Name ?? string.Empty;

                if (regex.IsMatch(sheetNum) || regex.IsMatch(sheetName))
                {
                    matched.Add(sheet);
                }
            }

            return matched;
        }

        private static string ConvertDigitToChinese(string digit)
        {
            switch (digit)
            {
                case "1": return "一";
                case "2": return "二";
                case "3": return "三";
                case "4": return "四";
                case "5": return "五";
                case "6": return "六";
                case "7": return "七";
                case "8": return "八";
                case "9": return "九";
                case "10": return "十";
                default: return digit;
            }
        }

        public async Task<List<CustomDrawingPackageItemDto>> GetAvailablePackageRulesAsync(string buildingId)
        {
            return await _fallbackMock.GetAvailablePackageRulesAsync(buildingId);
        }

        public async Task<List<IssueItemDto>> RunPreflightCheckAsync(GenerateRequestDto request)
        {
            if (_handler == null || RevitApp.ExternalEvent == null)
                return await _fallbackMock.RunPreflightCheckAsync(request);

            return await _handler.RunAsync(app =>
            {
                var sourceDoc = FindDocument(app, request.SourceProjectName, true, true);
                var targetDoc = FindDocument(app, request.TargetProjectName, true);
                var issues = new List<IssueItemDto>();

                if (sourceDoc == null)
                    issues.Add(CreateIssue("SOURCE", IssueSeverity.Error, "", "來源專案", "找不到來源 Revit 專案", "請先在 Revit 開啟來源專案"));
                if (targetDoc == null)
                    issues.Add(CreateIssue("TARGET", IssueSeverity.Error, "", "目標專案", "找不到目標 Revit 專案", "請先在 Revit 開啟目標專案"));
                if (targetDoc != null && targetDoc.IsReadOnly)
                    issues.Add(CreateIssue("READONLY", IssueSeverity.Error, "", targetDoc.Title, "目標專案目前為唯讀", "請取得可編輯權限後再執行"));
                if (targetDoc != null && targetDoc.IsLinked)
                    issues.Add(CreateIssue("LINKED", IssueSeverity.Error, "", targetDoc.Title,
                        "目標文件是 Revit Link，Revit 禁止在連結文件內建立圖紙", "請選擇已直接開啟的主專案文件作為複製目標 B"));

                if (sourceDoc == null || targetDoc == null) return issues;

                for (int i = 0; i < request.SelectedFloorIds.Count; i++)
                {
                    long rawId;
                    Level level = null;
                    if (long.TryParse(request.SelectedFloorIds[i], out rawId))
                        level = targetDoc.GetElement(new ElementId(rawId)) as Level;
                    if (level == null)
                    {
                        string floor = i < request.SelectedFloorNames.Count ? request.SelectedFloorNames[i] : request.SelectedFloorIds[i];
                        issues.Add(CreateIssue("LEVEL-" + i, IssueSeverity.Error, floor, "樓層", "目標樓層不存在或已被刪除", "請返回重新選擇樓層"));
                    }
                }

                var previews = BuildPreviewItems(sourceDoc, targetDoc, request);
                if (previews.Count == 0)
                    issues.Add(CreateIssue("RULES", IssueSeverity.Error, "", "標準圖說", "來源系列沒有可用的圖紙規則", "請選擇包含圖紙的來源系列"));
                foreach (var item in previews.Where(p => p.TargetSheetExists))
                    issues.Add(CreateIssue("SHEET-" + item.SheetNumber, IssueSeverity.Warning, item.SheetFloorName,
                        item.SheetNumber, "圖號已存在，執行時將跳過", "請修改起始系列代碼或取消該項"));
                var missingTitleBlocks = previews.Where(p => string.IsNullOrWhiteSpace(p.ResolvedTitleBlockTypeId)).ToList();
                if (missingTitleBlocks.Count > 0)
                    issues.Add(CreateIssue("TITLEBLOCK-AUTO", IssueSeverity.Info, "全部選擇樓層",
                        missingTitleBlocks.Count + " 張圖紙", "部分圖紙的基準圖框尚未在目標專案中",
                        "無需人工處理；建立時將自動由基準 A 複製需要的圖框族／類型"));

                var missingViewportTypes = previews.Where(p => p.PlannedViews.Any(v => v.ViewType != "明細表" &&
                    v.ViewType != "圖紙直接繪圖元件" &&
                    string.IsNullOrWhiteSpace(v.ResolvedViewportTypeId))).ToList();
                if (missingViewportTypes.Count > 0)
                    issues.Add(CreateIssue("VIEWPORT-AUTO", IssueSeverity.Info, "全部選擇樓層",
                        missingViewportTypes.Count + " 張圖紙", "目標專案尚無部分基準視埠類型",
                        "無需人工處理；建立時將自動建立同名類型並套用基準 A 的視埠參數"));
                foreach (var item in previews.Where(p => p.PlannedViews.Any(v =>
                    IsLevelBoundPlanView(v.ViewType) && string.IsNullOrWhiteSpace(v.TargetLevelId))))
                    issues.Add(CreateIssue("VIEWLEVEL-" + item.SheetNumber, IssueSeverity.Error, item.SheetFloorName,
                        item.SheetNumber, "見上／見下視圖找不到所需的實體 Level", "請確認目標專案包含當層及相鄰樓層 Level"));

                return issues;
            });
        }

        public async Task<List<SheetPreviewItemDto>> GetPreviewItemsAsync(GenerateRequestDto request)
        {
            if (_handler == null || RevitApp.ExternalEvent == null)
                return await _fallbackMock.GetPreviewItemsAsync(request);

            return await _handler.RunAsync(app =>
            {
                var sourceDoc = FindDocument(app, request.SourceProjectName, true, true);
                var targetDoc = FindDocument(app, request.TargetProjectName, true);
                return sourceDoc != null && targetDoc != null
                    ? BuildPreviewItems(sourceDoc, targetDoc, request)
                    : new List<SheetPreviewItemDto>();
            });
        }

        private static IssueItemDto CreateIssue(string id, IssueSeverity severity, string floor, string item, string message, string action)
        {
            return new IssueItemDto
            {
                IssueId = id,
                Severity = severity,
                FloorName = floor,
                SheetOrViewName = item,
                UserMessage = message,
                SuggestedAction = action,
                SystemRecommendation = action,
                TechnicalDetails = message,
                ResolutionOptions = new List<string> { action },
                SelectedResolution = action
            };
        }

        private List<SheetPreviewItemDto> BuildPreviewItems(Document sourceDoc, Document targetDoc, GenerateRequestDto request)
        {
            var allSeries = GetCachedSeries(sourceDoc);
            var series = allSeries.FirstOrDefault(s => string.Equals(s.SeriesId, request.SourceSeriesId, StringComparison.OrdinalIgnoreCase))
                         ?? allSeries.FirstOrDefault();
            if (series == null) return new List<SheetPreviewItemDto>();

            var selectedRuleIds = new HashSet<string>(request.CustomSelectedRuleIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var rules = series.Sheets
                .Where(r => selectedRuleIds.Count == 0 || selectedRuleIds.Contains(r.RuleId))
                .ToList();
            var existingNumbers = new HashSet<string>(new FilteredElementCollector(targetDoc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber), StringComparer.OrdinalIgnoreCase);
            var reservedPreviewNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mappings = (request.FloorMappings ?? new List<FloorSeriesMappingDto>())
                .ToDictionary(m => m.FloorName, m => m.TargetSeriesCode, StringComparer.OrdinalIgnoreCase);
            var result = new List<SheetPreviewItemDto>();
            var targetStories = GetStoryGroups(targetDoc);
            var targetViewportTypes = GetAllViewportTypes(targetDoc);

            for (int floorIndex = 0; floorIndex < request.SelectedFloorNames.Count; floorIndex++)
            {
                string floor = request.SelectedFloorNames[floorIndex];
                string targetSeries;
                if (!mappings.TryGetValue(floor, out targetSeries))
                    targetSeries = string.Format("{0}{1}", request.TargetSheetSeries, floorIndex);

                bool sameDocumentSeriesAdjusted = false;
                if (ReferenceEquals(sourceDoc, targetDoc))
                {
                    string availableSeries = FindAvailableSameDocumentSeries(targetSeries, series.SeriesId,
                        rules, existingNumbers, reservedPreviewNumbers);
                    sameDocumentSeriesAdjusted = !string.Equals(availableSeries, targetSeries,
                        StringComparison.OrdinalIgnoreCase);
                    targetSeries = availableSeries;
                }

                foreach (var rule in rules)
                {
                    string number = MapSheetNumber(rule.SourceSheetNumber, series.SeriesId, targetSeries);
                    reservedPreviewNumbers.Add(number);
                    string name = ApplyNameFormula(rule, request.BuildingName, floor);
                    bool exists = existingNumbers.Contains(number);
                    Level selectedSheetLevel = null;
                    if (floorIndex < request.SelectedFloorIds.Count)
                    {
                        long rawLevelId;
                        if (long.TryParse(request.SelectedFloorIds[floorIndex], out rawLevelId))
                            selectedSheetLevel = targetDoc.GetElement(new ElementId(rawLevelId)) as Level;
                    }
                    var plannedViews = BuildPlannedViews(rule, request.BuildingName, floor,
                        sourceDoc, targetDoc, selectedSheetLevel, targetStories, targetViewportTypes);
                    var resolvedTitleBlock = FindMatchingTitleBlockType(targetDoc, rule.TitleBlockName);
                    var primary = plannedViews.FirstOrDefault();
                    bool missingViewLevel = plannedViews.Any(v =>
                        IsLevelBoundPlanView(v.ViewType) && string.IsNullOrWhiteSpace(v.TargetLevelId));
                    bool resourcesNeedCopy = resolvedTitleBlock == null || plannedViews.Any(v =>
                        v.ViewType != "明細表" && v.ViewType != "圖紙直接繪圖元件" &&
                        string.IsNullOrWhiteSpace(v.ResolvedViewportTypeId));
                    result.Add(new SheetPreviewItemDto
                    {
                        IsSelected = !exists && !missingViewLevel,
                        BuildingName = request.BuildingName,
                        SheetFloorName = floor,
                        SheetNumber = number,
                        SheetName = name,
                        ViewName = primary != null ? primary.TargetViewName : ApplyViewName(rule.SourceViewName, request.BuildingName, floor),
                        ResolvedViewLevelName = primary != null ? primary.ResolvedLevelName : GetResolvedLevelText(floor, rule.LevelBinding),
                        ViewTemplateName = primary != null ? primary.ViewTemplateName : rule.ViewTemplateName,
                        ScopeBoxName = primary != null ? primary.ScopeBoxName : rule.ScopeBoxRule,
                        Scale = primary != null ? primary.Scale : rule.Scale,
                        Status = missingViewLevel ? UiItemStatus.Error : exists || resourcesNeedCopy ? UiItemStatus.Warning : UiItemStatus.Success,
                        StatusMessage = missingViewLevel ? "缺少見上／見下所需樓層" : exists ? "圖號已存在，將跳過"
                            : sameDocumentSeriesAdjusted ? "同檔複製：原目標系列已存在，已自動改用 " + targetSeries
                            : resourcesNeedCopy ? "圖框待複製／B 無相同視埠特徵" : "圖框與視埠已正確對應",
                        ActionDescription = "依來源建立視圖並保留 A3 配置；標籤與尺寸交由專用外掛處理",
                        SourceStandardSheet = rule.SourceSheetNumber,
                        SourceStandardView = rule.SourceViewName,
                        LevelRuleDescription = rule.LevelBinding.ToString(),
                        SourceTitleBlockName = rule.TitleBlockName,
                        SourceTitleBlockTypeId = rule.TitleBlockTypeId,
                        ResolvedTitleBlockName = resolvedTitleBlock != null
                            ? GetTitleBlockDisplayName(resolvedTitleBlock)
                            : rule.TitleBlockName + "（建立時由來源複製）",
                        ResolvedTitleBlockTypeId = resolvedTitleBlock != null ? resolvedTitleBlock.Id.ToString() : string.Empty,
                        TargetSheetExists = exists,
                        PlannedViews = plannedViews
                    });
                }
            }
            return result;
        }

        private static string FindAvailableSameDocumentSeries(string requestedSeries, string sourceSeriesId,
            IList<DrawingRuleDto> rules, ISet<string> existingNumbers, ISet<string> reservedNumbers)
        {
            string candidate = requestedSeries;
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                bool conflicts = (rules ?? new List<DrawingRuleDto>()).Any(rule =>
                {
                    string number = MapSheetNumber(rule.SourceSheetNumber, sourceSeriesId, candidate);
                    return existingNumbers.Contains(number) || reservedNumbers.Contains(number);
                });
                if (!conflicts) return candidate;
                candidate = IncrementSeriesCode(candidate);
            }
            return candidate;
        }

        private static string IncrementSeriesCode(string series)
        {
            if (string.IsNullOrWhiteSpace(series)) return FactoryConventions.Current.DefaultSeriesCode;
            var match = System.Text.RegularExpressions.Regex.Match(series.Trim(), @"^(.*?)(\d+)$");
            int value;
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out value))
                return series.Trim() + "-1";
            return match.Groups[1].Value + (value + 1).ToString();
        }

        private static string GetSheetSuffix(string sourceNumber)
        {
            if (string.IsNullOrWhiteSpace(sourceNumber)) return string.Empty;
            int index = sourceNumber.LastIndexOf('-');
            return index >= 0 && index < sourceNumber.Length - 1 ? sourceNumber.Substring(index + 1) : sourceNumber;
        }

        private static string MapSheetNumber(string sourceNumber, string sourceSeriesId, string targetSeries)
        {
            if (string.IsNullOrWhiteSpace(sourceNumber)) return targetSeries;
            if (!string.IsNullOrWhiteSpace(sourceSeriesId))
            {
                var pattern = FactoryConventions.Current.BuildSeriesMatchPattern(sourceSeriesId);
                if (System.Text.RegularExpressions.Regex.IsMatch(sourceNumber, pattern))
                    return new System.Text.RegularExpressions.Regex(pattern).Replace(sourceNumber, targetSeries, 1);
            }
            string suffix = GetSheetSuffix(sourceNumber);
            return string.IsNullOrEmpty(suffix) ? targetSeries : targetSeries + "-" + suffix;
        }

        private static LevelBindingOption ResolveViewBinding(DrawingRuleDto rule, SheetViewItemDto source)
        {
            string viewText = source != null ? source.ViewName : string.Empty;
            if (ContainsLookUp(viewText)) return LevelBindingOption.PreviousLevel;
            if (ContainsLookDown(viewText)) return LevelBindingOption.CurrentLevel;
            string sheetText = string.Format("{0} {1}", rule != null ? rule.SourceSheetName : string.Empty,
                rule != null ? rule.RuleName : string.Empty);
            // Sheet intent takes precedence over the source plan's GenLevel.  A
            // "2FL 見上" sheet deliberately uses the 1F structural plan even
            // when the source view itself has a generic CurrentLevel rule.
            if (ContainsLookUp(sheetText)) return LevelBindingOption.PreviousLevel;
            if (ContainsLookDown(sheetText)) return LevelBindingOption.CurrentLevel;
            LevelBindingOption inferred;
            if (source != null && Enum.TryParse(source.LevelBindingRule, out inferred) && inferred != LevelBindingOption.Unbound)
                return inferred;
            return rule != null ? rule.LevelBinding : LevelBindingOption.CurrentLevel;
        }

        private static LevelBindingOption ResolveRuntimeViewBinding(SheetPreviewItemDto item,
            SheetViewItemDto plannedView, Autodesk.Revit.DB.View sourceView)
        {
            string text = string.Join(" ", new[]
            {
                item != null ? item.SheetName : string.Empty,
                plannedView != null ? plannedView.ViewName : string.Empty,
                plannedView != null ? plannedView.TargetViewName : string.Empty,
                sourceView != null ? sourceView.Name : string.Empty
            });
            // Runtime names are authoritative. This deliberately overrides stale
            // preview TargetLevelIds created before the look-up/look-down fixes.
            if (ContainsLookUp(text)) return LevelBindingOption.PreviousLevel;
            if (ContainsLookDown(text)) return LevelBindingOption.CurrentLevel;
            LevelBindingOption parsed;
            if (plannedView != null && Enum.TryParse(plannedView.LevelBindingRule, out parsed) &&
                parsed != LevelBindingOption.Unbound)
                return parsed;
            return LevelBindingOption.CurrentLevel;
        }

        private static List<SheetViewItemDto> BuildPlannedViews(DrawingRuleDto rule, string building, string floor,
            Document sourceDoc, Document targetDoc, Level selectedSheetLevel, List<ParsedFloorGroup> targetStories,
            IList<ElementType> targetViewportTypes)
        {
            var result = new List<SheetViewItemDto>();
            foreach (var source in rule.PlacedViews ?? new List<SheetViewItemDto>())
            {
                ElementType sourceViewportType = ResolveSourceViewportType(sourceDoc, source);
                string sourceViewportTypeName = sourceViewportType != null
                    ? GetViewportTypeName(sourceViewportType)
                    : source.ViewportTypeName;
                var resolvedViewportType = sourceViewportType != null
                    ? FindEquivalentViewportType(sourceDoc, sourceViewportType, targetDoc, targetViewportTypes)
                    : FindMatchingViewportType(targetDoc, sourceViewportTypeName);
                bool levelBound = IsLevelBoundPlanView(source.ViewType);
                var binding = ResolveViewBinding(rule, source);
                Level resolvedLevel = levelBound
                    ? ResolveBuildingStoryLevel(selectedSheetLevel,
                        binding == LevelBindingOption.PreviousLevel ? -1 : binding == LevelBindingOption.NextLevel ? 1 : 0,
                        targetStories, building, floor)
                    : null;
                result.Add(new SheetViewItemDto
                {
                    ViewId = source.ViewId,
                    ViewName = source.ViewName,
                    ViewType = source.ViewType,
                    ViewTemplateName = source.ViewTemplateName,
                    Scale = source.Scale,
                    ScopeBoxName = ApplyResourceName(source.ScopeBoxName, floor),
                    PlacementCoord = source.PlacementCoord,
                    SourceLevelName = source.SourceLevelName,
                    TargetViewName = source.ViewType == "圖例"
                        ? source.ViewName
                        : ApplyViewName(source.ViewName, building, floor),
                    ResolvedLevelName = levelBound
                        ? (resolvedLevel != null ? resolvedLevel.Name : GetResolvedLevelText(floor, binding) + "（找不到對應 Level）")
                        : source.ViewType == "明細表" ? floor + "（改寫明細表 Level 篩選）" : "不依附樓層／跨樓層共用",
                    MigrationAction = source.ViewType == "明細表"
                        ? "複製獨立明細表、改寫或新增目標樓層篩選，依原座標放置"
                        : source.ViewType == "圖例"
                            ? "圖例不參與樓層映射；目標專案已有則直接共用，否則僅複製一次"
                            : source.IsDependentView
                                ? "先建立目標樓層主要視圖，再以 AsDependent 複製從屬視圖並保留圖紙座標"
                                : "依目標層高重建 View Range、Underlay、Phase 與範圍框；不分析標籤及尺寸",
                    LevelBindingRule = binding.ToString(),
                    ViewDirectionDescription = source.ViewType == "圖例"
                        ? "圖例：不依附樓層"
                        : binding == LevelBindingOption.PreviousLevel
                            ? "見上：由樓下一層往上看"
                            : ContainsLookDown(source.ViewName) ? "見下：由當層往下看" : "依當層視圖方向",
                    TargetLevelId = resolvedLevel != null ? resolvedLevel.Id.ToString() : string.Empty,
                    SourceLevelElevationMm = source.SourceLevelElevationMm,
                    TargetLevelElevationMm = resolvedLevel != null ? resolvedLevel.Elevation * 304.8 : 0,
                    TargetFloorHeightMm = resolvedLevel != null ? GetFloorHeightMm(resolvedLevel, targetStories) : 0,
                    HasPlanViewRange = source.HasPlanViewRange,
                    TopPlaneStoryDelta = source.TopPlaneStoryDelta,
                    CutPlaneStoryDelta = source.CutPlaneStoryDelta,
                    BottomPlaneStoryDelta = source.BottomPlaneStoryDelta,
                    ViewDepthPlaneStoryDelta = source.ViewDepthPlaneStoryDelta,
                    TopPlaneOffsetMm = source.TopPlaneOffsetMm,
                    CutPlaneOffsetMm = source.CutPlaneOffsetMm,
                    BottomPlaneOffsetMm = source.BottomPlaneOffsetMm,
                    ViewDepthPlaneOffsetMm = source.ViewDepthPlaneOffsetMm,
                    AnnotationCount = source.AnnotationCount,
                    DimensionCount = source.DimensionCount,
                    TextNoteCount = source.TextNoteCount,
                    DetailElementCount = source.DetailElementCount,
                    DimensionStyleNames = source.DimensionStyleNames,
                    DimensionLayoutSummary = source.DimensionLayoutSummary,
                    HasUnderlay = source.HasUnderlay,
                    UnderlayBottomStoryDelta = source.UnderlayBottomStoryDelta,
                    UnderlayTopStoryDelta = source.UnderlayTopStoryDelta,
                    UnderlayOrientation = source.UnderlayOrientation,
                    SourcePhaseName = source.SourcePhaseName,
                    SourcePhaseFilterName = source.SourcePhaseFilterName,
                    ViewportTypeName = sourceViewportTypeName,
                    SourceViewportTypeId = sourceViewportType != null ? sourceViewportType.Id.ToString() : source.SourceViewportTypeId,
                    ResolvedViewportTypeName = source.ViewType == "明細表" || source.ViewType == "圖紙直接繪圖元件"
                        ? "不適用"
                        : (resolvedViewportType != null ? GetViewportTypeName(resolvedViewportType) : sourceViewportTypeName + "（B 無相同視埠類型）"),
                    ResolvedViewportTypeId = source.ViewType == "明細表" || source.ViewType == "圖紙直接繪圖元件"
                        ? string.Empty
                        : (resolvedViewportType != null ? resolvedViewportType.Id.ToString() : string.Empty),
                    IsDependentView = source.IsDependentView,
                    SourcePrimaryViewId = source.SourcePrimaryViewId,
                    SourcePrimaryViewName = source.SourcePrimaryViewName,
                    TargetPrimaryViewName = source.IsDependentView
                        ? ApplyViewName(source.SourcePrimaryViewName, building, floor)
                        : string.Empty
                });
            }
            return result;
        }

        private static string GetTitleBlockDisplayName(FamilySymbol symbol)
        {
            if (symbol == null) return "未偵測到圖框";
            string familyName = symbol.Family != null ? symbol.Family.Name : symbol.FamilyName;
            return string.Format("{0} : {1}", familyName, symbol.Name);
        }

        private static ElementType ResolveSourceViewportType(Document sourceDoc, SheetViewItemDto source)
        {
            if (sourceDoc == null || source == null) return null;

            long sourceViewportTypeId;
            if (long.TryParse(source.SourceViewportTypeId, out sourceViewportTypeId))
            {
                var savedType = sourceDoc.GetElement(new ElementId(sourceViewportTypeId)) as ElementType;
                if (savedType != null && savedType.Category != null &&
                    savedType.Category.Id.Value == (long)BuiltInCategory.OST_Viewports)
                    return savedType;
            }

            // 舊的分析結果可能尚未儲存視埠類型 ID。以視圖 ID 回到
            // A 圖紙上的實際 Viewport，不使用 DTO 的「預設視埠」預設值。
            long sourceViewId;
            if (!long.TryParse(source.ViewId, out sourceViewId)) return null;
            var viewport = new FilteredElementCollector(sourceDoc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .FirstOrDefault(v => v.ViewId.Value == sourceViewId);
            return viewport != null ? sourceDoc.GetElement(viewport.GetTypeId()) as ElementType : null;
        }

        private static string GetViewportTypeName(ElementType type)
        {
            if (type == null) return string.Empty;
            try
            {
                var nameParameter = type.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM);
                string parameterName = nameParameter != null ? nameParameter.AsString() : null;
                if (!string.IsNullOrWhiteSpace(parameterName)) return parameterName.Trim();
            }
            catch { }
            return (type.Name ?? string.Empty).Trim();
        }

        private static List<ElementType> GetAllViewportTypes(Document doc)
        {
            var result = new Dictionary<long, ElementType>();
            if (doc == null) return result.Values.ToList();

            Action<ElementType> addType = type =>
            {
                if (type != null) result[type.Id.Value] = type;
            };

            // Some Revit projects do not return every unused system-family type
            // from an OST_Viewports category collector. Keep that fast path, then
            // expand the actual Viewport system family through GetSimilarTypes().
            try
            {
                foreach (var type in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Viewports)
                    .WhereElementIsElementType()
                    .Cast<ElementType>())
                    addType(type);
            }
            catch { }

            try
            {
                var viewportTypeSeeds = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .Select(v => doc.GetElement(v.GetTypeId()) as ElementType)
                    .Where(t => t != null)
                    .GroupBy(t => t.Id.Value)
                    .Select(g => g.First())
                    .ToList();

                foreach (var seed in viewportTypeSeeds)
                {
                    addType(seed);
                    ICollection<ElementId> similarIds = null;
                    try { similarIds = seed.GetSimilarTypes(); } catch { }
                    if (similarIds == null) continue;
                    foreach (var id in similarIds)
                        addType(doc.GetElement(id) as ElementType);
                }
            }
            catch { }

            return result.Values.ToList();
        }

        private static FamilySymbol FindMatchingTitleBlockType(Document doc, string sourceDisplayName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(sourceDisplayName)) return null;
            var symbols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .ToList();
            var exact = symbols.FirstOrDefault(s => string.Equals(GetTitleBlockDisplayName(s), sourceDisplayName,
                StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Revit/UI/localised data can represent the same "Family : Type"
            // with full-width punctuation or different whitespace. Compare the
            // two semantic parts instead of rejecting the existing B symbol.
            string requested = sourceDisplayName.Replace('：', ':').Trim();
            int separator = requested.LastIndexOf(':');
            if (separator >= 0)
            {
                string requestedFamily = NormalizeMatchText(requested.Substring(0, separator));
                string requestedType = NormalizeMatchText(requested.Substring(separator + 1));
                var semantic = symbols.FirstOrDefault(s =>
                {
                    string family = s.Family != null ? s.Family.Name : s.FamilyName;
                    return NormalizeMatchText(family) == requestedFamily &&
                        NormalizeMatchText(s.Name) == requestedType;
                });
                if (semantic != null) return semantic;
            }

            string normalizedDisplay = NormalizeMatchText(requested);
            var normalizedExact = symbols.FirstOrDefault(s =>
                NormalizeMatchText(GetTitleBlockDisplayName(s)) == normalizedDisplay);
            if (normalizedExact != null) return normalizedExact;

            string typeName = separator >= 0 ? requested.Substring(separator + 1).Trim() : requested;
            string normalizedType = NormalizeMatchText(typeName);
            var typeMatches = symbols.Where(s => NormalizeMatchText(s.Name) == normalizedType).ToList();
            return typeMatches.Count == 1 ? typeMatches[0] : null;
        }

        private static ElementType FindMatchingViewportType(Document doc, string typeName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(typeName) || typeName == "不適用") return null;
            var types = GetAllViewportTypes(doc);
            string requestedBaseName = System.Text.RegularExpressions.Regex.Replace(typeName.Trim(), @"\s+\d+$", string.Empty);
            // Always honour an exact A-to-B viewport type match first.  The old
            // order entered the "untitled" heuristic before checking exact names,
            // which selected a different title style even when B already had A's.
            var exact = types.FirstOrDefault(t => string.Equals(GetViewportTypeName(t), typeName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            string normalizedRequestedName = NormalizeMatchText(typeName);
            var normalizedExact = types.FirstOrDefault(t =>
                string.Equals(NormalizeMatchText(GetViewportTypeName(t)), normalizedRequestedName, StringComparison.Ordinal));
            if (normalizedExact != null) return normalizedExact;
            if (requestedBaseName.StartsWith("無標題", StringComparison.OrdinalIgnoreCase))
            {
                var canonicalUntitled = types
                    .Where(t => !System.Text.RegularExpressions.Regex.IsMatch(GetViewportTypeName(t), @"\s+\d+$"))
                    .OrderByDescending(t => GetViewportTypeName(t).Contains("一般圖面配置標題符號") ? 100 : 0)
                    .ThenByDescending(t => GetViewportTypeName(t).Contains("無圖圈") ? 20 : 0)
                    .ThenBy(t => GetViewportTypeName(t).Contains("無尺寸") ? 1 : 0)
                    .ThenBy(t => GetViewportTypeName(t).Length)
                    .FirstOrDefault(t => GetViewportTypeName(t).Contains("一般圖面配置標題符號") ||
                        string.Equals(GetViewportTypeName(t), "沒有線條的標題", StringComparison.OrdinalIgnoreCase));
                if (canonicalUntitled != null) return canonicalUntitled;
            }
            // 測試過程可能已留下「無標題 8、9、10」等複本。將尾端自動編號移除後
            // 優先使用 B 專案原有的基礎類型，不再產生新的視埠類型。
            string baseName = requestedBaseName;
            var baseMatch = types.FirstOrDefault(t => string.Equals(GetViewportTypeName(t), baseName, StringComparison.OrdinalIgnoreCase));
            if (baseMatch != null) return baseMatch;

            string normalized = NormalizeMatchText(baseName);
            var scored = types.Select(t => new
                {
                    Type = t,
                    Name = NormalizeMatchText(System.Text.RegularExpressions.Regex.Replace(GetViewportTypeName(t), @"\s+\d+$", string.Empty))
                })
                .Select(x => new
                {
                    x.Type,
                    Score = x.Name == normalized ? 1000 :
                        (!string.IsNullOrEmpty(normalized) && (x.Name.Contains(normalized) || normalized.Contains(x.Name))) ? 500 :
                        GetViewportSemanticScore(baseName, GetViewportTypeName(x.Type))
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => System.Text.RegularExpressions.Regex.IsMatch(GetViewportTypeName(x.Type), @"\s+\d+$") ? 1 : 0)
                .ThenBy(x => GetViewportTypeName(x.Type).Length)
                .FirstOrDefault();
            // Never silently substitute an unrelated viewport style.  A wrong
            // title family is more harmful than keeping B's default and reporting
            // the missing mapping to the user.
            return scored != null && scored.Score >= 100 ? scored.Type : null;
        }

        private static int GetViewportSemanticScore(string sourceName, string targetName)
        {
            string source = sourceName ?? string.Empty;
            string target = targetName ?? string.Empty;
            int score = 0;
            foreach (string token in new[] { "無標題", "有線條的標題", "沒有線條的標題", "無圖圈", "無尺寸", "一般圖面配置標題符號" })
                if (source.Contains(token) && target.Contains(token)) score += 100;
            return score;
        }

        private static ElementType FindEquivalentViewportType(Document sourceDoc, ElementType sourceType, Document targetDoc,
            IList<ElementType> knownTargetTypes = null)
        {
            if (sourceType == null || targetDoc == null) return null;
            if (ReferenceEquals(sourceDoc, targetDoc)) return sourceType;
            var targetTypes = knownTargetTypes != null ? knownTargetTypes.ToList() : GetAllViewportTypes(targetDoc);

            string sourceName = GetViewportTypeName(sourceType);
            string sourceFamily = NormalizeMatchText(sourceType.FamilyName);
            var exact = targetTypes.FirstOrDefault(t =>
                string.Equals(GetViewportTypeName(t), sourceName, StringComparison.OrdinalIgnoreCase));
            // In project B, an explicitly same-named viewport type is the user's
            // intended mapping even when its internal referenced ElementIds differ
            // from project A. ElementIds are document-local and must not make the
            // type fall back to Revit's default.
            if (exact != null) return exact;

            var normalizedFamilyAndTypeMatch = targetTypes.FirstOrDefault(t =>
                string.Equals(NormalizeMatchText(t.FamilyName), sourceFamily, StringComparison.Ordinal) &&
                string.Equals(NormalizeMatchText(GetViewportTypeName(t)), NormalizeMatchText(sourceName), StringComparison.Ordinal));
            if (normalizedFamilyAndTypeMatch != null) return normalizedFamilyAndTypeMatch;

            var featureMatches = targetTypes
                .Select(t => new
                {
                    Type = t,
                    Score = GetViewportDefinitionMatchScore(sourceDoc, sourceType, targetDoc, t)
                })
                .Where(x => x.Score >= 500)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Type.Name.Length)
                .ToList();
            if (featureMatches.Count > 0) return featureMatches[0].Type;

            // Last safe fallback is the strict name/semantic matcher. It returns
            // null instead of choosing an unrelated title style.
            return FindMatchingViewportType(targetDoc, sourceName);
        }

        private static bool ViewportTypesHaveSameDefinition(Document sourceDoc, ElementType sourceType,
            Document targetDoc, ElementType targetType)
        {
            int score = GetViewportDefinitionMatchScore(sourceDoc, sourceType, targetDoc, targetType);
            // Exact names are accepted when neither project exposes a referenced
            // title-family parameter; otherwise the referenced family must match.
            return score >= 500 || (score >= 0 &&
                string.Equals(GetViewportTypeName(sourceType), GetViewportTypeName(targetType), StringComparison.OrdinalIgnoreCase));
        }

        private static int GetViewportDefinitionMatchScore(Document sourceDoc, ElementType sourceType,
            Document targetDoc, ElementType targetType)
        {
            int score = string.Equals(NormalizeMatchText(GetViewportTypeName(sourceType)),
                NormalizeMatchText(GetViewportTypeName(targetType)), StringComparison.Ordinal) ? 100 : 0;
            int referencedMatches = 0;
            int referencedMismatches = 0;
            foreach (Parameter sourceParameter in sourceType.Parameters)
            {
                if (sourceParameter == null || sourceParameter.Definition == null) continue;
                var targetParameter = targetType.LookupParameter(sourceParameter.Definition.Name);
                if (targetParameter == null || targetParameter.StorageType != sourceParameter.StorageType) continue;
                try
                {
                    if (sourceParameter.StorageType == StorageType.ElementId)
                    {
                        string sourceReference = GetReferencedTypeSignature(sourceDoc, sourceParameter.AsElementId());
                        string targetReference = GetReferencedTypeSignature(targetDoc, targetParameter.AsElementId());
                        if (string.IsNullOrEmpty(sourceReference) || string.IsNullOrEmpty(targetReference)) continue;
                        if (string.Equals(sourceReference, targetReference, StringComparison.OrdinalIgnoreCase))
                        {
                            referencedMatches++;
                            score += 500;
                        }
                        else referencedMismatches++;
                    }
                    else if (sourceParameter.StorageType == StorageType.Integer &&
                        sourceParameter.AsInteger() == targetParameter.AsInteger())
                    {
                        string parameterName = sourceParameter.Definition.Name ?? string.Empty;
                        if (parameterName.IndexOf("標題", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            parameterName.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            parameterName.IndexOf("延伸線", StringComparison.OrdinalIgnoreCase) >= 0)
                            score += 25;
                    }
                }
                catch { }
            }
            if (referencedMismatches > 0 && referencedMatches == 0) return -1;
            return score;
        }

        private static string GetReferencedTypeSignature(Document doc, ElementId id)
        {
            if (doc == null || id == null || id == ElementId.InvalidElementId) return string.Empty;
            var element = doc.GetElement(id);
            if (element == null) return string.Empty;
            var symbol = element as FamilySymbol;
            string family = symbol != null ? symbol.FamilyName : string.Empty;
            string category = element.Category != null ? element.Category.Id.Value.ToString() : string.Empty;
            return category + "|" + family + "|" + (element.Name ?? string.Empty);
        }

        private static string GetResolvedViewportTypeName(Document targetDoc, string sourceTypeName)
        {
            var type = FindMatchingViewportType(targetDoc, sourceTypeName);
            return type != null ? type.Name : sourceTypeName + "（建立時由來源複製）";
        }

        private static string GetResolvedViewportTypeId(Document targetDoc, string sourceTypeName)
        {
            var type = FindMatchingViewportType(targetDoc, sourceTypeName);
            return type != null ? type.Id.ToString() : string.Empty;
        }

        private static string ApplyResourceName(string sourceName, string floor)
        {
            if (string.IsNullOrWhiteSpace(sourceName) || sourceName == "-") return "-";
            return System.Text.RegularExpressions.Regex.Replace(sourceName, @"(?i)(?:B\d+|\d+F)L?",
                floor.EndsWith("F", StringComparison.OrdinalIgnoreCase) ? floor + "L" : floor);
        }

        private static string NormalizeBuildingName(string building)
        {
            if (string.IsNullOrWhiteSpace(building)) return string.Empty;
            int index = building.IndexOfAny(new[] { '(', '（' });
            string name = (index > 0 ? building.Substring(0, index) : building).Trim();
            // 單棟模型的「全棟」卡片不是真正的棟別名稱，不要加進圖名
            return name == "全棟" ? string.Empty : name;
        }

        private static string ApplyNameFormula(DrawingRuleDto rule, string building, string floor)
        {
            // 自動偵測的規則必須保留 A 圖紙的完整語意。例如「B棟、A棟1FL」
            // 只能把 A棟的樓層改為 2FL，不可把 B棟文字刪除。
            if (rule != null && !string.IsNullOrWhiteSpace(rule.SourceSheetName))
                return ApplyFloorAwareName(rule.SourceSheetName, building, floor);
            string formula = string.IsNullOrWhiteSpace(rule.SheetNameFormula) ? rule.SourceSheetName : rule.SheetNameFormula;
            return (formula ?? string.Empty)
                .Replace("{棟別}", NormalizeBuildingName(building))
                .Replace("{樓層}", floor.EndsWith("F", StringComparison.OrdinalIgnoreCase) ? floor + "L" : floor);
        }

        private static string ApplyViewName(string sourceName, string building, string floor)
        {
            string mapped = string.IsNullOrWhiteSpace(sourceName)
                ? ApplyNameFormula(new DrawingRuleDto(), building, floor)
                : ApplyFloorAwareName(sourceName, building, floor);

            // Sheet names can occasionally be generic, but a generated model view
            // must be identifiable per storey. In an A == B workflow a generic
            // source name would otherwise produce the exact same requested name on
            // 2F, 3F, 4F... and Revit rejects the later views as duplicates.
            string cleanBuilding = NormalizeBuildingName(building);
            string targetFloor = floor != null && floor.EndsWith("F", StringComparison.OrdinalIgnoreCase)
                ? floor + "L" : (floor ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(targetFloor) && !ContainsFloorToken(mapped, targetFloor))
            {
                if (!string.IsNullOrWhiteSpace(cleanBuilding) &&
                    mapped.StartsWith(cleanBuilding, StringComparison.OrdinalIgnoreCase))
                    mapped = cleanBuilding + targetFloor + mapped.Substring(cleanBuilding.Length);
                else
                    mapped = cleanBuilding + targetFloor + mapped;
            }
            return mapped;
        }

        private static bool ContainsFloorToken(string text, string floorToken)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(floorToken)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(text,
                @"(?i)(?<![0-9])" + System.Text.RegularExpressions.Regex.Escape(floorToken) + @"(?![0-9])");
        }

        private static string ApplyFloorAwareName(string sourceName, string building, string floor)
        {
            if (string.IsNullOrWhiteSpace(sourceName)) return string.Empty;
            string cleanBuilding = NormalizeBuildingName(building);
            string targetFloor = floor.EndsWith("F", StringComparison.OrdinalIgnoreCase) ? floor + "L" : floor;
            string result = sourceName;
            if (!string.IsNullOrWhiteSpace(cleanBuilding))
            {
                string buildingFloorPattern = string.Format(@"(?i)({0})\s*(?:B[1-9]\d*(?:FL|F)?|[1-9]\d?F(?:L)?)",
                    System.Text.RegularExpressions.Regex.Escape(cleanBuilding));
                if (System.Text.RegularExpressions.Regex.IsMatch(result, buildingFloorPattern))
                    return System.Text.RegularExpressions.Regex.Replace(result, buildingFloorPattern,
                        m => m.Groups[1].Value + targetFloor);
            }
            // A 名稱只有另一棟別時，以目標棟別取代；若名稱已同時包含目標棟別，
            // 則上方分支會只改目標棟別的樓層，保留其他棟別文字。
            if (!string.IsNullOrWhiteSpace(cleanBuilding))
            {
                foreach (var convention in FactoryConventions.Current.NamedBuildings)
                {
                    string sourceBuilding = convention.DisplayName;
                    if (!string.Equals(sourceBuilding, cleanBuilding, StringComparison.OrdinalIgnoreCase) &&
                        result.Contains(sourceBuilding))
                    {
                        bool buildingBVariant = convention.HasBasementVariant &&
                            System.Text.RegularExpressions.Regex.IsMatch(result,
                                System.Text.RegularExpressions.Regex.Escape(sourceBuilding) + @"\s*B[1-9]\d*F(?:L)?",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        result = result.Replace(sourceBuilding, cleanBuilding);
                        if (buildingBVariant)
                        {
                            string variantPattern = System.Text.RegularExpressions.Regex.Escape(cleanBuilding) +
                                @"\s*B[1-9]\d*F(?:L)?";
                            return System.Text.RegularExpressions.Regex.Replace(result, variantPattern,
                                cleanBuilding + targetFloor + "B", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        }
                        break;
                    }
                }
            }
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?i)(?:B[1-9]\d*(?:FL|F)?|[1-9]\d?F(?:L)?)", targetFloor);
            return result.Contains(cleanBuilding) ? result : cleanBuilding + result;
        }

        private static bool IsLevelBoundPlanView(string viewType)
        {
            return viewType == "平面圖" || viewType == "結構平面圖" || viewType == "天花板圖" ||
                string.Equals(viewType, "EngineeringPlan", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(viewType, "FloorPlan", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(viewType, "CeilingPlan", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetResolvedLevelText(string floor, LevelBindingOption option)
        {
            string suffix = option == LevelBindingOption.PreviousLevel ? "上一層" : option == LevelBindingOption.NextLevel ? "下一層" : "本樓層";
            return string.Format("{0} ({1})", floor, suffix);
        }

        public async Task<GenerationResultDto> ExecuteGenerationAsync(GenerateRequestDto request, IProgress<GenerationProgressDto> progress, CancellationToken ct)
        {
            // 優先使用使用者在預覽頁已確認的快照，避免按下建立後又重新掃描
            // 大型／雲端 Revit 模型，造成進度遮罩長時間停在第一步之前。
            var previewItems = request.SelectedPreviewItems != null && request.SelectedPreviewItems.Count > 0
                ? request.SelectedPreviewItems.ToList()
                : await GetPreviewItemsAsync(request);
            var selectedNumbers = new HashSet<string>(request.SelectedSheetNumbers ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            previewItems = (previewItems ?? new List<SheetPreviewItemDto>())
                .Where(p => !p.TargetSheetExists && p.Status != UiItemStatus.Error &&
                    (selectedNumbers.Count == 0 || selectedNumbers.Contains(p.SheetNumber)))
                .ToList();

            int total = previewItems.Count;
            int createdSheets = 0;
            int createdViews = 0;
            int createdSchedules = 0;
            int createdLegends = 0;
            int failed = 0;
            int needsReview = 0;
            string firstSheetNumber = string.Empty;
            var summaries = new List<string>();
            var unresolvedIssues = new List<IssueItemDto>();
            var successfullyCreatedItems = new List<SheetPreviewItemDto>();
            var started = DateTime.Now;
            // The same source templates are used by many sheets. Keep this cache
            // for one generation run only so expensive template fingerprints and
            // full target-template collectors are not repeated for every view.
            var batchCache = new GenerationBatchCache();

            for (int i = 0; i < total; i++)
            {
                if (ct.IsCancellationRequested) break;

                var item = previewItems[i];
                if (progress != null)
                {
                    progress.Report(new GenerationProgressDto
                    {
                        CurrentIndex = i + 1,
                        TotalCount = total,
                        CurrentItemNumber = item.SheetNumber,
                        CurrentItemName = item.SheetName,
                        CurrentStepDescription = string.Format("[4/6] 正在建立圖紙與放置視圖 ({0}/{1})...", i + 1, total)
                    });
                }

                if (_handler == null || RevitApp.ExternalEvent == null)
                    throw new InvalidOperationException("Revit ExternalEvent 尚未初始化，無法建立圖紙。");

                try
                {
                    var itemResult = await _handler.RunAsync(app =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var sourceDoc = FindDocument(app, request.SourceProjectName, true, true);
                        var targetDoc = FindDocument(app, request.TargetProjectName, true);
                        if (sourceDoc == null || targetDoc == null)
                            return new GenerationItemResult { Error = "找不到來源或目標專案" };
                        if (targetDoc.IsLinked)
                            return new GenerationItemResult { Error = "目標文件是 Revit Link，不能建立 Transaction；請改選直接開啟的主專案" };
                        if (targetDoc.IsReadOnly)
                            return new GenerationItemResult { Error = "目標專案為唯讀" };

                        EventHandler<DialogBoxShowingEventArgs> renameDialogHandler = SuppressLevelViewRenameDialog;
                        app.DialogBoxShowing += renameDialogHandler;
                        try
                        {
                            using (var trans = new Transaction(targetDoc, string.Format("BIM Drawing Factory: {0}", item.SheetNumber)))
                            {
                                if (trans.Start() != TransactionStatus.Started)
                                    return new GenerationItemResult { Error = "無法啟動 Revit Transaction" };
                                try
                                {
                                    ct.ThrowIfCancellationRequested();
                                    var failureOptions = trans.GetFailureHandlingOptions();
                                    failureOptions.SetFailuresPreprocessor(new BatchWarningPreprocessor());
                                    trans.SetFailureHandlingOptions(failureOptions);
                                    var protectedLevels = CaptureProtectedLevels(targetDoc);
                                    var warnings = new List<string>();
                                    var titleBlockType = ResolveTitleBlockType(sourceDoc, targetDoc, item, warnings);
                                    if (titleBlockType == null)
                                        warnings.Add("無法取得基準圖框，已建立無圖框圖紙");
                                    var sheet = ViewSheet.Create(targetDoc,
                                        titleBlockType != null ? titleBlockType.Id : ElementId.InvalidElementId);
                                    sheet.SheetNumber = item.SheetNumber;
                                    sheet.Name = item.SheetName;
                                    int schedules;
                                    int legends;
                                    int views = CreateViewsForSheet(sourceDoc, targetDoc, sheet, item, request,
                                        out schedules, out legends, warnings, batchCache, stage =>
                                        {
                                            if (progress == null) return;
                                            progress.Report(new GenerationProgressDto
                                            {
                                                CurrentIndex = i + 1,
                                                TotalCount = total,
                                                CurrentItemNumber = item.SheetNumber,
                                                CurrentItemName = item.SheetName,
                                                CurrentStepDescription = stage
                                            });
                                        }, ct);
                                    ct.ThrowIfCancellationRequested();
                                    // Diagnostics belong in the generation result,
                                    // not as permanent TextNotes on production sheets.
                                    EnsureProtectedLevelsUnchanged(targetDoc, protectedLevels);
                                    trans.Commit();
                                    return new GenerationItemResult
                                    {
                                        SheetNumber = sheet.SheetNumber,
                                        CreatedViews = views,
                                        CreatedSchedules = schedules,
                                        CreatedLegends = legends,
                                        Warnings = warnings
                                    };
                                }
                                catch (OperationCanceledException)
                                {
                                    if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                                    using (var fallback = new Transaction(targetDoc, "BIM Drawing Factory: 建立降級圖紙"))
                                    {
                                        if (fallback.Start() != TransactionStatus.Started)
                                            return new GenerationItemResult { Error = ex.Message };
                                        try
                                        {
                                            var fallbackFailureOptions = fallback.GetFailureHandlingOptions();
                                            fallbackFailureOptions.SetFailuresPreprocessor(new BatchWarningPreprocessor());
                                            fallback.SetFailureHandlingOptions(fallbackFailureOptions);
                                            var fallbackSheet = ViewSheet.Create(targetDoc, ElementId.InvalidElementId);
                                            fallbackSheet.SheetNumber = item.SheetNumber;
                                            fallbackSheet.Name = item.SheetName;
                                            var warnings = new List<string> { "主要建立流程未完成：" + ex.Message };
                                            fallback.Commit();
                                            return new GenerationItemResult { SheetNumber = fallbackSheet.SheetNumber, Warnings = warnings };
                                        }
                                        catch (Exception fallbackEx)
                                        {
                                            if (fallback.GetStatus() == TransactionStatus.Started) fallback.RollBack();
                                            return new GenerationItemResult { Error = fallbackEx.Message };
                                        }
                                    }
                                }
                            }
                        }
                        finally { app.DialogBoxShowing -= renameDialogHandler; }
                    }, ct);

                    if (string.IsNullOrEmpty(itemResult.Error))
                    {
                        successfullyCreatedItems.Add(item);
                        createdSheets++;
                        createdViews += itemResult.CreatedViews;
                        createdSchedules += itemResult.CreatedSchedules;
                        createdLegends += itemResult.CreatedLegends;
                        if (string.IsNullOrEmpty(firstSheetNumber)) firstSheetNumber = itemResult.SheetNumber;
                        if (itemResult.Warnings != null && itemResult.Warnings.Count > 0)
                        {
                            needsReview++;
                            int warningIndex = 0;
                            foreach (var warning in itemResult.Warnings.Distinct())
                                unresolvedIssues.Add(CreateIssue(string.Format("GEN-{0}-{1}", item.SheetNumber, warningIndex++),
                                    IssueSeverity.Warning, item.SheetFloorName, item.SheetNumber, warning,
                                    "請在指定視圖依品類與 ElementId 檢查；圖紙與其他成功內容均已保留"));
                            summaries.Add(string.Format("{0}  {1} — 已建立；外掛待補製：{2}", itemResult.SheetNumber,
                                item.SheetName, string.Join("；", itemResult.Warnings.Take(3))));
                        }
                        else summaries.Add(string.Format("{0}  {1}", itemResult.SheetNumber, item.SheetName));
                    }
                    else
                    {
                        failed++;
                        summaries.Add(string.Format("失敗：{0}  {1} — {2}", item.SheetNumber, item.SheetName, itemResult.Error));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    failed++;
                    summaries.Add(string.Format("失敗：{0}  {1} — {2}", item.SheetNumber, item.SheetName, ex.Message));
                }
            }

            // Phase 5 deliberately begins only after every sheet/view transaction
            // above has completed. Tag scanning and creation can therefore never
            // slow or roll back the batch drawing creation phase.
            if (!ct.IsCancellationRequested && request.EnableDeferredTagging)
            {
                var taggingItems = successfullyCreatedItems
                    .Where(i => GetDeferredTaggingRule(i.SheetName) != null).ToList();
                bool nativeTagModeAvailable = true;
                for (int tagIndex = 0; tagIndex < taggingItems.Count; tagIndex++)
                {
                    if (ct.IsCancellationRequested) break;
                    var item = taggingItems[tagIndex];
                    if (progress != null)
                    {
                        progress.Report(new GenerationProgressDto
                        {
                            CurrentIndex = tagIndex + 1,
                            TotalCount = taggingItems.Count,
                            CurrentItemNumber = item.SheetNumber,
                            CurrentItemName = item.SheetName,
                            CurrentStepDescription = string.Format("[5/6] 圖紙已全部建立，正在套用指定標籤 ({0}/{1})...",
                                tagIndex + 1, taggingItems.Count)
                        });
                    }
                    try
                    {
                        // Prefer Revit's own batch command. Its dialog is allowed
                        // only a short fail-fast window; one failure disables the
                        // native path for the rest of this run, so a 40-sheet batch
                        // can never accumulate per-sheet modal wait time.
                        if (nativeTagModeAvailable)
                        {
                            if (progress != null)
                                progress.Report(new GenerationProgressDto
                                {
                                    CurrentIndex = tagIndex + 1,
                                    TotalCount = taggingItems.Count,
                                    CurrentItemNumber = item.SheetNumber,
                                    CurrentItemName = item.SheetName,
                                    CurrentStepDescription = "[5/6] 正在使用 Revit 內建『全部加上標籤』（只處理主模型）..."
                                });
                            var nativeResult = await TryApplyNativeTagAllFastAsync(request, item, ct);
                            if (nativeResult.Succeeded)
                            {
                                await ResolveDeferredTagCollisionsAsync(request, item, ct);
                                continue;
                            }
                            nativeTagModeAvailable = false;
                        }

                        if (progress != null)
                            progress.Report(new GenerationProgressDto
                            {
                                CurrentIndex = tagIndex + 1,
                                TotalCount = taggingItems.Count,
                                CurrentItemNumber = item.SheetNumber,
                                CurrentItemName = item.SheetName,
                                CurrentStepDescription = "[5/6] 內建模式不可用，正在使用快速 API 標籤（主模型優先）..."
                            });

                        var tagResult = await _handler.RunAsync(app =>
                        {
                            ct.ThrowIfCancellationRequested();
                            var sourceDoc = FindDocument(app, request.SourceProjectName, true, true);
                            var targetDoc = FindDocument(app, request.TargetProjectName, true);
                            if (sourceDoc == null || targetDoc == null)
                                return new DeferredTaggingResult { Warnings = new List<string> { "標籤階段找不到來源或目標專案" } };
                            using (var trans = new Transaction(targetDoc, "BIM Drawing Factory: 全部加上標籤 " + item.SheetNumber))
                            {
                                if (trans.Start() != TransactionStatus.Started)
                                    return new DeferredTaggingResult { Warnings = new List<string> { "標籤階段無法啟動 Transaction" } };
                                try
                                {
                                    var options = trans.GetFailureHandlingOptions();
                                    options.SetFailuresPreprocessor(new BatchWarningPreprocessor());
                                    trans.SetFailureHandlingOptions(options);
                                    var result = ApplyDeferredTags(sourceDoc, targetDoc, item, batchCache, ct);
                                    trans.Commit();
                                    return result;
                                }
                                catch (OperationCanceledException)
                                {
                                    if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                                    return new DeferredTaggingResult { Warnings = new List<string> { "標籤階段失敗：" + ex.Message } };
                                }
                            }
                        }, ct);
                        if (tagResult != null && tagResult.Warnings != null && tagResult.Warnings.Count > 0)
                        {
                            needsReview++;
                            int warningIndex = 0;
                            foreach (var warning in tagResult.Warnings.Distinct())
                                unresolvedIssues.Add(CreateIssue(string.Format("TAG-{0}-{1}", item.SheetNumber, warningIndex++),
                                    IssueSeverity.Warning, item.SheetFloorName, item.SheetNumber, warning,
                                    "圖紙已完成；請只檢查此圖的標籤結果"));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        needsReview++;
                        unresolvedIssues.Add(CreateIssue("TAG-" + item.SheetNumber, IssueSeverity.Warning,
                            item.SheetFloorName, item.SheetNumber, "標籤階段中斷：" + ex.Message,
                            "圖紙與視圖已完成，不受標籤階段影響"));
                    }
                }
            }

            // Dimensions are a separate final phase.  The drawing and tagging
            // transactions have already committed, so geometry analysis here can
            // neither slow down nor roll back sheet creation.
            if (!ct.IsCancellationRequested && request.EnableDeferredDimensioning)
            {
                var dimensionItems = successfullyCreatedItems
                    .Where(i => GetDeferredDimensionRules(i.SheetName).Count > 0).ToList();
                for (int dimensionIndex = 0; dimensionIndex < dimensionItems.Count; dimensionIndex++)
                {
                    if (ct.IsCancellationRequested) break;
                    var item = dimensionItems[dimensionIndex];
                    if (progress != null)
                    {
                        progress.Report(new GenerationProgressDto
                        {
                            CurrentIndex = dimensionIndex + 1,
                            TotalCount = dimensionItems.Count,
                            CurrentItemNumber = item.SheetNumber,
                            CurrentItemName = item.SheetName,
                            CurrentStepDescription = string.Format("[6/6] 圖紙與標籤已完成，正在建立尺寸 ({0}/{1})...",
                                dimensionIndex + 1, dimensionItems.Count)
                        });
                    }
                    try
                    {
                        var dimensionResult = await _handler.RunAsync(app =>
                        {
                            ct.ThrowIfCancellationRequested();
                            var sourceDoc = FindDocument(app, request.SourceProjectName, true, true);
                            var targetDoc = FindDocument(app, request.TargetProjectName, true);
                            if (sourceDoc == null || targetDoc == null)
                                return new DeferredDimensionResult { Warnings = new List<string> { "尺寸階段找不到來源或目標專案" } };
                            using (var trans = new Transaction(targetDoc, "BIM Drawing Factory: 建立尺寸 " + item.SheetNumber))
                            {
                                if (trans.Start() != TransactionStatus.Started)
                                    return new DeferredDimensionResult { Warnings = new List<string> { "尺寸階段無法啟動 Transaction" } };
                                try
                                {
                                    var commitWarnings = new List<string>();
                                    var options = trans.GetFailureHandlingOptions();
                                    options.SetFailuresPreprocessor(new DimensionFailurePreprocessor(commitWarnings));
                                    options.SetForcedModalHandling(false);
                                    options.SetClearAfterRollback(true);
                                    trans.SetFailureHandlingOptions(options);
                                    var value = ApplyDeferredDimensions(sourceDoc, targetDoc, item, ct);
                                    var commitStatus = trans.Commit();
                                    if (value.Warnings == null) value.Warnings = new List<string>();
                                    value.Warnings.AddRange(commitWarnings);
                                    if (commitStatus != TransactionStatus.Committed)
                                        value.Warnings.Add("尺寸 Transaction 未提交；本張無效尺寸已回復，不影響圖紙與標籤");
                                    return value;
                                }
                                catch (OperationCanceledException)
                                {
                                    if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                                    return new DeferredDimensionResult { Warnings = new List<string> { "尺寸階段失敗：" + ex.Message } };
                                }
                            }
                        }, ct);
                        if (dimensionResult != null && dimensionResult.Warnings != null && dimensionResult.Warnings.Count > 0)
                        {
                            needsReview++;
                            int warningIndex = 0;
                            foreach (var warning in dimensionResult.Warnings.Distinct())
                                unresolvedIssues.Add(CreateIssue(string.Format("DIM-{0}-{1}", item.SheetNumber, warningIndex++),
                                    IssueSeverity.Warning, item.SheetFloorName, item.SheetNumber, warning,
                                    "圖紙與標籤已完成；請只檢查此圖的尺寸結果"));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        needsReview++;
                        unresolvedIssues.Add(CreateIssue("DIM-" + item.SheetNumber, IssueSeverity.Warning,
                            item.SheetFloorName, item.SheetNumber, "尺寸階段中斷：" + ex.Message,
                            "圖紙與標籤已完成，不受尺寸階段影響"));
                    }
                }
            }

            return new GenerationResultDto
            {
                IsSuccess = failed == 0 && createdSheets > 0,
                TargetBuilding = request != null ? request.BuildingName : string.Empty,
                TargetFloorsSummary = request != null && request.SelectedFloorNames != null ? string.Join(", ", request.SelectedFloorNames) : "18F",
                CreatedSheetsCount = createdSheets,
                CreatedViewsCount = createdViews,
                CreatedSchedulesCount = createdSchedules,
                CreatedLegendsCount = createdLegends,
                SkippedCount = Math.Max(0, total - createdSheets - failed),
                ManualReviewCount = needsReview,
                FailedCount = failed,
                ElapsedTime = (DateTime.Now - started).ToString(@"mm\:ss"),
                FirstCreatedSheetNumber = firstSheetNumber,
                GeneratedSheetSummaries = summaries,
                UnresolvedIssues = unresolvedIssues
            };
        }

        private class GenerationItemResult
        {
            public string SheetNumber { get; set; }
            public int CreatedViews { get; set; }
            public int CreatedSchedules { get; set; }
            public int CreatedLegends { get; set; }
            public List<string> Warnings { get; set; }
            public string Error { get; set; }
        }

        private sealed class DeferredTaggingResult
        {
            public int CreatedCount { get; set; }
            public List<string> Warnings { get; set; }
        }

        private sealed class DeferredDimensionResult
        {
            public int CreatedCount { get; set; }
            public List<string> Warnings { get; set; }
        }

        private enum DimensionSemantic
        {
            ColumnCenter,
            BeamEdge,
            FloorOpening,
            WallOpening,
            WallEdge
        }

        private sealed class DeferredDimensionRule
        {
            public DimensionSemantic Semantic { get; set; }
            public string StyleName { get; set; }
            public string DisplayName { get; set; }
        }

        private sealed class DimensionReferenceCandidate
        {
            public Reference Reference { get; set; }
            public double Coordinate { get; set; }
        }

        private static List<DeferredDimensionRule> GetDeferredDimensionRules(string sheetName)
        {
            // 尺寸標註規則（圖名關鍵字 → 標註項目與尺寸類型）寫在 conventions.json 的 DimensionRules。
            var rules = new List<DeferredDimensionRule>();
            var convention = FactoryConventions.Current.FindDimensionRule(sheetName);
            if (convention == null) return rules;
            foreach (var item in convention.Items)
            {
                DimensionSemantic semantic;
                if (item == null || string.IsNullOrWhiteSpace(item.StyleName) ||
                    !Enum.TryParse(item.Semantic ?? string.Empty, true, out semantic))
                    continue;
                rules.Add(new DeferredDimensionRule
                {
                    Semantic = semantic,
                    StyleName = item.StyleName,
                    DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Semantic : item.DisplayName
                });
            }
            return rules;
        }

        private static DeferredDimensionResult ApplyDeferredDimensions(Document sourceDoc, Document targetDoc,
            SheetPreviewItemDto item, CancellationToken cancellationToken)
        {
            var result = new DeferredDimensionResult { Warnings = new List<string>() };
            var rules = GetDeferredDimensionRules(item != null ? item.SheetName : string.Empty);
            if (rules.Count == 0 || targetDoc == null) return result;

            var targetSheet = new FilteredElementCollector(targetDoc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .FirstOrDefault(s => string.Equals(s.SheetNumber, item.SheetNumber, StringComparison.OrdinalIgnoreCase));
            if (targetSheet == null)
            {
                result.Warnings.Add("尺寸：找不到目標圖紙 " + item.SheetNumber);
                return result;
            }

            var plannedNames = new HashSet<string>((item.PlannedViews ?? new List<SheetViewItemDto>())
                .Where(v => v.ViewType != "圖例" && v.ViewType != "明細表")
                .Select(v => v.TargetViewName), StringComparer.OrdinalIgnoreCase);
            var targetView = targetSheet.GetAllViewports()
                .Select(id => targetDoc.GetElement(id) as Viewport)
                .Where(vp => vp != null)
                .Select(vp => targetDoc.GetElement(vp.ViewId) as ViewPlan)
                .Where(v => v != null && !v.IsTemplate)
                .OrderByDescending(v => plannedNames.Contains(v.Name))
                .FirstOrDefault();
            if (targetView == null)
            {
                result.Warnings.Add("尺寸：圖紙上沒有可處理的主平面視圖");
                return result;
            }

            Autodesk.Revit.DB.View sourceView = null;
            var planned = (item.PlannedViews ?? new List<SheetViewItemDto>())
                .FirstOrDefault(v => string.Equals(v.TargetViewName, targetView.Name, StringComparison.OrdinalIgnoreCase));
            long sourceViewId;
            if (planned != null && long.TryParse(planned.ViewId, out sourceViewId))
                sourceView = sourceDoc.GetElement(new ElementId(sourceViewId)) as Autodesk.Revit.DB.View;
            if (sourceView == null)
            {
                var sourceSheet = new FilteredElementCollector(sourceDoc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .FirstOrDefault(s => string.Equals(s.SheetNumber, item.SourceStandardSheet, StringComparison.OrdinalIgnoreCase));
                if (sourceSheet != null)
                    sourceView = sourceSheet.GetAllViewports().Select(id => sourceDoc.GetElement(id) as Viewport)
                        .Where(vp => vp != null).Select(vp => sourceDoc.GetElement(vp.ViewId) as ViewPlan)
                        .FirstOrDefault(v => v != null && !v.IsTemplate);
            }

            // Column-centre rules are checked per direction below. One surviving
            // direction must not suppress the missing direction on a retry.
            var existingStyles = new HashSet<string>(new FilteredElementCollector(targetDoc, targetView.Id)
                .OfClass(typeof(Dimension)).Cast<Dimension>()
                .Select(d => targetDoc.GetElement(d.GetTypeId()) as DimensionType)
                .Where(t => t != null).Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

            int ruleIndex = 0;
            foreach (var styleGroup in rules.GroupBy(r => r.StyleName, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool containsColumnCenter = styleGroup.Any(r => r.Semantic == DimensionSemantic.ColumnCenter);
                if (!containsColumnCenter && existingStyles.Contains(styleGroup.Key)) continue;
                var dimensionType = ResolveExactDimensionType(sourceDoc, targetDoc, styleGroup.Key);
                if (dimensionType == null)
                {
                    result.Warnings.Add("尺寸：A、B 專案均找不到尺寸類型「" + styleGroup.Key + "」");
                    continue;
                }
                foreach (var rule in styleGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var existingAxes = rule.Semantic == DimensionSemantic.ColumnCenter
                        ? GetExistingDimensionAxes(targetDoc, targetView, dimensionType) : null;
                    int createdForRule = CreateDimensionsForRule(targetDoc, targetView, sourceDoc, sourceView,
                        rule, dimensionType, ruleIndex++, result.Warnings, cancellationToken, existingAxes);
                    result.CreatedCount += createdForRule;
                    if (createdForRule == 0 && (existingAxes == null || existingAxes.Count < 2))
                        result.Warnings.Add(string.Format("尺寸（{0}）：在 {1} 找不到足夠且可參照的當層構件",
                            rule.DisplayName, targetView.Name));
                }
            }
            return result;
        }

        private static DimensionType ResolveExactDimensionType(Document sourceDoc, Document targetDoc, string styleName)
        {
            Func<Document, DimensionType> find = doc => doc == null ? null : new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .FirstOrDefault(t => string.Equals(t.Name, styleName, StringComparison.OrdinalIgnoreCase));
            var targetType = find(targetDoc);
            if (targetType != null) return targetType;
            var sourceType = find(sourceDoc);
            if (sourceType == null || ReferenceEquals(sourceDoc, targetDoc)) return sourceType;
            try
            {
                var copied = ElementTransformUtils.CopyElements(sourceDoc, new List<ElementId> { sourceType.Id },
                    targetDoc, Transform.Identity, CreateCopyPasteOptions());
                return copied.Select(id => targetDoc.GetElement(id) as DimensionType).FirstOrDefault(t => t != null)
                    ?? find(targetDoc);
            }
            catch { return find(targetDoc); }
        }

        private static int CreateDimensionsForRule(Document targetDoc, ViewPlan targetView,
            Document sourceDoc, Autodesk.Revit.DB.View sourceView, DeferredDimensionRule rule,
            DimensionType dimensionType, int stackIndex, List<string> warnings,
            CancellationToken cancellationToken, HashSet<int> existingAxes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int created = 0;
            if (rule.Semantic == DimensionSemantic.ColumnCenter)
            {
                int sourceDriven = CreateSourceDrivenGridDimensions(sourceDoc, sourceView,
                    targetDoc, targetView, dimensionType, existingAxes, warnings, cancellationToken);
                created += sourceDriven;
                // Column-centre dimensions must be traceable to A. Never fall
                // back to an all-grid guessed chain, which can look plausible
                // while using the wrong references or placement.
                return sourceDriven;
            }
            var byAxis = CollectDimensionReferences(targetDoc, targetView, rule.Semantic, cancellationToken);
            for (int axis = 0; axis < 2; axis++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (existingAxes != null && existingAxes.Contains(axis)) continue;
                List<DimensionReferenceCandidate> candidates;
                if (!byAxis.TryGetValue(axis, out candidates)) continue;
                candidates = DeduplicateDimensionReferences(candidates);
                if (candidates.Count < 2) continue;

                // A single enormous reference array is both slow and fragile in
                // Revit. Cap each chain and report that the view needs review.
                if (candidates.Count > 80)
                {
                    candidates = candidates.Take(80).ToList();
                    warnings.Add(string.Format("尺寸（{0}）：{1} 方向參照超過 80 個，已限制處理量以避免卡頓",
                        rule.DisplayName, axis == 0 ? "水平" : "垂直"));
                }
                var ordered = candidates.OrderBy(c => c.Coordinate).ToList();
                // One stale Grid reference used to invalidate the entire large
                // chain. Short overlapping chains preserve the remaining spans.
                int chainSize = rule.Semantic == DimensionSemantic.ColumnCenter ? 10 : ordered.Count;
                for (int start = 0; start < ordered.Count - 1; start += Math.Max(1, chainSize - 1))
                {
                    var chain = ordered.Skip(start).Take(chainSize).ToList();
                    if (chain.Count < 2) break;
                    var array = new ReferenceArray();
                    foreach (var candidate in chain) array.Append(candidate.Reference);
                    var line = CreateLearnedDimensionLine(sourceDoc, sourceView, rule.StyleName,
                        targetView, chain, axis, stackIndex);
                    try
                    {
                        var dimension = targetDoc.Create.NewDimension(targetView, line, array, dimensionType);
                        if (dimension != null) created++;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(string.Format("尺寸（{0}／{1}）：Revit 拒絕建立尺寸鏈：{2}",
                            rule.DisplayName, axis == 0 ? "水平" : "垂直", ex.Message));
                    }
                    if (rule.Semantic != DimensionSemantic.ColumnCenter) break;
                }
            }
            return created;
        }

        private static int CreateSourceDrivenGridDimensions(Document sourceDoc,
            Autodesk.Revit.DB.View sourceView, Document targetDoc, ViewPlan targetView,
            DimensionType targetDimensionType, HashSet<int> existingAxes, List<string> warnings,
            CancellationToken cancellationToken)
        {
            if (sourceDoc == null || sourceView == null || targetDoc == null || targetView == null ||
                targetDimensionType == null) return 0;

            string styleName = targetDimensionType.Name;
            var allSourceDimensions = new FilteredElementCollector(sourceDoc, sourceView.Id)
                .OfClass(typeof(Dimension)).Cast<Dimension>()
                .Where(d => d.Curve is Line).ToList();
            var sourceDimensions = allSourceDimensions.Where(d =>
                {
                    var type = sourceDoc.GetElement(d.GetTypeId()) as DimensionType;
                    return type != null && string.Equals(type.Name, styleName, StringComparison.OrdinalIgnoreCase);
                }).ToList();
            // Older A sheets sometimes use a differently named dimension type.
            // The references are authoritative: a chain referencing at least
            // two Grids is still a column/grid-centre dimension.
            if (sourceDimensions.Count == 0)
                sourceDimensions = allSourceDimensions.Where(d => CountGridReferences(sourceDoc, d) >= 2).ToList();
            if (sourceDimensions.Count == 0)
            {
                AddWarningOnce(warnings, "柱心尺寸：A 對應主視圖沒有可辨識的軸線尺寸鏈，已停止而不猜測放置。");
                return 0;
            }

            var targetGrids = new FilteredElementCollector(targetDoc, targetView.Id)
                .OfClass(typeof(Grid)).Cast<Grid>().ToList();
            var gridsByName = targetGrids.GroupBy(g => NormalizeMatchText(g.Name))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            int created = 0;
            var initiallyExistingAxes = existingAxes != null
                ? new HashSet<int>(existingAxes) : new HashSet<int>();
            var createdAxes = new HashSet<int>();
            foreach (var sourceDimension in sourceDimensions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceLine = sourceDimension.Curve as Line;
                if (sourceLine == null) continue;
                var mappedLine = MapSourceDimensionLineToTarget(sourceView, targetView, sourceLine);
                if (mappedLine == null) continue;
                var direction = mappedLine.Direction.Normalize();
                int axis = Math.Abs(direction.X) >= Math.Abs(direction.Y) ? 0 : 1;
                if (initiallyExistingAxes.Contains(axis)) continue;

                var references = new ReferenceArray();
                var usedGridIds = new HashSet<long>();
                foreach (Reference sourceReference in sourceDimension.References)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourceGrid = sourceDoc.GetElement(sourceReference.ElementId) as Grid;
                    if (sourceGrid == null) continue;
                    Grid targetGrid;
                    if (!gridsByName.TryGetValue(NormalizeMatchText(sourceGrid.Name), out targetGrid)) continue;
                    if (!usedGridIds.Add(targetGrid.Id.Value)) continue;
                    references.Append(new Reference(targetGrid));
                }
                if (references.Size < 2) continue;
                try
                {
                    var createdDimension = targetDoc.Create.NewDimension(targetView, mappedLine,
                        references, targetDimensionType);
                    if (createdDimension != null)
                    {
                        created++;
                        createdAxes.Add(axis);
                    }
                }
                catch (Exception ex)
                {
                    AddWarningOnce(warnings, "柱心尺寸：依 A 圖軸線參照重建失敗，已改用後備方式。原因：" + ex.Message);
                }
            }
            if (existingAxes != null)
                foreach (int axis in createdAxes) existingAxes.Add(axis);
            if (created == 0)
                AddWarningOnce(warnings, "柱心尺寸：A 尺寸鏈的軸線名稱無法對應 B 主視圖，已停止而不猜測放置。");
            return created;
        }

        private static int CountGridReferences(Document doc, Dimension dimension)
        {
            if (doc == null || dimension == null || dimension.References == null) return 0;
            int count = 0;
            foreach (Reference reference in dimension.References)
                if (doc.GetElement(reference.ElementId) is Grid) count++;
            return count;
        }

        private static Line MapSourceDimensionLineToTarget(Autodesk.Revit.DB.View sourceView,
            ViewPlan targetView, Line sourceLine)
        {
            try
            {
                var sourceCrop = sourceView.CropBox;
                var targetCrop = targetView.CropBox;
                if (sourceCrop == null || targetCrop == null || sourceLine == null) return null;
                Func<XYZ, XYZ> mapPoint = sourcePoint =>
                {
                    XYZ sourceLocal = sourceCrop.Transform.Inverse.OfPoint(sourcePoint);
                    double sourceWidth = Math.Max(1e-9, sourceCrop.Max.X - sourceCrop.Min.X);
                    double sourceHeight = Math.Max(1e-9, sourceCrop.Max.Y - sourceCrop.Min.Y);
                    double u = (sourceLocal.X - sourceCrop.Min.X) / sourceWidth;
                    double v = (sourceLocal.Y - sourceCrop.Min.Y) / sourceHeight;
                    XYZ targetLocal = new XYZ(
                        targetCrop.Min.X + u * (targetCrop.Max.X - targetCrop.Min.X),
                        targetCrop.Min.Y + v * (targetCrop.Max.Y - targetCrop.Min.Y), 0);
                    XYZ mapped = targetCrop.Transform.OfPoint(targetLocal);
                    double z = targetView.GenLevel != null ? targetView.GenLevel.Elevation : targetView.Origin.Z;
                    return new XYZ(mapped.X, mapped.Y, z);
                };
                XYZ start = mapPoint(sourceLine.GetEndPoint(0));
                XYZ end = mapPoint(sourceLine.GetEndPoint(1));
                return start.DistanceTo(end) > 1e-6 ? Line.CreateBound(start, end) : null;
            }
            catch { return null; }
        }

        private static HashSet<int> GetExistingDimensionAxes(Document doc, Autodesk.Revit.DB.View view,
            DimensionType dimensionType)
        {
            var result = new HashSet<int>();
            if (doc == null || view == null || dimensionType == null) return result;
            foreach (var dimension in new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Dimension)).Cast<Dimension>()
                .Where(d => d.GetTypeId() == dimensionType.Id))
            {
                var line = dimension.Curve as Line;
                if (line == null) continue;
                var direction = line.Direction.Normalize();
                result.Add(Math.Abs(direction.X) >= Math.Abs(direction.Y) ? 0 : 1);
            }
            return result;
        }

        private static Dictionary<int, List<DimensionReferenceCandidate>> CollectDimensionReferences(
            Document doc, ViewPlan view, DimensionSemantic semantic, CancellationToken cancellationToken)
        {
            var result = new Dictionary<int, List<DimensionReferenceCandidate>>
            {
                { 0, new List<DimensionReferenceCandidate>() },
                { 1, new List<DimensionReferenceCandidate>() }
            };
            if (semantic == DimensionSemantic.ColumnCenter)
            {
                foreach (var grid in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Grid)).Cast<Grid>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = grid.Curve as Line;
                    if (line == null) continue;
                    var direction = line.Direction.Normalize();
                    int axis = Math.Abs(direction.Y) >= Math.Abs(direction.X) ? 0 : 1;
                    double coordinate = axis == 0 ? line.Origin.X : line.Origin.Y;
                    result[axis].Add(new DimensionReferenceCandidate { Reference = new Reference(grid), Coordinate = coordinate });
                }
                return result;
            }

            BuiltInCategory category = semantic == DimensionSemantic.BeamEdge
                ? BuiltInCategory.OST_StructuralFraming
                : semantic == DimensionSemantic.FloorOpening ? BuiltInCategory.OST_Floors
                : semantic == DimensionSemantic.WallEdge ? BuiltInCategory.OST_Walls
                : BuiltInCategory.OST_Doors;
            var elements = new FilteredElementCollector(doc, view.Id).OfCategory(category)
                .WhereElementIsNotElementType().Take(250).ToList();
            if (semantic == DimensionSemantic.WallOpening)
            {
                elements.AddRange(new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType().Take(100));
            }
            foreach (var element in elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddPlanarFaceReferences(element, view, result, cancellationToken);
            }
            return result;
        }

        private static void AddPlanarFaceReferences(Element element, Autodesk.Revit.DB.View view,
            Dictionary<int, List<DimensionReferenceCandidate>> result, CancellationToken cancellationToken)
        {
            try
            {
                var options = new Options { ComputeReferences = true, IncludeNonVisibleObjects = false, View = view };
                var geometry = element.get_Geometry(options);
                if (geometry == null) return;
                AddGeometryFaceReferences(geometry, Transform.Identity, result, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        private static void AddGeometryFaceReferences(GeometryElement geometry, Transform transform,
            Dictionary<int, List<DimensionReferenceCandidate>> result, CancellationToken cancellationToken)
        {
            if (geometry == null) return;
            foreach (var obj in geometry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var solid = obj as Solid;
                if (solid != null) AddSolidFaceReferences(solid, transform, result, cancellationToken);
                var instance = obj as GeometryInstance;
                if (instance == null) continue;
                // GetSymbolGeometry() keeps the original Revit references needed
                // by NewDimension. GetInstanceGeometry() returns transformed
                // copies whose references cannot reliably be dimensioned.
                var nestedTransform = transform.Multiply(instance.Transform);
                AddGeometryFaceReferences(instance.GetSymbolGeometry(), nestedTransform, result, cancellationToken);
            }
        }

        private static void AddSolidFaceReferences(Solid solid,
            Transform transform, Dictionary<int, List<DimensionReferenceCandidate>> result,
            CancellationToken cancellationToken)
        {
            if (solid == null || solid.Faces == null || solid.Volume <= 1e-9) return;
            foreach (Face rawFace in solid.Faces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var face = rawFace as PlanarFace;
                if (face == null || face.Reference == null) continue;
                var normal = transform.OfVector(face.FaceNormal).Normalize();
                int axis;
                if (Math.Abs(normal.X) > 0.98) axis = 0;
                else if (Math.Abs(normal.Y) > 0.98) axis = 1;
                else continue;
                var bounds = face.GetBoundingBox();
                var point = transform.OfPoint(face.Evaluate(new UV((bounds.Min.U + bounds.Max.U) * 0.5,
                    (bounds.Min.V + bounds.Max.V) * 0.5)));
                result[axis].Add(new DimensionReferenceCandidate
                {
                    Reference = face.Reference,
                    Coordinate = axis == 0 ? point.X : point.Y
                });
            }
        }

        private static List<DimensionReferenceCandidate> DeduplicateDimensionReferences(
            IEnumerable<DimensionReferenceCandidate> candidates)
        {
            const double tolerance = 0.00328084; // 1 mm in Revit internal feet
            var result = new List<DimensionReferenceCandidate>();
            foreach (var candidate in candidates.Where(c => c != null && c.Reference != null)
                .OrderBy(c => c.Coordinate))
            {
                if (result.Count == 0 || Math.Abs(result[result.Count - 1].Coordinate - candidate.Coordinate) > tolerance)
                    result.Add(candidate);
            }
            return result;
        }

        private static Line CreateLearnedDimensionLine(Document sourceDoc, Autodesk.Revit.DB.View sourceView,
            string styleName, ViewPlan targetView, List<DimensionReferenceCandidate> candidates,
            int axis, int stackIndex)
        {
            double normalized = -0.05;
            bool learned = false;
            if (sourceDoc != null && sourceView != null)
            {
                var sourceDimension = new FilteredElementCollector(sourceDoc, sourceView.Id)
                    .OfClass(typeof(Dimension)).Cast<Dimension>()
                    .FirstOrDefault(d =>
                    {
                        var type = sourceDoc.GetElement(d.GetTypeId()) as DimensionType;
                        var curve = d.Curve as Line;
                        if (type == null || curve == null || !string.Equals(type.Name, styleName, StringComparison.OrdinalIgnoreCase))
                            return false;
                        var direction = curve.Direction.Normalize();
                        return axis == 0 ? Math.Abs(direction.X) >= Math.Abs(direction.Y)
                            : Math.Abs(direction.Y) > Math.Abs(direction.X);
                    });
                var sourceLine = sourceDimension != null ? sourceDimension.Curve as Line : null;
                BoundingBoxXYZ sourceCrop = sourceView.CropBox;
                if (sourceLine != null && sourceCrop != null)
                {
                    double min = axis == 0 ? sourceCrop.Min.Y : sourceCrop.Min.X;
                    double max = axis == 0 ? sourceCrop.Max.Y : sourceCrop.Max.X;
                    double value = axis == 0 ? sourceLine.Origin.Y : sourceLine.Origin.X;
                    if (max - min > 1e-6)
                    {
                        normalized = (value - min) / (max - min);
                        learned = true;
                    }
                }
            }

            var crop = targetView.CropBox;
            double cropMin = axis == 0 ? crop.Min.Y : crop.Min.X;
            double cropMax = axis == 0 ? crop.Max.Y : crop.Max.X;
            double paperOffset = (10.0 + stackIndex * 7.0) * Math.Max(1, targetView.Scale) / 304.8;
            double constant = learned ? cropMin + normalized * (cropMax - cropMin) : cropMin - paperOffset;
            if (learned)
            {
                // Preserve A's side and relative placement, while separating
                // multiple semantic chains that share the same learned line.
                double middle = (cropMin + cropMax) * 0.5;
                constant += constant <= middle ? -stackIndex * 7.0 * targetView.Scale / 304.8
                    : stackIndex * 7.0 * targetView.Scale / 304.8;
            }
            double first = candidates.Min(c => c.Coordinate) - targetView.Scale * 5.0 / 304.8;
            double last = candidates.Max(c => c.Coordinate) + targetView.Scale * 5.0 / 304.8;
            double z = targetView.GenLevel != null ? targetView.GenLevel.Elevation : targetView.Origin.Z;
            return axis == 0
                ? Line.CreateBound(new XYZ(first, constant, z), new XYZ(last, constant, z))
                : Line.CreateBound(new XYZ(constant, first, z), new XYZ(constant, last, z));
        }

        private sealed class DeferredTaggingRule
        {
            public string FamilyName { get; set; }
            public string TypeName { get; set; }
            public BuiltInCategory TargetCategory { get; set; }
            public BuiltInCategory TagCategory { get; set; }
        }

        private static DeferredTaggingRule GetDeferredTaggingRule(string sheetName)
        {
            // 標籤規則（圖名關鍵字 → 標籤族群／類型）寫在 conventions.json 的 TaggingRules。
            var convention = FactoryConventions.Current.FindTaggingRule(sheetName);
            if (convention == null || string.IsNullOrWhiteSpace(convention.FamilyName)) return null;
            string category = (convention.TargetCategory ?? string.Empty).Trim();
            BuiltInCategory target, tag;
            if (string.Equals(category, "Walls", StringComparison.OrdinalIgnoreCase))
            {
                target = BuiltInCategory.OST_Walls; tag = BuiltInCategory.OST_WallTags;
            }
            else if (string.Equals(category, "Doors", StringComparison.OrdinalIgnoreCase))
            {
                target = BuiltInCategory.OST_Doors; tag = BuiltInCategory.OST_DoorTags;
            }
            else if (string.Equals(category, "Floors", StringComparison.OrdinalIgnoreCase))
            {
                target = BuiltInCategory.OST_Floors; tag = BuiltInCategory.OST_FloorTags;
            }
            else
            {
                return null;
            }
            return new DeferredTaggingRule
            {
                FamilyName = convention.FamilyName,
                TypeName = convention.TypeName ?? string.Empty,
                TargetCategory = target,
                TagCategory = tag
            };
        }

        private sealed class FastNativeTagResult
        {
            public bool Succeeded { get; set; }
            public string Message { get; set; }
        }

        private sealed class FastNativeTagPostResult
        {
            public bool Posted { get; set; }
            public string Message { get; set; }
        }

        private async Task<FastNativeTagResult> TryApplyNativeTagAllFastAsync(GenerateRequestDto request,
            SheetPreviewItemDto item, CancellationToken cancellationToken)
        {
            var rule = GetDeferredTaggingRule(item != null ? item.SheetName : string.Empty);
            if (rule == null) return new FastNativeTagResult { Message = "沒有對應標籤規則" };

            string categoryName = rule.TargetCategory == BuiltInCategory.OST_Walls ? "Walls" :
                rule.TargetCategory == BuiltInCategory.OST_Doors ? "Doors" : "Floors";
            using (var fastTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // The old implementation waited 45-50 seconds for every sheet.
                // Four seconds is sufficient for a native Revit modal to appear;
                // a miss disables native mode for the rest of the batch.
                fastTimeout.CancelAfter(TimeSpan.FromSeconds(4));
                var dialogTask = NativeTagAllDialogAutomation.ConfirmAsync(new NativeTagDialogRequest
                {
                    CategoryName = categoryName,
                    FamilyName = rule.FamilyName,
                    TypeName = rule.TypeName,
                    // Tagging linked elements can force Revit to enumerate every
                    // instance in large federated models. The requested drawings
                    // are created from target B's own model, so native fast mode
                    // deliberately follows Revit's normal local-model operation.
                    IncludeLinkedElements = false
                }, fastTimeout.Token);

                FastNativeTagPostResult postResult;
                try
                {
                    postResult = await _handler.RunAsync(app =>
                    {
                        fastTimeout.Token.ThrowIfCancellationRequested();
                        var sourceDoc = FindDocument(app, request.SourceProjectName, true, true);
                        var targetDoc = FindDocument(app, request.TargetProjectName, true);
                        var uiDoc = app.ActiveUIDocument;
                        if (sourceDoc == null || targetDoc == null)
                            return new FastNativeTagPostResult { Message = "找不到 A 或 B 專案" };
                        if (uiDoc == null || !ReferenceEquals(uiDoc.Document, targetDoc))
                            return new FastNativeTagPostResult { Message = "B 專案不是目前作用中的 Revit 文件" };

                        var sheet = new FilteredElementCollector(targetDoc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                            .FirstOrDefault(s => string.Equals(s.SheetNumber, item.SheetNumber, StringComparison.OrdinalIgnoreCase));
                        if (sheet == null) return new FastNativeTagPostResult { Message = "找不到目標圖紙" };
                        var plannedNames = new HashSet<string>((item.PlannedViews ?? new List<SheetViewItemDto>())
                            .Where(v => v.ViewType != "圖例" && v.ViewType != "明細表")
                            .Select(v => v.TargetViewName), StringComparer.OrdinalIgnoreCase);
                        var mainView = sheet.GetAllViewports().Select(id => targetDoc.GetElement(id) as Viewport)
                            .Where(vp => vp != null).Select(vp => targetDoc.GetElement(vp.ViewId) as ViewPlan)
                            .Where(v => v != null && !v.IsTemplate)
                            .OrderByDescending(v => plannedNames.Contains(v.Name)).FirstOrDefault();
                        if (mainView == null) return new FastNativeTagPostResult { Message = "找不到主平面視圖" };

                        using (var trans = new Transaction(targetDoc, "BIM Drawing Factory: 設定內建標籤類型"))
                        {
                            if (trans.Start() != TransactionStatus.Started)
                                return new FastNativeTagPostResult { Message = "無法設定標籤類型" };
                            try
                            {
                                var symbol = ResolveExactTagType(sourceDoc, targetDoc,
                                    rule.FamilyName, rule.TypeName) as FamilySymbol;
                                var categoryId = new ElementId(rule.TagCategory);
                                if (symbol == null || !symbol.IsValidDefaultFamilyType(categoryId))
                                {
                                    trans.RollBack();
                                    return new FastNativeTagPostResult { Message = "指定標籤不能設為品類預設" };
                                }
                                if (!symbol.IsActive) symbol.Activate();
                                targetDoc.SetDefaultFamilyTypeId(categoryId, symbol.Id);
                                trans.Commit();
                            }
                            catch (Exception ex)
                            {
                                if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                                return new FastNativeTagPostResult { Message = ex.Message };
                            }
                        }

                        try
                        {
                            if (uiDoc.ActiveView == null || uiDoc.ActiveView.Id != mainView.Id)
                                uiDoc.ActiveView = mainView;
                            uiDoc.Selection.SetElementIds(new List<ElementId>());
                            var commandId = RevitCommandId.LookupPostableCommandId(PostableCommand.TagAllNotTagged);
                            if (commandId == null || !app.CanPostCommand(commandId))
                                return new FastNativeTagPostResult { Message = "Revit 目前不能執行全部加上標籤" };
                            app.PostCommand(commandId);
                            return new FastNativeTagPostResult { Posted = true };
                        }
                        catch (Exception ex)
                        {
                            return new FastNativeTagPostResult { Message = ex.Message };
                        }
                    }, fastTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    return new FastNativeTagResult { Message = "內建模式啟動逾時" };
                }
                catch (Exception ex)
                {
                    fastTimeout.Cancel();
                    try { dialogTask.GetAwaiter().GetResult(); } catch { }
                    return new FastNativeTagResult { Message = ex.Message };
                }

                if (postResult == null || !postResult.Posted)
                {
                    fastTimeout.Cancel();
                    try { await dialogTask; } catch { }
                    return new FastNativeTagResult
                    {
                        Message = postResult != null ? postResult.Message : "內建命令未啟動"
                    };
                }

                NativeTagDialogResult dialogResult;
                try { dialogResult = await dialogTask; }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    return new FastNativeTagResult { Message = "內建對話框未在 4 秒內完成" };
                }
                catch (Exception ex) { return new FastNativeTagResult { Message = ex.Message }; }
                return new FastNativeTagResult
                {
                    Succeeded = dialogResult != null && dialogResult.Confirmed,
                    Message = dialogResult != null ? dialogResult.Message : "內建對話框沒有完成"
                };
            }
        }

        private async Task ResolveDeferredTagCollisionsAsync(GenerateRequestDto request,
            SheetPreviewItemDto item, CancellationToken cancellationToken)
        {
            await _handler.RunAsync(app =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetDoc = FindDocument(app, request.TargetProjectName, true);
                if (targetDoc == null) return false;
                var sheet = new FilteredElementCollector(targetDoc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .FirstOrDefault(s => string.Equals(s.SheetNumber, item.SheetNumber, StringComparison.OrdinalIgnoreCase));
                if (sheet == null) return false;
                var plannedNames = new HashSet<string>((item.PlannedViews ?? new List<SheetViewItemDto>())
                    .Where(v => v.ViewType != "圖例" && v.ViewType != "明細表")
                    .Select(v => v.TargetViewName), StringComparer.OrdinalIgnoreCase);
                var view = sheet.GetAllViewports().Select(id => targetDoc.GetElement(id) as Viewport)
                    .Where(vp => vp != null).Select(vp => targetDoc.GetElement(vp.ViewId) as ViewPlan)
                    .Where(v => v != null && !v.IsTemplate)
                    .OrderByDescending(v => plannedNames.Contains(v.Name)).FirstOrDefault();
                if (view == null) return false;
                using (var trans = new Transaction(targetDoc, "BIM Drawing Factory: 標籤避讓 " + item.SheetNumber))
                {
                    if (trans.Start() != TransactionStatus.Started) return false;
                    try
                    {
                        ResolveTagCollisions(targetDoc, view, new List<string>());
                        trans.Commit();
                        return true;
                    }
                    catch
                    {
                        if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                        return false;
                    }
                }
            }, cancellationToken);
        }

        private static DeferredTaggingResult ApplyDeferredTags(Document sourceDoc, Document targetDoc,
            SheetPreviewItemDto item, GenerationBatchCache batchCache, CancellationToken cancellationToken)
        {
            var result = new DeferredTaggingResult { Warnings = new List<string>() };
            var rule = GetDeferredTaggingRule(item != null ? item.SheetName : string.Empty);
            if (rule == null || targetDoc == null) return result;
            var sheet = new FilteredElementCollector(targetDoc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .FirstOrDefault(s => string.Equals(s.SheetNumber, item.SheetNumber, StringComparison.OrdinalIgnoreCase));
            if (sheet == null)
            {
                result.Warnings.Add("全部加上標籤：找不到目標圖紙 " + item.SheetNumber);
                return result;
            }
            string tagTypeKey = rule.FamilyName + "|" + rule.TypeName;
            ElementId cachedTagTypeId;
            ElementType tagType = null;
            if (batchCache != null && batchCache.ResolvedTagTypeIds.TryGetValue(tagTypeKey, out cachedTagTypeId))
                tagType = targetDoc.GetElement(cachedTagTypeId) as ElementType;
            if (tagType == null)
            {
                tagType = ResolveExactTagType(sourceDoc, targetDoc, rule.FamilyName, rule.TypeName);
                if (tagType != null && batchCache != null)
                    batchCache.ResolvedTagTypeIds[tagTypeKey] = tagType.Id;
            }
            if (tagType == null)
            {
                result.Warnings.Add(string.Format("全部加上標籤：找不到 {0} : {1}", rule.FamilyName, rule.TypeName));
                return result;
            }
            var symbol = tagType as FamilySymbol;
            if (symbol != null && !symbol.IsActive) symbol.Activate();

            var plannedNames = new HashSet<string>((item.PlannedViews ?? new List<SheetViewItemDto>())
                .Where(v => v.ViewType != "圖例" && v.ViewType != "明細表")
                .Select(v => v.TargetViewName), StringComparer.OrdinalIgnoreCase);
            var view = sheet.GetAllViewports()
                .Select(id => targetDoc.GetElement(id) as Viewport)
                .Where(vp => vp != null)
                .Select(vp => targetDoc.GetElement(vp.ViewId) as ViewPlan)
                .Where(v => v != null && !v.IsTemplate)
                .OrderByDescending(v => plannedNames.Contains(v.Name))
                .FirstOrDefault();
            if (view == null)
            {
                result.Warnings.Add("全部加上標籤：圖紙上沒有可套用規則的主平面視圖");
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var candidates = CollectDeferredTagCandidates(targetDoc, view, rule.TargetCategory, batchCache,
                cancellationToken);
            if (candidates.Count == 0)
            {
                result.Warnings.Add(string.Format("全部加上標籤（{0}）：主視圖內找不到指定品類 {1}，已略過；未執行全模型或連結模型掃描",
                    view.Name, rule.TargetCategory));
                return result;
            }
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in new FilteredElementCollector(targetDoc, view.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                try
                {
                    foreach (var id in tag.GetTaggedElementIds())
                        existing.Add(id.LinkInstanceId != ElementId.InvalidElementId
                            ? "R:" + id.LinkInstanceId.Value + ":" + id.LinkedElementId.Value
                            : "L:" + id.HostElementId.Value);
                }
                catch { }
            }
            int failed = 0;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key = GetTagCandidateKey(candidate);
                if (existing.Contains(key)) continue;
                try
                {
                    var tag = IndependentTag.Create(targetDoc, tagType.Id, view.Id,
                        candidate.TagReference ?? new Reference(candidate.Element), false,
                        TagOrientation.Horizontal, candidate.Center);
                    if (tag != null)
                    {
                        existing.Add(key);
                        result.CreatedCount++;
                    }
                }
                catch { failed++; }
            }
            if (failed > 0)
                result.Warnings.Add(string.Format("全部加上標籤（{0}）：{1} 個構件受 Revit 參照或視圖限制無法標記",
                    view.Name, failed));
            ResolveTagCollisions(targetDoc, view, result.Warnings);
            return result;
        }

        private static ElementType ResolveExactTagType(Document sourceDoc, Document targetDoc,
            string familyName, string typeName)
        {
            Func<Document, FamilySymbol> find = doc => new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .FirstOrDefault(s => string.Equals(s.FamilyName, familyName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
            var target = find(targetDoc);
            if (target != null) return target;
            var source = sourceDoc != null ? find(sourceDoc) : null;
            if (source == null || ReferenceEquals(sourceDoc, targetDoc)) return source;
            try
            {
                ElementTransformUtils.CopyElements(sourceDoc, new List<ElementId> { source.Id },
                    targetDoc, Transform.Identity, CreateCopyPasteOptions());
            }
            catch { }
            return find(targetDoc);
        }

        private static List<TargetElementCandidate> CollectDeferredTagCandidates(Document targetDoc,
            Autodesk.Revit.DB.View view, BuiltInCategory category, GenerationBatchCache batchCache,
            CancellationToken cancellationToken)
        {
            var index = new TargetViewElementIndex(batchCache);
            long categoryId = (long)category;
            var list = new List<TargetElementCandidate>();
            index.TagByCategory[categoryId] = list;
            foreach (var element in new FilteredElementCollector(targetDoc, view.Id)
                .OfCategory(category).WhereElementIsNotElementType())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var type = targetDoc.GetElement(element.GetTypeId());
                    list.Add(new TargetElementCandidate
                    {
                        Element = element,
                        Center = GetElementCenter(element, view),
                        TypeName = type != null ? type.Name : string.Empty,
                        TagReference = new Reference(element),
                        IsLinked = false
                    });
                }
                catch { }
            }
            // Deferred tagging is intentionally view-local. Every sheet rule
            // already identifies the exact category expected in its main view;
            // never widen a missing result into a full Revit-Link/model scan.
            return list;
        }

        private sealed class ProtectedLevelSnapshot
        {
            public ElementId Id { get; set; }
            public string Name { get; set; }
            public double Elevation { get; set; }
        }

        private static void SuppressLevelViewRenameDialog(object sender, DialogBoxShowingEventArgs args)
        {
            if (args == null) return;
            string dialogId = args.DialogId ?? string.Empty;
            // DialogId is language independent. During this tightly scoped event
            // subscription the add-in performs only view renames, so any Revit
            // rename confirmation must preserve the existing project Level.
            bool isRenameLevelView = dialogId.IndexOf("rename", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isRenameLevelView) return;
            var taskDialog = args as TaskDialogShowingEventArgs;
            if (taskDialog != null) taskDialog.OverrideResult((int)TaskDialogResult.No);
        }

        private static List<ProtectedLevelSnapshot> CaptureProtectedLevels(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Select(level => new ProtectedLevelSnapshot
                {
                    Id = level.Id,
                    Name = level.Name,
                    Elevation = level.Elevation
                }).ToList();
        }

        private static void EnsureProtectedLevelsUnchanged(Document doc, IList<ProtectedLevelSnapshot> snapshots)
        {
            foreach (var snapshot in snapshots ?? new List<ProtectedLevelSnapshot>())
            {
                var level = doc.GetElement(snapshot.Id) as Level;
                if (level == null)
                    throw new InvalidOperationException("安全保護：建圖過程將刪除原有 Level「" + snapshot.Name + "」，已回滾本張圖紙。");
                if (!string.Equals(level.Name, snapshot.Name, StringComparison.Ordinal) ||
                    Math.Abs(level.Elevation - snapshot.Elevation) > 1e-9)
                    throw new InvalidOperationException("安全保護：建圖過程將改動原有 Level「" + snapshot.Name + "」，已回滾本張圖紙。");
            }
        }

        private static FamilySymbol ResolveTitleBlockType(Document sourceDoc, Document targetDoc,
            SheetPreviewItemDto item, List<string> warnings)
        {
            FamilySymbol targetType = null;
            long targetTypeId;
            if (long.TryParse(item.ResolvedTitleBlockTypeId, out targetTypeId))
                targetType = targetDoc.GetElement(new ElementId(targetTypeId)) as FamilySymbol;
            if (targetType == null)
                targetType = FindMatchingTitleBlockType(targetDoc, item.SourceTitleBlockName);
            if (targetType != null) return targetType;

            FamilySymbol sourceType = null;
            long sourceTypeId;
            if (long.TryParse(item.SourceTitleBlockTypeId, out sourceTypeId))
                sourceType = sourceDoc.GetElement(new ElementId(sourceTypeId)) as FamilySymbol;
            if (sourceType == null)
            {
                var sourceSheet = new FilteredElementCollector(sourceDoc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .FirstOrDefault(s => string.Equals(s.SheetNumber, item.SourceStandardSheet, StringComparison.OrdinalIgnoreCase));
                if (sourceSheet != null)
                {
                    var instance = new FilteredElementCollector(sourceDoc, sourceSheet.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType()
                        .Cast<FamilyInstance>().FirstOrDefault();
                    sourceType = instance != null ? instance.Symbol : null;
                }
            }
            if (sourceType == null)
            {
                AddWarningOnce(warnings, "圖框診斷：A 基準圖紙上找不到圖框實例，無法取得族與類型。");
                return null;
            }
            if (ReferenceEquals(sourceDoc, targetDoc)) return sourceType;

            try
            {
                var copiedIds = ElementTransformUtils.CopyElements(sourceDoc, new List<ElementId> { sourceType.Id },
                    targetDoc, Transform.Identity, CreateCopyPasteOptions());
                targetType = copiedIds.Select(id => targetDoc.GetElement(id) as FamilySymbol).FirstOrDefault(x => x != null);
            }
            catch (Exception ex)
            {
                AddWarningOnce(warnings, string.Format(
                    "圖框診斷：B 沒有可比對的「{0}」，且由 A 複製類型失敗。原因：{1}",
                    GetTitleBlockDisplayName(sourceType), ex.Message));
            }
            return targetType ?? FindMatchingTitleBlockType(targetDoc, GetTitleBlockDisplayName(sourceType));
        }

        private static void AddPluginProductionNote(Document doc, ViewSheet sheet, List<string> warnings)
        {
            if (doc == null || sheet == null || warnings == null || warnings.Count == 0) return;
            try
            {
                var textType = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                if (textType == null) return;
                string detail = string.Join("；", warnings.Take(4));
                if (warnings.Count > 4) detail += string.Format("；另有 {0} 項", warnings.Count - 4);
                string text = "【外掛製作】圖紙已建立；部分內容採降級建立或待補製：" + detail;
                var outline = sheet.Outline;
                XYZ point = outline != null
                    ? new XYZ(outline.Min.U + 10.0 / 304.8, outline.Max.V - 10.0 / 304.8, 0)
                    : new XYZ(10.0 / 304.8, 10.0 / 304.8, 0);
                TextNote.Create(doc, sheet.Id, point, text, textType.Id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Plugin production note Warning] " + ex.Message);
            }
        }

        private static ElementType ResolveViewportType(Document sourceDoc, Document targetDoc,
            Viewport sourceViewport, Viewport createdViewport, SheetViewItemDto plannedView)
        {
            var sourceType = sourceDoc.GetElement(sourceViewport.GetTypeId()) as ElementType;
            if (sourceType == null) return null;
            if (ReferenceEquals(sourceDoc, targetDoc)) return sourceType;

            ElementType targetType = null;
            long targetTypeId;
            if (plannedView != null && long.TryParse(plannedView.ResolvedViewportTypeId, out targetTypeId))
                targetType = targetDoc.GetElement(new ElementId(targetTypeId)) as ElementType;
            if (targetType != null) return targetType;

            // Detect A's actual viewport definition and map it to an equivalent
            // existing type in B. If B has no match, create one B-local viewport
            // type based on its current default and apply A's parameters. This is
            // safer than copying a system viewport type across documents.
            targetType = FindEquivalentViewportType(sourceDoc, sourceType, targetDoc);
            if (targetType != null) return targetType;
            return CreateSynchronizedViewportType(sourceDoc, sourceType, targetDoc, createdViewport);
        }

        private static ElementType CreateSynchronizedViewportType(Document sourceDoc, ElementType sourceType,
            Document targetDoc, Viewport createdViewport)
        {
            if (sourceDoc == null || sourceType == null || targetDoc == null || createdViewport == null) return null;
            var baseType = targetDoc.GetElement(createdViewport.GetTypeId()) as ElementType;
            if (baseType == null) return null;
            var existingNames = new HashSet<string>(GetAllViewportTypes(targetDoc)
                .Select(GetViewportTypeName), StringComparer.OrdinalIgnoreCase);
            string requestedName = GetViewportTypeName(sourceType);
            if (existingNames.Contains(requestedName)) requestedName += " [BDF-A]";
            string name = requestedName;
            int suffix = 2;
            while (existingNames.Contains(name)) name = requestedName + " (" + suffix++ + ")";
            try
            {
                var duplicated = baseType.Duplicate(name) as ElementType;
                if (duplicated == null) return null;
                CopyViewportTypeParameters(sourceDoc, sourceType, targetDoc, duplicated);
                return duplicated;
            }
            catch { return null; }
        }

        private static void CopyViewportTypeParameters(Document sourceDoc, ElementType sourceType,
            Document targetDoc, ElementType targetType)
        {
            foreach (Parameter sourceParameter in sourceType.Parameters)
            {
                try
                {
                    if (sourceParameter == null || sourceParameter.Definition == null) continue;
                    var targetParameter = targetType.LookupParameter(sourceParameter.Definition.Name);
                    if (targetParameter == null || targetParameter.IsReadOnly ||
                        targetParameter.StorageType != sourceParameter.StorageType) continue;
                    if (sourceParameter.StorageType == StorageType.Integer)
                        targetParameter.Set(sourceParameter.AsInteger());
                    else if (sourceParameter.StorageType == StorageType.Double)
                        targetParameter.Set(sourceParameter.AsDouble());
                    else if (sourceParameter.StorageType == StorageType.String)
                        targetParameter.Set(sourceParameter.AsString() ?? string.Empty);
                    else if (sourceParameter.StorageType == StorageType.ElementId)
                    {
                        var sourceElement = sourceDoc.GetElement(sourceParameter.AsElementId());
                        if (sourceElement == null) continue;
                        string signature = GetReferencedTypeSignature(sourceDoc, sourceElement.Id);
                        var targetElement = new FilteredElementCollector(targetDoc)
                            .FirstOrDefault(e => string.Equals(GetReferencedTypeSignature(targetDoc, e.Id),
                                signature, StringComparison.OrdinalIgnoreCase));
                        if (targetElement == null && sourceElement is FamilySymbol)
                        {
                            var copied = ElementTransformUtils.CopyElements(sourceDoc,
                                new List<ElementId> { sourceElement.Id }, targetDoc, Transform.Identity, CreateCopyPasteOptions());
                            targetElement = copied.Select(id => targetDoc.GetElement(id)).FirstOrDefault(e => e != null);
                        }
                        if (targetElement != null) targetParameter.Set(targetElement.Id);
                    }
                }
                catch { }
            }
        }

        private static void CopyCompatibleParameters(Document sourceDoc, Element source, Document targetDoc, Element target)
        {
            if (source == null || target == null) return;
            foreach (Parameter sourceParameter in source.Parameters)
            {
                try
                {
                    if (sourceParameter == null || sourceParameter.Definition == null) continue;
                    // View.Name is assigned by the per-floor naming policy before
                    // parameter migration. Copying VIEW_NAME from A back onto the
                    // new view is invalid when A and B are the same document because
                    // the source view still owns that name.
                    if (target is Autodesk.Revit.DB.View &&
                        sourceParameter.Id.Value == (long)BuiltInParameter.VIEW_NAME)
                        continue;
                    var targetParameter = target.LookupParameter(sourceParameter.Definition.Name);
                    if (targetParameter == null || targetParameter.IsReadOnly || targetParameter.StorageType != sourceParameter.StorageType) continue;
                    switch (sourceParameter.StorageType)
                    {
                        case StorageType.Double: targetParameter.Set(sourceParameter.AsDouble()); break;
                        case StorageType.Integer: targetParameter.Set(sourceParameter.AsInteger()); break;
                        case StorageType.String: targetParameter.Set(sourceParameter.AsString() ?? string.Empty); break;
                        case StorageType.ElementId:
                            var sourceElement = sourceDoc.GetElement(sourceParameter.AsElementId());
                            if (sourceElement == null) break;
                            var targetElement = new FilteredElementCollector(targetDoc)
                                .FirstOrDefault(e => e.Category != null && sourceElement.Category != null &&
                                    e.Category.Id == sourceElement.Category.Id && string.Equals(e.Name, sourceElement.Name, StringComparison.OrdinalIgnoreCase));
                            if (targetElement != null) targetParameter.Set(targetElement.Id);
                            break;
                    }
                }
                catch { }
            }
        }

        private static void CopyViewGraphicSettings(Document sourceDoc, Autodesk.Revit.DB.View sourceView,
            Document targetDoc, Autodesk.Revit.DB.View targetView)
        {
            if (sourceView == null || targetView == null) return;
            try { targetView.DetailLevel = sourceView.DetailLevel; } catch { }
            try { targetView.PartsVisibility = sourceView.PartsVisibility; } catch { }
            foreach (Category sourceCategory in sourceDoc.Settings.Categories)
            {
                try
                {
                    // Datum visibility belongs to target project B.  Mirroring A's
                    // category settings could hide Level/Grid lines and make users
                    // believe a project Level was deleted.
                    if (sourceCategory.Id.Value == (long)BuiltInCategory.OST_Levels ||
                        sourceCategory.Id.Value == (long)BuiltInCategory.OST_Grids ||
                        sourceCategory.Id.Value == (long)BuiltInCategory.OST_CLines)
                        continue;
                    var targetCategory = targetDoc.Settings.Categories.get_Item((BuiltInCategory)(int)sourceCategory.Id.Value);
                    if (targetCategory != null && sourceView.CanCategoryBeHidden(sourceCategory.Id) &&
                        targetView.CanCategoryBeHidden(targetCategory.Id))
                        targetView.SetCategoryHidden(targetCategory.Id, sourceView.GetCategoryHidden(sourceCategory.Id));
                }
                catch { }
            }
            foreach (var sourceFilterId in sourceView.GetFilters())
            {
                try
                {
                    var sourceFilter = sourceDoc.GetElement(sourceFilterId);
                    if (sourceFilter == null) continue;
                    var targetFilter = new FilteredElementCollector(targetDoc)
                        .FirstOrDefault(e => e.GetType() == sourceFilter.GetType() &&
                            string.Equals(e.Name, sourceFilter.Name, StringComparison.OrdinalIgnoreCase));
                    if (targetFilter == null && !ReferenceEquals(sourceDoc, targetDoc))
                    {
                        var copied = ElementTransformUtils.CopyElements(sourceDoc, new List<ElementId> { sourceFilterId },
                            targetDoc, Transform.Identity, CreateCopyPasteOptions());
                        targetFilter = copied.Select(id => targetDoc.GetElement(id)).FirstOrDefault(e => e != null);
                    }
                    if (targetFilter == null) continue;
                    if (!targetView.GetFilters().Contains(targetFilter.Id)) targetView.AddFilter(targetFilter.Id);
                    targetView.SetFilterVisibility(targetFilter.Id, sourceView.GetFilterVisibility(sourceFilterId));
                    try { targetView.SetFilterOverrides(targetFilter.Id, sourceView.GetFilterOverrides(sourceFilterId)); } catch { }
                }
                catch { }
            }
        }

        private static int CreateViewsForSheet(Document sourceDoc, Document targetDoc, ViewSheet targetSheet,
            SheetPreviewItemDto item, GenerateRequestDto request, out int createdSchedules, out int createdLegends,
            List<string> warnings, GenerationBatchCache batchCache, Action<string> reportStage,
            CancellationToken cancellationToken)
        {
            createdSchedules = 0;
            createdLegends = 0;
            var targetStories = GetStoryGroups(targetDoc);
            var sourceStories = GetStoryGroups(sourceDoc);
            var sourceSheet = new FilteredElementCollector(sourceDoc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .FirstOrDefault(s => string.Equals(s.SheetNumber, item.SourceStandardSheet, StringComparison.OrdinalIgnoreCase));
            if (sourceSheet == null) return 0;
            var sourceSheetLevel = ResolveNominalSourceLevel(sourceStories, sourceSheet.Name);
            var targetScopeBoxes = new FilteredElementCollector(targetDoc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType().ToList();

            int floorIndex = request.SelectedFloorNames.FindIndex(f => string.Equals(f, item.SheetFloorName, StringComparison.OrdinalIgnoreCase));
            Level targetLevel = null;
            if (floorIndex >= 0 && floorIndex < request.SelectedFloorIds.Count)
            {
                long rawId;
                if (long.TryParse(request.SelectedFloorIds[floorIndex], out rawId))
                    targetLevel = targetDoc.GetElement(new ElementId(rawId)) as Level;
            }
            // Floor Id can become stale after a model reload.  Resolve again from
            // the actual target document so schedules never silently skip their
            // Base Constraint retargeting merely because the cached id changed.
            if (targetLevel == null)
            {
                var targetStory = targetStories.FirstOrDefault(s =>
                    string.Equals(s.FloorCode, item.SheetFloorName, StringComparison.OrdinalIgnoreCase));
                targetLevel = targetStory != null ? targetStory.PrimaryLevel : null;
            }
            // A floor chip identifies the storey, not necessarily the physical
            // Level for the requested building. Resolve the exact building Level
            // before creating or reusing a plan view.
            targetLevel = ResolveBuildingStoryLevel(targetLevel, 0, targetStories,
                item.BuildingName, item.SheetFloorName) ?? targetLevel;
            int created = 0;
            foreach (var viewportId in sourceSheet.GetAllViewports())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceViewport = sourceDoc.GetElement(viewportId) as Viewport;
                var sourceView = sourceViewport != null ? sourceDoc.GetElement(sourceViewport.ViewId) as Autodesk.Revit.DB.View : null;
                if (sourceView == null || sourceView.IsTemplate) continue;
                if (reportStage != null) reportStage("正在建立視圖：" + sourceView.Name);
                var plannedView = (item.PlannedViews ?? new List<SheetViewItemDto>())
                    .FirstOrDefault(v => v.ViewId == sourceView.Id.ToString());
                var sourcePlan = sourceView as ViewPlan;
                string lookUpBuildingKey = GetUpperFloorLookUpBuildingKey(item, plannedView, sourceView);
                bool isBuildingUpperFloorLookUpBodyPlan = !string.IsNullOrEmpty(lookUpBuildingKey);
                string lookUpBuildingLabel = FactoryConventions.Current.GetDisplayName(lookUpBuildingKey);
                string requiredLookUpLevelName = string.Empty;
                Level viewTargetLevel = targetLevel;
                if (sourcePlan != null && targetLevel != null)
                {
                    var runtimeBinding = ResolveRuntimeViewBinding(item, plannedView, sourceView);
                    int runtimeDelta = runtimeBinding == LevelBindingOption.PreviousLevel ? -1
                        : runtimeBinding == LevelBindingOption.NextLevel ? 1 : 0;
                    viewTargetLevel = ResolveBuildingStoryLevel(targetLevel, runtimeDelta, targetStories,
                        item.BuildingName, item.SheetFloorName) ?? targetLevel;
                    // Every upper-floor office look-up body plan is generated
                    // on the exact preceding OB level (3F -> OB_2FL, etc.).
                    if (isBuildingUpperFloorLookUpBodyPlan)
                    {
                        string requiredFloorCode = GetPreviousExistingFloorCode(targetStories, item.SheetFloorName);
                        var exactPreviousObLevel = FindPreviousBuildingStoryLevel(targetStories,
                            item.SheetFloorName, lookUpBuildingKey, targetLevel);
                        if (exactPreviousObLevel == null)
                        {
                            warnings.Add(string.Format(
                                "{0}{1} 見上軀體圖待補製：B 專案在 {2} 找不到 {3} 或 ALL 樓層；為避免錯用 R 樓層，已停止建立此視圖。",
                                lookUpBuildingLabel, item.SheetFloorName, requiredFloorCode, lookUpBuildingKey));
                            continue;
                        }
                        viewTargetLevel = exactPreviousObLevel;
                        requiredLookUpLevelName = viewTargetLevel.Name;
                    }
                }
                else if (plannedView != null && !string.IsNullOrWhiteSpace(plannedView.TargetLevelId))
                {
                    long plannedLevelId;
                    if (long.TryParse(plannedView.TargetLevelId, out plannedLevelId))
                        viewTargetLevel = targetDoc.GetElement(new ElementId(plannedLevelId)) as Level ?? targetLevel;
                }

                Autodesk.Revit.DB.View targetView = null;
                bool contentAlreadyCopied = false;
                bool reusedExistingTargetView = false;
                string plannedName = plannedView != null ? plannedView.TargetViewName
                    : ApplyViewName(sourceView.Name, item.BuildingName, item.SheetFloorName);
                if (sourceView.ViewType != ViewType.Legend && !string.IsNullOrWhiteSpace(plannedName))
                {
                    var reusableView = new FilteredElementCollector(targetDoc).OfClass(typeof(Autodesk.Revit.DB.View))
                        .Cast<Autodesk.Revit.DB.View>()
                        .FirstOrDefault(v => !v.IsTemplate && !(v is ViewSchedule) &&
                            string.Equals(v.Name, plannedName, StringComparison.OrdinalIgnoreCase) &&
                            IsReusableViewMatch(v, sourceView, viewTargetLevel) &&
                            CanReuseViewOnSheet(targetDoc, targetSheet, v));
                    if (reusableView != null)
                    {
                        if (isBuildingUpperFloorLookUpBodyPlan && !IsStructuralUpPlan(targetDoc, reusableView as ViewPlan))
                            reusableView = null;
                    }
                    if (reusableView != null)
                    {
                        targetView = reusableView;
                        contentAlreadyCopied = true;
                        reusedExistingTargetView = true;
                    }
                }
                if (targetView == null && sourceView.ViewType == ViewType.Legend && ReferenceEquals(sourceDoc, targetDoc))
                {
                    // 圖例不依附樓層，同一專案可合法放在多張圖紙上。
                    targetView = sourceView;
                    contentAlreadyCopied = true;
                }
                else if (targetView == null && sourceView.ViewType == ViewType.Legend)
                {
                    // 跨專案先尋找先前已複製的同名圖例；整批只建立一份，所有樓層共用。
                    targetView = new FilteredElementCollector(targetDoc).OfClass(typeof(Autodesk.Revit.DB.View))
                        .Cast<Autodesk.Revit.DB.View>()
                        .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.Legend &&
                            string.Equals(v.Name, sourceView.Name, StringComparison.OrdinalIgnoreCase));
                    if (targetView == null)
                        targetView = CopyLegendToTarget(sourceDoc, sourceView, targetDoc);
                    contentAlreadyCopied = targetView != null;
                    if (targetView == null)
                        warnings.Add("圖例待補製：無法跨專案複製 " + sourceView.Name);
                }
                else if (targetView == null && viewTargetLevel != null &&
                    plannedView != null && plannedView.IsDependentView)
                {
                    var targetPrimary = GetOrCreateTargetPrimaryView(sourceDoc, sourceView, targetDoc,
                        viewTargetLevel, plannedView, item, warnings, batchCache, sourceStories, targetStories);
                    if (targetPrimary != null && targetPrimary.CanViewBeDuplicated(ViewDuplicateOption.AsDependent))
                    {
                        try
                        {
                            targetView = targetDoc.GetElement(
                                targetPrimary.Duplicate(ViewDuplicateOption.AsDependent)) as Autodesk.Revit.DB.View;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add(string.Format("從屬視圖建立失敗（{0} → {1}）：{2}",
                                sourceView.Name, targetPrimary.Name, ex.Message));
                        }
                    }
                    else if (targetPrimary != null)
                    {
                        warnings.Add("主要視圖不允許建立從屬視圖：" + targetPrimary.Name);
                    }
                    // Deliberately do not fall back to an independent ViewPlan.
                    // Losing dependency is more harmful than reporting this view.
                }
                else if (targetView == null && sourcePlan != null && viewTargetLevel != null)
                {
                    var sourceType = sourceDoc.GetElement(sourcePlan.GetTypeId()) as ViewFamilyType;
                    var targetType = ResolveViewFamilyType(sourceDoc, sourceType, targetDoc);
                    if (isBuildingUpperFloorLookUpBodyPlan)
                        targetType = ResolveStructuralUpViewFamilyType(targetDoc, targetType);
                    else if (ContainsLookUp(item.SheetName) &&
                        (targetType == null || targetType.ViewFamily != ViewFamily.StructuralPlan))
                        targetType = ResolveStructuralUpViewFamilyType(targetDoc, targetType);
                    if (targetType != null)
                    {
                        targetView = CreatePlanForLevelResilient(targetDoc, sourcePlan, targetType,
                            viewTargetLevel, warnings);
                    }
                    else if (isBuildingUpperFloorLookUpBodyPlan)
                        warnings.Add(string.Format("{0}{1} 見上軀體圖待補製：B 專案無法取得「結構平面／向上看」視圖類型。",
                            lookUpBuildingLabel, item.SheetFloorName));
                }
                else if (targetView == null && sourceView is ViewSection)
                {
                    targetView = CreateSectionOrDetailView(sourceDoc, (ViewSection)sourceView, targetDoc,
                        sourceView.GenLevel ?? sourceSheetLevel, viewTargetLevel,
                        sourceStories, targetStories, warnings);
                }
                else if (targetView == null && sourceView.ViewType == ViewType.DraftingView && !ReferenceEquals(sourceDoc, targetDoc))
                {
                    var draftingType = new FilteredElementCollector(targetDoc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
                    if (draftingType != null)
                    {
                        var drafting = ViewDrafting.Create(targetDoc, draftingType.Id);
                        var copyIds = new FilteredElementCollector(sourceDoc, sourceView.Id).WhereElementIsNotElementType()
                            .Select(e => e.Id).ToList();
                        if (copyIds.Count > 0)
                            ElementTransformUtils.CopyElements(sourceView, copyIds, drafting, Transform.Identity, CreateCopyPasteOptions());
                        targetView = drafting;
                        contentAlreadyCopied = true;
                    }
                }
                else if (targetView == null && ReferenceEquals(sourceDoc, targetDoc) && sourceView.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing))
                {
                    targetView = targetDoc.GetElement(sourceView.Duplicate(ViewDuplicateOption.WithDetailing)) as Autodesk.Revit.DB.View;
                    contentAlreadyCopied = targetView != null;
                }

                // Do not copy an entire model view across documents as a fallback.
                // Revit may pull dependent model families/types (columns, lifts,
                // walls, etc.) with it and create unwanted "... 1" type names.
                // Supported views are rebuilt against B's model; only explicit
                // view-specific drawing elements are copied later.

                if (targetView == null && sourceView.ViewType == ViewType.Legend)
                {
                    warnings.Add("圖例待補製：" + sourceView.Name);
                    continue;
                }
                if (targetView == null)
                {
                    warnings.Add("視圖待補製：" + sourceView.Name);
                    continue;
                }
                if (!ReferenceEquals(targetView, sourceView) && !reusedExistingTargetView)
                {
                    // Reserve the final name immediately. Do this before copying any
                    // source parameters/settings so an A == B run never temporarily
                    // writes the already-owned source view name to the new view.
                    string finalViewName = GetUniqueViewName(targetDoc, plannedName);
                    try
                    {
                        targetView.Name = finalViewName;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(string.Format("視圖命名失敗（{0} → {1}）：{2}",
                            sourceView.Name, finalViewName, ex.Message));
                        try { targetDoc.Delete(targetView.Id); } catch { }
                        continue;
                    }
                    if ((plannedView == null || !plannedView.IsDependentView) &&
                        viewTargetLevel != null && batchCache != null)
                    {
                        batchCache.TargetPrimaryViewIds[GetTargetPrimaryCacheKey(
                            sourceDoc, sourceView, targetDoc, viewTargetLevel)] = targetView.Id;
                    }
                }
                if (isBuildingUpperFloorLookUpBodyPlan)
                {
                    var guardedPlan = targetView as ViewPlan;
                    if (guardedPlan != null && !IsStructuralUpPlan(targetDoc, guardedPlan))
                    {
                        try
                        {
                            var forcedUpType = ResolveStructuralUpViewFamilyType(targetDoc,
                                targetDoc.GetElement(guardedPlan.GetTypeId()) as ViewFamilyType);
                            if (forcedUpType != null && guardedPlan.GetTypeId() != forcedUpType.Id)
                                guardedPlan.ChangeTypeId(forcedUpType.Id);
                        }
                        catch { }
                    }
                    bool exactLevel = guardedPlan != null && guardedPlan.GenLevel != null &&
                        viewTargetLevel != null && guardedPlan.GenLevel.Id == viewTargetLevel.Id;
                    if (!exactLevel || !IsStructuralUpPlan(targetDoc, guardedPlan))
                    {
                        string actualLevel = guardedPlan != null && guardedPlan.GenLevel != null
                            ? guardedPlan.GenLevel.Name : "無";
                        var actualType = guardedPlan != null
                            ? targetDoc.GetElement(guardedPlan.GetTypeId()) as ViewFamilyType : null;
                        string actualTypeText = actualType != null
                            ? actualType.ViewFamily + "/" + actualType.PlanViewDirection : "無";
                        warnings.Add(string.Format(
                            "{0}{1} 見上軀體圖待補製：預期樓層「{2}」、StructuralPlan/Up；實際為樓層「{3}」、{4}，已拒絕放入圖紙。",
                            lookUpBuildingLabel, item.SheetFloorName, requiredLookUpLevelName,
                            actualLevel, actualTypeText));
                        if (!ReferenceEquals(targetView, sourceView) && !reusedExistingTargetView)
                        {
                            try { targetDoc.Delete(targetView.Id); } catch { }
                        }
                        continue;
                    }
                }
                if (!ReferenceEquals(targetView, sourceView) && !reusedExistingTargetView)
                {
                    CopyCompatibleParameters(sourceDoc, sourceView, targetDoc, targetView);
                    CopyViewGraphicSettings(sourceDoc, sourceView, targetDoc, targetView);
                    try { targetView.Scale = sourceView.Scale; } catch { }

                    var targetPlan = targetView as ViewPlan;
                    if (sourcePlan != null && targetPlan != null && plannedView != null && plannedView.HasPlanViewRange)
                        ApplyPlanViewRange(targetDoc, targetPlan, viewTargetLevel, plannedView, targetStories);
                    if (sourcePlan != null && targetPlan != null)
                        ApplyPlanViewDirection(sourcePlan, targetPlan, ContainsLookUp(item.SheetName));
                    if (targetPlan != null && plannedView != null)
                        ApplyLevelDependentViewSettings(targetDoc, targetPlan, viewTargetLevel, plannedView, targetStories);

                    try
                    {
                        Level sourceReferenceLevel = sourcePlan != null && sourcePlan.GenLevel != null
                            ? sourcePlan.GenLevel : sourceSheetLevel;
                        targetView.CropBox = targetView is ViewSection
                            ? CreateLevelBoundSectionBox(sourceView.CropBox, sourceReferenceLevel, viewTargetLevel,
                                sourceStories, targetStories)
                            : CreateShiftedCropBox(sourceView, sourceReferenceLevel, viewTargetLevel);
                        targetView.CropBoxActive = sourceView.CropBoxActive;
                        targetView.CropBoxVisible = sourceView.CropBoxVisible;
                    }
                    catch { }
                }

                string templateName = plannedView != null ? plannedView.ViewTemplateName : null;
                Autodesk.Revit.DB.View sourceTemplateView = null;
                if (sourceView.ViewTemplateId != ElementId.InvalidElementId)
                {
                    sourceTemplateView = sourceDoc.GetElement(sourceView.ViewTemplateId) as Autodesk.Revit.DB.View;
                    if (string.IsNullOrWhiteSpace(templateName))
                        templateName = sourceTemplateView != null ? sourceTemplateView.Name : null;
                }
                if (!reusedExistingTargetView && !string.IsNullOrWhiteSpace(templateName) &&
                    templateName != "無樣板" && templateName != "不適用")
                {
                    if (sourceTemplateView == null)
                        sourceTemplateView = new FilteredElementCollector(sourceDoc).OfClass(typeof(Autodesk.Revit.DB.View))
                            .Cast<Autodesk.Revit.DB.View>().FirstOrDefault(v => v.IsTemplate && v.Name == templateName);
                    var targetTemplate = ResolveSynchronizedViewTemplate(sourceDoc, sourceTemplateView,
                        targetDoc, templateName, warnings, batchCache);
                    if (targetTemplate != null && !ReferenceEquals(targetView, sourceView))
                    {
                        try
                        {
                            if (RequiresColumnWallScale250(item.SheetName))
                                targetTemplate = ResolveScaleSpecificTemplate(targetDoc, targetTemplate, 250);
                            targetView.ViewTemplateId = targetTemplate.Id;
                            ApplyTemplateScale(targetView, targetTemplate, warnings);
                        }
                        catch (Exception ex) { warnings.Add("視圖樣板改用視圖本身設定（" + sourceView.Name + "）：" + ex.Message); }
                    }
                }

                // 樣板可能覆寫 View Range／Underlay；套用樣板後再次以目標樓層資料校正。
                var finalTargetPlan = targetView as ViewPlan;
                if (!reusedExistingTargetView && finalTargetPlan != null && plannedView != null)
                {
                    if (plannedView.HasPlanViewRange)
                        ApplyPlanViewRange(targetDoc, finalTargetPlan, viewTargetLevel, plannedView, targetStories);
                    ApplyLevelDependentViewSettings(targetDoc, finalTargetPlan, viewTargetLevel, plannedView, targetStories);
                    ApplyPlanViewDirection(sourcePlan, finalTargetPlan, ContainsLookUp(item.SheetName));
                }

                if (!reusedExistingTargetView && plannedView != null && plannedView.ScopeBoxName != "-")
                {
                    try
                    {
                        var scopeBox = FindBestScopeBox(targetScopeBoxes, plannedView.ScopeBoxName,
                            item.BuildingName, item.SheetFloorName);
                        var scopeParameter = targetView.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                        if (scopeBox != null && scopeParameter != null && !scopeParameter.IsReadOnly)
                            scopeParameter.Set(scopeBox.Id);
                    }
                    catch { }
                }

                // Index plans must remain annotation-free; they receive only the
                // dedicated column-centre dimension phase. This also cleans up
                // tags left by older plug-in builds when an existing view is reused.
                if ((item.SheetName ?? string.Empty).Contains("索引圖") && targetView is ViewPlan)
                {
                    var staleTags = new FilteredElementCollector(targetDoc, targetView.Id)
                        .OfClass(typeof(IndependentTag)).Select(e => e.Id).ToList();
                    if (staleTags.Count > 0) targetDoc.Delete(staleTags);
                }

                // 樣板、裁切與顯示設定完成後，只複製安全的非標籤圖面內容。
                // 標籤與尺寸均由各自的專用外掛處理。
                targetDoc.Regenerate();
                cancellationToken.ThrowIfCancellationRequested();
                if (!contentAlreadyCopied)
                {
                    if (reportStage != null) reportStage("正在複製註解：" + sourceView.Name);
                    try { CopyViewSpecificElementsResilient(sourceDoc, sourceView, targetDoc, targetView); }
                    catch (Exception ex) { warnings.Add("一般註解待補製（" + sourceView.Name + "）：" + ex.Message); }
                }
                if (!reusedExistingTargetView && sourceView.ViewType != ViewType.Legend)
                    RetargetViewText(targetDoc, targetView, item.SheetFloorName);

                XYZ center = sourceViewport.GetBoxCenter();
                if (Viewport.CanAddViewToSheet(targetDoc, targetSheet.Id, targetView.Id))
                {
                    try
                    {
                        var createdViewport = Viewport.Create(targetDoc, targetSheet.Id, targetView.Id, center);
                        var viewportType = ResolveViewportType(sourceDoc, targetDoc, sourceViewport, createdViewport, plannedView);
                        if (viewportType == null)
                            warnings.Add("視埠類型改用目標預設：" +
                                (plannedView != null ? plannedView.ViewportTypeName : sourceViewport.Name));
                        if (viewportType != null && createdViewport.GetTypeId() != viewportType.Id)
                            createdViewport.ChangeTypeId(viewportType.Id);
                        // Viewport center alone is insufficient: title offset, line
                        // length and rotation are instance data, not type data.
                        try { createdViewport.LabelOffset = sourceViewport.LabelOffset; } catch { }
                        try { createdViewport.LabelLineLength = sourceViewport.LabelLineLength; } catch { }
                        try { createdViewport.Rotation = sourceViewport.Rotation; } catch { }
                        if (sourceView.ViewType == ViewType.Legend) createdLegends++;
                        else created++;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add("視圖已建立但尚未放上圖紙（" + sourceView.Name + "）：" + ex.Message);
                    }
                }
                else warnings.Add("視圖已建立但 Revit 不允許放上此圖紙：" + sourceView.Name);
            }

            // 明細表不是 Viewport，需另外複製／放置並保留來源座標。
            if (reportStage != null) reportStage("正在複製與重綁明細表：" + item.SheetNumber);
            var scheduleInstances = new FilteredElementCollector(sourceDoc, sourceSheet.Id)
                .OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                .Where(s => !s.IsTitleblockRevisionSchedule).ToList();
            foreach (var instance in scheduleInstances)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ElementId targetScheduleId = ElementId.InvalidElementId;
                var sourceSchedule = sourceDoc.GetElement(instance.ScheduleId) as ViewSchedule;
                string requestedScheduleName = sourceSchedule != null
                    ? ApplyViewName(sourceSchedule.Name, item.BuildingName, item.SheetFloorName) : string.Empty;
                var existingSchedule = !string.IsNullOrWhiteSpace(requestedScheduleName)
                    ? new FilteredElementCollector(targetDoc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                        .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, requestedScheduleName, StringComparison.OrdinalIgnoreCase))
                    : null;
                bool reusedSchedule = existingSchedule != null;
                if (existingSchedule != null) targetScheduleId = existingSchedule.Id;
                if (ReferenceEquals(sourceDoc, targetDoc))
                {
                    if (targetScheduleId == ElementId.InvalidElementId && sourceSchedule != null &&
                        sourceSchedule.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
                        targetScheduleId = sourceSchedule.Duplicate(ViewDuplicateOption.Duplicate);
                }
                else if (targetScheduleId == ElementId.InvalidElementId)
                {
                    try
                    {
                        var copied = ElementTransformUtils.CopyElements(sourceDoc,
                            new List<ElementId> { instance.ScheduleId }, targetDoc, Transform.Identity, CreateCopyPasteOptions());
                        targetScheduleId = copied.FirstOrDefault();
                    }
                    catch { }
                }
                if (targetScheduleId != ElementId.InvalidElementId)
                {
                    try
                    {
                        var targetSchedule = targetDoc.GetElement(targetScheduleId) as ViewSchedule;
                        if (targetSchedule != null && sourceSchedule != null)
                        {
                            if (!reusedSchedule) targetSchedule.Name = GetUniqueViewName(targetDoc, requestedScheduleName);
                            RetargetScheduleToContext(sourceDoc, sourceSchedule, targetSchedule, targetLevel,
                                sourceSheet.SheetNumber, item.SheetNumber);
                        }
                        ScheduleSheetInstance.Create(targetDoc, targetSheet.Id, targetScheduleId, instance.Point);
                        createdSchedules++;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add("明細表待補製：" +
                            (sourceSchedule != null ? sourceSchedule.Name : instance.ScheduleId.ToString()) + " — " + ex.Message);
                    }
                }
                else
                    warnings.Add("明細表待補製：" +
                        (sourceSchedule != null ? sourceSchedule.Name : instance.ScheduleId.ToString()));
            }
            if (reportStage != null) reportStage("正在複製基準圖紙上的直接繪圖元件：" + item.SheetNumber);
            try { CopySheetSpecificElementsResilient(sourceDoc, sourceSheet, targetDoc, targetSheet); }
            catch (Exception ex) { warnings.Add("圖紙上的直接繪圖元件待補製：" + ex.Message); }
            // Viewports and schedules were already created at A's exact sheet
            // coordinates.  Do not run the former auto-arranger here: it moved
            // viewports and could even change view scale, defeating the reference
            // layout and template.  Layout exceptions are reported for review.
            targetDoc.Regenerate();
            if (reportStage != null) reportStage("正在保留基準 A 的視埠座標與標題位置：" + item.SheetNumber);
            try { ReportSheetLayoutConflicts(targetDoc, targetSheet, warnings); }
            catch (Exception ex) { warnings.Add("圖紙排版檢查待確認：" + ex.Message); }
            return created;
        }

        private static Autodesk.Revit.DB.View GetOrCreateTargetPrimaryView(Document sourceDoc,
            Autodesk.Revit.DB.View sourceDependent, Document targetDoc, Level targetLevel, SheetViewItemDto plannedView,
            SheetPreviewItemDto item, List<string> warnings, GenerationBatchCache batchCache,
            List<ParsedFloorGroup> sourceStories, List<ParsedFloorGroup> targetStories)
        {
            ElementId sourcePrimaryId = ElementId.InvalidElementId;
            try { sourcePrimaryId = sourceDependent.GetPrimaryViewId(); } catch { }
            var sourcePrimary = sourcePrimaryId != null && sourcePrimaryId != ElementId.InvalidElementId
                ? sourceDoc.GetElement(sourcePrimaryId) as Autodesk.Revit.DB.View : null;
            if (sourcePrimary == null)
            {
                warnings.Add("從屬視圖找不到來源主要視圖：" + sourceDependent.Name);
                return null;
            }

            string cacheKey = GetTargetPrimaryCacheKey(sourceDoc, sourcePrimary, targetDoc, targetLevel);
            ElementId cachedId;
            if (batchCache != null && batchCache.TargetPrimaryViewIds.TryGetValue(cacheKey, out cachedId))
            {
                var cached = targetDoc.GetElement(cachedId) as Autodesk.Revit.DB.View;
                if (cached != null) return cached;
                batchCache.TargetPrimaryViewIds.Remove(cacheKey);
            }

            string targetPrimaryName = !string.IsNullOrWhiteSpace(plannedView.TargetPrimaryViewName)
                ? plannedView.TargetPrimaryViewName
                : ApplyViewName(sourcePrimary.Name, item.BuildingName, item.SheetFloorName);
            var existing = new FilteredElementCollector(targetDoc).OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .FirstOrDefault(v => !v.IsTemplate && v.GetPrimaryViewId() == ElementId.InvalidElementId &&
                    string.Equals(v.Name, targetPrimaryName, StringComparison.OrdinalIgnoreCase) &&
                    IsReusableViewMatch(v, sourcePrimary, targetLevel) &&
                    (!ReferenceEquals(sourceDoc, targetDoc) || v.Id != sourcePrimary.Id));
            if (existing != null)
            {
                if (batchCache != null) batchCache.TargetPrimaryViewIds[cacheKey] = existing.Id;
                return existing;
            }

            Autodesk.Revit.DB.View created = null;
            var sourcePrimaryPlan = sourcePrimary as ViewPlan;
            if (sourcePrimaryPlan != null)
            {
                var sourceType = sourceDoc.GetElement(sourcePrimaryPlan.GetTypeId()) as ViewFamilyType;
                var targetType = ResolveViewFamilyType(sourceDoc, sourceType, targetDoc);
                if (ContainsLookUp(item.SheetName))
                    targetType = ResolveStructuralUpViewFamilyType(targetDoc, targetType);
                created = CreatePlanForLevelResilient(targetDoc, sourcePrimaryPlan, targetType,
                    targetLevel, warnings);
            }
            else if (sourcePrimary is ViewSection)
            {
                created = CreateSectionOrDetailView(sourceDoc, (ViewSection)sourcePrimary, targetDoc,
                    sourcePrimary.GenLevel, targetLevel, sourceStories, targetStories, warnings);
            }
            else if (ReferenceEquals(sourceDoc, targetDoc) &&
                sourcePrimary.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
            {
                try
                {
                    created = targetDoc.GetElement(
                        sourcePrimary.Duplicate(ViewDuplicateOption.Duplicate)) as Autodesk.Revit.DB.View;
                }
                catch { }
            }
            if (created == null) return null;

            try { created.Name = GetUniqueViewName(targetDoc, targetPrimaryName); }
            catch (Exception ex)
            {
                warnings.Add("主要視圖命名失敗（" + targetPrimaryName + "）：" + ex.Message);
                try { targetDoc.Delete(created.Id); } catch { }
                return null;
            }
            CopyCompatibleParameters(sourceDoc, sourcePrimary, targetDoc, created);
            CopyViewGraphicSettings(sourceDoc, sourcePrimary, targetDoc, created);
            try { created.Scale = sourcePrimary.Scale; } catch { }
            var createdPlan = created as ViewPlan;
            if (createdPlan != null)
            {
                if (plannedView.HasPlanViewRange)
                    ApplyPlanViewRange(targetDoc, createdPlan, targetLevel, plannedView, targetStories);
                ApplyLevelDependentViewSettings(targetDoc, createdPlan, targetLevel, plannedView, targetStories);
            }

            Autodesk.Revit.DB.View sourceTemplate = null;
            if (sourcePrimary.ViewTemplateId != ElementId.InvalidElementId)
                sourceTemplate = sourceDoc.GetElement(sourcePrimary.ViewTemplateId) as Autodesk.Revit.DB.View;
            if (sourceTemplate != null)
            {
                var targetTemplate = ResolveSynchronizedViewTemplate(sourceDoc, sourceTemplate, targetDoc,
                    sourceTemplate.Name, warnings, batchCache);
                if (targetTemplate != null)
                {
                    try { created.ViewTemplateId = targetTemplate.Id; } catch { }
                }
            }
            if (batchCache != null) batchCache.TargetPrimaryViewIds[cacheKey] = created.Id;
            return created;
        }

        private static string GetTargetPrimaryCacheKey(Document sourceDoc, Autodesk.Revit.DB.View sourcePrimary,
            Document targetDoc, Level targetLevel)
        {
            return string.Format("{0}|{1}|{2}|{3}", sourceDoc.GetHashCode(), sourcePrimary.UniqueId,
                targetDoc.GetHashCode(), targetLevel.Id.Value);
        }

        private static ViewPlan CreatePlanForLevelResilient(Document targetDoc, ViewPlan sourcePlan,
            ViewFamilyType targetType, Level targetLevel, List<string> warnings)
        {
            if (targetDoc == null || sourcePlan == null || targetType == null || targetLevel == null) return null;
            Exception createError = null;
            try
            {
                return ViewPlan.Create(targetDoc, targetType.Id, targetLevel.Id);
            }
            catch (Exception ex)
            {
                createError = ex;
            }

            // Revit can reject ViewPlan.Create when this document already owns a
            // plan of the requested family/type for the Level. This is normal in
            // A == B workflows. Duplicate an existing view bound to the *target*
            // Level without detailing, then apply A's settings and annotations.
            var levelPlans = new FilteredElementCollector(targetDoc)
                .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == targetLevel.Id &&
                    v.ViewType == sourcePlan.ViewType)
                .OrderByDescending(v => v.GetTypeId() == targetType.Id)
                .ThenBy(v => v.Name)
                .ToList();

            Exception duplicateError = null;
            foreach (var seed in levelPlans)
            {
                if (!seed.CanViewBeDuplicated(ViewDuplicateOption.Duplicate)) continue;
                try
                {
                    var duplicate = targetDoc.GetElement(
                        seed.Duplicate(ViewDuplicateOption.Duplicate)) as ViewPlan;
                    if (duplicate == null) continue;
                    try
                    {
                        if (duplicate.GetTypeId() != targetType.Id)
                            duplicate.ChangeTypeId(targetType.Id);
                    }
                    catch
                    {
                        // The duplicated view is still valid and level-correct. A
                        // later direction/template pass may supply the final type.
                    }
                    return duplicate;
                }
                catch (Exception ex)
                {
                    duplicateError = ex;
                }
            }

            if (warnings != null)
            {
                string reason = duplicateError != null ? duplicateError.Message
                    : createError != null ? createError.Message
                    : "目標樓層沒有可複製的同類平面視圖";
                warnings.Add(string.Format("平面視圖建立失敗（{0} / {1}）：{2}",
                    sourcePlan.Name, targetLevel.Name, reason));
            }
            return null;
        }

        private static void ApplyTemplateScale(Autodesk.Revit.DB.View targetView,
            Autodesk.Revit.DB.View targetTemplate, List<string> warnings)
        {
            if (targetView == null || targetTemplate == null || targetView.ViewType == ViewType.Legend) return;
            int templateScale;
            try { templateScale = targetTemplate.Scale; }
            catch { return; }
            if (templateScale <= 0 || targetView.Scale == templateScale) return;
            try { targetView.Scale = templateScale; }
            catch
            {
                // If Scale is controlled by the template, assigning the template
                // should already have applied it. A remaining mismatch is explicit.
                if (targetView.Scale != templateScale && warnings != null)
                    warnings.Add(string.Format("視圖比例待確認（{0}）：樣板 1:{1}，視圖 1:{2}",
                        targetView.Name, templateScale, targetView.Scale));
            }
        }

        private static bool RequiresColumnWallScale250(string sheetName)
        {
            string name = sheetName ?? string.Empty;
            return name.Contains("柱牆圖-1") && !name.Contains("索引圖");
        }

        private static string GetUpperFloorLookUpBuildingKey(SheetPreviewItemDto item,
            SheetViewItemDto plannedView, Autodesk.Revit.DB.View sourceView)
        {
            if (item == null) return string.Empty;
            int floorNumber;
            bool isUpperFloor = int.TryParse(System.Text.RegularExpressions.Regex.Replace(
                item.SheetFloorName ?? string.Empty, @"(?i)F.*$", string.Empty), out floorNumber) && floorNumber >= 2;
            string allText = string.Join(" ", new[]
            {
                item.SheetName ?? string.Empty,
                plannedView != null ? plannedView.ViewName : string.Empty,
                plannedView != null ? plannedView.TargetViewName : string.Empty,
                sourceView != null ? sourceView.Name : string.Empty
            });
            if (!isUpperFloor || !allText.Contains("見上軀體圖") || allText.Contains("索引圖"))
                return string.Empty;

            // View-level text has precedence so a combined OB/LB source sheet
            // can map each viewport independently.
            string viewText = string.Join(" ", new[]
            {
                plannedView != null ? plannedView.ViewName : string.Empty,
                plannedView != null ? plannedView.TargetViewName : string.Empty,
                sourceView != null ? sourceView.Name : string.Empty
            });
            string viewKey = GetRequestedBuildingKey(viewText);
            var conventions = FactoryConventions.Current;
            if (conventions.IsSpecificBuildingKey(viewKey)) return viewKey;

            string requestedKey = GetRequestedBuildingKey(item.BuildingName);
            if (conventions.IsSpecificBuildingKey(requestedKey)) return requestedKey;

            string sheetKey = GetRequestedBuildingKey(item.SheetName);
            return conventions.IsSpecificBuildingKey(sheetKey) ? sheetKey : string.Empty;
        }

        private static string GetPreviousExistingFloorCode(List<ParsedFloorGroup> stories, string currentFloorCode)
        {
            if (stories == null) return string.Empty;
            int index = stories.FindIndex(s => string.Equals(s.FloorCode, currentFloorCode,
                StringComparison.OrdinalIgnoreCase));
            return index > 0 ? stories[index - 1].FloorCode : string.Empty;
        }

        private static Level FindPreviousBuildingStoryLevel(List<ParsedFloorGroup> stories,
            string currentFloorCode, string buildingKey, Level preferredLevel)
        {
            if (stories == null || string.IsNullOrWhiteSpace(buildingKey)) return null;
            int index = stories.FindIndex(s => string.Equals(s.FloorCode, currentFloorCode,
                StringComparison.OrdinalIgnoreCase));
            if (index <= 0) return null;
            var previousStory = stories[index - 1];
            var selected = SelectLevelForBuilding(previousStory, buildingKey, preferredLevel);
            if (selected != null && !IsAuxiliaryRLevelName(selected.Name) &&
                string.Equals(GetLevelBuildingKey(selected.Name), buildingKey,
                StringComparison.OrdinalIgnoreCase)) return selected;

            // ALL levels are the project's shared physical storey datum and are
            // valid for the office structural plan when no OB-specific datum is
            // present. R-prefixed levels remain explicitly excluded.
            string sharedKey = FactoryConventions.Current.SharedBuildingKey;
            var shared = SelectLevelForBuilding(previousStory, sharedKey, preferredLevel);
            return shared != null && !IsAuxiliaryRLevelName(shared.Name) &&
                string.Equals(GetLevelBuildingKey(shared.Name), sharedKey,
                StringComparison.OrdinalIgnoreCase) ? shared : null;
        }

        private static bool IsStructuralUpPlan(Document doc, ViewPlan plan)
        {
            if (doc == null || plan == null) return false;
            var type = doc.GetElement(plan.GetTypeId()) as ViewFamilyType;
            return type != null && type.ViewFamily == ViewFamily.StructuralPlan &&
                type.PlanViewDirection == PlanViewDirection.Up;
        }

        private static ViewFamilyType ResolveStructuralUpViewFamilyType(Document doc, ViewFamilyType preferred)
        {
            if (doc == null) return null;
            var structuralTypes = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().Where(t => t.ViewFamily == ViewFamily.StructuralPlan).ToList();
            var existingUp = structuralTypes.FirstOrDefault(t => t.PlanViewDirection == PlanViewDirection.Up);
            if (existingUp != null) return existingUp;

            var seed = preferred != null && preferred.ViewFamily == ViewFamily.StructuralPlan
                ? preferred : structuralTypes.FirstOrDefault();
            if (seed == null) return null;
            try
            {
                string baseName = seed.Name + " (BDF-見上向上)";
                var names = new HashSet<string>(structuralTypes.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
                string name = baseName;
                int suffix = 2;
                while (names.Contains(name)) name = baseName + " (" + suffix++ + ")";
                var duplicate = seed.Duplicate(name) as ViewFamilyType;
                if (duplicate == null) return null;
                duplicate.PlanViewDirection = PlanViewDirection.Up;
                return duplicate;
            }
            catch { return null; }
        }

        private static void ApplyPlanViewDirection(ViewPlan sourcePlan, ViewPlan targetPlan, bool forceUp)
        {
            if (targetPlan == null || !forceUp) return;
            try
            {
                var upType = ResolveStructuralUpViewFamilyType(targetPlan.Document,
                    targetPlan.Document.GetElement(targetPlan.GetTypeId()) as ViewFamilyType);
                if (upType != null && targetPlan.GetTypeId() != upType.Id)
                    targetPlan.ChangeTypeId(upType.Id);
            }
            catch { }
        }

        private static Autodesk.Revit.DB.View ResolveScaleSpecificTemplate(Document doc,
            Autodesk.Revit.DB.View baseTemplate, int scale)
        {
            if (doc == null || baseTemplate == null || !baseTemplate.IsTemplate) return baseTemplate;
            if (baseTemplate.Scale == scale) return baseTemplate;
            string requestedName = baseTemplate.Name + " (BDF-1-" + scale + ")";
            var existing = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>().FirstOrDefault(v => v.IsTemplate &&
                    string.Equals(v.Name, requestedName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                try { existing.Scale = scale; } catch { }
                return existing;
            }
            try
            {
                var duplicate = doc.GetElement(baseTemplate.Duplicate(ViewDuplicateOption.Duplicate)) as Autodesk.Revit.DB.View;
                if (duplicate == null) return baseTemplate;
                duplicate.Name = GetUniqueViewName(doc, requestedName);
                duplicate.Scale = scale;
                return duplicate;
            }
            catch { return baseTemplate; }
        }

        private static Autodesk.Revit.DB.View ResolveSynchronizedViewTemplate(Document sourceDoc,
            Autodesk.Revit.DB.View sourceTemplate, Document targetDoc, string templateName, List<string> warnings,
            GenerationBatchCache batchCache)
        {
            if (sourceTemplate == null || !sourceTemplate.IsTemplate || targetDoc == null) return null;
            if (ReferenceEquals(sourceDoc, targetDoc)) return sourceTemplate;

            string cacheKey = sourceDoc.Title + "|" + sourceTemplate.UniqueId + "|" + targetDoc.Title;
            ElementId cachedId;
            if (batchCache != null && batchCache.SynchronizedTemplateIds.TryGetValue(cacheKey, out cachedId))
            {
                var cachedTemplate = targetDoc.GetElement(cachedId) as Autodesk.Revit.DB.View;
                if (cachedTemplate != null && cachedTemplate.IsTemplate) return cachedTemplate;
                batchCache.SynchronizedTemplateIds.Remove(cacheKey);
            }

            var targetTemplates = new FilteredElementCollector(targetDoc).OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>().Where(v => v.IsTemplate).ToList();
            var sameName = targetTemplates.FirstOrDefault(v =>
                string.Equals(v.Name, templateName, StringComparison.OrdinalIgnoreCase));
            string sourceSignature = GetViewTemplateSignature(sourceDoc, sourceTemplate);
            if (sameName != null && string.Equals(sourceSignature,
                GetViewTemplateSignature(targetDoc, sameName), StringComparison.Ordinal))
            {
                if (batchCache != null) batchCache.SynchronizedTemplateIds[cacheKey] = sameName.Id;
                return sameName;
            }

            // Square brackets are prohibited in Revit view names.
            string synchronizedName = templateName + " (BDF-A-" + ComputeStableHash(sourceSignature) + ")";
            var synchronized = targetTemplates.FirstOrDefault(v =>
                string.Equals(v.Name, synchronizedName, StringComparison.OrdinalIgnoreCase));
            if (synchronized != null)
            {
                if (batchCache != null) batchCache.SynchronizedTemplateIds[cacheKey] = synchronized.Id;
                return synchronized;
            }

            // When B already has the same named system template, do not try to
            // copy A's View element across documents. Revit can reject that copy
            // because templates own document-specific filters and dependencies.
            // Duplicate B's valid local template, then transfer A's compatible
            // settings onto the duplicate instead.
            if (sameName != null)
            {
                Autodesk.Revit.DB.View localSynchronized = null;
                try
                {
                    localSynchronized = targetDoc.GetElement(
                        sameName.Duplicate(ViewDuplicateOption.Duplicate)) as Autodesk.Revit.DB.View;
                    if (localSynchronized == null)
                        throw new InvalidOperationException("無法複製 B 的同名視圖樣板");
                    localSynchronized.Name = GetUniqueViewName(targetDoc, synchronizedName);
                    CopyCompatibleParameters(sourceDoc, sourceTemplate, targetDoc, localSynchronized);
                    CopyViewGraphicSettings(sourceDoc, sourceTemplate, targetDoc, localSynchronized);
                    try { localSynchronized.Scale = sourceTemplate.Scale; } catch { }
                    try { localSynchronized.DetailLevel = sourceTemplate.DetailLevel; } catch { }
                    if (batchCache != null)
                        batchCache.SynchronizedTemplateIds[cacheKey] = localSynchronized.Id;
                    return localSynchronized;
                }
                catch (Exception ex)
                {
                    if (localSynchronized != null)
                    {
                        try { targetDoc.Delete(localSynchronized.Id); } catch { }
                    }
                    if (warnings != null)
                        AddWarningOnce(warnings, string.Format(
                            "同名視圖樣板「{0}」的 A/B 設定不同；套用 A 設定到 B 本地副本失敗，已安全改用 B 現有樣板。原因：{1}",
                            templateName, ex.Message));
                    if (batchCache != null) batchCache.SynchronizedTemplateIds[cacheKey] = sameName.Id;
                    return sameName;
                }
            }

            // View names must be unique in one document. Temporarily free the A
            // template name, copy A's actual template, rename the copy to a
            // plugin-owned name, then restore B's original template unchanged.
            string originalTargetName = sameName != null ? sameName.Name : null;
            bool targetRenamed = false;
            try
            {
                if (sameName != null)
                {
                    sameName.Name = GetUniqueViewName(targetDoc, templateName + " (B原樣板暫存)");
                    targetRenamed = true;
                }
                var copiedIds = ElementTransformUtils.CopyElements(sourceDoc,
                    new List<ElementId> { sourceTemplate.Id }, targetDoc, Transform.Identity, CreateCopyPasteOptions());
                synchronized = copiedIds.Select(id => targetDoc.GetElement(id) as Autodesk.Revit.DB.View)
                    .FirstOrDefault(v => v != null && v.IsTemplate);
                if (synchronized != null) synchronized.Name = synchronizedName;
            }
            catch (Exception ex)
            {
                if (warnings != null)
                    AddWarningOnce(warnings, "A 視圖樣板同步失敗（" + templateName + "）：" + ex.Message);
            }
            finally
            {
                if (targetRenamed && sameName != null)
                {
                    try { sameName.Name = originalTargetName; }
                    catch (Exception ex)
                    {
                        if (warnings != null)
                            AddWarningOnce(warnings, "B 原視圖樣板名稱還原失敗（" + templateName + "）：" + ex.Message);
                    }
                }
            }

            if (synchronized != null && sameName != null && warnings != null)
                AddWarningOnce(warnings, "同名視圖樣板內容不同，已保留 B 原樣板並改用 A 同步版：" + synchronized.Name);
            if (synchronized != null && batchCache != null)
                batchCache.SynchronizedTemplateIds[cacheKey] = synchronized.Id;
            return synchronized ?? sameName;
        }

        private static string GetViewTemplateSignature(Document doc, Autodesk.Revit.DB.View template)
        {
            if (doc == null || template == null) return string.Empty;
            var parts = new List<string>
            {
                "ViewType=" + template.ViewType,
                "Scale=" + SafeGetViewScale(template),
                "Detail=" + SafeGetDetailLevel(template)
            };
            var nonControlled = new HashSet<long>();
            try
            {
                foreach (var id in template.GetNonControlledTemplateParameterIds()) nonControlled.Add(id.Value);
            }
            catch { }
            try
            {
                foreach (var id in template.GetTemplateParameterIds())
                {
                    var parameter = template.Parameters.Cast<Parameter>()
                        .FirstOrDefault(p => p != null && p.Id == id);
                    if (parameter == null || parameter.Definition == null) continue;
                    parts.Add("P:" + parameter.Definition.Name + ":" +
                        (nonControlled.Contains(id.Value) ? "N:" : "C:") + GetStableParameterValue(doc, parameter));
                }
            }
            catch { }
            try
            {
                foreach (Category category in doc.Settings.Categories)
                {
                    try
                    {
                        if (template.CanCategoryBeHidden(category.Id))
                            parts.Add("CAT:" + category.Id.Value + ":" + template.GetCategoryHidden(category.Id));
                    }
                    catch { }
                }
            }
            catch { }
            try
            {
                foreach (var filterId in template.GetFilters())
                {
                    var filter = doc.GetElement(filterId);
                    parts.Add("FILTER:" + (filter != null ? filter.Name : filterId.Value.ToString()) + ":" +
                        template.GetFilterVisibility(filterId));
                }
            }
            catch { }
            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts);
        }

        private static string GetStableParameterValue(Document doc, Parameter parameter)
        {
            try
            {
                switch (parameter.StorageType)
                {
                    case StorageType.Double: return parameter.AsDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    case StorageType.Integer: return parameter.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case StorageType.String: return parameter.AsString() ?? string.Empty;
                    case StorageType.ElementId:
                        var id = parameter.AsElementId();
                        var element = doc.GetElement(id);
                        return element != null ? element.GetType().FullName + ":" + element.Name : id.Value.ToString();
                    default: return string.Empty;
                }
            }
            catch { return string.Empty; }
        }

        private static int SafeGetViewScale(Autodesk.Revit.DB.View view)
        {
            try { return view.Scale; } catch { return 0; }
        }

        private static string SafeGetDetailLevel(Autodesk.Revit.DB.View view)
        {
            try { return view.DetailLevel.ToString(); } catch { return string.Empty; }
        }

        private static string ComputeStableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in value ?? string.Empty)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static void AddWarningOnce(List<string> warnings, string message)
        {
            if (warnings != null && !warnings.Contains(message)) warnings.Add(message);
        }

        private static bool CanReuseViewOnSheet(Document doc, ViewSheet sheet, Autodesk.Revit.DB.View view)
        {
            try { return doc != null && sheet != null && view != null && Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id); }
            catch { return false; }
        }

        private static bool IsReusableViewMatch(Autodesk.Revit.DB.View candidate,
            Autodesk.Revit.DB.View sourceView, Level requiredLevel)
        {
            if (candidate == null || sourceView == null) return false;
            bool sourceDependent = false;
            bool candidateDependent = false;
            try { sourceDependent = sourceView.GetPrimaryViewId() != ElementId.InvalidElementId; } catch { }
            try { candidateDependent = candidate.GetPrimaryViewId() != ElementId.InvalidElementId; } catch { }
            if (sourceDependent != candidateDependent) return false;
            var sourcePlan = sourceView as ViewPlan;
            if (sourcePlan != null)
            {
                var candidatePlan = candidate as ViewPlan;
                if (candidatePlan == null || candidatePlan.ViewType != sourcePlan.ViewType) return false;
                return requiredLevel == null ||
                    (candidatePlan.GenLevel != null && candidatePlan.GenLevel.Id == requiredLevel.Id);
            }
            return candidate.ViewType == sourceView.ViewType;
        }

        private static Autodesk.Revit.DB.View CreateSectionOrDetailView(Document sourceDoc, ViewSection sourceView,
            Document targetDoc, Level sourceReferenceLevel, Level targetLevel,
            List<ParsedFloorGroup> sourceStories, List<ParsedFloorGroup> targetStories, List<string> warnings)
        {
            var sourceType = sourceDoc.GetElement(sourceView.GetTypeId()) as ViewFamilyType;
            var candidates = new List<ViewFamilyType>();
            var resolved = ResolveViewFamilyType(sourceDoc, sourceType, targetDoc);
            if (resolved != null) candidates.Add(resolved);
            // Revit 不允許以部分 Elevation 系統類型呼叫 CreateSection；若原類型不能建立，
            // 以目標專案的 Section 類型重建同一剖刀／裁切範圍，再套回 A 的樣板與圖面設定。
            candidates.AddRange(new FilteredElementCollector(targetDoc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(t => t.ViewFamily == ViewFamily.Section && candidates.All(c => c.Id != t.Id)));
            Exception lastError = null;
            foreach (var targetType in candidates)
            {
                try
                {
                    return ViewSection.CreateSection(targetDoc, targetType.Id,
                        CreateLevelBoundSectionBox(sourceView.CropBox, sourceReferenceLevel, targetLevel,
                            sourceStories, targetStories));
                }
                catch (Exception ex) { lastError = ex; }
            }
            warnings.Add("剖面／立面／詳圖專用建立方式失敗（" + sourceView.Name + "）：" +
                (lastError != null ? lastError.Message : "找不到可用的視圖類型"));
            return null;
        }

        private static ViewFamilyType ResolveViewFamilyType(Document sourceDoc, ViewFamilyType sourceType, Document targetDoc)
        {
            if (sourceType == null) return null;
            var targetTypes = new FilteredElementCollector(targetDoc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().ToList();
            var targetType = targetTypes.FirstOrDefault(t => t.ViewFamily == sourceType.ViewFamily &&
                    string.Equals(t.Name, sourceType.Name, StringComparison.OrdinalIgnoreCase))
                ?? targetTypes.FirstOrDefault(t => t.ViewFamily == sourceType.ViewFamily);
            if (targetType != null) return targetType;
            if (ReferenceEquals(sourceDoc, targetDoc)) return sourceType;
            try
            {
                var copied = ElementTransformUtils.CopyElements(sourceDoc, new List<ElementId> { sourceType.Id },
                    targetDoc, Transform.Identity, CreateCopyPasteOptions());
                targetType = copied.Select(id => targetDoc.GetElement(id) as ViewFamilyType).FirstOrDefault(t => t != null);
            }
            catch { }
            return targetType;
        }

        private static BoundingBoxXYZ CreateShiftedCropBox(Autodesk.Revit.DB.View sourceView,
            Level sourceReferenceLevel, Level targetReferenceLevel)
        {
            double shift = sourceReferenceLevel != null && targetReferenceLevel != null
                ? targetReferenceLevel.Elevation - sourceReferenceLevel.Elevation : 0;
            return CloneBoundingBox(sourceView.CropBox, new XYZ(0, 0, shift));
        }

        private static BoundingBoxXYZ CreateLevelBoundSectionBox(BoundingBoxXYZ source,
            Level sourceLevel, Level targetLevel, List<ParsedFloorGroup> sourceStories,
            List<ParsedFloorGroup> targetStories)
        {
            if (source == null) return null;
            if (sourceLevel == null || targetLevel == null)
                return CloneBoundingBox(source, XYZ.Zero);

            // In a Revit section box local Y is normally the vertical axis.  Keep
            // A's cut direction, horizontal width and view depth, but rebuild the
            // vertical interval from B's target floor to its next real Level.
            XYZ vertical = source.Transform.BasisY;
            double verticalDot = vertical.DotProduct(XYZ.BasisZ);
            if (Math.Abs(verticalDot) < 0.90)
                return CloneBoundingBox(source, new XYZ(0, 0, targetLevel.Elevation - sourceLevel.Elevation));

            var sourceNext = ResolveStoryLevel(sourceLevel, 1, sourceStories);
            var targetNext = ResolveStoryLevel(targetLevel, 1, targetStories);
            if (targetNext == null)
                return CloneBoundingBox(source, new XYZ(0, 0, targetLevel.Elevation - sourceLevel.Elevation));

            double sourceWorldBottom;
            double sourceWorldTop;
            GetWorldVerticalExtents(source, out sourceWorldBottom, out sourceWorldTop);
            double bottomPadding = sourceWorldBottom - sourceLevel.Elevation;
            double topPadding = sourceNext != null
                ? sourceWorldTop - sourceNext.Elevation
                : bottomPadding;
            // Preserve A's small crop margins but reject multi-storey accidental
            // extents; elevator sections for one sheet must stop at adjacent Levels.
            double maxPadding = 1000.0 / 304.8;
            bottomPadding = Math.Max(-maxPadding, Math.Min(maxPadding, bottomPadding));
            topPadding = Math.Max(-maxPadding, Math.Min(maxPadding, topPadding));
            double desiredBottom = targetLevel.Elevation + bottomPadding;
            double desiredTop = targetNext.Elevation + topPadding;
            if (desiredTop <= desiredBottom + 100.0 / 304.8)
                desiredTop = targetNext.Elevation;

            var result = CloneBoundingBox(source, XYZ.Zero);
            double localA = (desiredBottom - result.Transform.Origin.Z) / verticalDot;
            double localB = (desiredTop - result.Transform.Origin.Z) / verticalDot;
            result.Min = new XYZ(result.Min.X, Math.Min(localA, localB), result.Min.Z);
            result.Max = new XYZ(result.Max.X, Math.Max(localA, localB), result.Max.Z);
            return result;
        }

        private static void GetWorldVerticalExtents(BoundingBoxXYZ box, out double minimum, out double maximum)
        {
            minimum = double.MaxValue;
            maximum = double.MinValue;
            foreach (double x in new[] { box.Min.X, box.Max.X })
                foreach (double y in new[] { box.Min.Y, box.Max.Y })
                    foreach (double z in new[] { box.Min.Z, box.Max.Z })
                    {
                        double worldZ = box.Transform.OfPoint(new XYZ(x, y, z)).Z;
                        minimum = Math.Min(minimum, worldZ);
                        maximum = Math.Max(maximum, worldZ);
                    }
        }

        private static BoundingBoxXYZ CloneBoundingBox(BoundingBoxXYZ source, XYZ translation)
        {
            if (source == null) return null;
            var transform = Transform.Identity;
            transform.BasisX = source.Transform.BasisX;
            transform.BasisY = source.Transform.BasisY;
            transform.BasisZ = source.Transform.BasisZ;
            transform.Origin = source.Transform.Origin + translation;
            return new BoundingBoxXYZ
            {
                Min = source.Min,
                Max = source.Max,
                Transform = transform,
                Enabled = source.Enabled
            };
        }

        private sealed class SheetLayoutItem
        {
            public Element Element { get; set; }
            public SheetRectangle Bounds { get; set; }
            public XYZ Anchor { get; set; }
        }

        private sealed class SheetRectangle
        {
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }
            public double Width { get { return MaxX - MinX; } }
            public double Height { get { return MaxY - MinY; } }
            public bool Intersects(SheetRectangle other, double gap)
            {
                return !(MaxX + gap <= other.MinX || other.MaxX + gap <= MinX ||
                    MaxY + gap <= other.MinY || other.MaxY + gap <= MinY);
            }
            public SheetRectangle MoveTo(XYZ oldAnchor, XYZ newAnchor)
            {
                double dx = newAnchor.X - oldAnchor.X;
                double dy = newAnchor.Y - oldAnchor.Y;
                return new SheetRectangle { MinX = MinX + dx, MinY = MinY + dy, MaxX = MaxX + dx, MaxY = MaxY + dy };
            }
        }

        private static void ReportSheetLayoutConflicts(Document doc, ViewSheet sheet, List<string> warnings)
        {
            var rectangles = new List<Tuple<string, SheetRectangle>>();
            foreach (var viewport in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                var rect = GetViewportRectangle(viewport);
                var view = doc.GetElement(viewport.ViewId) as Autodesk.Revit.DB.View;
                if (rect != null) rectangles.Add(Tuple.Create(view != null ? view.Name : "視埠", rect));
            }
            foreach (var schedule in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>().Where(s => !s.IsTitleblockRevisionSchedule))
            {
                var box = schedule.get_BoundingBox(sheet);
                var view = doc.GetElement(schedule.ScheduleId) as ViewSchedule;
                if (box != null) rectangles.Add(Tuple.Create(view != null ? view.Name : "明細表",
                    new SheetRectangle { MinX = box.Min.X, MinY = box.Min.Y, MaxX = box.Max.X, MaxY = box.Max.Y }));
            }
            double gap = 1.0 / 304.8;
            for (int i = 0; i < rectangles.Count; i++)
                for (int j = i + 1; j < rectangles.Count; j++)
                    if (rectangles[i].Item2.Intersects(rectangles[j].Item2, gap) && warnings != null)
                        warnings.Add("保留 A 排版後偵測到重疊：" + rectangles[i].Item1 + " / " + rectangles[j].Item1);
        }

        private static void AutoArrangeSheetContent(Document doc, ViewSheet sheet)
        {
            var titleBlock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstOrDefault();
            var sheetBox = titleBlock != null ? titleBlock.get_BoundingBox(sheet) : null;
            if (sheetBox == null) return;

            double mm = 1.0 / 304.8;
            double margin = 8 * mm;
            double gap = 5 * mm;
            var usable = new SheetRectangle
            {
                MinX = sheetBox.Min.X + margin,
                MinY = sheetBox.Min.Y + margin,
                MaxX = sheetBox.Max.X - margin,
                MaxY = sheetBox.Max.Y - margin
            };
            // A3/A系列圖框通常將資訊欄放在右下角；保留該區域，避免視圖或明細表壓住圖框文字。
            var titleZone = new SheetRectangle
            {
                MinX = sheetBox.Max.X - Math.Min(85 * mm, (sheetBox.Max.X - sheetBox.Min.X) * 0.30),
                MinY = sheetBox.Min.Y,
                MaxX = sheetBox.Max.X,
                MaxY = sheetBox.Min.Y + Math.Min(58 * mm, (sheetBox.Max.Y - sheetBox.Min.Y) * 0.28)
            };

            var items = new List<SheetLayoutItem>();
            foreach (var viewport in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                var rect = GetViewportRectangle(viewport);
                if (rect != null) items.Add(new SheetLayoutItem { Element = viewport, Bounds = rect, Anchor = viewport.GetBoxCenter() });
            }
            foreach (var schedule in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                .Where(s => !s.IsTitleblockRevisionSchedule))
            {
                var box = schedule.get_BoundingBox(sheet);
                if (box != null) items.Add(new SheetLayoutItem
                {
                    Element = schedule,
                    Bounds = new SheetRectangle { MinX = box.Min.X, MinY = box.Min.Y, MaxX = box.Max.X, MaxY = box.Max.Y },
                    Anchor = schedule.Point
                });
            }

            // 大視圖先定位，小視圖與明細表填補剩餘區域，可最大化使用 A3 紙面。
            items = items.OrderByDescending(i => i.Bounds.Width * i.Bounds.Height).ToList();
            var occupied = new List<SheetRectangle>();
            foreach (var item in items)
            {
                XYZ selected = FindFreeLayoutAnchor(item, usable, titleZone, occupied, gap);
                if (selected == null)
                    selected = TryShrinkViewportToFit(doc, item, usable, titleZone, occupied, gap);
                if (selected == null)
                    throw new InvalidOperationException("A3 可用區域不足：自動調整比例後仍無法讓所有視圖避開圖框文字與彼此衝突。");

                MoveSheetLayoutItem(doc, item, selected);
                occupied.Add(item.Bounds.MoveTo(item.Anchor, selected));
            }
            doc.Regenerate();
        }

        private static XYZ FindFreeLayoutAnchor(SheetLayoutItem item, SheetRectangle usable, SheetRectangle titleZone,
            List<SheetRectangle> occupied, double gap)
        {
            var desired = ClampLayoutAnchor(item.Bounds, item.Anchor, usable);
            var desiredRect = item.Bounds.MoveTo(item.Anchor, desired);
            if (IsLayoutPositionFree(desiredRect, usable, titleZone, occupied, gap)) return desired;
            double step = 6.0 / 304.8;
            for (double y = usable.MaxY - item.Bounds.Height / 2; y >= usable.MinY + item.Bounds.Height / 2; y -= step)
                for (double x = usable.MinX + item.Bounds.Width / 2; x <= usable.MaxX - item.Bounds.Width / 2; x += step)
                {
                    var candidate = new XYZ(x, y, item.Anchor.Z);
                    if (IsLayoutPositionFree(item.Bounds.MoveTo(item.Anchor, candidate), usable, titleZone, occupied, gap))
                        return candidate;
                }
            return null;
        }

        private static XYZ TryShrinkViewportToFit(Document doc, SheetLayoutItem item, SheetRectangle usable,
            SheetRectangle titleZone, List<SheetRectangle> occupied, double gap)
        {
            var viewport = item.Element as Viewport;
            var view = viewport != null ? doc.GetElement(viewport.ViewId) as Autodesk.Revit.DB.View : null;
            if (view == null || view.ViewType == ViewType.Legend || view.ViewType == ViewType.DraftingView) return null;
            int originalScale = Math.Max(1, view.Scale);
            int[] scales = { 20, 25, 50, 75, 100, 125, 150, 200, 250, 300, 400, 500, 750, 1000 };
            foreach (int scale in scales.Where(s => s > originalScale && s <= originalScale * 4))
            {
                try
                {
                    view.Scale = scale;
                    doc.Regenerate();
                    var rect = GetViewportRectangle(viewport);
                    if (rect == null) continue;
                    item.Bounds = rect;
                    item.Anchor = viewport.GetBoxCenter();
                    var selected = FindFreeLayoutAnchor(item, usable, titleZone, occupied, gap);
                    if (selected != null) return selected;
                }
                catch { }
            }
            return null;
        }

        private static SheetRectangle GetViewportRectangle(Viewport viewport)
        {
            try
            {
                var box = viewport.GetBoxOutline();
                var result = new SheetRectangle { MinX = box.MinimumPoint.X, MinY = box.MinimumPoint.Y, MaxX = box.MaximumPoint.X, MaxY = box.MaximumPoint.Y };
                try
                {
                    var label = viewport.GetLabelOutline();
                    result.MinX = Math.Min(result.MinX, label.MinimumPoint.X);
                    result.MinY = Math.Min(result.MinY, label.MinimumPoint.Y);
                    result.MaxX = Math.Max(result.MaxX, label.MaximumPoint.X);
                    result.MaxY = Math.Max(result.MaxY, label.MaximumPoint.Y);
                }
                catch { }
                return result;
            }
            catch { return null; }
        }

        private static XYZ ClampLayoutAnchor(SheetRectangle rect, XYZ anchor, SheetRectangle usable)
        {
            double left = anchor.X - rect.MinX;
            double right = rect.MaxX - anchor.X;
            double bottom = anchor.Y - rect.MinY;
            double top = rect.MaxY - anchor.Y;
            return new XYZ(Math.Max(usable.MinX + left, Math.Min(usable.MaxX - right, anchor.X)),
                Math.Max(usable.MinY + bottom, Math.Min(usable.MaxY - top, anchor.Y)), anchor.Z);
        }

        private static bool IsLayoutPositionFree(SheetRectangle rect, SheetRectangle usable, SheetRectangle titleZone,
            List<SheetRectangle> occupied, double gap)
        {
            if (rect.MinX < usable.MinX || rect.MaxX > usable.MaxX || rect.MinY < usable.MinY || rect.MaxY > usable.MaxY)
                return false;
            if (rect.Intersects(titleZone, gap)) return false;
            return occupied.All(o => !rect.Intersects(o, gap));
        }

        private static void MoveSheetLayoutItem(Document doc, SheetLayoutItem item, XYZ target)
        {
            var viewport = item.Element as Viewport;
            if (viewport != null) viewport.SetBoxCenter(target);
            else ElementTransformUtils.MoveElement(doc, item.Element.Id, target - item.Anchor);
        }

        private static Autodesk.Revit.DB.View CopyLegendToTarget(Document sourceDoc, Autodesk.Revit.DB.View sourceLegend,
            Document targetDoc)
        {
            try
            {
                var copied = ElementTransformUtils.CopyElements(sourceDoc,
                    new List<ElementId> { sourceLegend.Id }, targetDoc, Transform.Identity, CreateCopyPasteOptions());
                var copiedLegend = copied.Select(id => targetDoc.GetElement(id) as Autodesk.Revit.DB.View)
                    .FirstOrDefault(v => v != null && v.ViewType == ViewType.Legend);
                if (copiedLegend != null) return copiedLegend;
            }
            catch { }

            var seedLegend = new FilteredElementCollector(targetDoc).OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.Legend &&
                    v.CanViewBeDuplicated(ViewDuplicateOption.Duplicate));
            if (seedLegend == null) return null;
            try
            {
                var duplicate = targetDoc.GetElement(seedLegend.Duplicate(ViewDuplicateOption.Duplicate)) as Autodesk.Revit.DB.View;
                if (duplicate == null) return null;
                CopyViewSpecificElementsResilient(sourceDoc, sourceLegend, targetDoc, duplicate);
                return duplicate;
            }
            catch { return null; }
        }

        private static Element FindBestScopeBox(List<Element> scopeBoxes, string requestedName, string building, string floor)
        {
            if (scopeBoxes == null || scopeBoxes.Count == 0) return null;
            string requested = NormalizeMatchText(requestedName);
            string buildingText = NormalizeMatchText(NormalizeBuildingName(building));
            string floorText = NormalizeMatchText(floor);
            string discipline = requested.Contains("結構") ? "結構" : requested.Contains("建築") ? "建築" : string.Empty;
            return scopeBoxes
                .Select(e =>
                {
                    string name = NormalizeMatchText(e.Name);
                    int score = name == requested ? 1000 : 0;
                    if (!string.IsNullOrEmpty(floorText) && name.Contains(floorText)) score += 100;
                    if (!string.IsNullOrEmpty(buildingText) && name.Contains(buildingText)) score += 50;
                    if (!string.IsNullOrEmpty(discipline) && name.Contains(discipline)) score += 25;
                    if (!string.IsNullOrEmpty(requested) && (name.Contains(requested) || requested.Contains(name))) score += 20;
                    return new { Element = e, Score = score };
                })
                .Where(x => x.Score >= 100)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Element)
                .FirstOrDefault();
        }

        private static string NormalizeMatchText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return new string(value.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray())
                .Replace("FL", "F");
        }

        private static void ApplyLevelDependentViewSettings(Document targetDoc, ViewPlan targetPlan, Level baseLevel,
            SheetViewItemDto source, List<ParsedFloorGroup> stories)
        {
            if (targetPlan == null || baseLevel == null || source == null) return;
            if (source.HasUnderlay)
            {
                SetElementIdParameter(targetPlan, BuiltInParameter.VIEW_UNDERLAY_BOTTOM_ID,
                    ResolveStoryLevel(baseLevel, source.UnderlayBottomStoryDelta, stories));
                SetElementIdParameter(targetPlan, BuiltInParameter.VIEW_UNDERLAY_TOP_ID,
                    ResolveStoryLevel(baseLevel, source.UnderlayTopStoryDelta, stories));
                try
                {
                    var orientation = targetPlan.get_Parameter(BuiltInParameter.VIEW_UNDERLAY_ORIENTATION);
                    if (orientation != null && !orientation.IsReadOnly) orientation.Set(source.UnderlayOrientation);
                }
                catch { }
            }

            SetNamedElementParameter(targetDoc, targetPlan, BuiltInParameter.VIEW_PHASE,
                source.SourcePhaseName, typeof(Phase));
            SetNamedElementParameter(targetDoc, targetPlan, BuiltInParameter.VIEW_PHASE_FILTER,
                source.SourcePhaseFilterName, typeof(PhaseFilter));
        }

        private static void SetElementIdParameter(Autodesk.Revit.DB.View view, BuiltInParameter parameterId, Element element)
        {
            if (element == null) return;
            try
            {
                var parameter = view.get_Parameter(parameterId);
                if (parameter != null && !parameter.IsReadOnly) parameter.Set(element.Id);
            }
            catch { }
        }

        private static void SetNamedElementParameter(Document doc, Autodesk.Revit.DB.View view,
            BuiltInParameter parameterId, string elementName, Type elementType)
        {
            if (string.IsNullOrWhiteSpace(elementName)) return;
            try
            {
                var match = new FilteredElementCollector(doc).OfClass(elementType)
                    .FirstOrDefault(e => string.Equals(e.Name, elementName, StringComparison.OrdinalIgnoreCase));
                SetElementIdParameter(view, parameterId, match);
            }
            catch { }
        }

        private static void RetargetScheduleToContext(Document sourceDoc, ViewSchedule sourceSchedule,
            ViewSchedule targetSchedule, Level targetLevel, string sourceSheetNumber, string targetSheetNumber)
        {
            if (sourceSchedule == null || targetSchedule == null || targetLevel == null) return;
            var targetDefinition = targetSchedule.Definition;
            bool levelFilterUpdated = false;
            for (int i = 0; i < targetDefinition.GetFilterCount(); i++)
            {
                try
                {
                    var targetFilter = targetDefinition.GetFilter(i);
                    var targetField = targetDefinition.GetField(targetFilter.FieldId);
                    string fieldName = targetField != null ? targetField.GetName() : string.Empty;
                    bool isLevelField = IsScheduleLevelField(targetField);
                    bool isSheetNumberField = IsScheduleSheetNumberField(fieldName);
                    if (targetFilter.IsElementIdValue)
                    {
                        var referenced = targetSchedule.Document.GetElement(targetFilter.GetElementIdValue());
                        isLevelField = isLevelField || referenced is Level;
                    }
                    if (!isLevelField && !isSheetNumberField) continue;

                    ScheduleFilter replacement;
                    if (targetFilter.IsStringValue)
                    {
                        string sourceValue = targetFilter.GetStringValue();
                        string replacementValue = isSheetNumberField
                            ? RetargetSheetSeriesFilter(sourceValue, sourceSheetNumber, targetSheetNumber)
                            : targetLevel.Name;
                        replacement = new ScheduleFilter(targetFilter.FieldId, targetFilter.FilterType, replacementValue);
                    }
                    else
                        replacement = new ScheduleFilter(targetFilter.FieldId, targetFilter.FilterType, targetLevel.Id);
                    targetDefinition.SetFilter(i, replacement);
                    if (isLevelField) levelFilterUpdated = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[RetargetSchedule Warning] " + ex.Message);
                }
            }

            if (!levelFilterUpdated)
            {
                foreach (var fieldId in targetDefinition.GetFieldOrder())
                {
                    try
                    {
                        var field = targetDefinition.GetField(fieldId);
                        string name = field != null ? field.GetName() : string.Empty;
                        bool isLevelField = IsScheduleLevelField(field);
                        if (!isLevelField || !targetDefinition.CanFilterByValue(fieldId)) continue;
                        // Base Constraint is an ElementId-valued field in Revit.
                        // Prefer the actual target Level id; use its displayed name
                        // only for custom text parameters that expose the same header.
                        try { targetDefinition.AddFilter(new ScheduleFilter(fieldId, ScheduleFilterType.Equal, targetLevel.Id)); }
                        catch { targetDefinition.AddFilter(new ScheduleFilter(fieldId, ScheduleFilterType.Equal, targetLevel.Name)); }
                        levelFilterUpdated = true;
                        break;
                    }
                    catch { }
                }
            }
        }

        private static bool IsScheduleLevelField(string fieldName)
        {
            string name = fieldName ?? string.Empty;
            return name.IndexOf("LEVEL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("BASE CONSTRAINT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("REFERENCE LEVEL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.Contains("樓層") || name.Contains("基準層") || name.Contains("參考層") ||
                name.Contains("基準約束") || name.Contains("底部約束");
        }

        private static bool IsScheduleLevelField(ScheduleField field)
        {
            if (field == null) return false;
            if (IsScheduleLevelField(field.GetName())) return true;
            try
            {
                long parameterId = field.ParameterId.Value;
                return parameterId == (long)BuiltInParameter.WALL_BASE_CONSTRAINT ||
                    parameterId == (long)BuiltInParameter.FAMILY_BASE_LEVEL_PARAM ||
                    parameterId == (long)BuiltInParameter.FAMILY_LEVEL_PARAM ||
                    parameterId == (long)BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM;
            }
            catch { return false; }
        }

        private static bool IsScheduleSheetNumberField(string fieldName)
        {
            string name = fieldName ?? string.Empty;
            return name.IndexOf("SHEET NUMBER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.Contains("圖紙號碼") || name.Contains("圖號");
        }

        private static string RetargetSheetSeriesFilter(string sourceValue, string sourceSheetNumber, string targetSheetNumber)
        {
            string value = sourceValue ?? string.Empty;
            var sourceMatch = System.Text.RegularExpressions.Regex.Match(sourceSheetNumber ?? string.Empty,
                @"(?i)^(.*?SB\d+)");
            var targetMatch = System.Text.RegularExpressions.Regex.Match(targetSheetNumber ?? string.Empty,
                @"(?i)^(.*?SB\d+)");
            if (sourceMatch.Success && targetMatch.Success)
                value = System.Text.RegularExpressions.Regex.Replace(value,
                    System.Text.RegularExpressions.Regex.Escape(sourceMatch.Groups[1].Value),
                    targetMatch.Groups[1].Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return value;
        }

        private static void RetargetViewText(Document doc, Autodesk.Revit.DB.View view, string floor)
        {
            if (doc == null || view == null || string.IsNullOrWhiteSpace(floor)) return;
            string replacement = floor.EndsWith("F", StringComparison.OrdinalIgnoreCase) ? floor + "L" : floor;
            foreach (var note in new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote)).Cast<TextNote>())
            {
                try
                {
                    string updated = System.Text.RegularExpressions.Regex.Replace(note.Text ?? string.Empty,
                        @"(?i)(?:B[1-9]\d*(?:FL|F)|[1-9]\d?F(?:L)?)", replacement);
                    if (!string.Equals(updated, note.Text, StringComparison.Ordinal)) note.Text = updated;
                }
                catch { }
            }
        }

        private static void ApplyPlanViewRange(Document doc, ViewPlan targetPlan, Level baseLevel, SheetViewItemDto source,
            List<ParsedFloorGroup> stories)
        {
            if (doc == null || targetPlan == null || baseLevel == null || source == null) return;
            try
            {
                var range = targetPlan.GetViewRange();
                SetViewRangePlane(range, PlanViewPlane.TopClipPlane, baseLevel, source.TopPlaneStoryDelta, source.TopPlaneOffsetMm, stories);
                SetViewRangePlane(range, PlanViewPlane.CutPlane, baseLevel, source.CutPlaneStoryDelta, source.CutPlaneOffsetMm, stories);
                SetViewRangePlane(range, PlanViewPlane.BottomClipPlane, baseLevel, source.BottomPlaneStoryDelta, source.BottomPlaneOffsetMm, stories);
                SetViewRangePlane(range, PlanViewPlane.ViewDepthPlane, baseLevel, source.ViewDepthPlaneStoryDelta, source.ViewDepthPlaneOffsetMm, stories);
                targetPlan.SetViewRange(range);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ApplyPlanViewRange Warning] " + ex.Message);
            }
        }

        private static void SetViewRangePlane(PlanViewRange range, PlanViewPlane plane,
            Level baseLevel, int storyDelta, double offsetMm, List<ParsedFloorGroup> stories)
        {
            var planeLevel = ResolveStoryLevel(baseLevel, storyDelta, stories);
            if (planeLevel != null) range.SetLevelId(plane, planeLevel.Id);
            range.SetOffset(plane, offsetMm / 304.8);
        }

        private static void RecreateHostedTags(Document sourceDoc, Autodesk.Revit.DB.View sourceView,
            Document targetDoc, Autodesk.Revit.DB.View targetView, ViewPlan sourcePlan, ViewPlan targetPlan,
            List<string> warnings, TargetViewElementIndex targetElementIndex)
        {
            var sourceTags = new FilteredElementCollector(sourceDoc, sourceView.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
            if (sourceTags.Count == 0) return;
            targetDoc.Regenerate();
            int createdCount = 0;

            createdCount += TagAllDetectedCategories(sourceDoc, sourceView, sourceTags, targetDoc, targetView,
                warnings, targetElementIndex);
            targetDoc.Regenerate();
            if (createdCount == 0)
                throw new InvalidOperationException("來源含 " + sourceTags.Count + " 個標籤，但無法重綁或複製至目標構件");
        }

        private static int TagAllDetectedCategories(Document sourceDoc, Autodesk.Revit.DB.View sourceView,
            List<IndependentTag> sourceTags, Document targetDoc, Autodesk.Revit.DB.View targetView,
            List<string> warnings, TargetViewElementIndex targetElementIndex)
        {
            var ruleSamples = new Dictionary<long, List<Tuple<IndependentTag, Element, XYZ>>>();
            foreach (var tag in sourceTags)
            {
                try
                {
                    Element host;
                    XYZ sourceHostCenter;
                    if (!TryGetTaggedHost(sourceDoc, sourceView, tag, out host, out sourceHostCenter)) continue;
                    if (host == null || host.Category == null) continue;
                    List<Tuple<IndependentTag, Element, XYZ>> samples;
                    if (!ruleSamples.TryGetValue(host.Category.Id.Value, out samples))
                    {
                        samples = new List<Tuple<IndependentTag, Element, XYZ>>();
                        ruleSamples[host.Category.Id.Value] = samples;
                    }
                    samples.Add(Tuple.Create(tag, host, tag.TagHeadPosition - sourceHostCenter));
                }
                catch { }
            }

            var existingTagged = new HashSet<long>();
            foreach (var tag in new FilteredElementCollector(targetDoc, targetView.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                foreach (var id in tag.GetTaggedLocalElementIds())
                    if (id != null && id != ElementId.InvalidElementId) existingTagged.Add(id.Value);

            int created = 0;
            AddLinkedTagCandidates(targetDoc, targetView, ruleSamples.Keys, targetElementIndex);
            foreach (var pair in ruleSamples)
            {
                var first = pair.Value.First();
                var targetTagType = ResolveAnnotationType(sourceDoc, first.Item1.GetTypeId(), targetDoc);
                if (targetTagType == null)
                {
                    warnings.Add(string.Format("標籤人工處理｜視圖：{0}｜品類 ID：{1}｜原因：無法取得與基準相同的標籤族／類型",
                        targetView.Name, pair.Key));
                    continue;
                }
                var offsets = pair.Value.Select(x => x.Item3).ToList();
                int middle = offsets.Count / 2;
                XYZ sourceOffset = new XYZ(offsets.Select(x => x.X).OrderBy(x => x).ElementAt(middle),
                    offsets.Select(x => x.Y).OrderBy(x => x).ElementAt(middle),
                    offsets.Select(x => x.Z).OrderBy(x => x).ElementAt(middle));
                List<TargetElementCandidate> targets;
                if (!targetElementIndex.TagByCategory.TryGetValue(pair.Key, out targets))
                    targets = new List<TargetElementCandidate>();
                XYZ categoryOffset = targets.Count > 0
                    ? LearnTargetTagOffset(targetDoc, targetView, targets[0].Element, targetTagType, sourceOffset)
                    : sourceOffset;
                foreach (var candidate in targets)
                {
                    var target = candidate.Element;
                    if (!candidate.IsLinked && existingTagged.Contains(target.Id.Value)) continue;
                    try
                    {
                        XYZ hostPoint = candidate.Center;
                        XYZ learned = GetPreferredTagOffset(targetView, target, categoryOffset);
                        var reference = candidate.TagReference ?? new Reference(target);
                        var newTag = IndependentTag.Create(targetDoc, targetTagType.Id, targetView.Id, reference,
                            first.Item1.HasLeader, first.Item1.TagOrientation, hostPoint + learned);
                        if (newTag != null)
                        {
                            if (!candidate.IsLinked) existingTagged.Add(target.Id.Value);
                            created++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[Tag all detected category Warning] " + ex.Message);
                        warnings.Add(FormatTagManualAction(targetView, target, ex.Message));
                    }
                }
            }
            return created;
        }

        private static bool IsOpeningDrawing(params string[] texts)
        {
            var values = (texts ?? new string[0]).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            // Opening index sheets are navigation drawings and must not receive
            // door tags. Only the actual opening drawing is tagged.
            if (values.Any(t => t.Contains("索引圖"))) return false;
            return values.Any(t => t.Contains("結構開口") || t.Contains("開口圖"));
        }

        private static bool IsFinishElevationDrawing(params string[] texts)
        {
            return (texts ?? new string[0]).Any(t => !string.IsNullOrWhiteSpace(t) &&
                (t.Contains("裝修高程") || t.Contains("裝修平面")));
        }

        private static bool IsRequestedFinishTagType(ElementType type)
        {
            if (type == null) return false;
            string familyName = type is FamilySymbol ? ((FamilySymbol)type).FamilyName : string.Empty;
            string text = familyName + " " + (type.Name ?? string.Empty);
            return text.Contains("裝修厚度") && text.Contains("類型描述");
        }

        private static void EnsureFinishFloorTags(Document sourceDoc, Autodesk.Revit.DB.View sourceView,
            Document targetDoc, Autodesk.Revit.DB.View targetView, TargetViewElementIndex targetElementIndex,
            List<string> warnings)
        {
            long floorCategoryId = (long)BuiltInCategory.OST_Floors;
            var sourceTags = new FilteredElementCollector(sourceDoc, sourceView.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                .Where(tag => IsRequestedFinishTagType(sourceDoc.GetElement(tag.GetTypeId()) as ElementType))
                .ToList();
            ElementType sourceType = sourceTags.Select(t => sourceDoc.GetElement(t.GetTypeId()) as ElementType)
                .FirstOrDefault(t => t != null)
                ?? new FilteredElementCollector(sourceDoc).OfCategory(BuiltInCategory.OST_FloorTags)
                    .WhereElementIsElementType().Cast<ElementType>().FirstOrDefault(IsRequestedFinishTagType);
            if (sourceType == null)
            {
                warnings.Add("裝修標籤：來源 A 找不到名稱含「裝修厚度」與「類型描述」的樓板標籤");
                return;
            }
            var targetType = ResolveAnnotationType(sourceDoc, sourceType.Id, targetDoc);
            if (targetType == null)
            {
                warnings.Add("裝修標籤：目標 B 無法取得 " + sourceType.Name);
                return;
            }

            AddLinkedTagCandidates(targetDoc, targetView, new[] { floorCategoryId }, targetElementIndex);
            List<TargetElementCandidate> floors;
            if (!targetElementIndex.TagByCategory.TryGetValue(floorCategoryId, out floors) || floors.Count == 0)
            {
                warnings.Add("裝修標籤：目標視圖範圍內找不到樓板元件");
                return;
            }

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in new FilteredElementCollector(targetDoc, targetView.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                .Where(t => t.GetTypeId() == targetType.Id))
            {
                try
                {
                    foreach (var id in tag.GetTaggedElementIds())
                        existing.Add(id.LinkInstanceId != ElementId.InvalidElementId
                            ? "R:" + id.LinkInstanceId.Value + ":" + id.LinkedElementId.Value
                            : "L:" + id.HostElementId.Value);
                }
                catch { }
            }

            XYZ sourceOffset = targetView.UpDirection.Normalize() * Math.Max(1, targetView.Scale) * 10.0 / 304.8;
            bool hasLeader = true;
            TagOrientation orientation = TagOrientation.Horizontal;
            var sample = sourceTags.FirstOrDefault();
            if (sample != null)
            {
                Element host;
                XYZ center;
                if (TryGetTaggedHost(sourceDoc, sourceView, sample, out host, out center))
                    sourceOffset = sample.TagHeadPosition - center;
                hasLeader = sample.HasLeader;
                orientation = sample.TagOrientation;
            }

            int created = 0;
            foreach (var floor in floors)
            {
                string key = GetTagCandidateKey(floor);
                if (existing.Contains(key)) continue;
                try
                {
                    var tag = IndependentTag.Create(targetDoc, targetType.Id, targetView.Id,
                        floor.TagReference ?? new Reference(floor.Element), hasLeader, orientation,
                        floor.Center + sourceOffset);
                    if (tag != null) { existing.Add(key); created++; }
                }
                catch { }
            }
            if (created == 0 && existing.Count == 0)
                warnings.Add("裝修標籤：已找到樓板與指定標籤族，但 Revit 拒絕在此視圖建立標籤");
        }

        private static void EnsureDoorTags(Document sourceDoc, Autodesk.Revit.DB.View sourceView,
            Document targetDoc, Autodesk.Revit.DB.View targetView, TargetViewElementIndex targetElementIndex,
            List<string> warnings)
        {
            long doorCategoryId = (long)BuiltInCategory.OST_Doors;
            AddLinkedTagCandidates(targetDoc, targetView, new[] { doorCategoryId }, targetElementIndex);
            List<TargetElementCandidate> doors;
            if (!targetElementIndex.TagByCategory.TryGetValue(doorCategoryId, out doors) || doors.Count == 0)
            {
                warnings.Add("門標籤：目標視圖的裁切範圍與樓層內沒有偵測到門元件");
                return;
            }

            var sourceDoorTags = new FilteredElementCollector(sourceDoc, sourceView.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                .Where(t => t.Category != null && t.Category.Id.Value == (long)BuiltInCategory.OST_DoorTags)
                .ToList();
            ElementType sourceTagType = sourceDoorTags.Select(t => sourceDoc.GetElement(t.GetTypeId()) as ElementType)
                .FirstOrDefault(t => t != null)
                ?? new FilteredElementCollector(sourceDoc).OfCategory(BuiltInCategory.OST_DoorTags)
                    .WhereElementIsElementType().Cast<ElementType>().FirstOrDefault();
            if (sourceTagType == null)
            {
                warnings.Add("門標籤：來源 A 找不到可用的門標籤族／類型");
                return;
            }
            var targetTagType = ResolveAnnotationType(sourceDoc, sourceTagType.Id, targetDoc);
            if (targetTagType == null)
            {
                warnings.Add("門標籤：無法在目標 B 取得或複製 " + sourceTagType.Name);
                return;
            }

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in new FilteredElementCollector(targetDoc, targetView.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                try
                {
                    foreach (var id in tag.GetTaggedElementIds())
                    {
                        if (id.LinkInstanceId != ElementId.InvalidElementId)
                            existing.Add("R:" + id.LinkInstanceId.Value + ":" + id.LinkedElementId.Value);
                        else if (id.HostElementId != ElementId.InvalidElementId)
                            existing.Add("L:" + id.HostElementId.Value);
                    }
                }
                catch { }
            }

            bool hasLeader = sourceDoorTags.Count > 0 && sourceDoorTags[0].HasLeader;
            TagOrientation orientation = sourceDoorTags.Count > 0 ? sourceDoorTags[0].TagOrientation : TagOrientation.Horizontal;
            XYZ sourceOffset = XYZ.Zero;
            if (sourceDoorTags.Count > 0)
            {
                Element sourceHost;
                XYZ sourceCenter;
                if (TryGetTaggedHost(sourceDoc, sourceView, sourceDoorTags[0], out sourceHost, out sourceCenter))
                    sourceOffset = sourceDoorTags[0].TagHeadPosition - sourceCenter;
            }

            int failed = 0;
            int created = 0;
            int alreadyTagged = 0;
            foreach (var door in doors)
            {
                string key = GetTagCandidateKey(door);
                if (existing.Contains(key)) { alreadyTagged++; continue; }
                try
                {
                    var tag = IndependentTag.Create(targetDoc, targetTagType.Id, targetView.Id,
                        door.TagReference ?? new Reference(door.Element), hasLeader, orientation, door.Center + sourceOffset);
                    if (tag != null)
                    {
                        existing.Add(key);
                        created++;
                    }
                }
                catch { failed++; }
            }
            if (created == 0 && doors.Count > 0 && alreadyTagged == 0)
                warnings.Add("門標籤：已偵測到門，但 Revit 沒有允許在此視圖建立任何標籤");
            if (failed > 0)
                warnings.Add(string.Format("門標籤：{0} 個門元件因 Revit 參照或視圖限制無法自動標記", failed));
        }

        private static string GetTagCandidateKey(TargetElementCandidate candidate)
        {
            if (candidate == null || candidate.Element == null) return string.Empty;
            try
            {
                if (candidate.IsLinked && candidate.TagReference != null)
                    return "R:" + candidate.TagReference.ElementId.Value + ":" + candidate.TagReference.LinkedElementId.Value;
            }
            catch { }
            return "L:" + candidate.Element.Id.Value;
        }

        private static bool TryGetTaggedHost(Document hostDoc, Autodesk.Revit.DB.View view, IndependentTag tag,
            out Element taggedElement, out XYZ center)
        {
            taggedElement = null;
            center = XYZ.Zero;
            try
            {
                var localId = tag.GetTaggedLocalElementIds().FirstOrDefault();
                if (localId != null && localId != ElementId.InvalidElementId)
                {
                    taggedElement = hostDoc.GetElement(localId);
                    if (taggedElement != null)
                    {
                        center = GetElementCenter(taggedElement, view);
                        return true;
                    }
                }
                foreach (var taggedId in tag.GetTaggedElementIds())
                {
                    if (taggedId == null || taggedId.LinkInstanceId == ElementId.InvalidElementId ||
                        taggedId.LinkedElementId == ElementId.InvalidElementId) continue;
                    var link = hostDoc.GetElement(taggedId.LinkInstanceId) as RevitLinkInstance;
                    var linkDoc = link != null ? link.GetLinkDocument() : null;
                    taggedElement = linkDoc != null ? linkDoc.GetElement(taggedId.LinkedElementId) : null;
                    if (taggedElement == null) continue;
                    center = link.GetTotalTransform().OfPoint(GetElementCenter(taggedElement, null));
                    return true;
                }
            }
            catch { }
            taggedElement = null;
            center = XYZ.Zero;
            return false;
        }

        private static XYZ GetPreferredTagOffset(Autodesk.Revit.DB.View view, Element host, XYZ learnedOffset)
        {
            if (view == null || host == null || host.Category == null) return learnedOffset;
            long categoryId = host.Category.Id.Value;
            // 紙面約 4 mm 的上移量；乘上視圖比例後換算為模型座標。
            double beamOffset = Math.Max(1, view.Scale) * 4.0 / 304.8;
            if (categoryId == (long)BuiltInCategory.OST_StructuralFraming)
                return view.UpDirection.Normalize() * beamOffset;
            if (categoryId == (long)BuiltInCategory.OST_StructuralColumns ||
                categoryId == (long)BuiltInCategory.OST_Columns)
                return XYZ.Zero;
            // Floor/finish tags keep A's learned offset and leader placement;
            // forcing them to the slab centre discarded the reference layout.
            return learnedOffset;
        }

        private static string FormatTagManualAction(Autodesk.Revit.DB.View view, Element element, string reason)
        {
            string category = element != null && element.Category != null ? element.Category.Name : "未知品類";
            string elementId = element != null ? element.Id.ToString() : "無";
            string typeName = "未知類型";
            try
            {
                var type = element != null ? element.Document.GetElement(element.GetTypeId()) : null;
                if (type != null) typeName = type.Name;
            }
            catch { }
            return string.Format("標籤人工處理｜視圖：{0}｜品類：{1}｜ElementId：{2}｜構件類型：{3}｜原因：{4}",
                view != null ? view.Name : "未知視圖", category, elementId, typeName, reason);
        }

        private static Element GetFirstTaggedHost(Document doc, IndependentTag tag)
        {
            try
            {
                var id = tag.GetTaggedLocalElementIds().FirstOrDefault();
                return id != null && id != ElementId.InvalidElementId ? doc.GetElement(id) : null;
            }
            catch { return null; }
        }

        private static XYZ LearnTargetTagOffset(Document doc, Autodesk.Revit.DB.View targetView, Element targetHost,
            ElementType tagType, XYZ sourceOffset)
        {
            var targetPlan = targetView as ViewPlan;
            if (targetPlan == null || targetPlan.GenLevel == null || targetHost == null || targetHost.Category == null)
                return sourceOffset;
            var samples = new List<XYZ>();
            var candidateViews = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.Id != targetView.Id && v.GenLevel != null &&
                    v.GenLevel.Id == targetPlan.GenLevel.Id).Take(12).ToList();
            foreach (var view in candidateViews)
            {
                foreach (var tag in new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                {
                    try
                    {
                        if (tagType != null && tag.GetTypeId() != tagType.Id) continue;
                        var hostId = tag.GetTaggedLocalElementIds().FirstOrDefault();
                        var host = hostId != null ? doc.GetElement(hostId) : null;
                        if (host == null || host.Category == null || host.Category.Id != targetHost.Category.Id) continue;
                        samples.Add(tag.TagHeadPosition - GetElementCenter(host, view));
                    }
                    catch { }
                }
            }
            if (samples.Count < 2) return sourceOffset;
            var xs = samples.Select(p => p.X).OrderBy(x => x).ToList();
            var ys = samples.Select(p => p.Y).OrderBy(y => y).ToList();
            var zs = samples.Select(p => p.Z).OrderBy(z => z).ToList();
            int middle = samples.Count / 2;
            return new XYZ(xs[middle], ys[middle], zs[middle]);
        }

        private sealed class TargetElementCandidate
        {
            public Element Element { get; set; }
            public XYZ Center { get; set; }
            public string TypeName { get; set; }
            public Reference TagReference { get; set; }
            public bool IsLinked { get; set; }
        }

        private sealed class TargetViewElementIndex
        {
            public Dictionary<long, List<TargetElementCandidate>> ByCategory { get; private set; }
            public Dictionary<long, List<TargetElementCandidate>> TagByCategory { get; private set; }
            public HashSet<long> LoadedLinkedCategories { get; private set; }
            public GenerationBatchCache BatchCache { get; private set; }
            public TargetViewElementIndex(GenerationBatchCache batchCache)
            {
                ByCategory = new Dictionary<long, List<TargetElementCandidate>>();
                TagByCategory = new Dictionary<long, List<TargetElementCandidate>>();
                LoadedLinkedCategories = new HashSet<long>();
                BatchCache = batchCache;
            }
        }

        private static TargetViewElementIndex BuildTargetViewElementIndex(Document doc, Autodesk.Revit.DB.View view,
            GenerationBatchCache batchCache)
        {
            var index = new TargetViewElementIndex(batchCache);
            foreach (var element in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType()
                .Where(e => e.Category != null && !e.ViewSpecific))
            {
                List<TargetElementCandidate> list;
                if (!index.ByCategory.TryGetValue(element.Category.Id.Value, out list))
                {
                    list = new List<TargetElementCandidate>();
                    index.ByCategory[element.Category.Id.Value] = list;
                }
                var type = doc.GetElement(element.GetTypeId());
                var candidate = new TargetElementCandidate
                {
                    Element = element,
                    Center = GetElementCenter(element, view),
                    TypeName = type != null ? type.Name : string.Empty,
                    TagReference = new Reference(element),
                    IsLinked = false
                };
                list.Add(candidate);
                List<TargetElementCandidate> tagList;
                if (!index.TagByCategory.TryGetValue(element.Category.Id.Value, out tagList))
                {
                    tagList = new List<TargetElementCandidate>();
                    index.TagByCategory[element.Category.Id.Value] = tagList;
                }
                tagList.Add(candidate);
            }
            return index;
        }

        private static void AddLinkedTagCandidates(Document hostDoc, Autodesk.Revit.DB.View targetView,
            IEnumerable<long> categoryIds, TargetViewElementIndex index,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (hostDoc == null || targetView == null || index == null) return;
            var categories = new HashSet<long>((categoryIds ?? Enumerable.Empty<long>())
                .Where(id => !index.LoadedLinkedCategories.Contains(id)));
            if (categories.Count == 0) return;
            foreach (long categoryId in categories) index.LoadedLinkedCategories.Add(categoryId);
            var targetPlan = targetView as ViewPlan;
            double levelElevation = targetPlan != null && targetPlan.GenLevel != null ? targetPlan.GenLevel.Elevation : 0;
            double verticalTolerance = 2500.0 / 304.8;
            // Collect link instances from the document, not only from the freshly
            // created view.  View-scoped collection can be empty before Revit has
            // regenerated template/link visibility, which previously yielded zero
            // door candidates on opening drawings.
            foreach (var link in new FilteredElementCollector(hostDoc).OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var linkDoc = link.GetLinkDocument();
                if (linkDoc == null) continue;
                foreach (long categoryId in categories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    List<TargetElementCandidate> tagList;
                    if (!index.TagByCategory.TryGetValue(categoryId, out tagList))
                    {
                        tagList = new List<TargetElementCandidate>();
                        index.TagByCategory[categoryId] = tagList;
                    }
                    string cacheKey = hostDoc.Title + "|" + link.UniqueId + "|" + categoryId;
                    List<TargetElementCandidate> allCandidates = null;
                    if (index.BatchCache != null)
                        index.BatchCache.LinkedTagCandidates.TryGetValue(cacheKey, out allCandidates);
                    if (allCandidates == null)
                    {
                        allCandidates = new List<TargetElementCandidate>();
                        try
                        {
                            var transform = link.GetTotalTransform();
                            foreach (var element in new FilteredElementCollector(linkDoc)
                                .OfCategoryId(new ElementId(categoryId)).WhereElementIsNotElementType())
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                try
                                {
                                    var type = linkDoc.GetElement(element.GetTypeId());
                                    allCandidates.Add(new TargetElementCandidate
                                    {
                                        Element = element,
                                        Center = transform.OfPoint(GetElementCenter(element, null)),
                                        TypeName = type != null ? type.Name : string.Empty,
                                        TagReference = new Reference(element).CreateLinkReference(link),
                                        IsLinked = true
                                    });
                                }
                                catch { }
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                        if (index.BatchCache != null)
                            index.BatchCache.LinkedTagCandidates[cacheKey] = allCandidates;
                    }
                    foreach (var candidate in allCandidates)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if (targetPlan != null && Math.Abs(candidate.Center.Z - levelElevation) > verticalTolerance) continue;
                            if (!IsPointInsideViewCrop(targetView, candidate.Center)) continue;
                            tagList.Add(candidate);
                        }
                        catch { }
                    }
                }
            }
        }

        private static bool IsPointInsideViewCrop(Autodesk.Revit.DB.View view, XYZ point)
        {
            try
            {
                if (!view.CropBoxActive || view.CropBox == null) return true;
                var crop = view.CropBox;
                var local = crop.Transform.Inverse.OfPoint(point);
                return local.X >= crop.Min.X && local.X <= crop.Max.X &&
                    local.Y >= crop.Min.Y && local.Y <= crop.Max.Y;
            }
            catch { return true; }
        }

        private static Element FindCorrespondingTagHost(Document sourceDoc, Autodesk.Revit.DB.View sourceView,
            Element sourceHost, Document targetDoc, Autodesk.Revit.DB.View targetView, double levelShift,
            TargetViewElementIndex targetElementIndex)
        {
            if (sourceHost == null || sourceHost.Category == null) return null;
            XYZ sourceCenter = GetElementCenter(sourceHost, sourceView);
            XYZ expected = sourceCenter + new XYZ(0, 0, levelShift);
            string sourceTypeName = string.Empty;
            var sourceType = sourceDoc.GetElement(sourceHost.GetTypeId());
            if (sourceType != null) sourceTypeName = sourceType.Name;

            List<TargetElementCandidate> indexedCandidates;
            if (targetElementIndex == null || !targetElementIndex.ByCategory.TryGetValue(sourceHost.Category.Id.Value, out indexedCandidates))
                return null;
            TargetElementCandidate bestSameType = null;
            TargetElementCandidate bestAnyType = null;
            double bestSameDistance = double.MaxValue;
            double bestAnyDistance = double.MaxValue;
            foreach (var candidate in indexedCandidates)
            {
                double distance = candidate.Center.DistanceTo(expected);
                if (distance < bestAnyDistance)
                {
                    bestAnyDistance = distance;
                    bestAnyType = candidate;
                }
                if (string.Equals(candidate.TypeName, sourceTypeName, StringComparison.OrdinalIgnoreCase) &&
                    distance < bestSameDistance)
                {
                    bestSameDistance = distance;
                    bestSameType = candidate;
                }
            }

            // 同類型允許 2 m、同類別後備允許 300 mm；避免把標籤掛到遠處的不相關構件。
            if (bestSameType != null && bestSameDistance <= 2000.0 / 304.8) return bestSameType.Element;
            return bestAnyType != null && bestAnyDistance <= 300.0 / 304.8 ? bestAnyType.Element : null;
        }

        private static XYZ GetElementCenter(Element element, Autodesk.Revit.DB.View view)
        {
            if (element == null) return XYZ.Zero;
            try
            {
                var box = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
                if (box != null) return (box.Min + box.Max) * 0.5;
            }
            catch { }
            var locationPoint = element.Location as LocationPoint;
            if (locationPoint != null) return locationPoint.Point;
            var locationCurve = element.Location as LocationCurve;
            return locationCurve != null ? locationCurve.Curve.Evaluate(0.5, true) : XYZ.Zero;
        }

        private static ElementType ResolveAnnotationType(Document sourceDoc, ElementId sourceTypeId, Document targetDoc)
        {
            var sourceType = sourceDoc.GetElement(sourceTypeId) as ElementType;
            if (sourceType == null) return null;
            if (ReferenceEquals(sourceDoc, targetDoc)) return sourceType;
            string familyName = sourceType is FamilySymbol ? ((FamilySymbol)sourceType).FamilyName : string.Empty;
            var targetType = new FilteredElementCollector(targetDoc).WhereElementIsElementType().Cast<ElementType>()
                .FirstOrDefault(t => t.Category != null && sourceType.Category != null && t.Category.Id == sourceType.Category.Id &&
                    string.Equals(t.Name, sourceType.Name, StringComparison.OrdinalIgnoreCase) &&
                    (!(t is FamilySymbol) || string.Equals(((FamilySymbol)t).FamilyName, familyName, StringComparison.OrdinalIgnoreCase)));
            if (targetType != null) return targetType;
            try
            {
                var copied = ElementTransformUtils.CopyElements(sourceDoc, new List<ElementId> { sourceType.Id },
                    targetDoc, Transform.Identity, CreateCopyPasteOptions());
                return copied.Select(id => targetDoc.GetElement(id) as ElementType).FirstOrDefault(t => t != null);
            }
            catch { return null; }
        }

        private static void ResolveTagCollisions(Document doc, Autodesk.Revit.DB.View view, List<string> warnings)
        {
            doc.Regenerate();
            var tags = new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
            // Bounding boxes are read once and candidate moves are tested in
            // memory; no per-candidate Regenerate is performed. This keeps the
            // pass bounded without abandoning dense views.
            var occupied = new List<BoundingBoxXYZ>();
            double paperStep = Math.Max(1, view.Scale) * 4.0 / 304.8;
            foreach (var tag in tags)
            {
                var box = tag.get_BoundingBox(view);
                if (box == null) continue;
                if (occupied.All(o => !BoundingBoxesOverlap(box, o)))
                {
                    occupied.Add(box);
                    continue;
                }
                XYZ original = tag.TagHeadPosition;
                bool placed = false;
                for (int ring = 1; ring <= 8 && !placed; ring++)
                {
                    foreach (var direction in new[] { view.RightDirection, -view.RightDirection, view.UpDirection, -view.UpDirection,
                        (view.RightDirection + view.UpDirection).Normalize(), (view.RightDirection - view.UpDirection).Normalize() })
                    {
                        try
                        {
                            var displacement = direction * paperStep * ring;
                            var candidateBox = TranslateBoundingBox(box, displacement);
                            if (candidateBox == null || occupied.Any(o => BoundingBoxesOverlap(candidateBox, o))) continue;
                            tag.TagHeadPosition = original + displacement;
                            box = candidateBox;
                            placed = true;
                            break;
                        }
                        catch { }
                    }
                }
                if (!placed)
                {
                    tag.TagHeadPosition = original;
                    box = tag.get_BoundingBox(view);
                }
                if (box != null) occupied.Add(box);
            }
            // 避免每個候選位置都觸發昂貴的完整模型重算；全部標籤定位後只重算一次。
            doc.Regenerate();
        }

        private static BoundingBoxXYZ TranslateBoundingBox(BoundingBoxXYZ source, XYZ displacement)
        {
            if (source == null || displacement == null) return null;
            return new BoundingBoxXYZ
            {
                Min = source.Min + displacement,
                Max = source.Max + displacement,
                Transform = source.Transform
            };
        }

        private static bool BoundingBoxesOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            return !(a.Max.X <= b.Min.X || b.Max.X <= a.Min.X || a.Max.Y <= b.Min.Y || b.Max.Y <= a.Min.Y);
        }

        private static void CopyViewSpecificElementsResilient(Document sourceDoc, Autodesk.Revit.DB.View sourceView,
            Document targetDoc, Autodesk.Revit.DB.View targetView)
        {
            var elements = new FilteredElementCollector(sourceDoc, sourceView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.ViewSpecific && !(e is IndependentTag) && !(e is Dimension) && IsSafeDrawingElement(e))
                .ToList();
            if (elements.Count == 0) return;

            if (TryCopyViewElements(targetDoc, sourceView, targetView, elements.Select(e => e.Id).ToList())) return;

            // 尺寸常因模型參考無法跨樓層重建；按類別拆批，避免它使文字、標註與詳圖元件一起失敗。
            foreach (var group in elements.GroupBy(e => e.Category != null ? e.Category.Id.Value : long.MinValue))
            {
                var ids = group.Select(e => e.Id).ToList();
                CopyViewElementsByBisection(targetDoc, sourceView, targetView, ids);
            }
        }

        private static void CopySheetSpecificElementsResilient(Document sourceDoc, ViewSheet sourceSheet,
            Document targetDoc, ViewSheet targetSheet)
        {
            var elements = new FilteredElementCollector(sourceDoc, sourceSheet.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.ViewSpecific && !(e is Viewport) && !(e is ScheduleSheetInstance) &&
                    (e.Category == null || e.Category.Id.Value != (long)BuiltInCategory.OST_TitleBlocks) &&
                    IsSafeDrawingElement(e))
                .Select(e => e.Id).ToList();
            if (elements.Count == 0) return;
            CopyViewElementsByBisection(targetDoc, sourceSheet, targetSheet, elements);
        }

        /// <summary>
        /// Only permit sheet/view-owned drafting content to cross documents.
        /// Model instances and their types must always come from target project B;
        /// copying them is what produced unwanted column/elevator type suffixes.
        /// </summary>
        private static bool IsSafeDrawingElement(Element element)
        {
            if (element == null || !element.ViewSpecific) return false;
            if (element is TextNote || element is DetailCurve || element is FilledRegion ||
                element is RevisionCloud || element is IndependentTag || element is Dimension)
                return true;
            try
            {
                return element.Category != null && element.Category.CategoryType == CategoryType.Annotation;
            }
            catch { return false; }
        }

        private static void CopyViewElementsByBisection(Document targetDoc, Autodesk.Revit.DB.View sourceView,
            Autodesk.Revit.DB.View targetView, IList<ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return;
            if (TryCopyViewElements(targetDoc, sourceView, targetView, ids.ToList())) return;
            if (ids.Count == 1) return;

            int middle = ids.Count / 2;
            CopyViewElementsByBisection(targetDoc, sourceView, targetView, ids.Take(middle).ToList());
            CopyViewElementsByBisection(targetDoc, sourceView, targetView, ids.Skip(middle).ToList());
        }

        private static bool TryCopyViewElements(Document targetDoc, Autodesk.Revit.DB.View sourceView,
            Autodesk.Revit.DB.View targetView, ICollection<ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return true;
            using (var sub = new SubTransaction(targetDoc))
            {
                try
                {
                    sub.Start();
                    ElementTransformUtils.CopyElements(sourceView, ids, targetView, Transform.Identity, CreateCopyPasteOptions());
                    sub.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    try { if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack(); } catch { }
                    System.Diagnostics.Debug.WriteLine("[Copy annotation Warning] " + ex.Message);
                    return false;
                }
            }
        }

        private static string GetUniqueViewName(Document doc, string requestedName)
        {
            var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(requestedName)) return requestedName;
            int index = 2;
            while (names.Contains(string.Format("{0} ({1})", requestedName, index))) index++;
            return string.Format("{0} ({1})", requestedName, index);
        }

        public async Task<bool> SaveDrawingRuleAsync(DrawingRuleDto rule)
        {
            return await _fallbackMock.SaveDrawingRuleAsync(rule);
        }

        public async Task<List<ResourceMappingDto>> GetResourceMappingsAsync(ResourceMappingType type)
        {
            return await _fallbackMock.GetResourceMappingsAsync(type);
        }

        public async Task<bool> SaveResourceMappingsAsync(List<ResourceMappingDto> mappings)
        {
            return await _fallbackMock.SaveResourceMappingsAsync(mappings);
        }

        public async Task<List<ResourceMappingDto>> RunAutoMappingAsync(ResourceMappingType type)
        {
            return await _fallbackMock.RunAutoMappingAsync(type);
        }

        public async Task<bool> OpenSheetInRevitAsync(string sheetNumber)
        {
            if (_handler == null) return await _fallbackMock.OpenSheetInRevitAsync(sheetNumber);

            try
            {
                return await _handler.RunAsync(app =>
                {
                    var uiDoc = app.ActiveUIDocument;
                    var doc = uiDoc != null ? uiDoc.Document : null;
                    if (uiDoc == null || doc == null) return false;

                    var sheet = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .FirstOrDefault(s => s.SheetNumber == sheetNumber || s.Name.Contains(sheetNumber));

                    if (sheet != null)
                    {
                        uiDoc.ActiveView = sheet;
                        return true;
                    }

                    return false;
                });
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> OpenViewInRevitAsync(string viewName)
        {
            if (_handler == null) return await _fallbackMock.OpenViewInRevitAsync(viewName);

            try
            {
                return await _handler.RunAsync(app =>
                {
                    var uiDoc = app.ActiveUIDocument;
                    var doc = uiDoc != null ? uiDoc.Document : null;
                    if (uiDoc == null || doc == null) return false;

                    var view = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.View))
                        .Cast<Autodesk.Revit.DB.View>()
                        .FirstOrDefault(v => !v.IsTemplate && v.Name == viewName);

                    if (view != null)
                    {
                        uiDoc.ActiveView = view;
                        return true;
                    }

                    return false;
                });
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractFloorShortName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "1F";
            string upper = fullName.ToUpper().Trim();

            // 檢查地下層 (B1~B10)
            if (upper.Contains("B") || upper.Contains("地下"))
            {
                var d = new string(upper.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(d))
                {
                    return string.Format("B{0}F", d);
                }
                return "B1F";
            }

            // 檢查屋頂層 (RF, R1F, R2F)
            if (upper.Contains("RF") || upper.Contains("屋頂") || upper.Contains("ROOF"))
            {
                var d = new string(upper.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(d))
                {
                    return string.Format("R{0}F", d);
                }
                return "RF";
            }

            // 檢查屋突層
            if (upper.Contains("屋突") || upper.Contains("突出"))
            {
                var d = new string(upper.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(d))
                {
                    return string.Format("屋突{0}F", d);
                }
                return "屋突1F";
            }

            // 地上一般層 (1F, 2F, 3F...)
            var digits = new string(upper.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digits))
            {
                return string.Format("{0}F", digits);
            }

            return fullName;
        }

        private static string GetUniqueSheetNumber(Document doc, string requestedNumber)
        {
            var existingNumbers = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Select(s => s.SheetNumber));

            if (!existingNumbers.Contains(requestedNumber)) return requestedNumber;

            int count = 1;
            while (existingNumbers.Contains(string.Format("{0}_{1}", requestedNumber, count)))
            {
                count++;
            }
            return string.Format("{0}_{1}", requestedNumber, count);
        }
    }
}
