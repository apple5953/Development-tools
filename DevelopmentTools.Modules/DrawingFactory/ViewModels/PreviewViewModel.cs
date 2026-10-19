using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class PreviewViewModel : BaseViewModel
    {
        private readonly IRevitDataService _revitService;
        private readonly INavigationService _navigationService;
        private readonly INotificationService _notificationService;
        private readonly IDialogService _dialogService;
        private readonly InspectorViewModel _inspectorViewModel;

        private GenerateRequestDto _currentRequest;
        private List<SheetPreviewItemDto> _allPreviewItems = new List<SheetPreviewItemDto>();
        private SheetPreviewItemDto _selectedItem;
        private string _filterKeyword = string.Empty;
        private string _generateButtonText = "建立圖紙";
        private bool _isGenerating;
        private bool _isDeferredTaggingEnabled = true;
        private bool _isDeferredDimensioningEnabled = true;
        private GenerationProgressDto _progress = new GenerationProgressDto();
        private CancellationTokenSource _generationCts;

        public ObservableCollection<SheetPreviewItemDto> FilteredItems { get; private set; }

        public InspectorViewModel Inspector
        {
            get { return _inspectorViewModel; }
        }

        public SheetPreviewItemDto SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    _inspectorViewModel.SelectedSheet = value;
                }
            }
        }

        public string FilterKeyword
        {
            get { return _filterKeyword; }
            set
            {
                if (SetProperty(ref _filterKeyword, value))
                {
                    ApplyFilter();
                }
            }
        }

        public string GenerateButtonText
        {
            get { return _generateButtonText; }
            set { SetProperty(ref _generateButtonText, value); }
        }

        public bool IsGenerating
        {
            get { return _isGenerating; }
            set { SetProperty(ref _isGenerating, value); }
        }

        public bool IsDeferredTaggingEnabled
        {
            get { return _isDeferredTaggingEnabled; }
            set { SetProperty(ref _isDeferredTaggingEnabled, value); }
        }

        public bool IsDeferredDimensioningEnabled
        {
            get { return _isDeferredDimensioningEnabled; }
            set { SetProperty(ref _isDeferredDimensioningEnabled, value); }
        }

        public GenerationProgressDto Progress
        {
            get { return _progress; }
            set { SetProperty(ref _progress, value); }
        }

        public ICommand GenerateCommand { get; private set; }
        public ICommand CancelGenerationCommand { get; private set; }
        public ICommand GoBackCommand { get; private set; }
        public ICommand SelectAllItemsCommand { get; private set; }
        public ICommand InvertSelectionCommand { get; private set; }
        public ICommand SetSelectedToSkipCommand { get; private set; }

        public PreviewViewModel(
            IRevitDataService revitService,
            INavigationService navigationService,
            INotificationService notificationService,
            IDialogService dialogService,
            InspectorViewModel inspectorViewModel)
        {
            _revitService = revitService;
            _navigationService = navigationService;
            _notificationService = notificationService;
            _dialogService = dialogService;
            _inspectorViewModel = inspectorViewModel;

            FilteredItems = new ObservableCollection<SheetPreviewItemDto>();

            GenerateCommand = new AsyncRelayCommand(StartGenerationAsync);
            CancelGenerationCommand = new RelayCommand(StopGenerationSafely);
            GoBackCommand = new RelayCommand(() => _navigationService.GoBack());

            SelectAllItemsCommand = new RelayCommand(() =>
            {
                foreach (var item in FilteredItems) item.IsSelected = true;
                UpdateGenerateButtonText();
            });

            InvertSelectionCommand = new RelayCommand(() =>
            {
                foreach (var item in FilteredItems) item.IsSelected = !item.IsSelected;
                UpdateGenerateButtonText();
            });

            SetSelectedToSkipCommand = new RelayCommand(() =>
            {
                foreach (var item in FilteredItems.Where(i => i.IsSelected))
                {
                    item.IsSelected = false;
                    item.Status = UiItemStatus.Inactive;
                    item.StatusMessage = "已設定為跳過";
                }
                UpdateGenerateButtonText();
            });
        }

        public async Task LoadPreviewDataAsync(GenerateRequestDto request)
        {
            _currentRequest = request;
            IsDeferredTaggingEnabled = request != null && request.EnableDeferredTagging;
            IsDeferredDimensioningEnabled = request == null || request.EnableDeferredDimensioning;
            var items = await _revitService.GetPreviewItemsAsync(request);
            _allPreviewItems = items ?? new List<SheetPreviewItemDto>();
            foreach (var item in _allPreviewItems)
            {
                if (item.PlannedViews == null) item.PlannedViews = new List<SheetViewItemDto>();
                if (item.PlannedViews.Count == 0)
                {
                    item.PlannedViews.Add(new SheetViewItemDto
                    {
                        ViewName = item.SourceStandardView,
                        TargetViewName = item.ViewName,
                        ViewType = "視圖",
                        ViewTemplateName = item.ViewTemplateName,
                        Scale = item.Scale,
                        ScopeBoxName = item.ScopeBoxName,
                        ResolvedLevelName = item.ResolvedViewLevelName,
                        MigrationAction = item.ActionDescription
                    });
                }
            }
            ApplyFilter();
            SelectedItem = FilteredItems.FirstOrDefault();
            UpdateGenerateButtonText();
        }

        private void ApplyFilter()
        {
            FilteredItems.Clear();
            var keyword = FilterKeyword != null ? FilterKeyword.Trim().ToLowerInvariant() : string.Empty;

            foreach (var item in _allPreviewItems)
            {
                if (string.IsNullOrEmpty(keyword) ||
                    item.SheetNumber.ToLowerInvariant().Contains(keyword) ||
                    item.SheetName.ToLowerInvariant().Contains(keyword) ||
                    item.ViewName.ToLowerInvariant().Contains(keyword) ||
                    item.ResolvedViewLevelName.ToLowerInvariant().Contains(keyword))
                {
                    FilteredItems.Add(item);
                }
            }
        }

        private void UpdateGenerateButtonText()
        {
            var selectedCount = FilteredItems.Count(i => i.IsSelected);
            var buildingName = _currentRequest != null ? _currentRequest.BuildingName : "A棟";
            var floors = string.Join(", ", _currentRequest != null ? _currentRequest.SelectedFloorNames : new List<string> { "7F" });

            GenerateButtonText = string.Format("建立{0} {1} 圖紙 ({2} 張)", buildingName, floors, selectedCount);
        }

        private async Task StartGenerationAsync()
        {
            var selectedItems = FilteredItems.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                _notificationService.ShowToast("請先勾選至少一張要建立的圖紙", UiItemStatus.Warning);
                return;
            }

            var warningCount = selectedItems.Count(i => i.Status == UiItemStatus.Warning);
            if (warningCount > 0)
            {
                var confirm = await _dialogService.ShowWarningConfirmAsync(
                    "建立圖紙確認",
                    string.Format("目前選取的圖紙中有 {0} 項包含建議提醒（如採用推薦範圍框），系統將會自動套用建議並繼續建立，是否開始？", warningCount));
                if (!confirm) return;
            }

            Progress.TotalCount = selectedItems.Count;
            Progress.CurrentIndex = 0;
            Progress.CurrentItemNumber = selectedItems[0].SheetNumber;
            Progress.CurrentItemName = selectedItems[0].SheetName;
            Progress.CurrentStepDescription = "正在準備建立圖紙與視圖...";

            IsGenerating = true;
            _generationCts = new CancellationTokenSource();

            var progressHandler = new Progress<GenerationProgressDto>(p =>
            {
                if (p != null)
                {
                    Progress.CurrentIndex = p.CurrentIndex;
                    Progress.TotalCount = p.TotalCount;
                    Progress.CurrentItemNumber = p.CurrentItemNumber;
                    Progress.CurrentItemName = p.CurrentItemName;
                    Progress.CurrentStepDescription = p.CurrentStepDescription;
                }
            });

            try
            {
                _currentRequest.SelectedSheetNumbers = selectedItems.Select(i => i.SheetNumber).ToList();
                _currentRequest.SelectedPreviewItems = selectedItems;
                _currentRequest.EnableDeferredTagging = IsDeferredTaggingEnabled;
                _currentRequest.EnableDeferredDimensioning = IsDeferredDimensioningEnabled;
                var result = await _revitService.ExecuteGenerationAsync(_currentRequest, progressHandler, _generationCts.Token);
                if (result != null)
                {
                    _navigationService.NavigateTo(NavigationPageKey.GenerationResult, result);
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowToast(string.Format("建立作業中斷：{0}", ex.Message), UiItemStatus.Error);
            }
            finally
            {
                IsGenerating = false;
                if (_generationCts != null)
                {
                    _generationCts.Dispose();
                    _generationCts = null;
                }
            }
        }

        private void StopGenerationSafely()
        {
            if (_generationCts != null)
            {
                _generationCts.Cancel();
                Progress.CurrentStepDescription = "已收到停止要求：正在回復目前未完成的安全步驟，後續項目不再執行...";
            }
            _notificationService.ShowToast("已要求停止：目前未完成的 Transaction 將回復，後續圖紙、標籤與尺寸不再執行", UiItemStatus.Warning);
        }
    }
}
