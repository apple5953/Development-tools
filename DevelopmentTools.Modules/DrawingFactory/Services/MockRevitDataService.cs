using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIMDrawingFactory.UI.Models;

namespace BIMDrawingFactory.UI.Services
{
    public class MockRevitDataService : IRevitDataService
    {
        public void InvalidateAnalysisCache() { }
        private readonly ProjectProfileDto _profile = new ProjectProfileDto();
        private readonly List<BuildingCardDto> _buildings = new List<BuildingCardDto>();
        private readonly List<DrawingRuleDto> _rules = new List<DrawingRuleDto>();

        public MockRevitDataService()
        {
            InitializeMockData();
        }

        private void InitializeMockData()
        {
            _profile.ProjectName = "新北市多功能綜合大樓新建工程.rvt";
            _profile.SourceDocumentName = "C:\\Projects\\NewTaipei_Building_2024.rvt";
            _profile.StandardDrawingGroup = "1FL 標準基準圖組";
            _profile.StatusSummary = "專案設定正常 (Revit 2024 Connected)";
            _profile.IsSetupComplete = true;
            _profile.TotalRulesCount = 38;
            _profile.BuildingCount = 2;
            _profile.LastAnalysisTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm");

            var mainBuildingFloors = new List<FloorChipDto>
            {
                new FloorChipDto { FloorId = "B4", FloorName = "B4", Status = FloorStatus.Completed, StatusText = "已完成 (13張)", CompletedSheetsCount = 13, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "B3", FloorName = "B3", Status = FloorStatus.Completed, StatusText = "已完成 (13張)", CompletedSheetsCount = 13, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "B2", FloorName = "B2", Status = FloorStatus.Completed, StatusText = "已完成 (13張)", CompletedSheetsCount = 13, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "B1", FloorName = "B1", Status = FloorStatus.Completed, StatusText = "已完成 (13張)", CompletedSheetsCount = 13, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "1F", FloorName = "1F", Status = FloorStatus.Completed, StatusText = "標準圖說 (13張)", IsStandardMaster = true, CompletedSheetsCount = 13, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "2F", FloorName = "2F", Status = FloorStatus.Completed, StatusText = "已完成 (13張)", CompletedSheetsCount = 13, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "3F", FloorName = "3F", Status = FloorStatus.Completed, StatusText = "已完成 (13張)", CompletedSheetsCount = 13, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "4F", FloorName = "4F", Status = FloorStatus.Completed, StatusText = "已完成 (13張)", CompletedSheetsCount = 13, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "5F", FloorName = "5F", Status = FloorStatus.Completed, StatusText = "已完成 (13張)", CompletedSheetsCount = 13, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "6F", FloorName = "6F", Status = FloorStatus.Completed, StatusText = "已完成 (13張)", CompletedSheetsCount = 13, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "7F", FloorName = "7F", Status = FloorStatus.NotCreated, StatusText = "尚未建立", IsSelected = true, CompletedSheetsCount = 0, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "8F", FloorName = "8F", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "9F", FloorName = "9F", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "10F", FloorName = "10F", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "11F", FloorName = "11F", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "12F", FloorName = "12F", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 13 },
                new FloorChipDto { FloorId = "RF", FloorName = "RF", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 8 }
            };

            var buildingBFloors = new List<FloorChipDto>
            {
                new FloorChipDto { FloorId = "B2", FloorName = "B2", Status = FloorStatus.Completed, StatusText = "已完成 (11張)", CompletedSheetsCount = 11, TotalSheetsCount = 11 },
                new FloorChipDto { FloorId = "B1", FloorName = "B1", Status = FloorStatus.Completed, StatusText = "已完成 (11張)", CompletedSheetsCount = 11, TotalSheetsCount = 11 },
                new FloorChipDto { FloorId = "1F", FloorName = "1F", Status = FloorStatus.Completed, StatusText = "標準圖說 (11張)", IsStandardMaster = true, CompletedSheetsCount = 11, TotalSheetsCount = 11 },
                new FloorChipDto { FloorId = "2F", FloorName = "2F", Status = FloorStatus.Completed, StatusText = "已完成 (11張)", CompletedSheetsCount = 11, TotalSheetsCount = 11 },
                new FloorChipDto { FloorId = "3F", FloorName = "3F", Status = FloorStatus.Completed, StatusText = "已完成 (11張)", CompletedSheetsCount = 11, TotalSheetsCount = 11 },
                new FloorChipDto { FloorId = "4F", FloorName = "4F", Status = FloorStatus.Completed, StatusText = "已完成 (11張)", CompletedSheetsCount = 11, TotalSheetsCount = 11 },
                new FloorChipDto { FloorId = "5F", FloorName = "5F", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 11 },
                new FloorChipDto { FloorId = "6F", FloorName = "6F", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 11 },
                new FloorChipDto { FloorId = "7F", FloorName = "7F", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 11 },
                new FloorChipDto { FloorId = "8F", FloorName = "8F", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 11 },
                new FloorChipDto { FloorId = "RF", FloorName = "RF", Status = FloorStatus.NotCreated, StatusText = "尚未建立", CompletedSheetsCount = 0, TotalSheetsCount = 6 }
            };

            _buildings.Add(new BuildingCardDto
            {
                Id = "B_MAIN",
                Name = "主建築 / 全區",
                CompletedFloors = 9,
                TotalFloors = 17,
                IsSelected = true,
                Floors = mainBuildingFloors
            });

            _buildings.Add(new BuildingCardDto
            {
                Id = "B_SIDE",
                Name = "副棟 / B棟",
                CompletedFloors = 5,
                TotalFloors = 11,
                IsSelected = false,
                Floors = buildingBFloors
            });

            _rules.Add(new DrawingRuleDto
            {
                RuleId = "R_SB1_01",
                RuleName = "施工目錄表",
                BuildingName = "全棟通用",
                TargetFloorScope = "所有樓層",
                SheetNameFormula = "{棟別}{樓層}施工目錄表",
                SheetNumberFormula = "SB1-{樓層代碼}-01",
                LevelBinding = LevelBindingOption.CurrentLevel,
                ViewTemplateName = "A-目錄表樣板",
                Scale = "無比例",
                ScopeBoxRule = "-",
                SourceViewName = "1FL 施工目錄表",
                SourceSheetNumber = "SB1-01",
                SourceSheetName = "1FL 施工目錄表",
                TitleBlockName = "A1 標準圖框 (自動偵測)",
                ViewportPlacementCoord = "X: 420.0 mm, Y: 297.0 mm (對齊圖框中心)",
                AssociatedSchedules = "施工說明圖表, 圖面目錄清單",
                AssociatedLegends = "-",
                ActiveUsageCount = 13
            });

            _rules.Add(new DrawingRuleDto
            {
                RuleId = "R_SB1_02",
                RuleName = "建築平面圖",
                BuildingName = "全棟通用",
                TargetFloorScope = "所有樓層",
                SheetNameFormula = "{棟別}{樓層}建築平面圖",
                SheetNumberFormula = "SB1-{樓層代碼}-02",
                LevelBinding = LevelBindingOption.CurrentLevel,
                ViewTemplateName = "A-建築平面樣板",
                Scale = "1:100",
                ScopeBoxRule = "依棟別自動套用範圍框",
                SourceViewName = "1FL 建築平面圖",
                SourceSheetNumber = "SB1-02",
                SourceSheetName = "1FL 建築平面圖",
                TitleBlockName = "A1 標準圖框 (自動偵測)",
                ViewportPlacementCoord = "X: 420.0 mm, Y: 297.0 mm (對齊圖框中心)",
                AssociatedSchedules = "門窗編號說明表",
                AssociatedLegends = "門窗標記圖例",
                ActiveUsageCount = 13
            });

            _rules.Add(new DrawingRuleDto
            {
                RuleId = "R_SB1_05",
                RuleName = "建築放樣圖",
                BuildingName = "全棟通用",
                TargetFloorScope = "所有樓層",
                SheetNameFormula = "{棟別}{樓層}放樣圖",
                SheetNumberFormula = "SB1-{樓層代碼}-05",
                LevelBinding = LevelBindingOption.CurrentLevel,
                ViewTemplateName = "A-建築放樣圖",
                Scale = "1:100",
                ScopeBoxRule = "依棟別與樓層自動比對範圍框",
                SourceViewName = "1FL 放樣圖",
                SourceSheetNumber = "SB1-05",
                SourceSheetName = "1FL 建築放樣圖",
                TitleBlockName = "A1 標準圖框 (自動偵測)",
                ViewportPlacementCoord = "X: 418.5 mm, Y: 295.0 mm (對齊圖框中心)",
                AssociatedSchedules = "放樣基準座標表",
                AssociatedLegends = "放樣大樣圖例",
                ActiveUsageCount = 13
            });

            _rules.Add(new DrawingRuleDto
            {
                RuleId = "R_SB1_06",
                RuleName = "結構柱牆圖",
                BuildingName = "全棟通用",
                TargetFloorScope = "所有樓層",
                SheetNameFormula = "{棟別}{樓層}柱牆圖",
                SheetNumberFormula = "SB1-{樓層代碼}-06",
                LevelBinding = LevelBindingOption.CurrentLevel,
                ViewTemplateName = "A-結構柱牆樣板",
                Scale = "1:100",
                ScopeBoxRule = "依棟別與樓層自動比對範圍框",
                SourceViewName = "1FL 柱牆圖",
                SourceSheetNumber = "SB1-06",
                SourceSheetName = "1FL 結構柱牆圖",
                TitleBlockName = "A1 標準圖框 (自動偵測)",
                ViewportPlacementCoord = "X: 420.0 mm, Y: 297.0 mm (對齊圖框中心)",
                AssociatedSchedules = "柱配筋明細表",
                AssociatedLegends = "柱牆標示圖例",
                ActiveUsageCount = 13
            });

            _rules.Add(new DrawingRuleDto
            {
                RuleId = "R_SB1_07",
                RuleName = "結構見下層圖",
                BuildingName = "全棟通用",
                TargetFloorScope = "所有樓層",
                SheetNameFormula = "{棟別}{樓層}見下層圖",
                SheetNumberFormula = "SB1-{樓層代碼}-07",
                LevelBinding = LevelBindingOption.CurrentLevel,
                ViewTemplateName = "A-結構見下層樣板",
                Scale = "1:100",
                ScopeBoxRule = "依棟別與樓層自動比對範圍框",
                SourceViewName = "1FL 見下層圖",
                SourceSheetNumber = "SB1-07",
                SourceSheetName = "1FL 見下層圖",
                TitleBlockName = "A1 標準圖框 (自動偵測)",
                ViewportPlacementCoord = "X: 420.0 mm, Y: 297.0 mm (對齊圖框中心)",
                AssociatedSchedules = "梁配筋明細表",
                AssociatedLegends = "下層結構符號圖例",
                ActiveUsageCount = 13
            });

            _rules.Add(new DrawingRuleDto
            {
                RuleId = "R_SB1_08",
                RuleName = "結構見上層圖",
                BuildingName = "全棟通用",
                TargetFloorScope = "所有樓層",
                SheetNameFormula = "{棟別}{樓層}見上層圖",
                SheetNumberFormula = "SB1-{樓層代碼}-08",
                LevelBinding = LevelBindingOption.PreviousLevel,
                ViewTemplateName = "A-結構見上層樣板",
                Scale = "1:100",
                ScopeBoxRule = "依棟別與樓層自動比對範圍框",
                SourceViewName = "1FL 見上層圖",
                SourceSheetNumber = "SB1-08",
                SourceSheetName = "1FL 見上層圖",
                TitleBlockName = "A1 標準圖框 (自動偵測)",
                ViewportPlacementCoord = "X: 420.0 mm, Y: 297.0 mm (對齊圖框中心)",
                AssociatedSchedules = "板厚配筋表",
                AssociatedLegends = "上層結構符號圖例",
                ActiveUsageCount = 13
            });

            _rules.Add(new DrawingRuleDto
            {
                RuleId = "R_SB1_09",
                RuleName = "結構開口圖",
                BuildingName = "全棟通用",
                TargetFloorScope = "所有樓層",
                SheetNameFormula = "{棟別}{樓層}結構開口圖",
                SheetNumberFormula = "SB1-{樓層代碼}-09",
                LevelBinding = LevelBindingOption.CurrentLevel,
                ViewTemplateName = "A-結構開口樣板",
                Scale = "1:100",
                ScopeBoxRule = "依棟別與樓層自動比對範圍框",
                SourceViewName = "1FL 結構開口圖",
                SourceSheetNumber = "SB1-09",
                SourceSheetName = "1FL 結構開口圖",
                TitleBlockName = "A1 標準圖框 (自動偵測)",
                ViewportPlacementCoord = "X: 420.0 mm, Y: 297.0 mm (對齊圖框中心)",
                AssociatedSchedules = "開口補強配筋表",
                AssociatedLegends = "套管補強圖例",
                ActiveUsageCount = 13
            });

            _rules.Add(new DrawingRuleDto
            {
                RuleId = "R_SB1_10",
                RuleName = "裝修高程圖",
                BuildingName = "全棟通用",
                TargetFloorScope = "所有樓層",
                SheetNameFormula = "{棟別}{樓層}裝修高程圖",
                SheetNumberFormula = "SB1-{樓層代碼}-10",
                LevelBinding = LevelBindingOption.CurrentLevel,
                ViewTemplateName = "A-裝修高程樣板",
                Scale = "1:100",
                ScopeBoxRule = "依棟別與樓層自動比對範圍框",
                SourceViewName = "1FL 裝修高程圖",
                SourceSheetNumber = "SB1-10",
                SourceSheetName = "1FL 裝修高程圖",
                TitleBlockName = "A1 標準圖框 (自動偵測)",
                ViewportPlacementCoord = "X: 420.0 mm, Y: 297.0 mm (對齊圖框中心)",
                AssociatedSchedules = "室內裝修材料表",
                AssociatedLegends = "高程標高圖例",
                ActiveUsageCount = 13
            });
        }

