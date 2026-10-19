using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class DrawingProgressViewModel : BaseViewModel
    {
        private readonly IRevitDataService _revitService;
        private readonly INavigationService _navigationService;
        private readonly INotificationService _notificationService;

        private BuildingCardDto _selectedBuilding;
        private FloorChipDto _selectedFloor;

        public ObservableCollection<BuildingCardDto> Buildings { get; private set; }
        public ObservableCollection<FloorChipDto> Floors { get; private set; }

        public BuildingCardDto SelectedBuilding
        {
            get { return _selectedBuilding; }
            set
            {
                if (SetProperty(ref _selectedBuilding, value))
                {
                    OnBuildingChanged(value);
                }
            }
        }

        public FloorChipDto SelectedFloor
        {
            get { return _selectedFloor; }
            set { SetProperty(ref _selectedFloor, value); }
        }

        public ICommand SelectFloorCommand { get; private set; }
        public ICommand QuickGenerateFloorCommand { get; private set; }
        public ICommand RefreshProgressCommand { get; private set; }

        public DrawingProgressViewModel(
            IRevitDataService revitService,
            INavigationService navigationService,
            INotificationService notificationService)
        {
            _revitService = revitService;
            _navigationService = navigationService;
            _notificationService = notificationService;

            Buildings = new ObservableCollection<BuildingCardDto>();
            Floors = new ObservableCollection<FloorChipDto>();

            SelectFloorCommand = new RelayCommand(param =>
            {
                var chip = param as FloorChipDto;
                if (chip != null)
                {
                    SelectedFloor = chip;
                }
            });

            QuickGenerateFloorCommand = new RelayCommand(param =>
            {
                var chip = param as FloorChipDto;
                if (chip != null && SelectedBuilding != null)
                {
                    var req = new GenerateRequestDto
                    {
                        BuildingId = SelectedBuilding.Id,
                        BuildingName = SelectedBuilding.Name,
                        SelectedFloorIds = new List<string> { chip.FloorId },
                        SelectedFloorNames = new List<string> { chip.FloorName },
                        PackageType = SheetCreationType.StandardFloorPackage
                    };
                    _navigationService.NavigateTo(NavigationPageKey.Preflight, req);
                }
            });

            RefreshProgressCommand = new AsyncRelayCommand(InitializeAsync);
        }

        public async Task InitializeAsync()
        {
            var buildings = await _revitService.GetBuildingsAsync();
            Buildings.Clear();
            foreach (var b in buildings)
            {
                Buildings.Add(b);
            }

            var defaultB = Buildings.FirstOrDefault();
            if (defaultB != null)
            {
                SelectedBuilding = defaultB;
            }
        }

        private void OnBuildingChanged(BuildingCardDto building)
        {
            if (building == null) return;
            Floors.Clear();
            foreach (var f in building.Floors)
            {
                Floors.Add(f);
            }
            SelectedFloor = Floors.FirstOrDefault(f => f.FloorName == "7F") ?? Floors.FirstOrDefault();
        }
    }
}
