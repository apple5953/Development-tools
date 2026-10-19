using System.Threading.Tasks;
using System.Windows.Input;
using BIMDrawingFactory.UI.Models;
using BIMDrawingFactory.UI.Services;

namespace BIMDrawingFactory.UI.ViewModels
{
    public class InspectorViewModel : BaseViewModel
    {
        private readonly IRevitDataService _revitService;
        private readonly INotificationService _notificationService;

        private SheetPreviewItemDto _selectedSheet;
        private bool _isVisible = true;

        public SheetPreviewItemDto SelectedSheet
        {
            get { return _selectedSheet; }
            set
            {
                if (SetProperty(ref _selectedSheet, value))
                {
                    OnPropertyChanged("HasSelection");
                }
            }
        }

        public bool HasSelection
        {
            get { return _selectedSheet != null; }
        }

        public bool IsVisible
        {
            get { return _isVisible; }
            set { SetProperty(ref _isVisible, value); }
        }

        public ICommand OpenStandardSheetCommand { get; private set; }
        public ICommand OpenSourceViewCommand { get; private set; }
        public ICommand CloseInspectorCommand { get; private set; }

        public InspectorViewModel(IRevitDataService revitService, INotificationService notificationService)
        {
            _revitService = revitService;
            _notificationService = notificationService;

            OpenStandardSheetCommand = new AsyncRelayCommand(OpenStandardSheetAsync);
            OpenSourceViewCommand = new AsyncRelayCommand(OpenSourceViewAsync);
            CloseInspectorCommand = new RelayCommand(() => IsVisible = false);
        }

        private async Task OpenStandardSheetAsync()
        {
            if (SelectedSheet != null)
            {
                await _revitService.OpenSheetInRevitAsync(SelectedSheet.SourceStandardSheet);
                _notificationService.ShowToast(string.Format("已在 Revit 切換至標準圖紙：{0}", SelectedSheet.SourceStandardSheet));
            }
        }

        private async Task OpenSourceViewAsync()
        {
            if (SelectedSheet != null)
            {
                await _revitService.OpenViewInRevitAsync(SelectedSheet.SourceStandardView);
                _notificationService.ShowToast(string.Format("已在 Revit 切換至來源視圖：{0}", SelectedSheet.SourceStandardView));
            }
        }
    }
}
