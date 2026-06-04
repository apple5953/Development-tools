using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.SheetTools.SheetViewPlacer
{
    /// <summary>
    /// 已放置在圖紙中的視圖/明細表 ViewModel
    /// </summary>
    public class PlacedViewItemViewModel : INotifyPropertyChanged
    {
        public ElementId ViewId { get; set; }
        public ElementId ElementId { get; set; } // Viewport 或 ScheduleSheetInstance
        public string Name { get; set; }
        public string Type { get; set; } // "Viewport" 或 "Schedule"

        private double _xMm;
        public double XMm
        {
            get => _xMm;
            set { _xMm = value; OnPropertyChanged(); }
        }

        private double _yMm;
        public double YMm
        {
            get => _yMm;
            set { _yMm = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
