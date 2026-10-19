using System.Collections.Generic;

namespace BIMDrawingFactory.UI.Models
{
    public class SheetPreviewItemDto
    {
        public bool IsSelected { get; set; }
        public string BuildingName { get; set; }
        public string SheetFloorName { get; set; }
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public string ViewName { get; set; }
        public string ResolvedViewLevelName { get; set; }
        public string ViewTemplateName { get; set; }
        public string ScopeBoxName { get; set; }
        public string Scale { get; set; }
        public UiItemStatus Status { get; set; }
        public string StatusMessage { get; set; }
        public int IssueCount { get; set; }
        public string ActionDescription { get; set; }
        public string SourceStandardSheet { get; set; }
        public string SourceStandardView { get; set; }
        public string LevelRuleDescription { get; set; }
        public string SourceTitleBlockName { get; set; }
        public string SourceTitleBlockTypeId { get; set; }
        public string ResolvedTitleBlockName { get; set; }
        public string ResolvedTitleBlockTypeId { get; set; }
        public bool TargetSheetExists { get; set; }
        public bool IsCreatedByDrawingFactory { get; set; }
        public List<IssueItemDto> Issues { get; set; }
        public List<SheetViewItemDto> PlannedViews { get; set; }

        public int PlannedViewsCount
        {
            get { return PlannedViews != null ? PlannedViews.Count : 0; }
        }

        public string PlannedViewsCountText
        {
            get { return string.Format("{0} 個圖／表", PlannedViewsCount); }
        }

        public SheetPreviewItemDto()
        {
            IsSelected = true;
            BuildingName = string.Empty;
            SheetFloorName = string.Empty;
            SheetNumber = string.Empty;
            SheetName = string.Empty;
            ViewName = string.Empty;
            ResolvedViewLevelName = string.Empty;
            ViewTemplateName = string.Empty;
            ScopeBoxName = string.Empty;
            Scale = "1:100";
            Status = UiItemStatus.Success;
            StatusMessage = "可以建立";
            ActionDescription = "建立新的平面視圖並放置於圖紙";
            SourceStandardSheet = string.Empty;
            SourceStandardView = string.Empty;
            LevelRuleDescription = "使用本樓層";
            SourceTitleBlockName = string.Empty;
            SourceTitleBlockTypeId = string.Empty;
            ResolvedTitleBlockName = string.Empty;
            ResolvedTitleBlockTypeId = string.Empty;
            Issues = new List<IssueItemDto>();
            PlannedViews = new List<SheetViewItemDto>();
        }
    }

    public class IssueItemDto
    {
        public string IssueId { get; set; }
        public IssueSeverity Severity { get; set; }
        public string FloorName { get; set; }
        public string SheetOrViewName { get; set; }
        public string UserMessage { get; set; }
        public string SuggestedAction { get; set; }
        public string StandardRequirement { get; set; }
        public string SystemRecommendation { get; set; }
        public string TechnicalDetails { get; set; }
        public List<string> ResolutionOptions { get; set; }
        public string SelectedResolution { get; set; }

        public IssueItemDto()
        {
            IssueId = string.Empty;
            Severity = IssueSeverity.Warning;
            FloorName = string.Empty;
            SheetOrViewName = string.Empty;
            UserMessage = string.Empty;
            SuggestedAction = string.Empty;
            StandardRequirement = string.Empty;
            SystemRecommendation = string.Empty;
            TechnicalDetails = string.Empty;
            ResolutionOptions = new List<string>();
            SelectedResolution = string.Empty;
        }
    }

