using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class QuickGenerateViewModel : BaseViewModel
    {
        private readonly IRevitDataService _revitService;
        private readonly INavigationService _navigationService;
        private readonly INotificationService _notificationService;

        private BuildingCardDto _selectedBuilding;
        private SheetCreationType _selectedPackageType = SheetCreationType.StandardFloorPackage;
        private int _estimatedSheetsCount = 13;
        private int _estimatedViewsCount = 18;
        private string _selectionSummaryText = "尚未選擇樓層";
        private string _sourceMasterDescription = "【基準專案 A】尚未選擇";
        private string _selectedSourceProject = string.Empty;
        private SheetSeriesDto _selectedSourceSheetSeries;
        private string _selectedTargetProject = string.Empty;
        private string _targetSheetSeries = BIMDrawingFactory.UI.Services.FactoryConventions.Current.DefaultTargetSeries;
        private bool _isLoadingPreview;
        private bool _isLoadingSource;
        private bool _suppressAutomaticLoads;
        private bool _isInitializing;
        private int _seriesRequestVersion;
        private int _targetRequestVersion;
        private int _previewRequestVersion;

        public ObservableCollection<string> SourceProjects { get; private set; }
        public ObservableCollection<SheetSeriesDto> SourceSheetSeriesList { get; private set; }
        public ObservableCollection<DrawingRuleDto> SourceSheetItems { get; private set; }
        public ObservableCollection<string> TargetProjects { get; private set; }
        public ObservableCollection<BuildingCardDto> Buildings { get; private set; }
        public ObservableCollection<FloorChipDto> CurrentFloors { get; private set; }
        public ObservableCollection<FloorSeriesMappingDto> FloorSeriesMappings { get; private set; }
        public ObservableCollection<SheetPreviewItemDto> OnDemandPreviewItems { get; private set; }

        public string SelectedTargetProject
        {
            get { return _selectedTargetProject; }
            set
            {
                if (SetProperty(ref _selectedTargetProject, value))
                {
                    if (!_suppressAutomaticLoads) OnTargetProjectChanged();
                }
            }
        }

        public string TargetSheetSeries
        {
            get { return _targetSheetSeries; }
            set
            {
                if (SetProperty(ref _targetSheetSeries, value))
                {
                    RecalculateEstimates();
                }
            }
        }

        public string SelectedSourceProject
        {
            get { return _selectedSourceProject; }
            set
            {
                if (SetProperty(ref _selectedSourceProject, value))
                {
                    if (!_suppressAutomaticLoads) OnSourceProjectChanged();
                }
            }
        }

        public SheetSeriesDto SelectedSourceSheetSeries
        {
            get { return _selectedSourceSheetSeries; }
            set
            {
                if (SetProperty(ref _selectedSourceSheetSeries, value))
                {
                    ApplySelectedSeries(value);
                }
            }
        }

        public string SourceMasterDescription
        {
            get { return _sourceMasterDescription; }
            set { SetProperty(ref _sourceMasterDescription, value); }
        }

        public bool IsLoadingPreview
        {
            get { return _isLoadingPreview; }
            set { SetProperty(ref _isLoadingPreview, value); }
        }

        public bool IsLoadingSource
        {
            get { return _isLoadingSource; }
            set { SetProperty(ref _isLoadingSource, value); }
        }

        public BuildingCardDto SelectedBuilding
        {
            get { return _selectedBuilding; }
            set
            {
                if (SetProperty(ref _selectedBuilding, value))
                {
                    OnBuildingSelected(value);
                }
            }
        }

        public SheetCreationType SelectedPackageType
        {
            get { return _selectedPackageType; }
            set
            {
                if (SetProperty(ref _selectedPackageType, value))
                {
                    RecalculateEstimates();
                }
            }
        }

        public int EstimatedSheetsCount
        {
            get { return _estimatedSheetsCount; }
            set { SetProperty(ref _estimatedSheetsCount, value); }
        }

        public int EstimatedViewsCount
        {
            get { return _estimatedViewsCount; }
            set { SetProperty(ref _estimatedViewsCount, value); }
        }

        public string SelectionSummaryText
        {
            get { return _selectionSummaryText; }
            set { SetProperty(ref _selectionSummaryText, value); }
        }

        public ICommand SelectAllSourceSheetsCommand { get; private set; }
        public ICommand ClearSourceSheetsCommand { get; private set; }
        public ICommand RefreshSourceCommand { get; private set; }
        public ICommand SelectBuildingCommand { get; private set; }
        public ICommand ToggleFloorSelectionCommand { get; private set; }
        public ICommand SelectAllFloorsCommand { get; private set; }
        public ICommand SelectBasementFloorsCommand { get; private set; }
        public ICommand SelectAboveGroundFloorsCommand { get; private set; }
        public ICommand SelectTypicalFloorsCommand { get; private set; }
        public ICommand SelectUncreatedFloorsCommand { get; private set; }
        public ICommand ClearFloorSelectionCommand { get; private set; }
        public ICommand ProceedToPreflightCommand { get; private set; }

        public QuickGenerateViewModel(
            IRevitDataService revitService,
            INavigationService navigationService,
            INotificationService notificationService)
        {
            _revitService = revitService;
            _navigationService = navigationService;
            _notificationService = notificationService;

            SourceProjects = new ObservableCollection<string>();

            SourceSheetSeriesList = new ObservableCollection<SheetSeriesDto>();
            SourceSheetItems = new ObservableCollection<DrawingRuleDto>();
            TargetProjects = new ObservableCollection<string>();

            Buildings = new ObservableCollection<BuildingCardDto>();
            CurrentFloors = new ObservableCollection<FloorChipDto>();
            FloorSeriesMappings = new ObservableCollection<FloorSeriesMappingDto>();
            OnDemandPreviewItems = new ObservableCollection<SheetPreviewItemDto>();

            SelectAllSourceSheetsCommand = new RelayCommand(() =>
            {
                foreach (var s in SourceSheetItems) s.IsChecked = true;
                RecalculateEstimates();
            });

            ClearSourceSheetsCommand = new RelayCommand(() =>
            {
                foreach (var s in SourceSheetItems) s.IsChecked = false;
                RecalculateEstimates();
            });

            RefreshSourceCommand = new AsyncRelayCommand(RefreshSourceAsync);

            SelectBuildingCommand = new RelayCommand(param =>
            {
                var building = param as BuildingCardDto;
                if (building != null)
                {
                    SelectedBuilding = building;
                }
            });

            ToggleFloorSelectionCommand = new RelayCommand(param =>
            {
                var chip = param as FloorChipDto;
                if (chip != null)
                {
                    chip.IsSelected = !chip.IsSelected;
                    RecalculateEstimates();
                }
            });

            SelectAllFloorsCommand = new RelayCommand(() =>
            {
                foreach (var f in CurrentFloors) f.IsSelected = true;
                RecalculateEstimates();
            });

            SelectBasementFloorsCommand = new RelayCommand(() =>
            {
                foreach (var f in CurrentFloors)
                {
                    f.IsSelected = f.FloorName.StartsWith("B") || f.FloorName.Contains("地下");
                }
                RecalculateEstimates();
            });

            SelectAboveGroundFloorsCommand = new RelayCommand(() =>
            {
                foreach (var f in CurrentFloors)
                {
                    f.IsSelected = !f.FloorName.StartsWith("B") && !f.FloorName.Contains("地下");
                }
                RecalculateEstimates();
            });

            SelectTypicalFloorsCommand = new RelayCommand(() =>
            {
                foreach (var f in CurrentFloors)
                {
                    f.IsSelected = f.FloorName != "1F" && f.FloorName != "RF" && !f.FloorName.StartsWith("B") && !f.FloorName.Contains("地下");
                }
                RecalculateEstimates();
            });

            SelectUncreatedFloorsCommand = new RelayCommand(() =>
            {
                foreach (var f in CurrentFloors)
                {
                    f.IsSelected = f.Status == FloorStatus.NotCreated || f.Status == FloorStatus.Outdated;
                }
                RecalculateEstimates();
            });

            ClearFloorSelectionCommand = new RelayCommand(() =>
            {
                foreach (var f in CurrentFloors) f.IsSelected = false;
                RecalculateEstimates();
            });

            ProceedToPreflightCommand = new RelayCommand(ProceedToPreflight);
        }

        private async void OnSourceProjectChanged()
        {
            if (string.IsNullOrEmpty(SelectedSourceProject)) return;
            await ReloadSelectedSourceAsync(false);
        }

        private async void OnTargetProjectChanged()
        {
            if (string.IsNullOrEmpty(SelectedTargetProject)) return;
            await ReloadTargetProjectAsync(SelectedTargetProject);
        }

        public async Task InitializeAsync()
        {
            if (_isInitializing) return;
            _isInitializing = true;
            try
            {
                var targetProjects = await _revitService.GetOpenProjectNamesAsync();
                var sourceProjects = await _revitService.GetOpenSourceProjectNamesAsync();
                string previousSource = SelectedSourceProject;
                string previousTarget = SelectedTargetProject;
                _suppressAutomaticLoads = true;
                SourceProjects.Clear();
                TargetProjects.Clear();
                foreach (var project in sourceProjects) SourceProjects.Add(project);
                foreach (var project in targetProjects) TargetProjects.Add(project);

                if (TargetProjects.Count > 0 && !TargetProjects.Contains(previousTarget))
                    SelectedTargetProject = TargetProjects[0];
                if (SourceProjects.Count > 0 && !SourceProjects.Contains(previousSource))
                {
                    // 兩個文件同時開啟時，預設以目前作用中的主文件為 B，另一個文件為 A。
                    SelectedSourceProject = SourceProjects.FirstOrDefault(p =>
                        !string.Equals(p, SelectedTargetProject, StringComparison.OrdinalIgnoreCase)) ?? SourceProjects[0];
                }

                await ReloadTargetProjectAsync(SelectedTargetProject);

                // Every entry/reload must reflect the currently open Revit documents.
                // Do not reuse the analysis captured when the add-in window first opened.
                _revitService.InvalidateAnalysisCache();
                await LoadSeriesListAsync(SelectedSourceProject, null);
                _suppressAutomaticLoads = false;
                RecalculateEstimates();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[InitializeAsync Exception] {0}", ex.Message));
                _notificationService.ShowToast(ex.Message, UiItemStatus.Error);
            }
            finally
            {
                _suppressAutomaticLoads = false;
                _isInitializing = false;
            }
        }

        private async Task RefreshSourceAsync()
        {
            if (_isLoadingSource) return;

            string selectedSource = SelectedSourceProject;
            var projects = await _revitService.GetOpenSourceProjectNamesAsync();
            _suppressAutomaticLoads = true;
            try
            {
                SourceProjects.Clear();
                foreach (var project in projects) SourceProjects.Add(project);
                SelectedSourceProject = SourceProjects.FirstOrDefault(p =>
                    string.Equals(p, selectedSource, StringComparison.OrdinalIgnoreCase))
                    ?? SourceProjects.FirstOrDefault();
            }
            finally
            {
                _suppressAutomaticLoads = false;
            }

            if (!string.IsNullOrEmpty(SelectedSourceProject))
                await ReloadSelectedSourceAsync(true);
        }

        private async Task ReloadSelectedSourceAsync(bool preserveSeries)
        {
            string sourceProject = SelectedSourceProject;
            string previousSeriesId = preserveSeries && SelectedSourceSheetSeries != null
                ? SelectedSourceSheetSeries.SeriesId
                : null;

            // Invalidate both the source analysis and any preview request that still
            // belongs to the previous document. Clear the UI immediately so stale A
            // data can never be submitted while the new A document is being scanned.
            _revitService.InvalidateAnalysisCache();
            ++_previewRequestVersion;
            IsLoadingPreview = false;
            OnDemandPreviewItems.Clear();
            SourceSheetSeriesList.Clear();
            SourceSheetItems.Clear();
            _selectedSourceSheetSeries = null;
            OnPropertyChanged("SelectedSourceSheetSeries");

            await LoadSeriesListAsync(sourceProject, previousSeriesId);
        }

        private async Task LoadSeriesListAsync(string sourceProject, string preferredSeriesId)
        {
            int requestVersion = ++_seriesRequestVersion;
            IsLoadingSource = true;
            try
            {
                var seriesList = await _revitService.GetAvailableSheetSeriesAsync(sourceProject);
                if (requestVersion != _seriesRequestVersion ||
                    !string.Equals(sourceProject, SelectedSourceProject, StringComparison.OrdinalIgnoreCase)) return;
                SourceSheetSeriesList.Clear();
                if (seriesList != null && seriesList.Count > 0)
                {
                    foreach (var s in seriesList)
                    {
                        SourceSheetSeriesList.Add(s);
                    }

                    var defaultSeries = SourceSheetSeriesList.FirstOrDefault(s =>
                                            !string.IsNullOrEmpty(preferredSeriesId) &&
                                            string.Equals(s.SeriesId, preferredSeriesId, StringComparison.OrdinalIgnoreCase))
                                      ?? SourceSheetSeriesList.FirstOrDefault(s => !string.IsNullOrEmpty(BIMDrawingFactory.UI.Services.FactoryConventions.Current.PreferredSourceSeries) &&
                                            string.Equals(s.SeriesId, BIMDrawingFactory.UI.Services.FactoryConventions.Current.PreferredSourceSeries, StringComparison.OrdinalIgnoreCase))
                                      ?? SourceSheetSeriesList.FirstOrDefault();
                    SelectedSourceSheetSeries = defaultSeries;
                }
                else
                {
                    SelectedSourceSheetSeries = null;
                    _notificationService.ShowToast("所選來源專案沒有可用的標準圖紙系列", UiItemStatus.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[LoadSeriesListAsync Error] {0}", ex.Message));
                if (requestVersion == _seriesRequestVersion)
                {
                    SourceSheetSeriesList.Clear();
                    SourceSheetItems.Clear();
                    _notificationService.ShowToast(ex.Message, UiItemStatus.Error);
                }
            }
            finally
            {
                if (requestVersion == _seriesRequestVersion) IsLoadingSource = false;
            }
        }

        private async Task ReloadTargetProjectAsync(string targetProject)
        {
            if (string.IsNullOrEmpty(targetProject)) return;
            int requestVersion = ++_targetRequestVersion;

            // Target B owns all downstream Level ids, floor chips, mappings and
            // previews. Clear them immediately so the user can never continue with
            // a mixture of the new document name and the old document's state.
            ++_previewRequestVersion;
            IsLoadingPreview = false;
            OnDemandPreviewItems.Clear();
            FloorSeriesMappings.Clear();
            CurrentFloors.Clear();
            Buildings.Clear();
            _selectedBuilding = null;
            OnPropertyChanged("SelectedBuilding");
            SelectionSummaryText = "正在讀取目標專案…";
            try
            {
                var buildings = await _revitService.GetBuildingsAsync(targetProject);
                if (requestVersion != _targetRequestVersion ||
                    !string.Equals(targetProject, SelectedTargetProject, StringComparison.OrdinalIgnoreCase)) return;
                Buildings.Clear();
                foreach (var building in buildings) Buildings.Add(building);
                SelectedBuilding = SelectTargetBuilding(Buildings, targetProject);
                if (SelectedBuilding == null)
                {
                    RecalculateEstimates();
                    _notificationService.ShowToast("目標專案沒有可用的 Level 或棟別資料", UiItemStatus.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[LoadBuildingsForTargetAsync Error] {0}", ex.Message));
                if (requestVersion == _targetRequestVersion)
                    _notificationService.ShowToast(ex.Message, UiItemStatus.Error);
            }
        }

        private static BuildingCardDto SelectTargetBuilding(IEnumerable<BuildingCardDto> buildings, string targetProject)
        {
            var candidates = (buildings ?? Enumerable.Empty<BuildingCardDto>()).ToList();
            if (candidates.Count == 0) return null;
            string target = targetProject ?? string.Empty;
            var conventions = BIMDrawingFactory.UI.Services.FactoryConventions.Current;
            // 目標專案檔名若帶有棟別（例如「…_LB_…」或 conventions.json 設定的棟別名稱），優先選那一棟
            string wantedKey = conventions.GetRequestedBuildingKey(target);
            if (!conventions.IsSpecificBuildingKey(wantedKey)) wantedKey = string.Empty;
            bool wantsBasement = target.IndexOf("地下", StringComparison.OrdinalIgnoreCase) >= 0;

            Func<BuildingCardDto, int> score = b =>
            {
                string name = (b.Name ?? string.Empty) + " " + (b.Id ?? string.Empty);
                int value = b.IsSelected ? 1 : 0;
                if (!string.IsNullOrEmpty(wantedKey))
                {
                    string cardKey = b.Id != null && b.Id.StartsWith("B_", StringComparison.Ordinal) ? b.Id.Substring(2) : string.Empty;
                    if (string.Equals(cardKey, wantedKey, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(conventions.GetRequestedBuildingKey(b.Name), wantedKey, StringComparison.OrdinalIgnoreCase))
                        value += 100;
                }
                if (wantsBasement && (name.Contains("地下") || name.IndexOf("BASEMENT", StringComparison.OrdinalIgnoreCase) >= 0)) value += 100;
                return value;
            };
            return candidates.OrderByDescending(score).First();
        }

        private void ApplySelectedSeries(SheetSeriesDto series)
        {
            SourceSheetItems.Clear();
            if (series != null && series.Sheets != null)
            {
                foreach (var r in series.Sheets)
                {
                    r.IsChecked = true;
                    SourceSheetItems.Add(r);
                }
            }
            RecalculateEstimates();
        }

        private void OnBuildingSelected(BuildingCardDto building)
        {
            if (building == null) return;

            try
            {
                foreach (var b in Buildings)
                {
                    b.IsSelected = (b.Id == building.Id);
                }

                CurrentFloors.Clear();
                foreach (var f in building.Floors)
                {
                    CurrentFloors.Add(f);
                }

                RecalculateEstimates();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[OnBuildingSelected Error] {0}", ex));
            }
        }

        private void RecalculateEstimates()
        {
            var selectedFloors = CurrentFloors.Where(f => f.IsSelected).ToList();
            var floorCount = selectedFloors.Count;
            var buildingName = SelectedBuilding != null ? SelectedBuilding.Name : "全區 / 主建築";

            var checkedRulesCount = SourceSheetItems.Count(s => s.IsChecked);
            if (checkedRulesCount == 0 && SourceSheetItems.Count > 0)
            {
                checkedRulesCount = 0;
            }
            else if (checkedRulesCount == 0)
            {
                checkedRulesCount = 41; // 預設 41 張圖紙
            }

            // 解析 TargetSheetSeries 基礎編號 (例如 S6、A-6；前綴規則見 conventions.json)
            string rawSeries = string.IsNullOrEmpty(TargetSheetSeries) ? BIMDrawingFactory.UI.Services.FactoryConventions.Current.DefaultTargetSeries : TargetSheetSeries.Trim();
            string seriesPrefix = BIMDrawingFactory.UI.Services.FactoryConventions.Current.ApplySeriesPrefix(BIMDrawingFactory.UI.Services.FactoryConventions.Current.DefaultSeriesLetters);
            int startSeriesNum = 6;

            var matchSeries = System.Text.RegularExpressions.Regex.Match(rawSeries, @"^(.*?)(SB)?(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (matchSeries.Success)
            {
                string pre = matchSeries.Groups[1].Value;
                string sb = matchSeries.Groups[2].Value;
                int num;
                if (int.TryParse(matchSeries.Groups[3].Value, out num))
                {
                    startSeriesNum = num;
                    if (!string.IsNullOrEmpty(pre) || !string.IsNullOrEmpty(sb))
                    {
                        seriesPrefix = string.Format("{0}{1}", pre, sb);
                        seriesPrefix = BIMDrawingFactory.UI.Services.FactoryConventions.Current.ApplySeriesPrefix(seriesPrefix);
                    }
                }
            }
            else
            {
                seriesPrefix = rawSeries;
            }

            FloorSeriesMappings.Clear();
            for (int i = 0; i < selectedFloors.Count; i++)
            {
                var f = selectedFloors[i];
                int currentSeriesNum = startSeriesNum + i;
                string curSeriesCode = string.Format("{0}{1}", seriesPrefix, currentSeriesNum);
                string rangeText = checkedRulesCount > 0 
                    ? string.Format("{0}-01 ~ {0}-{1:D2}", curSeriesCode, checkedRulesCount)
                    : string.Format("{0}-01", curSeriesCode);

                FloorSeriesMappings.Add(new FloorSeriesMappingDto
                {
                    FloorId = f.FloorId,
                    FloorName = f.FloorName,
                    TargetSeriesCode = curSeriesCode,
                    SheetNumberRange = rangeText,
                    SheetsCount = checkedRulesCount,
                    ViewsCount = checkedRulesCount
                });
            }

            EstimatedSheetsCount = floorCount * checkedRulesCount;
            EstimatedViewsCount = (int)(floorCount * checkedRulesCount * 1.4);

            string floorText;
            if (floorCount == 0)
            {
                floorText = "尚未選擇樓層";
            }
            else if (floorCount == 1)
            {
                floorText = selectedFloors[0].FloorName;
            }
            else
            {
                floorText = string.Format("{0} ~ {1} ({2} 層)", selectedFloors.First().FloorName, selectedFloors.Last().FloorName, floorCount);
            }

            SelectionSummaryText = string.Format("{0} {1} (預計 {2} 張圖紙 / {3} 個視圖)", buildingName, floorText, EstimatedSheetsCount, EstimatedViewsCount);

            if (!_suppressAutomaticLoads) RefreshOnDemandPreview(selectedFloors);
        }

        private async void RefreshOnDemandPreview(List<FloorChipDto> selectedFloors)
        {
            int requestVersion = ++_previewRequestVersion;
            if (selectedFloors == null || selectedFloors.Count == 0)
            {
                OnDemandPreviewItems.Clear();
                return;
            }

            try
            {
                await Task.Delay(350);
                if (requestVersion != _previewRequestVersion) return;
                IsLoadingPreview = true;
                var request = new GenerateRequestDto
                {
                    SourceProjectName = SelectedSourceProject,
                    TargetProjectName = SelectedTargetProject,
                    SourceSeriesId = SelectedSourceSheetSeries != null ? SelectedSourceSheetSeries.SeriesId : string.Empty,
                    BuildingId = SelectedBuilding != null ? SelectedBuilding.Id : "B_MAIN",
                    BuildingName = SelectedBuilding != null ? SelectedBuilding.Name : string.Empty,
                    TargetSheetSeries = string.IsNullOrEmpty(TargetSheetSeries) ? BIMDrawingFactory.UI.Services.FactoryConventions.Current.DefaultTargetSeries : TargetSheetSeries.Trim(),
                    SelectedFloorIds = selectedFloors.Select(f => f.FloorId).ToList(),
                    SelectedFloorNames = selectedFloors.Select(f => f.FloorName).ToList(),
                    PackageType = SelectedPackageType,
                    CustomSelectedRuleIds = SourceSheetItems.Where(c => c.IsChecked).Select(c => c.RuleId).ToList(),
                    FloorMappings = FloorSeriesMappings.ToList()
                };

                var items = await _revitService.GetPreviewItemsAsync(request);
                if (requestVersion != _previewRequestVersion) return;
                OnDemandPreviewItems.Clear();
                if (items != null)
                {
                    foreach (var itm in items)
                    {
                        OnDemandPreviewItems.Add(itm);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[RefreshOnDemandPreview Error] {0}", ex.Message));
                if (requestVersion == _previewRequestVersion)
                    _notificationService.ShowToast("即時預覽失敗：" + ex.Message, UiItemStatus.Error);
            }
            finally
            {
                if (requestVersion == _previewRequestVersion) IsLoadingPreview = false;
            }
        }

        private void ProceedToPreflight()
        {
            var selectedFloors = CurrentFloors.Where(f => f.IsSelected).ToList();
            if (selectedFloors.Count == 0)
            {
                _notificationService.ShowToast("請先選擇至少一個要製作的目標樓層", UiItemStatus.Warning);
                return;
            }

            var request = new GenerateRequestDto
            {
                SourceProjectName = SelectedSourceProject,
                TargetProjectName = SelectedTargetProject,
                SourceSeriesId = SelectedSourceSheetSeries != null ? SelectedSourceSheetSeries.SeriesId : string.Empty,
                BuildingId = SelectedBuilding != null ? SelectedBuilding.Id : "B_MAIN",
                BuildingName = SelectedBuilding != null ? SelectedBuilding.Name : "主建築",
                TargetSheetSeries = string.IsNullOrEmpty(TargetSheetSeries) ? BIMDrawingFactory.UI.Services.FactoryConventions.Current.DefaultTargetSeries : TargetSheetSeries.Trim(),
                SelectedFloorIds = selectedFloors.Select(f => f.FloorId).ToList(),
                SelectedFloorNames = selectedFloors.Select(f => f.FloorName).ToList(),
                PackageType = SelectedPackageType,
                CustomSelectedRuleIds = SourceSheetItems.Where(c => c.IsChecked).Select(c => c.RuleId).ToList(),
                FloorMappings = FloorSeriesMappings.ToList()
            };

            _navigationService.NavigateTo(NavigationPageKey.Preflight, request);
        }
    }
}
