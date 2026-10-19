using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class MappingViewModel : BaseViewModel
    {
        private readonly IRevitDataService _revitService;
        private readonly INotificationService _notificationService;

        private ResourceMappingType _selectedType = ResourceMappingType.Level;
        private string _autoMappingSummary = "系統找到 4 個對應：3 個高可信度對齊，1 個需人工確認。";

        public ObservableCollection<ResourceMappingDto> Mappings { get; private set; }

        public ResourceMappingType SelectedType
        {
            get { return _selectedType; }
            set
            {
                if (SetProperty(ref _selectedType, value))
                {
                    var task = LoadMappingsAsync(value);
                }
            }
        }

        public string AutoMappingSummary
        {
            get { return _autoMappingSummary; }
            set { SetProperty(ref _autoMappingSummary, value); }
        }

        public ICommand SelectTabCommand { get; private set; }
        public ICommand RunAutoMappingCommand { get; private set; }
        public ICommand SaveMappingsCommand { get; private set; }

        public MappingViewModel(
            IRevitDataService revitService,
            INotificationService notificationService)
        {
            _revitService = revitService;
            _notificationService = notificationService;

            Mappings = new ObservableCollection<ResourceMappingDto>();

            SelectTabCommand = new RelayCommand(param =>
            {
                if (param is ResourceMappingType)
                {
                    SelectedType = (ResourceMappingType)param;
                }
            });

            RunAutoMappingCommand = new AsyncRelayCommand(async () =>
            {
                var results = await _revitService.RunAutoMappingAsync(SelectedType);
                Mappings.Clear();
                foreach (var m in results)
                {
                    Mappings.Add(m);
                }
                AutoMappingSummary = string.Format("自動對應分析完成：共比對 {0} 個資源項目。", Mappings.Count);
                _notificationService.ShowToast("智慧自動比對已完成，請檢視高可信與待確認項目");
            });

            SaveMappingsCommand = new AsyncRelayCommand(async () =>
            {
                await _revitService.SaveResourceMappingsAsync(new List<ResourceMappingDto>(Mappings));
                _notificationService.ShowToast("對應設定已成功寫入 Project Profile");
            });
        }

        public async Task LoadMappingsAsync(ResourceMappingType type)
        {
            var list = await _revitService.GetResourceMappingsAsync(type);
            Mappings.Clear();
            foreach (var m in list)
            {
                Mappings.Add(m);
            }
        }
    }
}
