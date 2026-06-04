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
            set 
            { 
                _xMm = value; 
                OnPropertyChanged(); 
                UpdateCanvasCoordinates();
            }
        }

        private double _yMm;
        public double YMm
        {
            get => _yMm;
            set 
            { 
                _yMm = value; 
                OnPropertyChanged(); 
                UpdateCanvasCoordinates();
            }
        }

        private double _widthMm;
        public double WidthMm
        {
            get => _widthMm;
            set { _widthMm = value; OnPropertyChanged(); UpdateCanvasCoordinates(); }
        }

        private double _heightMm;
        public double HeightMm
        {
            get => _heightMm;
            set { _heightMm = value; OnPropertyChanged(); UpdateCanvasCoordinates(); }
        }

        private double _sheetWidth;
        public double SheetWidth
        {
            get => _sheetWidth;
            set { _sheetWidth = value; OnPropertyChanged(); UpdateCanvasCoordinates(); }
        }

        private double _sheetHeight;
        public double SheetHeight
        {
            get => _sheetHeight;
            set { _sheetHeight = value; OnPropertyChanged(); UpdateCanvasCoordinates(); }
        }

        private double _canvasX;
        public double CanvasX
        {
            get => _canvasX;
            set { _canvasX = value; OnPropertyChanged(); }
        }

        private double _canvasY;
        public double CanvasY
        {
            get => _canvasY;
            set { _canvasY = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public void UpdateCanvasCoordinates()
        {
            CanvasX = XMm - WidthMm / 2;
            CanvasY = SheetHeight - (YMm + HeightMm / 2);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