        public Task<ProjectProfileDto> GetProjectProfileAsync()
        {
            return Task.FromResult(_profile);
        }

        public Task<List<string>> GetOpenProjectNamesAsync()
        {
            return Task.FromResult(new List<string>
            {
                "範例專案_地下層",
                "範例專案_A棟",
                "範例專案_B棟"
            });
        }

        public Task<List<string>> GetOpenSourceProjectNamesAsync()
        {
            return GetOpenProjectNamesAsync();
        }

        public Task<List<BuildingCardDto>> GetBuildingsAsync(string projectName = null)
        {
            return Task.FromResult(_buildings);
        }

        public Task<List<CustomDrawingPackageItemDto>> GetAvailablePackageRulesAsync(string buildingId)
        {
            var list = new List<CustomDrawingPackageItemDto>
            {
                new CustomDrawingPackageItemDto { RuleId = "R1", Name = "放樣圖", IsChecked = true, DefaultViewTemplate = "A-放樣圖" },
                new CustomDrawingPackageItemDto { RuleId = "R2", Name = "建築平面圖", IsChecked = true, DefaultViewTemplate = "A-平面圖" },
                new CustomDrawingPackageItemDto { RuleId = "R3", Name = "柱牆圖", IsChecked = true, DefaultViewTemplate = "A-柱牆圖" },
                new CustomDrawingPackageItemDto { RuleId = "R4", Name = "RC柱牆圖", IsChecked = true, DefaultViewTemplate = "A-RC柱牆" },
                new CustomDrawingPackageItemDto { RuleId = "R5", Name = "結構開口圖", IsChecked = true, DefaultViewTemplate = "A-結構開口" },
                new CustomDrawingPackageItemDto { RuleId = "R6", Name = "見上層圖", IsChecked = true, DefaultViewTemplate = "A-見上層" },
                new CustomDrawingPackageItemDto { RuleId = "R7", Name = "見下層圖", IsChecked = true, DefaultViewTemplate = "A-見下層" },
                new CustomDrawingPackageItemDto { RuleId = "R8", Name = "裝修高程圖", IsChecked = true, DefaultViewTemplate = "A-裝修圖" },
                new CustomDrawingPackageItemDto { RuleId = "R9", Name = "樓梯圖", IsChecked = true, DefaultViewTemplate = "A-樓梯圖" },
                new CustomDrawingPackageItemDto { RuleId = "R10", Name = "天花板平面圖", IsChecked = false, DefaultViewTemplate = "A-天花板" },
                new CustomDrawingPackageItemDto { RuleId = "R11", Name = "地坪材質圖", IsChecked = false, DefaultViewTemplate = "A-地坪圖" }
            };
            return Task.FromResult(list);
        }