    public class SheetViewItemDto
    {
        public string ViewId { get; set; }
        public string ViewName { get; set; }
        public string ViewType { get; set; }
        public string ViewTemplateName { get; set; }
        public string Scale { get; set; }
        public string ScopeBoxName { get; set; }
        public string PlacementCoord { get; set; }
        public string SourceLevelName { get; set; }
        public string TargetViewName { get; set; }
        public string ResolvedLevelName { get; set; }
        public string MigrationAction { get; set; }
        public string LevelBindingRule { get; set; }
        public string ViewDirectionDescription { get; set; }
        public string TargetLevelId { get; set; }
        public double SourceLevelElevationMm { get; set; }
        public double TargetLevelElevationMm { get; set; }
        public double TargetFloorHeightMm { get; set; }
        public bool HasPlanViewRange { get; set; }
        public int TopPlaneStoryDelta { get; set; }
        public int CutPlaneStoryDelta { get; set; }
        public int BottomPlaneStoryDelta { get; set; }
        public int ViewDepthPlaneStoryDelta { get; set; }
        public double TopPlaneOffsetMm { get; set; }
        public double CutPlaneOffsetMm { get; set; }
        public double BottomPlaneOffsetMm { get; set; }
        public double ViewDepthPlaneOffsetMm { get; set; }
        public int AnnotationCount { get; set; }
        public int DimensionCount { get; set; }
        public int TextNoteCount { get; set; }
        public int DetailElementCount { get; set; }
        public string DimensionStyleNames { get; set; }
        public string DimensionLayoutSummary { get; set; }
        public bool HasUnderlay { get; set; }
        public int UnderlayBottomStoryDelta { get; set; }
        public int UnderlayTopStoryDelta { get; set; }
        public int UnderlayOrientation { get; set; }
        public string SourcePhaseName { get; set; }
        public string SourcePhaseFilterName { get; set; }
        public string ViewportTypeName { get; set; }
        public string SourceViewportTypeId { get; set; }
        public string ResolvedViewportTypeName { get; set; }
        public string ResolvedViewportTypeId { get; set; }
        public bool IsDependentView { get; set; }
        public string SourcePrimaryViewId { get; set; }
        public string SourcePrimaryViewName { get; set; }
        public string TargetPrimaryViewName { get; set; }

        public string ElevationSummary
        {
            get { return string.Format("來源 {0:F0} mm → 目標 {1:F0} mm（層高 {2:F0} mm）", SourceLevelElevationMm, TargetLevelElevationMm, TargetFloorHeightMm); }
        }

        public string ViewRangeSummary
        {
            get
            {
                return HasPlanViewRange
                    ? string.Format("上界 {0:+0;-0;0}/{1:F0}、剖切 {2:+0;-0;0}/{3:F0}、下界 {4:+0;-0;0}/{5:F0}、深度 {6:+0;-0;0}/{7:F0} mm",
                        TopPlaneStoryDelta, TopPlaneOffsetMm, CutPlaneStoryDelta, CutPlaneOffsetMm,
                        BottomPlaneStoryDelta, BottomPlaneOffsetMm, ViewDepthPlaneStoryDelta, ViewDepthPlaneOffsetMm)
                    : "不適用";
            }
        }

        public string AnnotationSummary
        {
            get
            {
                int total = AnnotationCount + TextNoteCount + DetailElementCount;
                return total > 0
                    ? string.Format("一般註解 {0}、文字 {1}、詳圖元件 {2}", AnnotationCount, TextNoteCount, DetailElementCount)
                    : "建立時複製文字與詳圖元件；標籤及尺寸由專用外掛處理";
            }
        }

        public SheetViewItemDto()
        {
            ViewId = string.Empty;
            ViewName = string.Empty;
            ViewType = "平面圖";
            ViewTemplateName = "無樣板";
            Scale = "1:100";
            ScopeBoxName = "-";
            PlacementCoord = "-";
            SourceLevelName = "不依附樓層";
            TargetViewName = string.Empty;
            ResolvedLevelName = "不依附樓層";
            MigrationAction = "複製並依基準位置放置";
            LevelBindingRule = LevelBindingOption.Unbound.ToString();
            ViewDirectionDescription = "依來源視圖";
            TargetLevelId = string.Empty;
            SourcePhaseName = string.Empty;
            SourcePhaseFilterName = string.Empty;
            DimensionStyleNames = "交由尺寸專用外掛處理";
            DimensionLayoutSummary = "本外掛不分析";
            ViewportTypeName = "預設視埠";
            SourceViewportTypeId = string.Empty;
            ResolvedViewportTypeName = "預設視埠";
            ResolvedViewportTypeId = string.Empty;
            SourcePrimaryViewId = string.Empty;
            SourcePrimaryViewName = string.Empty;
            TargetPrimaryViewName = string.Empty;
        }
    }

    public class DrawingRuleDto
    {
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string BuildingName { get; set; }
        public string TargetFloorScope { get; set; }
        public string SheetNameFormula { get; set; }
        public string SheetNumberFormula { get; set; }
        public LevelBindingOption LevelBinding { get; set; }
        public string ViewTemplateName { get; set; }
        public string Scale { get; set; }
        public string ScopeBoxRule { get; set; }
        public string SourceViewName { get; set; }
        public string SourceSheetNumber { get; set; }
        public string SourceSheetName { get; set; }
        public string TitleBlockName { get; set; }
        public string TitleBlockTypeId { get; set; }
        public string ViewportPlacementCoord { get; set; }
        public string AssociatedSchedules { get; set; }
        public string AssociatedLegends { get; set; }
        public int ActiveUsageCount { get; set; }
        public bool IsModified { get; set; }
        public bool IsChecked { get; set; }
        public List<SheetViewItemDto> PlacedViews { get; set; }

