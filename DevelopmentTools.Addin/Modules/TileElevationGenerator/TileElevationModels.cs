using System;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.TileElevationGenerator
{
    public enum SourceMode
    {
        Floor,
        Wall
    }

    public class ViewTemplateItem
    {
        public View View { get; set; }
        public string Name => View != null ? View.Name : "<專案預設 (無樣板)>";
        public ElementId Id => View != null ? View.Id : ElementId.InvalidElementId;
    }

    public class ComboboxItem
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public ComboboxItem(ElementId id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    public class WallElevationData
    {
        public ElementId WallId { get; set; }
        public string WallName { get; set; }
        
        // 以下均為 Revit 內部單位 (Feet)
        public XYZ StartPoint { get; set; }
        public XYZ EndPoint { get; set; }
        public XYZ MidPoint { get; set; }
        
        public double WallLength { get; set; } // Feet
        public double WallHeight { get; set; } // Feet
        
        public XYZ WallDirection { get; set; }
        public XYZ WallNormal { get; set; }
        public XYZ RoomSideDirection { get; set; }
        
        public double WallThickness { get; set; } // Feet
        public double LevelElevation { get; set; } // Feet
        public Wall WallElement { get; set; }
        public Curve BoundaryCurve { get; set; }
    }

    public class WallElevationDataItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        private string _viewName;
        public string ViewName
        {
            get => _viewName;
            set { _viewName = value; OnPropertyChanged(); }
        }

        public double WallLength => GeometryData != null ? GeometryData.WallLength * 304.8 : 0.0; // mm

        private double _viewDepth = 600.0;
        public double ViewDepth
        {
            get => _viewDepth;
            set { _viewDepth = value; OnPropertyChanged(); }
        }

        private double _wallOffset = 30.0;
        public double WallOffset
        {
            get => _wallOffset;
            set { _wallOffset = value; OnPropertyChanged(); }
        }

        private double _sideExtension = 150.0;
        public double SideExtension
        {
            get => _sideExtension;
            set { _sideExtension = value; OnPropertyChanged(); }
        }

        private double _topOffset = 50.0;
        public double TopOffset
        {
            get => _topOffset;
            set { _topOffset = value; OnPropertyChanged(); }
        }

        private double _bottomOffset = 0.0;
        public double BottomOffset
        {
            get => _bottomOffset;
            set { _bottomOffset = value; OnPropertyChanged(); }
        }

        public WallElevationData GeometryData { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }

    public class GeneratorSettings
    {
        public SourceMode SourceMode { get; set; } = SourceMode.Floor;
        public ElementId SelectedViewTemplateId { get; set; } = ElementId.InvalidElementId;
        
        // 以下為使用者輸入單位 (mm)
        public double ViewDepth { get; set; } = 600.0;
        public double WallOffset { get; set; } = 30.0;
        
        public bool AutoWallHeight { get; set; } = true;
        public bool AutoWallLength { get; set; } = true;
        
        public double BottomOffset { get; set; } = 0.0;
        public double TopOffset { get; set; } = 50.0; // mm
        public double SideExtension { get; set; } = 150.0; // 左右延伸 (mm)
        public string NamePrefix { get; set; } = "TE";
        
        public bool SkipShortWall { get; set; } = true;
        public double MinWallLength { get; set; } = 500.0; // mm
    }
}