        public Task<List<IssueItemDto>> RunPreflightCheckAsync(GenerateRequestDto request)
        {
            var issues = new List<IssueItemDto>();

            if (request.SelectedFloorNames.Contains("4F") || request.SelectedFloorNames.Contains("7F"))
            {
                var floor = request.SelectedFloorNames.FirstOrDefault() ?? "7F";
                issues.Add(new IssueItemDto
                {
                    IssueId = "ISSUE-01",
                    Severity = IssueSeverity.Warning,
                    FloorName = floor,
                    SheetOrViewName = "A棟7FL結構開口圖",
                    UserMessage = "找不到完全吻合的視圖範圍框",
                    SuggestedAction = "系統已找到高度相似的範圍框『A棟7FL建築』(相似度 92%)，可直接採用。",
                    StandardRequirement = "A棟1FL建築",
                    SystemRecommendation = "A棟7FL建築",
                    TechnicalDetails = "ScopeBoxResolver: Exact match for 'A棟7FL結構開口' not found. Fuzzy score 0.92 matched.",
                    ResolutionOptions = new List<string>
                    {
                        "自動採用系統建議：A棟7FL建築",
                        "手動指定其他現有範圍框",
                        "暫時跳過此張圖紙 (本批次不建立)"
                    },
                    SelectedResolution = "自動採用系統建議：A棟7FL建築"
                });
            }

            return Task.FromResult(issues);
        }

