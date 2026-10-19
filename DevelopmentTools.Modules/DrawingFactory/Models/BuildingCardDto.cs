using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BIMDrawingFactory.UI.Models
{
    public class BuildingCardDto : INotifyPropertyChanged
    {
        private string _id;
        private string _name;
        private int _completedFloors;
        private int _totalFloors;
        private bool _isSelected;
        private List<FloorChipDto> _floors;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public string Id
        {
            get { return _id; }
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get { return _name; }
            set { _name = value; OnPropertyChanged(); }
        }

        public int CompletedFloors
        {
            get { return _completedFloors; }
            set
            {
                _completedFloors = value;
                OnPropertyChanged();
                OnPropertyChanged("Description");
                OnPropertyChanged("CompletionPercentage");
            }
        }

        public int TotalFloors
        {
            get { return _totalFloors; }
            set
            {
                _totalFloors = value;
                OnPropertyChanged();
                OnPropertyChanged("Description");
                OnPropertyChanged("CompletionPercentage");
            }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get { return string.Format("已完成 {0} / {1} 樓", CompletedFloors, TotalFloors); }
        }

        public double CompletionPercentage
        {
            get { return TotalFloors > 0 ? (double)CompletedFloors / TotalFloors * 100.0 : 0; }
        }

        public List<FloorChipDto> Floors
        {
            get { return _floors; }
            set { _floors = value; OnPropertyChanged(); }
        }

        public BuildingCardDto()
        {
            Id = string.Empty;
            Name = string.Empty;
            Floors = new List<FloorChipDto>();
        }
    }

    public class FloorChipDto : INotifyPropertyChanged
    {
        private string _floorId;
        private string _floorName;
        private FloorStatus _status;
        private bool _isSelected;
        private int _completedSheetsCount;
        private int _totalSheetsCount;
        private string _statusText;
        private bool _isStandardMaster;
        private double _elevationMm;
        private double _heightToNextMm;
        private string _revitLevelName;
        private string _associatedLevelsSummary;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public string FloorId
        {
            get { return _floorId; }
            set { _floorId = value; OnPropertyChanged(); }
        }

        public string FloorName
        {
            get { return _floorName; }
            set { _floorName = value; OnPropertyChanged(); }
        }

        public FloorStatus Status
        {
            get { return _status; }
            set { _status = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public int CompletedSheetsCount
        {
            get { return _completedSheetsCount; }
            set { _completedSheetsCount = value; OnPropertyChanged(); }
        }

        public int TotalSheetsCount
        {
            get { return _totalSheetsCount; }
            set { _totalSheetsCount = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get { return _statusText; }
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsStandardMaster
        {
            get { return _isStandardMaster; }
            set { _isStandardMaster = value; OnPropertyChanged(); }
        }

        public double ElevationMm
        {
            get { return _elevationMm; }
            set { _elevationMm = value; OnPropertyChanged(); }
        }

        public double HeightToNextMm
        {
            get { return _heightToNextMm; }
            set { _heightToNextMm = value; OnPropertyChanged(); }
        }

        public string RevitLevelName
        {
            get { return _revitLevelName; }
            set { _revitLevelName = value; OnPropertyChanged(); }
        }

        public string AssociatedLevelsSummary
        {
            get { return _associatedLevelsSummary; }
            set { _associatedLevelsSummary = value; OnPropertyChanged(); }
        }

        public FloorChipDto()
        {
            FloorId = string.Empty;
            FloorName = string.Empty;
            Status = FloorStatus.NotCreated;
            StatusText = string.Empty;
            RevitLevelName = string.Empty;
            AssociatedLevelsSummary = string.Empty;
        }
    }
}