        public int PlacedViewsCount
        {
            get { return PlacedViews != null ? PlacedViews.Count : 0; }
        }

        public string PlacedViewsBadgeText
        {
            get { return string.Format("{0} 個視圖", PlacedViewsCount); }
        }

        public DrawingRuleDto()
        {
            IsChecked = true;
            RuleId = string.Empty;
            RuleName = string.Empty;
            BuildingName = string.Empty;
            TargetFloorScope = "一般樓層";
            SheetNameFormula = "{棟別}{樓層}放樣圖";
            SheetNumberFormula = "S{樓層}-05";
            LevelBinding = LevelBindingOption.CurrentLevel;
            ViewTemplateName = string.Empty;
            Scale = "1:100";
            ScopeBoxRule = "依棟別與樓層自動比對";
            SourceViewName = string.Empty;
            SourceSheetNumber = "SB1-05";
            SourceSheetName = "1FL 放樣圖";
            TitleBlockName = "自動比照基準圖框 (已偵測)";
            TitleBlockTypeId = string.Empty;
            ViewportPlacementCoord = "X: 420.0 mm, Y: 297.0 mm (圖紙中心自動對齊)";
            AssociatedSchedules = "無";
            AssociatedLegends = "放樣大樣圖例";
            PlacedViews = new List<SheetViewItemDto>();
        }
    }

    public class ResourceMappingDto
    {
        public string MappingId { get; set; }
        public ResourceMappingType Type { get; set; }
        public string SystemName { get; set; }
        public string RevitActualName { get; set; }
        public List<string> AvailableRevitItems { get; set; }
        public UiItemStatus Status { get; set; }
        public double ConfidenceScore { get; set; }
        public string StatusText { get; set; }
        public bool IsLocked { get; set; }

        public ResourceMappingDto()
        {
            MappingId = string.Empty;
            SystemName = string.Empty;
            RevitActualName = string.Empty;
            AvailableRevitItems = new List<string>();
            Status = UiItemStatus.Success;
            ConfidenceScore = 1.0;
            StatusText = "已精確對應";
        }
    }

