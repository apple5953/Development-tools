using System.Windows.Controls;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.ViewModels;

namespace BIMDrawingFactory.UI.Views
{
    public partial class QuickGenerateView : UserControl
    {
        public QuickGenerateView()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }

        private void OnBuildingCardSelected(object sender, BuildingCardDto building)
        {
            var vm = DataContext as QuickGenerateViewModel;
            if (vm != null)
            {
                vm.SelectedBuilding = building;
            }
        }

        private void OnFloorSelectionToggled(object sender, FloorChipDto chip)
        {
            var vm = DataContext as QuickGenerateViewModel;
            if (vm != null)
            {
                vm.ToggleFloorSelectionCommand.Execute(chip);
            }
        }
    }
}
