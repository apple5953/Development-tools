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

        /// <summary>
        /// 樓層頭標高 → Revit 內部座標的換算差值 (mm)。
        /// 內部 Z = 樓層頭標高 + InternalOffsetMm。高程基準為專案基準點/測量點且其高程不為 0 時，此值不為 0。
        /// </summary>
        public double InternalOffsetMm
        {
            get { return _projectElevationMm - _elevationMm; }
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
                // 顯示與 Revit 樓層頭相同的標高 (Level.Elevation)，計算相機 Z 時另用 InternalOffsetMm 換算
                return string.Format("{0} (FL {1:+0.00;-0.00;0.00}m) [{2} 個視角]", _name, _elevationMm / 1000.0, _cameraCount);
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