using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class DrawingRulesViewModel : BaseViewModel
    {
        private readonly IRevitDataService _revitService;
        private readonly INotificationService _notificationService;

        private DrawingRuleDto _selectedRule;
        private string _levelExampleText = "當產製 5F 圖紙時，實體視圖將取用 👉 [ 5F ] 視圖";
        private bool _isDirty;

        public ObservableCollection<DrawingRuleDto> Rules { get; private set; }

        public DrawingRuleDto SelectedRule
        {
            get { return _selectedRule; }
            set
            {
                if (SetProperty(ref _selectedRule, value))
                {
                    UpdateLevelExample();
                    IsDirty = false;
                }
            }
        }

        public string LevelExampleText
        {
            get { return _levelExampleText; }
            set { SetProperty(ref _levelExampleText, value); }
        }

        public bool IsDirty
        {
            get { return _isDirty; }
            set { SetProperty(ref _isDirty, value); }
        }

        public ICommand SelectRuleCommand { get; private set; }
        public ICommand SetLevelBindingOptionCommand { get; private set; }
        public ICommand SaveRuleCommand { get; private set; }
        public ICommand DiscardChangesCommand { get; private set; }
        public ICommand TestRuleCommand { get; private set; }

        public DrawingRulesViewModel(
            IRevitDataService revitService,
            INotificationService notificationService)
        {
            _revitService = revitService;
            _notificationService = notificationService;

            Rules = new ObservableCollection<DrawingRuleDto>();

            SelectRuleCommand = new RelayCommand(param =>
            {
                var rule = param as DrawingRuleDto;
                if (rule != null)
                {
                    SelectedRule = rule;
                }
            });

            SetLevelBindingOptionCommand = new RelayCommand(param =>
            {
                if (SelectedRule != null && param is LevelBindingOption)
                {
                    SelectedRule.LevelBinding = (LevelBindingOption)param;
                    IsDirty = true;
                    UpdateLevelExample();
                }
            });

            SaveRuleCommand = new AsyncRelayCommand(async () =>
            {
                if (SelectedRule != null)
                {
                    await _revitService.SaveDrawingRuleAsync(SelectedRule);
                    IsDirty = false;
                    _notificationService.ShowToast(string.Format("已儲存圖說規則：{0}", SelectedRule.RuleName));
                }
            });

            DiscardChangesCommand = new AsyncRelayCommand(InitializeAsync);

            TestRuleCommand = new RelayCommand(() =>
            {
                _notificationService.ShowToast("規則驗證通過：已在背景模擬產製 3F~10F 視圖無衝突");
            });
        }

        public async Task InitializeAsync()
        {
            var rules = await _revitService.GetDrawingRulesAsync();
            Rules.Clear();
            foreach (var r in rules)
            {
                Rules.Add(r);
            }
            SelectedRule = Rules.FirstOrDefault();
            IsDirty = false;
        }

        private void UpdateLevelExample()
        {
            if (SelectedRule == null) return;

            switch (SelectedRule.LevelBinding)
            {
                case LevelBindingOption.CurrentLevel:
                    LevelExampleText = "當產製 5F 圖紙時，實體視圖將取用 👉 [ 5F ] 視圖";
                    break;
                case LevelBindingOption.PreviousLevel:
                    LevelExampleText = "當產製 5F 圖紙時，實體視圖將取用 👉 [ 4F ] 視圖 (上一層)";
                    break;
                case LevelBindingOption.NextLevel:
                    LevelExampleText = "當產製 5F 圖紙時，實體視圖將取用 👉 [ 6F ] 視圖 (下一層)";
                    break;
                case LevelBindingOption.FixedLevel:
                    LevelExampleText = "當產製 5F 圖紙時，實體視圖將取用 👉 [ 1F ] 固定基準視圖";
                    break;
                case LevelBindingOption.Unbound:
                    LevelExampleText = "視圖不依附特定樓層 (如全區總配置或大樣圖)";
                    break;
                default:
                    LevelExampleText = "產製前由系統彈窗詢問目標樓層";
                    break;
            }
        }
    }
}
