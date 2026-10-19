using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class StandardDrawingViewModel : BaseViewModel
    {
        private readonly IRevitDataService _revitService;
        private readonly INotificationService _notificationService;
        private ProjectProfileDto _profile = new ProjectProfileDto();

        public ProjectProfileDto Profile
        {
            get { return _profile; }
            set { SetProperty(ref _profile, value); }
        }

        public ICommand ReanalyzeCommand { get; private set; }
        public ICommand ExportAnalysisCommand { get; private set; }

        public StandardDrawingViewModel(
            IRevitDataService revitService,
            INotificationService notificationService)
        {
            _revitService = revitService;
            _notificationService = notificationService;

            ReanalyzeCommand = new AsyncRelayCommand(async () =>
            {
                await Task.Delay(500);
                _notificationService.ShowToast("標準圖說 (S5) 重新分析完成，特徵與圖紙結構已更新");
            });

            ExportAnalysisCommand = new RelayCommand(() =>
            {
                _notificationService.ShowToast("已匯出標準圖說剖析資料 (JSON/Excel)");
            });
        }

        public async Task InitializeAsync()
        {
            Profile = await _revitService.GetProjectProfileAsync();
        }
    }

    public class HistoryViewModel : BaseViewModel
    {
        private readonly INotificationService _notificationService;
        private readonly IDialogService _dialogService;

        public ObservableCollection<HistoryItemDto> HistoryItems { get; private set; }

        public ICommand DeleteHistoryItemCommand { get; private set; }
        public ICommand ViewReportCommand { get; private set; }

        public HistoryViewModel(INotificationService notificationService, IDialogService dialogService)
        {
            _notificationService = notificationService;
            _dialogService = dialogService;

            HistoryItems = new ObservableCollection<HistoryItemDto>();

            DeleteHistoryItemCommand = new AsyncRelayCommand(async param =>
            {
                var item = param as HistoryItemDto;
                if (item != null)
                {
                    var confirm = await _dialogService.ShowDangerConfirmAsync(
                        "刪除外掛產生物件",
                        string.Format("即將刪除：{0} 張圖紙與 {1} 個視圖。\n\n⚠️ 系統只會刪除 BIM Drawing Factory 建立的內容，人工建立內容不會受影響。", item.SheetsCount, item.ViewsCount));
                    if (confirm)
                    {
                        HistoryItems.Remove(item);
                        _notificationService.ShowToast(string.Format("已安全刪除 {0} 產製之內容", item.Timestamp), UiItemStatus.Warning);
                    }
                }
            });

            ViewReportCommand = new RelayCommand(param =>
            {
                _notificationService.ShowToast("已開啟執行產出報告");
            });

            InitializeMockHistory();
        }

        private void InitializeMockHistory()
        {
            HistoryItems.Add(new HistoryItemDto
            {
                Timestamp = "2026/09/14 10:32",
                BuildingName = "A棟",
                FloorsSummary = "7F",
                SheetsCount = 13,
                ViewsCount = 18,
                Status = "成功",
                Operator = "BIM_Admin"
            });
            HistoryItems.Add(new HistoryItemDto
            {
                Timestamp = "2026/09/10 16:15",
                BuildingName = "A棟",
                FloorsSummary = "2F ~ 6F",
                SheetsCount = 65,
                ViewsCount = 90,
                Status = "成功",
                Operator = "BIM_Admin"
            });
        }
    }

    public class HistoryItemDto
    {
        public string Timestamp { get; set; }
        public string BuildingName { get; set; }
        public string FloorsSummary { get; set; }
        public int SheetsCount { get; set; }
        public int ViewsCount { get; set; }
        public string Status { get; set; }
        public string Operator { get; set; }

        public HistoryItemDto()
        {
            Timestamp = string.Empty;
            BuildingName = string.Empty;
            FloorsSummary = string.Empty;
            Status = "成功";
            Operator = string.Empty;
        }
    }

    public class SettingsViewModel : BaseViewModel
    {
        private readonly INotificationService _notificationService;

        private bool _autoSavePreference = true;
        private bool _enableSafeCancellation = true;
        private string _defaultTitleBlock = "A1 標準圖框 (T1)";

        public bool AutoSavePreference
        {
            get { return _autoSavePreference; }
            set { SetProperty(ref _autoSavePreference, value); }
        }

        public bool EnableSafeCancellation
        {
            get { return _enableSafeCancellation; }
            set { SetProperty(ref _enableSafeCancellation, value); }
        }

        public string DefaultTitleBlock
        {
            get { return _defaultTitleBlock; }
            set { SetProperty(ref _defaultTitleBlock, value); }
        }

        public ICommand SaveSettingsCommand { get; private set; }

        public SettingsViewModel(INotificationService notificationService)
        {
            _notificationService = notificationService;
            SaveSettingsCommand = new RelayCommand(() =>
            {
                _notificationService.ShowToast("使用者偏好設定已儲存");
            });
        }
    }
}
