using System.ComponentModel;
using Autodesk.Revit.DB;

namespace RevitPerspectiveMinimap.Models
{
    public class LevelInfoModel : INotifyPropertyChanged
    {
        private ElementId _levelId;
        private string _name;
        private double _elevationFeet;
        private double _elevationMm;
        private double _projectElevationFeet;
        private double _projectElevationMm;
        private int _cameraCount;

        public ElementId LevelId
        {
            get { return _levelId; }
            set { _levelId = value; OnPropertyChanged("LevelId"); }
        }

        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                OnPropertyChanged("Name");
                OnPropertyChanged("DisplayName");
            }
        }

        public double ElevationFeet
        {
            get { return _elevationFeet; }
            set
            {
                _elevationFeet = value;
                _elevationMm = value * 304.8;
                OnPropertyChanged("ElevationFeet");
                OnPropertyChanged("ElevationMm");
                OnPropertyChanged("DisplayName");
            }
        }

        public double ElevationMm
        {
            get { return _elevationMm; }
            set
            {
                _elevationMm = value;
                _elevationFeet = value / 304.8;
                OnPropertyChanged("ElevationMm");
                OnPropertyChanged("ElevationFeet");
                OnPropertyChanged("DisplayName");
            }
        }

        public double ProjectElevationFeet
        {
            get { return _projectElevationFeet; }
            set
            {
                _projectElevationFeet = value;
                _projectElevationMm = value * 304.8;
                OnPropertyChanged("ProjectElevationFeet");
                OnPropertyChanged("ProjectElevationMm");
                OnPropertyChanged("DisplayName");
            }
        }

        public double ProjectElevationMm
        {
            get { return _projectElevationMm; }
            set
            {
                _projectElevationMm = value;
                _projectElevationFeet = value / 304.8;
                OnPropertyChanged("ProjectElevationMm");
                OnPropertyChanged("ProjectElevationFeet");
                OnPropertyChanged("DisplayName");
            }
        }

        public int CameraCount
        {
            get { return _cameraCount; }
            set
            {
                _cameraCount = value;
                OnPropertyChanged("CameraCount");
                OnPropertyChanged("DisplayName");
            }
        }

        public string DisplayName
        {
            get
            {
                double displayElevMm = (_projectElevationMm != 0 || _elevationMm == 0) ? _projectElevationMm : _elevationMm;
                return string.Format("{0} (FL {1:+0.00;-0.00;0.00}m) [{2} 個視角]", _name, displayElevMm / 1000.0, _cameraCount);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}