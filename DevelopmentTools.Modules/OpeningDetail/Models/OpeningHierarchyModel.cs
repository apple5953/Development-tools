using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Autodesk.Revit.DB;

namespace OpeningDetailAddin.Models
{
    /// <summary>
    /// 開口尺寸資料 (寬與高)
    /// </summary>
    public class OpeningDimensionInfo
    {
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }

        public OpeningDimensionInfo()
        {
            WidthMm = 300;
            HeightMm = 300;
        }

        public OpeningDimensionInfo(double widthMm, double heightMm)
        {
            WidthMm = widthMm;
            HeightMm = heightMm;
        }
    }

    /// <summary>
    /// 單一開口元件資料 (支援勾選)
    /// </summary>
    public class OpeningItem : INotifyPropertyChanged
    {
        public FamilyInstance Instance { get; set; }
        public ElementId Id { get { return Instance != null ? Instance.Id : null; } }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string Mark { get; set; }
        public XYZ LocationPoint { get; set; }
        public double SillHeightMm { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }

        public WallGroup ParentWall { get; set; }

        private bool _hasExistingSheet;
        public bool HasExistingSheet
        {
            get { return _hasExistingSheet; }
            set
            {
                _hasExistingSheet = value;
                OnPropertyChanged("HasExistingSheet");
                OnPropertyChanged("HasExistingStatusVisibility");
                OnPropertyChanged("ExistingStatusBadge");
            }
        }

        private string _existingSheetNumber;
        public string ExistingSheetNumber
        {
            get { return _existingSheetNumber; }
            set
            {
                _existingSheetNumber = value;
                OnPropertyChanged("ExistingSheetNumber");
                OnPropertyChanged("ExistingStatusBadge");
            }
        }

        private bool _hasExistingViews;
        public bool HasExistingViews
        {
            get { return _hasExistingViews; }
            set
            {
                _hasExistingViews = value;
                OnPropertyChanged("HasExistingViews");
                OnPropertyChanged("HasExistingStatusVisibility");
                OnPropertyChanged("ExistingStatusBadge");
            }
        }

        public string ExistingStatusBadge
        {
            get
            {
                if (HasExistingSheet)
                {
                    return string.Format("📄 已建圖紙: {0}", ExistingSheetNumber ?? "既有圖紙");
                }
                if (HasExistingViews)
                {
                    return "👁️ 已有對應視圖";
                }
                return string.Empty;
            }
        }

        public System.Windows.Visibility HasExistingStatusVisibility
        {
            get
            {
                return (HasExistingSheet || HasExistingViews)
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                SetIsSelected(value, true);
            }
        }

        public void SetIsSelected(bool value, bool updateParent)
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged("IsSelected");
                if (updateParent && ParentWall != null)
                {
                    ParentWall.VerifyCheckState();
                }
            }
        }

        public string DisplayName 
        { 
            get 
            { 
                string idVal = Id != null ? Id.Value.ToString() : "0";
                return string.Format("開口 [{0}] {1} (ID: {2})", Mark, TypeName, idVal); 
            } 
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    /// <summary>
    /// 牆面群組 (支援勾選與三態聯動)
    /// </summary>
    public class WallGroup : INotifyPropertyChanged
    {
        public Wall Wall { get; set; }
        public ElementId WallId { get { return Wall != null ? Wall.Id : null; } }
        public string WallName { get; set; }
        public string WallTypeName { get; set; }
        public double WallThicknessMm { get; set; }
        public double WallLengthMm { get; set; }
        public double WallHeightMm { get; set; }
        public ObservableCollection<OpeningItem> Openings { get; set; }

        public LevelGroup ParentLevel { get; set; }

        private bool? _isSelected;
        public bool? IsSelected
        {
            get { return _isSelected; }
            set
            {
                SetIsSelected(value, true, true);
            }
        }

        public void SetIsSelected(bool? value, bool updateChildren, bool updateParent)
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                if (updateChildren && value.HasValue)
                {
                    foreach (var op in Openings)
                    {
                        op.SetIsSelected(value.Value, false);
                    }
                }
                OnPropertyChanged("IsSelected");
                if (updateParent && ParentLevel != null)
                {
                    ParentLevel.VerifyCheckState();
                }
            }
        }

        public void VerifyCheckState()
        {
            bool? state = null;
            if (Openings.Count > 0)
            {
                bool allChecked = true;
                bool allUnchecked = true;
                foreach (var op in Openings)
                {
                    if (op.IsSelected) allUnchecked = false;
                    else allChecked = false;
                }

                if (allChecked) state = true;
                else if (allUnchecked) state = false;
                else state = null;
            }
            SetIsSelected(state, false, true);
        }

        public WallGroup()
        {
            Openings = new ObservableCollection<OpeningItem>();
            _isSelected = true; // 預設勾選
        }

        public int OpeningCount { get { return Openings.Count; } }

        public string DisplayName 
        { 
            get 
            { 
                string idVal = WallId != null ? WallId.Value.ToString() : "0";
                return string.Format("牆面 [{0}] ({1}, 厚{2:F0}mm, 長{3:F1}m, 高{4:F1}m) - ID: {5} (開口: {6} 個)",
                    WallName, WallTypeName, WallThicknessMm, WallLengthMm / 1000.0, WallHeightMm / 1000.0, idVal, Openings.Count); 
            } 
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    /// <summary>
    /// 樓層群組 (支援勾選與三態聯動，方便各樓層分開檢查與出圖)
    /// </summary>
    public class LevelGroup : INotifyPropertyChanged
    {
        public Level Level { get; set; }
        public ElementId LevelId { get { return Level != null ? Level.Id : null; } }
        public string LevelName { get { return Level != null ? Level.Name : "未指定樓層"; } }
        public ObservableCollection<WallGroup> Walls { get; set; }

        private bool? _isSelected;
        public bool? IsSelected
        {
            get { return _isSelected; }
            set
            {
                SetIsSelected(value, true);
            }
        }

        public void SetIsSelected(bool? value, bool updateChildren)
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                if (updateChildren && value.HasValue)
                {
                    foreach (var wall in Walls)
                    {
                        wall.SetIsSelected(value.Value, true, false);
                    }
                }
                OnPropertyChanged("IsSelected");
            }
        }

        public void VerifyCheckState()
        {
            bool? state = null;
            if (Walls.Count > 0)
            {
                bool allChecked = true;
                bool allUnchecked = true;
                foreach (var wall in Walls)
                {
                    if (wall.IsSelected == true) allUnchecked = false;
                    else if (wall.IsSelected == false) allChecked = false;
                    else { allChecked = false; allUnchecked = false; }
                }

                if (allChecked) state = true;
                else if (allUnchecked) state = false;
                else state = null;
            }
            SetIsSelected(state, false);
        }

        public LevelGroup()
        {
            Walls = new ObservableCollection<WallGroup>();
            _isSelected = true; // 預設勾選
        }

        public int TotalOpenings
        {
            get
            {
                int sum = 0;
                foreach (var w in Walls) sum += w.OpeningCount;
                return sum;
            }
        }

        public string DisplayName 
        { 
            get 
            { 
                return string.Format("{0} - 共有 {1} 面牆設有開孔 (共 {2} 處)", LevelName, Walls.Count, TotalOpenings); 
            } 
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
