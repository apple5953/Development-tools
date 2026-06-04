using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Modules.SheetTools.RoomFinishConfigurator
{
    /// <summary>
    /// 代表單一房間資訊的 ViewModel，用於 DataGrid 展示與編輯
    /// </summary>
    public class RoomItemViewModel : INotifyPropertyChanged
    {
        public ElementId Id { get; }
        public string Number { get; }
        public string Name { get; }

        public string OriginalFloorFinish { get; }
        public string OriginalWallFinish { get; }
        public string OriginalCeilingFinish { get; }

        private string _floorFinish;
        public string FloorFinish
        {
            get => _floorFinish;
            set
            {
                if (_floorFinish != value)
                {
                    _floorFinish = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsModified));
                }
            }
        }

        private string _wallFinish;
        public string WallFinish
        {
            get => _wallFinish;
            set
            {
                if (_wallFinish != value)
                {
                    _wallFinish = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsModified));
                }
            }
        }

        private string _ceilingFinish;
        public string CeilingFinish
        {
            get => _ceilingFinish;
            set
            {
                if (_ceilingFinish != value)
                {
                    _ceilingFinish = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsModified));
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsModified => 
            FloorFinish != OriginalFloorFinish || 
            WallFinish != OriginalWallFinish || 
            CeilingFinish != OriginalCeilingFinish;

        public RoomItemViewModel(Room room)
        {
            Id = room.Id;
            Number = room.Number;
            Name = room.Name;

            // 讀取內建裝修參數值
            OriginalFloorFinish = GetParamValue(room, BuiltInParameter.ROOM_FINISH_FLOOR);
            OriginalWallFinish = GetParamValue(room, BuiltInParameter.ROOM_FINISH_WALL);
            OriginalCeilingFinish = GetParamValue(room, BuiltInParameter.ROOM_FINISH_CEILING);

            _floorFinish = OriginalFloorFinish;
            _wallFinish = OriginalWallFinish;
            _ceilingFinish = OriginalCeilingFinish;
            _isSelected = false;
        }

        private string GetParamValue(Room room, BuiltInParameter bip)
        {
            Parameter p = room.get_Parameter(bip);
            return p != null && p.HasValue ? p.AsString() : "";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 房間裝修配置工具主 ViewModel
    /// </summary>
    public class RoomFinishConfiguratorViewModel : INotifyPropertyChanged
    {
        private readonly Document _doc;
        private readonly Window _window;

        public ObservableCollection<RoomItemViewModel> Rooms { get; }
        public ICollectionView FilteredRooms { get; }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilteredRooms.Refresh();
            }
        }

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // 批次修改屬性
        public int BatchTarget { get; set; } = 0; // 0: 地板裝修, 1: 牆面裝修, 2: 天花板裝修
        private string _batchValue;
        public string BatchValue
        {
            get => _batchValue;
            set { _batchValue = value; OnPropertyChanged(); }
        }

        // 命令
        public ICommand ApplyBatchCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ToggleSelectAllCommand { get; }

        private bool _allSelected;
        public bool AllSelected
        {
            get => _allSelected;
            set
            {
                _allSelected = value;
                OnPropertyChanged();
                foreach (var r in FilteredRooms.Cast<RoomItemViewModel>())
                {
                    r.IsSelected = _allSelected;
                }
            }
        }

        public RoomFinishConfiguratorViewModel(Document doc, Window window)
        {
            _doc = doc;
            _window = window;

            // 1. 篩選所有有效的 Rooms
            var roomsInDoc = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0) // 排除未放置或無面積的房間
                .OrderBy(r => r.Number)
                .Select(r => new RoomItemViewModel(r))
                .ToList();

            Rooms = new ObservableCollection<RoomItemViewModel>(roomsInDoc);
            FilteredRooms = CollectionViewSource.GetDefaultView(Rooms);
            FilteredRooms.Filter = FilterRoomsPredicate;

            // 2. 初始化命令
            ApplyBatchCommand = new RelayCommand(OnApplyBatch);
            SaveCommand = new RelayCommand(OnSave);
            CancelCommand = new RelayCommand(() => _window.Close());
            ToggleSelectAllCommand = new RelayCommand(() => AllSelected = !AllSelected);

            UpdateStatusText();
        }

        private bool FilterRoomsPredicate(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (!(obj is RoomItemViewModel item)) return false;

            return item.Number.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.FloorFinish.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.WallFinish.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.CeilingFinish.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateStatusText()
        {
            int total = Rooms.Count;
            int modified = Rooms.Count(r => r.IsModified);
            StatusText = $"專案共 {total} 間房間，目前已編輯 {modified} 間的裝修材質。";
        }

        // 批次套用裝修值
        private void OnApplyBatch()
        {
            var targets = GetActiveTargets();
            if (!targets.Any())
            {
                MessageBox.Show("目前清單中沒有可套用的房間。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string val = BatchValue ?? "";

            foreach (var r in targets)
            {
                if (BatchTarget == 0) // 地板
                {
                    r.FloorFinish = val;
                }
                else if (BatchTarget == 1) // 牆面
                {
                    r.WallFinish = val;
                }
                else // 天花板
                {
                    r.CeilingFinish = val;
                }
            }

            UpdateStatusText();
        }

        private List<RoomItemViewModel> GetActiveTargets()
        {
            var selected = Rooms.Where(r => r.IsSelected).ToList();
            if (selected.Any()) return selected;

            return FilteredRooms.Cast<RoomItemViewModel>().ToList();
        }

        // 儲存寫回 Revit
        private void OnSave()
        {
            var modifiedItems = Rooms.Where(r => r.IsModified).ToList();
            if (!modifiedItems.Any())
            {
                _window.DialogResult = true;
                _window.Close();
                return;
            }

            try
            {
                using (Transaction trans = new Transaction(_doc, "批次配置房間裝修材"))
                {
                    trans.Start();

                    foreach (var item in modifiedItems)
                    {
                        if (_doc.GetElement(item.Id) is Room room)
                        {
                            SetParamValue(room, BuiltInParameter.ROOM_FINISH_FLOOR, item.FloorFinish);
                            SetParamValue(room, BuiltInParameter.ROOM_FINISH_WALL, item.WallFinish);
                            SetParamValue(room, BuiltInParameter.ROOM_FINISH_CEILING, item.CeilingFinish);
                        }
                    }

                    trans.Commit();
                }

                MessageBox.Show("✓ 房間裝修材質配置更新成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                _window.DialogResult = true;
                _window.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"寫入 Revit 房間參數時發生錯誤：{ex.Message}\n交易已自動撤銷。", "更新錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetParamValue(Room room, BuiltInParameter bip, string val)
        {
            Parameter p = room.get_Parameter(bip);
            if (p != null && !p.IsReadOnly)
            {
                p.Set(val ?? "");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// RelayCommand 簡化版類別
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