        public Task<List<SheetPreviewItemDto>> GetPreviewItemsAsync(GenerateRequestDto request)
        {
            var list = new List<SheetPreviewItemDto>();
            var buildingName = string.IsNullOrEmpty(request.BuildingName) ? "A棟" : request.BuildingName;
            var floors = request.SelectedFloorNames.Count > 0 ? request.SelectedFloorNames : new List<string> { "2F" };

            var mappingDict = (request.FloorMappings != null && request.FloorMappings.Count > 0)
                ? request.FloorMappings.ToDictionary(m => m.FloorName, m => m.TargetSeriesCode, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>();

            string basePrefix = "S";
            int defaultStartNum = 6;
            if (!string.IsNullOrEmpty(request.TargetSheetSeries))
            {
                var match = System.Text.RegularExpressions.Regex.Match(request.TargetSheetSeries, @"^(.*?)(SB)?(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string pre = match.Groups[1].Value;
                    string sb = match.Groups[2].Value;
                    int num;
                    if (int.TryParse(match.Groups[3].Value, out num))
                    {
                        defaultStartNum = num;
                        if (!string.IsNullOrEmpty(pre) || !string.IsNullOrEmpty(sb))
                        {
                            basePrefix = string.Format("{0}{1}", pre, sb);
                            basePrefix = FactoryConventions.Current.ApplySeriesPrefix(basePrefix);
                        }
                    }
                }
            }

            int fIdx = 0;
            foreach (var floor in floors)
            {
                string seriesCode;
                if (!mappingDict.TryGetValue(floor, out seriesCode))
                {
                    seriesCode = string.Format("{0}{1}", basePrefix, defaultStartNum + fIdx);
                }

                var floorNum = floor.Replace("F", "").Replace("FL", "");
                int fn;
                int.TryParse(floorNum, out fn);
                var curFloorName = string.Format("{0}FL", fn > 0 ? fn.ToString() : floor);
                var prevFloorName = fn > 1 ? string.Format("{0}FL", fn - 1) : "1FL";
                var nextFloorName = string.Format("{0}FL", fn + 1);

                list.Add(new SheetPreviewItemDto
                {
                    IsSelected = true,
                    BuildingName = buildingName,
                    SheetFloorName = floor,
                    SheetNumber = string.Format("{0}-01", seriesCode),
                    SheetName = string.Format("{0}{1}施工目錄表", buildingName, curFloorName),
                    ViewName = string.Format("{0}{1}施工目錄表", buildingName, curFloorName),
                    ResolvedViewLevelName = string.Format("{0} (本樓層)", curFloorName),
                    ViewTemplateName = "A-目錄表樣板",
                    ScopeBoxName = "-",
                    Status = UiItemStatus.Success,
                    StatusMessage = "可以建立",
                    ActionDescription = "放置既有明細表與說明清單",
                    SourceStandardSheet = "S05-01 (A棟1FL施工目錄表)",
                    LevelRuleDescription = "本樓層"
                });

                list.Add(new SheetPreviewItemDto
                {
                    IsSelected = true,
                    BuildingName = buildingName,
                    SheetFloorName = floor,
                    SheetNumber = string.Format("{0}-05", seriesCode),
                    SheetName = string.Format("{0}{1}放樣圖", buildingName, curFloorName),
                    ViewName = string.Format("{0}{1}放樣圖", buildingName, curFloorName),
                    ResolvedViewLevelName = string.Format("{0} (本樓層)", curFloorName),
                    ViewTemplateName = "A-建築放樣圖",
                    ScopeBoxName = string.Format("{0}{1}建築", buildingName, curFloorName),
                    Status = UiItemStatus.Success,
                    StatusMessage = "可以建立",
                    ActionDescription = "建立新平面視圖並套用放樣樣板",
                    SourceStandardSheet = "S05-05 (A棟1FL放樣圖)",
                    LevelRuleDescription = "本樓層"
                });

                list.Add(new SheetPreviewItemDto
                {
                    IsSelected = true,
                    BuildingName = buildingName,
                    SheetFloorName = floor,
                    SheetNumber = string.Format("{0}-06", seriesCode),
                    SheetName = string.Format("{0}{1}柱牆圖", buildingName, curFloorName),
                    ViewName = string.Format("{0}{1}柱牆圖", buildingName, curFloorName),
                    ResolvedViewLevelName = string.Format("{0} (本樓層)", curFloorName),
                    ViewTemplateName = "A-結構柱牆圖",
                    ScopeBoxName = string.Format("{0}{1}結構", buildingName, curFloorName),
                    Status = UiItemStatus.Success,
                    StatusMessage = "可以建立",
                    ActionDescription = "建立新平面視圖並套用柱牆樣板",
                    SourceStandardSheet = "S05-06 (A棟1FL柱牆圖)",
                    LevelRuleDescription = "本樓層"
                });

                list.Add(new SheetPreviewItemDto
                {
                    IsSelected = true,
                    BuildingName = buildingName,
                    SheetFloorName = floor,
                    SheetNumber = string.Format("{0}-07", seriesCode),
                    SheetName = string.Format("{0}{1}見下層圖", buildingName, curFloorName),
                    ViewName = string.Format("{0}{1}見下層圖", buildingName, curFloorName),
                    ResolvedViewLevelName = string.Format("{0} (本樓層)", curFloorName),
                    ViewTemplateName = "A-結構見下層樣板",
                    ScopeBoxName = string.Format("{0}{1}結構", buildingName, curFloorName),
                    Status = UiItemStatus.Success,
                    StatusMessage = "可以建立",
                    ActionDescription = "由當層向下看，依實際層高重建切割範圍",
                    SourceStandardSheet = "S05-07 (A棟1FL見下層圖)",
                    LevelRuleDescription = "本樓層 (Current Level)"
                });

                list.Add(new SheetPreviewItemDto
                {
                    IsSelected = true,
                    BuildingName = buildingName,
                    SheetFloorName = floor,
                    SheetNumber = string.Format("{0}-08", seriesCode),
                    SheetName = string.Format("{0}{1}見上層圖", buildingName, curFloorName),
                    ViewName = string.Format("{0}{1}見上層圖", buildingName, curFloorName),
                    ResolvedViewLevelName = string.Format("{0} (樓下一層)", prevFloorName),
                    ViewTemplateName = "A-結構見上層樣板",
                    ScopeBoxName = string.Format("{0}{1}結構", buildingName, curFloorName),
                    Status = UiItemStatus.Success,
                    StatusMessage = "可以建立",
                    ActionDescription = "由樓下一層往上看，依實際層高重建仰視範圍",
                    SourceStandardSheet = "S05-08 (A棟1FL見上層圖)",
                    LevelRuleDescription = "樓下一層 (Previous Level)"
                });

                list.Add(new SheetPreviewItemDto
                {
                    IsSelected = true,
                    BuildingName = buildingName,
                    SheetFloorName = floor,
                    SheetNumber = string.Format("{0}-09", seriesCode),
                    SheetName = string.Format("{0}{1}結構開口圖", buildingName, curFloorName),
                    ViewName = string.Format("{0}{1}結構開口圖", buildingName, curFloorName),
                    ResolvedViewLevelName = string.Format("{0} (本樓層)", curFloorName),
                    ViewTemplateName = "A-結構開口樣板",
                    ScopeBoxName = string.Format("{0}{1}建築 (已採用建議)", buildingName, curFloorName),
                    Status = UiItemStatus.Warning,
                    StatusMessage = "已採用建議範圍框",
                    IssueCount = 1,
                    ActionDescription = "建立新平面視圖並綁定建議範圍框",
                    SourceStandardSheet = "S05-09 (A棟1FL結構開口圖)",
                    LevelRuleDescription = "本樓層"
                });

                list.Add(new SheetPreviewItemDto
                {
                    IsSelected = true,
                    BuildingName = buildingName,
                    SheetFloorName = floor,
                    SheetNumber = string.Format("{0}-10", seriesCode),
                    SheetName = string.Format("{0}{1}裝修高程圖", buildingName, curFloorName),
                    ViewName = string.Format("{0}{1}裝修高程圖", buildingName, curFloorName),
                    ResolvedViewLevelName = string.Format("{0} (本樓層)", curFloorName),
                    ViewTemplateName = "A-裝修高程樣板",
                    ScopeBoxName = string.Format("{0}{1}建築", buildingName, curFloorName),
                    Status = UiItemStatus.Success,
                    StatusMessage = "可以建立",
                    ActionDescription = "建立新平面視圖並放置高程標籤",
                    SourceStandardSheet = "S05-10 (A棟1FL裝修高程圖)",
                    LevelRuleDescription = "本樓層"
                });

                fIdx++;
            }

            return Task.FromResult(list);
        }

        public async Task<GenerationResultDto> ExecuteGenerationAsync(GenerateRequestDto request, IProgress<GenerationProgressDto> progress, CancellationToken ct)
        {
            var previews = await GetPreviewItemsAsync(request);
            var total = previews.Count;

            for (int i = 0; i < total; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var item = previews[i];
                if (progress != null)
                {
                    progress.Report(new GenerationProgressDto
                    {
                        CurrentIndex = i + 1,
                        TotalCount = total,
                        CurrentItemNumber = item.SheetNumber,
                        CurrentItemName = item.SheetName,
                        CurrentStepDescription = string.Format("[4/5] 正在放置視圖與明細表至圖紙 ({0}/{1})...", i + 1, total)
                    });
                }

                await Task.Delay(180, ct);
            }

            var summaries = new List<string>();
            foreach (var p in previews)
            {
                summaries.Add(string.Format("{0}  {1}", p.SheetNumber, p.SheetName));
            }

            return new GenerationResultDto
            {
                IsSuccess = true,
                TargetBuilding = request.BuildingName,
                TargetFloorsSummary = string.Join(", ", request.SelectedFloorNames),
                CreatedSheetsCount = total,
                CreatedViewsCount = (int)(total * 1.4),
                CreatedSchedulesCount = 4,
                CreatedLegendsCount = 2,
                SkippedCount = 0,
                ManualReviewCount = 1,
                FailedCount = 0,
                ElapsedTime = "00:18",
                FirstCreatedSheetNumber = previews.FirstOrDefault() != null ? previews.FirstOrDefault().SheetNumber : "S11-01",
                GeneratedSheetSummaries = summaries
            };
        }

        public Task<List<DrawingRuleDto>> GetDrawingRulesAsync()
        {
            return Task.FromResult(_rules);
        }

        public Task<bool> SaveDrawingRuleAsync(DrawingRuleDto rule)
        {
            var idx = _rules.FindIndex(r => r.RuleId == rule.RuleId);
            if (idx >= 0)
            {
                _rules[idx] = rule;
            }
            return Task.FromResult(true);
        }

        public Task<List<SheetSeriesDto>> GetAvailableSheetSeriesAsync(string projectName = null)
        {
            var result = new List<SheetSeriesDto>();
            for (int i = 1; i <= 5; i++)
            {
                var sId = string.Format("SB{0}", i);
                var prefix = string.Format("S{0}", i);
                var seriesSheets = new List<DrawingRuleDto>();
                foreach (var r in _rules)
                {
                    seriesSheets.Add(new DrawingRuleDto
                    {
                        RuleId = string.Format("R_SB{0}_{1}", i, r.SourceSheetNumber),
                        RuleName = r.RuleName,
                        BuildingName = r.BuildingName,
                        TargetFloorScope = r.TargetFloorScope,
                        SheetNameFormula = r.SheetNameFormula,
                        SheetNumberFormula = r.SheetNumberFormula.Replace("SB1", sId),
                        LevelBinding = r.LevelBinding,
                        ViewTemplateName = r.ViewTemplateName,
                        Scale = r.Scale,
                        ScopeBoxRule = r.ScopeBoxRule,
                        SourceViewName = r.SourceViewName.Replace("1FL", string.Format("{0}FL", i)),
                        SourceSheetNumber = r.SourceSheetNumber.Replace("SB1", prefix),
                        SourceSheetName = r.SourceSheetName.Replace("1FL", string.Format("{0}FL", i)),
                        TitleBlockName = r.TitleBlockName,
                        ViewportPlacementCoord = r.ViewportPlacementCoord,
                        AssociatedSchedules = r.AssociatedSchedules,
                        AssociatedLegends = r.AssociatedLegends,
                        ActiveUsageCount = r.ActiveUsageCount,
                        IsChecked = true
                    });
                }

                result.Add(new SheetSeriesDto
                {
                    SeriesId = sId,
                    DisplayName = string.Format("{0} 系列標準圖說 ({1}，共 {2} 張圖紙)", sId, prefix, seriesSheets.Count),
                    SheetCount = seriesSheets.Count,
                    Sheets = seriesSheets
                });
            }
            return Task.FromResult(result);
        }

        public Task<List<ResourceMappingDto>> GetResourceMappingsAsync(ResourceMappingType type)
        {
            var list = new List<ResourceMappingDto>();
            if (type == ResourceMappingType.Level)
            {
                list.Add(new ResourceMappingDto { MappingId = "M1", Type = type, SystemName = "A棟 2F", RevitActualName = "A棟 2FL", ConfidenceScore = 1.0, Status = UiItemStatus.Success, StatusText = "已精確對應", AvailableRevitItems = new List<string> { "A棟 2FL", "A棟 2FL(結構)" } });
                list.Add(new ResourceMappingDto { MappingId = "M2", Type = type, SystemName = "A棟 3F", RevitActualName = "A棟 3FL", ConfidenceScore = 1.0, Status = UiItemStatus.Success, StatusText = "已精確對應", AvailableRevitItems = new List<string> { "A棟 3FL", "A棟 3FL(結構)" } });
                list.Add(new ResourceMappingDto { MappingId = "M3", Type = type, SystemName = "A棟 4F", RevitActualName = "A棟 4FL(結構)", ConfidenceScore = 0.85, Status = UiItemStatus.Warning, StatusText = "需要確認", AvailableRevitItems = new List<string> { "A棟 4FL(結構)", "A棟 4FL(建築)" } });
                list.Add(new ResourceMappingDto { MappingId = "M4", Type = type, SystemName = "A棟 7F", RevitActualName = "A棟 7FL", ConfidenceScore = 1.0, Status = UiItemStatus.Success, StatusText = "已精確對應", AvailableRevitItems = new List<string> { "A棟 7FL", "A棟 7FL(結構)" } });
            }
            else if (type == ResourceMappingType.ScopeBox)
            {
                list.Add(new ResourceMappingDto { MappingId = "SB1", Type = type, SystemName = "A棟 2F 建築範圍框", RevitActualName = "A棟2FL建築", ConfidenceScore = 1.0, Status = UiItemStatus.Success, StatusText = "已精確對應", AvailableRevitItems = new List<string> { "A棟2FL建築", "A棟2FL結構" } });
                list.Add(new ResourceMappingDto { MappingId = "SB2", Type = type, SystemName = "A棟 7F 建築範圍框", RevitActualName = "A棟7FL建築", ConfidenceScore = 0.92, Status = UiItemStatus.Warning, StatusText = "建議對應", AvailableRevitItems = new List<string> { "A棟7FL建築", "全區建築" } });
            }
            return Task.FromResult(list);
        }

        public Task<bool> SaveResourceMappingsAsync(List<ResourceMappingDto> mappings)
        {
            return Task.FromResult(true);
        }

        public Task<List<ResourceMappingDto>> RunAutoMappingAsync(ResourceMappingType type)
        {
            return GetResourceMappingsAsync(type);
        }

        public Task<bool> OpenSheetInRevitAsync(string sheetNumber)
        {
            return Task.FromResult(true);
        }

        public Task<bool> OpenViewInRevitAsync(string viewName)
        {
            return Task.FromResult(true);
        }
    }
}
