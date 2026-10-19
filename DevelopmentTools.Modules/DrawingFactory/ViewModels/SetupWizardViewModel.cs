using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class SetupWizardViewModel : BaseViewModel
    {
        private readonly INavigationService _navigationService;
        private readonly INotificationService _notificationService;

        private int _currentStep = 1;
        private int _totalSteps = 8;
        private string _stepTitle = "步驟 1：選取標準來源模型";

        public int CurrentStep
        {
            get { return _currentStep; }
            set
            {
                if (SetProperty(ref _currentStep, value))
                {
                    UpdateStepTitle();
                    OnPropertyChanged("CanGoPrevious");
                    OnPropertyChanged("IsLastStep");
                }
            }
        }

        public int TotalSteps
        {
            get { return _totalSteps; }
        }

        public string StepTitle
        {
            get { return _stepTitle; }
            set { SetProperty(ref _stepTitle, value); }
        }

        public bool CanGoPrevious
        {
            get { return CurrentStep > 1; }
        }

        public bool IsLastStep
        {
            get { return CurrentStep == TotalSteps; }
        }

        public ICommand NextStepCommand { get; private set; }
        public ICommand PreviousStepCommand { get; private set; }
        public ICommand CancelWizardCommand { get; private set; }
        public ICommand FinishWizardCommand { get; private set; }

        public SetupWizardViewModel(
            INavigationService navigationService,
            INotificationService notificationService)
        {
            _navigationService = navigationService;
            _notificationService = notificationService;

            NextStepCommand = new RelayCommand(() =>
            {
                if (CurrentStep < TotalSteps)
                {
                    CurrentStep++;
                }
            });

            PreviousStepCommand = new RelayCommand(() =>
            {
                if (CurrentStep > 1)
                {
                    CurrentStep--;
                }
            });

            CancelWizardCommand = new RelayCommand(() =>
            {
                _navigationService.NavigateTo(NavigationPageKey.QuickGenerate);
            });

            FinishWizardCommand = new RelayCommand(() =>
            {
                _notificationService.ShowToast("專案標準設定完成！即刻進入日常快速出圖模式。");
                _navigationService.NavigateTo(NavigationPageKey.QuickGenerate);
            });
        }

        private void UpdateStepTitle()
        {
            switch (CurrentStep)
            {
                case 1: StepTitle = "步驟 1：選取標準來源模型"; break;
                case 2: StepTitle = "步驟 2：選取標準圖紙群組 (例如 S5)"; break;
                case 3: StepTitle = "步驟 3：智慧剖析與特徵比對"; break;
                case 4: StepTitle = "步驟 4：確認圖說分類與規則"; break;
                case 5: StepTitle = "步驟 5：確認棟別定義"; break;
                case 6: StepTitle = "步驟 6：樓層對應確認 (Auto-mapping)"; break;
                case 7: StepTitle = "步驟 7：範圍框與樣板對應"; break;
                case 8: StepTitle = "步驟 8：專案標準設定摘要與完成"; break;
                default: StepTitle = string.Format("步驟 {0}", CurrentStep); break;
            }
        }
    }
}
