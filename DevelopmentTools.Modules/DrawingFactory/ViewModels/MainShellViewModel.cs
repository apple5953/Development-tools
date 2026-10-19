using System;
using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class MainShellViewModel : BaseViewModel
    {
        private readonly INavigationService _navigationService;
        private readonly INotificationService _notificationService;
        private readonly IRevitDataService _revitService;

        private BaseViewModel _currentPageViewModel;
        private NavigationPageKey _selectedNavKey = NavigationPageKey.QuickGenerate;
        private string _projectName = "南山科技園區二期新建工程";
        private string _revitDocName = "NS2_StandardMaster_2024.rvt";
        private string _standardGroup = "S5";
        private string _statusMessage = "就緒 (Revit 2024 API Context Connected)";
        private string _toastMessage = string.Empty;
        private bool _isToastVisible;
        private UiItemStatus _toastStatus = UiItemStatus.Success;

        public QuickGenerateViewModel QuickGenerate { get; private set; }
        public DrawingProgressViewModel DrawingProgress { get; private set; }
        public DrawingRulesViewModel DrawingRules { get; private set; }
        public MappingViewModel Mapping { get; private set; }
        public PreflightViewModel Preflight { get; private set; }
        public PreviewViewModel Preview { get; private set; }
        public GenerationResultViewModel GenerationResult { get; private set; }
        public StandardDrawingViewModel StandardDrawing { get; private set; }
        public HistoryViewModel History { get; private set; }
        public SettingsViewModel Settings { get; private set; }
        public SetupWizardViewModel SetupWizard { get; private set; }
        public InspectorViewModel Inspector { get; private set; }

        public BaseViewModel CurrentPageViewModel
        {
            get { return _currentPageViewModel; }
            set { SetProperty(ref _currentPageViewModel, value); }
        }

        public NavigationPageKey SelectedNavKey
        {
            get { return _selectedNavKey; }
            set
            {
                if (SetProperty(ref _selectedNavKey, value))
                {
                    OnPropertyChanged("IsQuickGenerateSelected");
                    OnPropertyChanged("IsDrawingProgressSelected");
                    OnPropertyChanged("IsStandardDrawingSelected");
                    OnPropertyChanged("IsDrawingRulesSelected");
                    OnPropertyChanged("IsMappingSelected");
                    OnPropertyChanged("IsHistorySelected");
                    OnPropertyChanged("IsSettingsSelected");
                }
            }
        }

        public bool IsQuickGenerateSelected { get { return SelectedNavKey == NavigationPageKey.QuickGenerate; } }
        public bool IsDrawingProgressSelected { get { return SelectedNavKey == NavigationPageKey.DrawingProgress; } }
        public bool IsStandardDrawingSelected { get { return SelectedNavKey == NavigationPageKey.StandardDrawing; } }
        public bool IsDrawingRulesSelected { get { return SelectedNavKey == NavigationPageKey.DrawingRules; } }
        public bool IsMappingSelected { get { return SelectedNavKey == NavigationPageKey.Mapping; } }
        public bool IsHistorySelected { get { return SelectedNavKey == NavigationPageKey.History; } }
        public bool IsSettingsSelected { get { return SelectedNavKey == NavigationPageKey.Settings; } }

        public string ProjectName
        {
            get { return _projectName; }
            set { SetProperty(ref _projectName, value); }
        }

        public string RevitDocName
        {
            get { return _revitDocName; }
            set { SetProperty(ref _revitDocName, value); }
        }

        public string StandardGroup
        {
            get { return _standardGroup; }
            set { SetProperty(ref _standardGroup, value); }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value); }
        }

        public string ToastMessage
        {
            get { return _toastMessage; }
            set { SetProperty(ref _toastMessage, value); }
        }

        public bool IsToastVisible
        {
            get { return _isToastVisible; }
            set { SetProperty(ref _isToastVisible, value); }
        }

        public UiItemStatus ToastStatus
        {
            get { return _toastStatus; }
            set { SetProperty(ref _toastStatus, value); }
        }

        public ICommand NavigateCommand { get; private set; }
        public ICommand ReloadCommand { get; private set; }
        public ICommand HelpCommand { get; private set; }
        public ICommand StartWizardCommand { get; private set; }

        public MainShellViewModel(
            INavigationService navigationService,
            INotificationService notificationService,
            IRevitDataService revitService,
            IDialogService dialogService)
        {
            _navigationService = navigationService;
            _notificationService = notificationService;
            _revitService = revitService;

            Inspector = new InspectorViewModel(revitService, notificationService);

            QuickGenerate = new QuickGenerateViewModel(revitService, navigationService, notificationService);
            DrawingProgress = new DrawingProgressViewModel(revitService, navigationService, notificationService);
            DrawingRules = new DrawingRulesViewModel(revitService, notificationService);
            Mapping = new MappingViewModel(revitService, notificationService);
            Preflight = new PreflightViewModel(revitService, navigationService, notificationService);
            Preview = new PreviewViewModel(revitService, navigationService, notificationService, dialogService, Inspector);
            GenerationResult = new GenerationResultViewModel(revitService, navigationService, notificationService);
            StandardDrawing = new StandardDrawingViewModel(revitService, notificationService);
            History = new HistoryViewModel(notificationService, dialogService);
            Settings = new SettingsViewModel(notificationService);
            SetupWizard = new SetupWizardViewModel(navigationService, notificationService);

            _currentPageViewModel = QuickGenerate;

            NavigateCommand = new RelayCommand(param =>
            {
                if (param is NavigationPageKey)
                {
                    _navigationService.NavigateTo((NavigationPageKey)param);
                }
                else if (param is string)
                {
                    NavigationPageKey parsedKey;
                    if (Enum.TryParse<NavigationPageKey>((string)param, out parsedKey))
                    {
                        _navigationService.NavigateTo(parsedKey);
                    }
                }
            });

            ReloadCommand = new AsyncRelayCommand(async () =>
            {
                _revitService.InvalidateAnalysisCache();
                await InitializeAsync();
                _notificationService.ShowToast("專案設定與模型特徵已重新載入");
            });

            HelpCommand = new RelayCommand(() =>
            {
                _notificationService.ShowToast("BIM Drawing Factory 說明：請依照 棟別 → 樓層 → 檢查 → 預覽 → 建立 之 5 步流程出圖");
            });

            StartWizardCommand = new RelayCommand(() =>
            {
                _navigationService.NavigateTo(NavigationPageKey.SetupWizard);
            });

            _navigationService.PageChanged += OnPageChanged;
            _notificationService.ToastRequested += OnToastRequested;
        }

        public async Task InitializeAsync()
        {
            try
            {
                var profile = await _revitService.GetProjectProfileAsync();
                if (profile != null)
                {
                    ProjectName = !string.IsNullOrEmpty(profile.ProjectName) ? profile.ProjectName : "Revit Project";
                    RevitDocName = !string.IsNullOrEmpty(profile.SourceDocumentName) ? profile.SourceDocumentName : profile.ProjectName;
                    StandardGroup = !string.IsNullOrEmpty(profile.StandardDrawingGroup) ? profile.StandardDrawingGroup : "標準圖說組";
                    StatusMessage = string.Format("就緒 - {0}", profile.StatusSummary);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[MainShellViewModel Profile Error] {0}", ex.Message));
            }

            await QuickGenerate.InitializeAsync();
        }

        private async void OnPageChanged(NavigationPageKey pageKey, object param)
        {
            try
            {
                SelectedNavKey = pageKey;

                switch (pageKey)
                {
                    case NavigationPageKey.QuickGenerate:
                        CurrentPageViewModel = QuickGenerate;
                        await QuickGenerate.InitializeAsync();
                        break;
                    case NavigationPageKey.DrawingProgress:
                        CurrentPageViewModel = DrawingProgress;
                        await DrawingProgress.InitializeAsync();
                        break;
                    case NavigationPageKey.StandardDrawing:
                        CurrentPageViewModel = StandardDrawing;
                        await StandardDrawing.InitializeAsync();
                        break;
                    case NavigationPageKey.DrawingRules:
                        CurrentPageViewModel = DrawingRules;
                        await DrawingRules.InitializeAsync();
                        break;
                    case NavigationPageKey.Mapping:
                        CurrentPageViewModel = Mapping;
                        await Mapping.LoadMappingsAsync(ResourceMappingType.Level);
                        break;
                    case NavigationPageKey.Preflight:
                        var req = param as GenerateRequestDto;
                        if (req != null)
                        {
                            await Preflight.LoadPreflightDataAsync(req);
                        }
                        CurrentPageViewModel = Preflight;
                        break;
                    case NavigationPageKey.Preview:
                        var pReq = param as GenerateRequestDto;
                        if (pReq != null)
                        {
                            await Preview.LoadPreviewDataAsync(pReq);
                        }
                        CurrentPageViewModel = Preview;
                        break;
                    case NavigationPageKey.GenerationResult:
                        var res = param as GenerationResultDto;
                        if (res != null)
                        {
                            GenerationResult.SetResult(res);
                        }
                        CurrentPageViewModel = GenerationResult;
                        break;
                    case NavigationPageKey.History:
                        CurrentPageViewModel = History;
                        break;
                    case NavigationPageKey.Settings:
                        CurrentPageViewModel = Settings;
                        break;
                    case NavigationPageKey.SetupWizard:
                        CurrentPageViewModel = SetupWizard;
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[OnPageChanged Error] {0}", ex));
            }
        }

        private async void OnToastRequested(string message, UiItemStatus status)
        {
            try
            {
                ToastMessage = message;
                ToastStatus = status;
                IsToastVisible = true;
                await Task.Delay(3200);
                IsToastVisible = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[OnToastRequested Error] {0}", ex));
            }
        }
    }
}
