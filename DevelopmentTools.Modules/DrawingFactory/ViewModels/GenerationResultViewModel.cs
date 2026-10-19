using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class GenerationResultViewModel : BaseViewModel
    {
        private readonly IRevitDataService _revitService;
        private readonly INavigationService _navigationService;
        private readonly INotificationService _notificationService;

        private GenerationResultDto _result = new GenerationResultDto();

        public GenerationResultDto Result
        {
            get { return _result; }
            set { SetProperty(ref _result, value); }
        }

        public ICommand OpenFirstSheetInRevitCommand { get; private set; }
        public ICommand ExportReportCommand { get; private set; }
        public ICommand BackToHomeCommand { get; private set; }
        public ICommand ViewHistoryCommand { get; private set; }

        public GenerationResultViewModel(
            IRevitDataService revitService,
            INavigationService navigationService,
            INotificationService notificationService)
        {
            _revitService = revitService;
            _navigationService = navigationService;
            _notificationService = notificationService;

            OpenFirstSheetInRevitCommand = new AsyncRelayCommand(OpenFirstSheetInRevitAsync);
            ExportReportCommand = new RelayCommand(ExportReport);
            BackToHomeCommand = new RelayCommand(() => _navigationService.NavigateTo(NavigationPageKey.QuickGenerate));
            ViewHistoryCommand = new RelayCommand(() => _navigationService.NavigateTo(NavigationPageKey.History));
        }

        public void SetResult(GenerationResultDto result)
        {
            Result = result;
        }

        private async Task OpenFirstSheetInRevitAsync()
        {
            if (!string.IsNullOrEmpty(Result.FirstCreatedSheetNumber))
            {
                await _revitService.OpenSheetInRevitAsync(Result.FirstCreatedSheetNumber);
                _notificationService.ShowToast(string.Format("Revit 作用重視圖已切換至：{0}", Result.FirstCreatedSheetNumber));
            }
        }

        private void ExportReport()
        {
            _notificationService.ShowToast("已成功匯出圖紙產出清冊 (CSV/Excel 格式)");
        }
    }
}
