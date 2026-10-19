using System;
using System.ComponentModel;
using Autodesk.Revit.DB;

namespace RevitPerspectiveMinimap.Models
{
    public class PerspectiveCameraModel : INotifyPropertyChanged
    {
        private ElementId _viewId;
        private string _viewName;
        private XYZ _eyePosition;
        private XYZ _targetPosition;
        private XYZ _forwardDirection;
        private XYZ _upDirection;
        private double _eyeHeightAboveFloorMm;
        private double _absoluteElevationMm;
        private double _fovDegrees = 65.0;
        private ElementId _associatedLevelId;
        private string _associatedLevelName;
        private string _detectedSource;
        private bool _isSelected;
        private bool _isActive;
        private string _colorHex = "#2196F3";

        public ElementId ViewId
        {
            get { return _viewId; }
            set { _viewId = value; OnPropertyChanged("ViewId"); }
        }

        public string ViewName
        {
            get { return _viewName; }
            set { _viewName = value; OnPropertyChanged("ViewName"); }
        }

        public XYZ EyePosition
        {
            get { return _eyePosition; }
            set { _eyePosition = value; OnPropertyChanged("EyePosition"); }
        }

        public XYZ TargetPosition
        {
            get { return _targetPosition; }
            set { _targetPosition = value; OnPropertyChanged("TargetPosition"); }
        }

        public XYZ ForwardDirection
        {
            get { return _forwardDirection; }
            set { _forwardDirection = value; OnPropertyChanged("ForwardDirection"); }
        }

        public XYZ UpDirection
        {
            get { return _upDirection; }
            set { _upDirection = value; OnPropertyChanged("UpDirection"); }
        }

        public double EyeHeightAboveFloorMm
        {
            get { return _eyeHeightAboveFloorMm; }
            set
            {
                _eyeHeightAboveFloorMm = value;
                OnPropertyChanged("EyeHeightAboveFloorMm");
                OnPropertyChanged("EyeHeightDisplay");
            }
        }

        public double AbsoluteElevationMm
        {
            get { return _absoluteElevationMm; }
            set
            {
                _absoluteElevationMm = value;
                OnPropertyChanged("AbsoluteElevationMm");
                OnPropertyChanged("AbsoluteElevationDisplay");
            }
        }

        public string EyeHeightDisplay
        {
            get { return string.Format("{0:F0} mm", _eyeHeightAboveFloorMm); }
        }

        public string AbsoluteElevationDisplay
        {
            get { return string.Format("{0:F2} m", _absoluteElevationMm / 1000.0); }
        }

        public double FovDegrees
        {
            get { return _fovDegrees; }
            set { _fovDegrees = value; OnPropertyChanged("FovDegrees"); }
        }

        public ElementId AssociatedLevelId
        {
            get { return _associatedLevelId; }
            set { _associatedLevelId = value; OnPropertyChanged("AssociatedLevelId"); }
        }

        public string AssociatedLevelName
        {
            get { return _associatedLevelName; }
            set { _associatedLevelName = value; OnPropertyChanged("AssociatedLevelName"); }
        }

        public string DetectedSource
        {
            get { return _detectedSource; }
            set { _detectedSource = value; OnPropertyChanged("DetectedSource"); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; OnPropertyChanged("IsSelected"); }
        }

        public bool IsActive
        {
            get { return _isActive; }
            set { _isActive = value; OnPropertyChanged("IsActive"); }
        }

        public string ColorHex
        {
            get { return _colorHex; }
            set { _colorHex = value; OnPropertyChanged("ColorHex"); }
        }

        public double YawAngleDegrees
        {
            get
            {
                if (_forwardDirection == null) return 0;
                double angleRad = Math.Atan2(_forwardDirection.Y, _forwardDirection.X);
                double deg = angleRad * (180.0 / Math.PI);
                return (deg + 360.0) % 360.0;
            }
        }

        public double PitchAngleDegrees
        {
            get
            {
                if (_forwardDirection == null) return 0;
                double horizDist = Math.Sqrt(_forwardDirection.X * _forwardDirection.X + _forwardDirection.Y * _forwardDirection.Y);
                double pitchRad = Math.Atan2(_forwardDirection.Z, horizDist);
                return pitchRad * (180.0 / Math.PI);
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