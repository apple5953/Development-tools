using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class PreflightViewModel : BaseViewModel
    {
        private readonly IRevitDataService _revitService;
        private readonly INavigationService _navigationService;
        private readonly INotificationService _notificationService;

        private GenerateRequestDto _currentRequest;
        private int _successCount;
        private int _warningCount;
        private int _errorCount;
        private bool _canProceedToPreview = true;
        private string _targetSummary = string.Empty;

        public ObservableCollection<IssueItemDto> Issues { get; private set; }

        public int SuccessCount
        {
            get { return _successCount; }
            set { SetProperty(ref _successCount, value); }
        }

        public int WarningCount
        {
            get { return _warningCount; }
            set { SetProperty(ref _warningCount, value); }
        }

        public int ErrorCount
        {
            get { return _errorCount; }
            set
            {
                if (SetProperty(ref _errorCount, value))
                {
                    CanProceedToPreview = (value == 0);
                }
            }
        }

        public bool CanProceedToPreview
        {
            get { return _canProceedToPreview; }
            set { SetProperty(ref _canProceedToPreview, value); }
        }

        public string TargetSummary
        {
            get { return _targetSummary; }
            set { SetProperty(ref _targetSummary, value); }
        }

        public ICommand ProceedToPreviewCommand { get; private set; }
        public ICommand GoBackCommand { get; private set; }
        public ICommand AutoFixAllCommand { get; private set; }
        public ICommand RecheckCommand { get; private set; }

        public PreflightViewModel(
            IRevitDataService revitService,
            INavigationService navigationService,
            INotificationService notificationService)
        {
            _revitService = revitService;
            _navigationService = navigationService;
            _notificationService = notificationService;

            Issues = new ObservableCollection<IssueItemDto>();

            ProceedToPreviewCommand = new RelayCommand(() =>
            {
                if (_currentRequest != null)
                {
                    _navigationService.NavigateTo(NavigationPageKey.Preview, _currentRequest);
                }
            }, () => CanProceedToPreview);

            GoBackCommand = new RelayCommand(() => _navigationService.GoBack());

            AutoFixAllCommand = new RelayCommand(() =>
            {
                foreach (var issue in Issues)
                {
                    issue.SelectedResolution = issue.ResolutionOptions.FirstOrDefault() ?? "已採用建議";
                }
                WarningCount = 0;
                SuccessCount += Issues.Count;
                _notificationService.ShowToast("已自動套用所有建議設定");
            });

            RecheckCommand = new AsyncRelayCommand(async () =>
            {
                if (_currentRequest != null)
                {
                    await LoadPreflightDataAsync(_currentRequest);
                    _notificationService.ShowToast("建圖前檢查已重新整理完成");
                }
            });
        }

        public async Task LoadPreflightDataAsync(GenerateRequestDto request)
        {
            _currentRequest = request;
            var floorsText = string.Join(", ", request.SelectedFloorNames);
            TargetSummary = string.Format("{0} {1}", request.BuildingName, floorsText);

            var issues = await _revitService.RunPreflightCheckAsync(request);
            var previewItems = await _revitService.GetPreviewItemsAsync(request);
            Issues.Clear();
            foreach (var i in issues)
            {
                Issues.Add(i);
            }

            WarningCount = issues.Count(i => i.Severity == IssueSeverity.Warning);
            ErrorCount = issues.Count(i => i.Severity == IssueSeverity.Error);
            // 以實際解析出的圖紙計數，不再使用「每層固定 13 張」的錯誤假設。
            // Info 表示建立時可自動補齊，Warning 也不應扣除可建立數。
            SuccessCount = ErrorCount > 0 ? 0 : previewItems.Count(p => !p.TargetSheetExists && p.Status != UiItemStatus.Error);

            CanProceedToPreview = (ErrorCount == 0);
        }
    }
}