    public class GenerationProgressDto : System.ComponentModel.INotifyPropertyChanged
    {
        private int _currentIndex;
        private int _totalCount;
        private string _currentItemNumber;
        private string _currentItemName;
        private string _currentStepDescription;

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
            }
        }

        public int CurrentIndex
        {
            get { return _currentIndex; }
            set
            {
                _currentIndex = value;
                OnPropertyChanged();
                OnPropertyChanged("Percentage");
            }
        }

        public int TotalCount
        {
            get { return _totalCount; }
            set
            {
                _totalCount = value;
                OnPropertyChanged();
                OnPropertyChanged("Percentage");
            }
        }

        public string CurrentItemNumber
        {
            get { return _currentItemNumber; }
            set { _currentItemNumber = value; OnPropertyChanged(); }
        }

        public string CurrentItemName
        {
            get { return _currentItemName; }
            set { _currentItemName = value; OnPropertyChanged(); }
        }

        public string CurrentStepDescription
        {
            get { return _currentStepDescription; }
            set { _currentStepDescription = value; OnPropertyChanged(); }
        }

        public double Percentage
        {
            get { return TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100.0 : 0; }
        }

        public GenerationProgressDto()
        {
            CurrentItemNumber = string.Empty;
            CurrentItemName = string.Empty;
            CurrentStepDescription = string.Empty;
        }
    }

    public class GenerationResultDto
    {
        public bool IsSuccess { get; set; }
        public string TargetBuilding { get; set; }
        public string TargetFloorsSummary { get; set; }
        public int CreatedSheetsCount { get; set; }
        public int CreatedViewsCount { get; set; }
        public int CreatedSchedulesCount { get; set; }
        public int CreatedLegendsCount { get; set; }
        public int SkippedCount { get; set; }
        public int ManualReviewCount { get; set; }
        public int FailedCount { get; set; }
        public string ElapsedTime { get; set; }
        public string FirstCreatedSheetNumber { get; set; }
        public List<string> GeneratedSheetSummaries { get; set; }
        public List<IssueItemDto> UnresolvedIssues { get; set; }

        public string ResultTitle
        {
            get { return IsSuccess ? "圖紙建立完成！" : "圖紙建立失敗"; }
        }

        public string ResultSummary
        {
            get
            {
                return IsSuccess
                    ? string.Format("目標：{0} 之圖紙與視圖已建立完成", TargetFloorsSummary)
                    : string.Format("成功 {0} 張、失敗 {1} 張；請依下方錯誤修正後重試", CreatedSheetsCount, FailedCount);
            }
        }

        public GenerationResultDto()
        {
            IsSuccess = true;
            TargetBuilding = string.Empty;
            TargetFloorsSummary = string.Empty;
            ElapsedTime = "00:18";
            FirstCreatedSheetNumber = string.Empty;
            GeneratedSheetSummaries = new List<string>();
            UnresolvedIssues = new List<IssueItemDto>();
        }
    }

    public class FloorSeriesMappingDto
    {
        public string FloorId { get; set; }
        public string FloorName { get; set; }
        public string TargetSeriesCode { get; set; }
        public string SheetNumberRange { get; set; }
        public int SheetsCount { get; set; }
        public int ViewsCount { get; set; }

        public FloorSeriesMappingDto()
        {
            FloorId = string.Empty;
            FloorName = string.Empty;
            TargetSeriesCode = "S6";
            SheetNumberRange = string.Empty;
            SheetsCount = 0;
            ViewsCount = 0;
        }
    }

    public class GenerateRequestDto
    {
        public string SourceProjectName { get; set; }
        public string TargetProjectName { get; set; }
        public string SourceSeriesId { get; set; }
        public string BuildingId { get; set; }
        public string BuildingName { get; set; }
        public string TargetSheetSeries { get; set; }
        public List<string> SelectedFloorIds { get; set; }
        public List<string> SelectedFloorNames { get; set; }
        public SheetCreationType PackageType { get; set; }
        public List<string> CustomSelectedRuleIds { get; set; }
        public List<FloorSeriesMappingDto> FloorMappings { get; set; }
        public List<string> SelectedSheetNumbers { get; set; }
        public List<SheetPreviewItemDto> SelectedPreviewItems { get; set; }
        public bool EnableDeferredTagging { get; set; }
        public bool EnableDeferredDimensioning { get; set; }

        public GenerateRequestDto()
        {
            SourceProjectName = string.Empty;
            TargetProjectName = string.Empty;
            SourceSeriesId = string.Empty;
            BuildingId = string.Empty;
            BuildingName = string.Empty;
            TargetSheetSeries = "SB6";
            SelectedFloorIds = new List<string>();
            SelectedFloorNames = new List<string>();
            PackageType = SheetCreationType.StandardFloorPackage;
            CustomSelectedRuleIds = new List<string>();
            FloorMappings = new List<FloorSeriesMappingDto>();
            SelectedSheetNumbers = new List<string>();
            SelectedPreviewItems = new List<SheetPreviewItemDto>();
            EnableDeferredTagging = true;
            EnableDeferredDimensioning = true;
        }
    }

    public class ProjectProfileDto
    {
        public string ProjectName { get; set; }
        public string StandardDrawingGroup { get; set; }
        public string SourceDocumentName { get; set; }
        public string StatusSummary { get; set; }
        public bool IsSetupComplete { get; set; }
        public int TotalRulesCount { get; set; }
        public int BuildingCount { get; set; }
        public string LastAnalysisTime { get; set; }

        public ProjectProfileDto()
        {
            ProjectName = "南山科技園區二期新建工程";
            StandardDrawingGroup = "S5";
            SourceDocumentName = "NS2_StandardMaster_2024.rvt";
            StatusSummary = "專案設定正常";
            IsSetupComplete = true;
            TotalRulesCount = 38;
            BuildingCount = 2;
            LastAnalysisTime = "2026/09/14 10:15";
        }
    }

    public class CustomDrawingPackageItemDto
    {
        public string RuleId { get; set; }
        public string Name { get; set; }
        public bool IsChecked { get; set; }
        public string DefaultViewTemplate { get; set; }

        public CustomDrawingPackageItemDto()
        {
            RuleId = string.Empty;
            Name = string.Empty;
            IsChecked = true;
            DefaultViewTemplate = string.Empty;
        }
    }

    public class SheetSeriesDto
    {
        public string SeriesId { get; set; }
        public string DisplayName { get; set; }
        public int SheetCount { get; set; }
        public List<DrawingRuleDto> Sheets { get; set; }

        public SheetSeriesDto()
        {
            SeriesId = string.Empty;
            DisplayName = string.Empty;
            Sheets = new List<DrawingRuleDto>();
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
