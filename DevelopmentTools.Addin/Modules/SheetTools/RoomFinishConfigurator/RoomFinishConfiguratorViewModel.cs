using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using DevelopmentTools.Core;

namespace DevelopmentTools.Modules.SheetTools.RoomFinishConfigurator
{
    public class FinishConfigItem
    {
        public string Code { get; set; }
        public string Type { get; set; }
        public double Thickness { get; set; }
        public double HeightOrOffset { get; set; }
        public string Material { get; set; }
        public string Space { get; set; }
        public string Note { get; set; }
    }

    internal sealed class RevitExternalEventRunner : IExternalEventHandler, IDisposable
    {
        private readonly Queue<Action<UIApplication>> _actions = new Queue<Action<UIApplication>>();
        private readonly object _lock = new object();
        private readonly Dispatcher _dispatcher;
        private readonly ExternalEvent _externalEvent;
        private bool _disposed;

        public RevitExternalEventRunner(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _externalEvent = ExternalEvent.Create(this);
        }

        public void Raise(Action<UIApplication> action)
        {
            if (action == null || _disposed) return;

            lock (_lock)
            {
                _actions.Enqueue(action);
            }

            _externalEvent.Raise();
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                Action<UIApplication> action;
                lock (_lock)
                {
                    if (_actions.Count == 0) break;
                    action = _actions.Dequeue();
                }

                try
                {
                    action(app);
                }
                catch (Exception ex)
                {
                    _dispatcher.BeginInvoke(new Action(() =>
                        MessageBox.Show($"Revit 操作失敗：{ex.Message}", "房間裝修配置", MessageBoxButton.OK, MessageBoxImage.Error)));
                }
            }
        }

        public string GetName() => "Room Finish Configurator External Event";

        public void Dispose()
        {
            _disposed = true;
            _externalEvent.Dispose();
        }
    }

    public class RoomItemViewModel : INotifyPropertyChanged
    {
        private bool _suppressDetectedChange;

        public ElementId Id { get; }
        public string Number { get; }
        public string Name { get; }
        public Action<RoomItemViewModel, string, string> ModelCodeChangeRequested { get; set; }

        public string OriginalFloorFinish { get; }
        public string OriginalWallFinish { get; }
        public string OriginalCeilingFinish { get; }
        public string OriginalBaseboardFinish { get; }

        private string _floorFinish;
        public string FloorFinish
        {
            get => _floorFinish;
            set => SetConfiguredFinish(ref _floorFinish, value, nameof(FloorMatchStatus), nameof(FloorStatusDisplay));
        }

        private string _wallFinish;
        public string WallFinish
        {
            get => _wallFinish;
            set => SetConfiguredFinish(ref _wallFinish, value, nameof(WallMatchStatus), nameof(WallStatusDisplay));
        }

        private string _ceilingFinish;
        public string CeilingFinish
        {
            get => _ceilingFinish;
            set => SetConfiguredFinish(ref _ceilingFinish, value, nameof(CeilingMatchStatus), nameof(CeilingStatusDisplay));
        }

        private string _baseboardFinish;
        public string BaseboardFinish
        {
            get => _baseboardFinish;
            set => SetConfiguredFinish(ref _baseboardFinish, value, nameof(BaseboardMatchStatus), nameof(BaseboardStatusDisplay));
        }

        private string _detectedFloorFinish = "尚未檢查地板";
        public string DetectedFloorFinish
        {
            get => _detectedFloorFinish;
            set => SetDetectedFinish(ref _detectedFloorFinish, value, "Floor", nameof(FloorMatchStatus), nameof(FloorStatusDisplay));
        }

        private string _detectedWallFinish = "尚未檢查牆面";
        public string DetectedWallFinish
        {
            get => _detectedWallFinish;
            set => SetDetectedFinish(ref _detectedWallFinish, value, "Wall", nameof(WallMatchStatus), nameof(WallStatusDisplay));
        }

        private string _detectedCeilingFinish = "尚未檢查天花板";
        public string DetectedCeilingFinish
        {
            get => _detectedCeilingFinish;
            set => SetDetectedFinish(ref _detectedCeilingFinish, value, "Ceiling", nameof(CeilingMatchStatus), nameof(CeilingStatusDisplay));
        }

        private string _detectedBaseboardFinish = "尚未檢查踢腳板";
        public string DetectedBaseboardFinish
        {
            get => _detectedBaseboardFinish;
            set => SetDetectedFinish(ref _detectedBaseboardFinish, value, "Baseboard", nameof(BaseboardMatchStatus), nameof(BaseboardStatusDisplay));
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public string FloorMatchStatus => GetMatchStatus(FloorFinish, DetectedFloorFinish);
        public string WallMatchStatus => GetMatchStatus(WallFinish, DetectedWallFinish);
        public string CeilingMatchStatus => GetMatchStatus(CeilingFinish, DetectedCeilingFinish);
        public string BaseboardMatchStatus => GetMatchStatus(BaseboardFinish, DetectedBaseboardFinish);

        public string FloorStatusDisplay => $"{FloorMatchStatus} {DetectedFloorFinish}";
        public string WallStatusDisplay => $"{WallMatchStatus} {DetectedWallFinish}";
        public string CeilingStatusDisplay => $"{CeilingMatchStatus} {DetectedCeilingFinish}";
        public string BaseboardStatusDisplay => $"{BaseboardMatchStatus} {DetectedBaseboardFinish}";

        public bool IsModified =>
            FloorFinish != OriginalFloorFinish ||
            WallFinish != OriginalWallFinish ||
            CeilingFinish != OriginalCeilingFinish ||
            BaseboardFinish != OriginalBaseboardFinish;

        public RoomItemViewModel(Room room)
        {
            Id = room.Id;
            Number = room.Number;
            Name = room.Name;

            OriginalFloorFinish = GetParamValue(room, BuiltInParameter.ROOM_FINISH_FLOOR);
            OriginalWallFinish = GetParamValue(room, BuiltInParameter.ROOM_FINISH_WALL);
            OriginalCeilingFinish = GetParamValue(room, BuiltInParameter.ROOM_FINISH_CEILING);
            OriginalBaseboardFinish = GetParamValue(room, "踢腳板") ??
                                      GetParamValue(room, "踢腳板裝修") ??
                                      GetParamValue(room, "Baseboard") ??
                                      string.Empty;

            _floorFinish = OriginalFloorFinish;
            _wallFinish = OriginalWallFinish;
            _ceilingFinish = OriginalCeilingFinish;
            _baseboardFinish = OriginalBaseboardFinish;
        }

        public void SetDetectedFinishes(string floor, string wall, string ceiling, string baseboard)
        {
            _suppressDetectedChange = true;
            try
            {
                DetectedFloorFinish = floor;
                DetectedWallFinish = wall;
                DetectedCeilingFinish = ceiling;
                DetectedBaseboardFinish = baseboard;
            }
            finally
            {
                _suppressDetectedChange = false;
            }
        }

        private void SetConfiguredFinish(ref string field, string value, params string[] dependentProperties)
        {
            if (field == value) return;
            field = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsModified));
            foreach (string property in dependentProperties)
            {
                OnPropertyChanged(property);
            }
        }

        private void SetDetectedFinish(ref string field, string value, string role, params string[] dependentProperties)
        {
            string oldValue = field;
            if (oldValue == value) return;

            field = value ?? string.Empty;
            OnPropertyChanged();
            foreach (string property in dependentProperties)
            {
                OnPropertyChanged(property);
            }

            if (!_suppressDetectedChange)
            {
                ModelCodeChangeRequested?.Invoke(this, role, field);
            }
        }

        private static string GetParamValue(Room room, BuiltInParameter bip)
        {
            Parameter p = room.get_Parameter(bip);
            return p != null && p.HasValue ? p.AsString() ?? string.Empty : string.Empty;
        }

        private static string GetParamValue(Room room, string paramName)
        {
            Parameter p = room.LookupParameter(paramName);
            return p != null && p.HasValue ? p.AsString() ?? string.Empty : null;
        }

        private static string GetMatchStatus(string configured, string detected)
        {
            if (string.IsNullOrWhiteSpace(configured)) return "—";
            if (string.IsNullOrWhiteSpace(detected) || detected.Contains("未建置") || detected.Contains("尚未檢查")) return "!";

            var detectedCodes = detected.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v));

            foreach (string code in detectedCodes)
            {
                if (configured.Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    return "OK";
                }
            }

            return "!";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RoomFinishConfiguratorViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;
        private readonly Window _window;
        private readonly RevitExternalEventRunner _revitEvents;
        private readonly Dictionary<string, FinishConfigItem> _finishConfigs =
            new Dictionary<string, FinishConfigItem>(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<RoomItemViewModel> Rooms { get; }
        public ICollectionView FilteredRooms { get; }

        public IEnumerable<string> FloorConfigCodes => GetConfigCodes("Floor");
        public IEnumerable<string> WallConfigCodes => GetConfigCodes("Wall");
        public IEnumerable<string> CeilingConfigCodes => GetConfigCodes("Ceiling");
        public IEnumerable<string> BaseboardConfigCodes => GetConfigCodes("Baseboard");

        private string _cloudUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vT2Nq_H2c_h2Fk4Jg5nL9XGZ5y8sC-wz9U/pub?output=csv";
        public string CloudUrl
        {
            get => _cloudUrl;
            set { _cloudUrl = value ?? string.Empty; OnPropertyChanged(); }
        }

        private int _jointRelation;
        public int JointRelation
        {
            get => _jointRelation;
            set { _jointRelation = value; OnPropertyChanged(); }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value ?? string.Empty;
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

        public int BatchTarget { get; set; }

        private string _batchValue;
        public string BatchValue
        {
            get => _batchValue;
            set { _batchValue = value ?? string.Empty; OnPropertyChanged(); }
        }

        private bool _allSelected;
        public bool AllSelected
        {
            get => _allSelected;
            set
            {
                _allSelected = value;
                OnPropertyChanged();
                foreach (RoomItemViewModel room in FilteredRooms.Cast<RoomItemViewModel>())
                {
                    room.IsSelected = value;
                }
            }
        }

        public ICommand ApplyBatchCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ToggleSelectAllCommand { get; }
        public ICommand SyncCloudCommand { get; }
        public ICommand GenerateFinishesCommand { get; }
        public ICommand DownloadTemplateCommand { get; }
        public ICommand NavigateToRoomCommand { get; }
        public ICommand RefreshDetectedMaterialsCommand { get; }
        public ICommand OpenHelpCommand { get; }

        private void OnOpenHelp()
        {
            string title = LanguageManager.Instance["Tut_RoomFinish_Title"];
            string content = LanguageManager.Instance["Tut_RoomFinish_Content"];
            TaskDialog td = new TaskDialog(title)
            {
                TitleAutoPrefix = false,
                MainInstruction = title,
                MainContent = content,
                CommonButtons = TaskDialogCommonButtons.Close
            };
            td.Show();
        }

        public RoomFinishConfiguratorViewModel(UIDocument uidoc, Window window)
        {
            _uiDoc = uidoc;
            _doc = uidoc.Document;
            _window = window;
            _revitEvents = new RevitExternalEventRunner(window.Dispatcher);

            LoadDefaultConfigs();

            var rooms = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .OrderBy(r => r.Number)
                .Select(r =>
                {
                    var vm = new RoomItemViewModel(r);
                    vm.ModelCodeChangeRequested = OnModelCodeChangeRequested;
                    return vm;
                })
                .ToList();

            Rooms = new ObservableCollection<RoomItemViewModel>(rooms);
            FilteredRooms = CollectionViewSource.GetDefaultView(Rooms);
            FilteredRooms.Filter = FilterRoomsPredicate;

            ApplyBatchCommand = new RelayCommand(OnApplyBatch);
            SaveCommand = new RelayCommand(OnSave);
            CancelCommand = new RelayCommand(() => _window.Close());
            ToggleSelectAllCommand = new RelayCommand(() => AllSelected = !AllSelected);
            SyncCloudCommand = new RelayCommand(OnSyncCloud);
            GenerateFinishesCommand = new RelayCommand(OnGenerateFinishes);
            DownloadTemplateCommand = new RelayCommand(OnDownloadTemplate);
            NavigateToRoomCommand = new RelayCommandParam(OnNavigateToRoom);
            RefreshDetectedMaterialsCommand = new RelayCommand(OnRefreshDetectedMaterials);
            OpenHelpCommand = new RelayCommand(OnOpenHelp);

            NotifyConfigCodeLists();
            UpdateStatusText();
        }

        private void OnRefreshDetectedMaterials()
        {
            const string checking = "檢查中...";
            StatusText = "正在檢查實際模型類型備註...";
            foreach (RoomItemViewModel vm in Rooms)
            {
                vm.SetDetectedFinishes(checking, checking, checking, checking);
            }

            _revitEvents.Raise(_ =>
            {
                try
                {
                    RunOnUi(() => StatusText = "正在讀取 Revit 模型內的實際裝修代號...");
                    RefreshAllDetectedMaterials();

                    foreach (RoomItemViewModel vm in Rooms)
                    {
                        vm.ModelCodeChangeRequested = OnModelCodeChangeRequested;
                    }

                    RunOnUi(() =>
                    {
                        FilteredRooms.Refresh();
                        StatusText = $"已完成 {Rooms.Count} 間房間的實際模型檢查。";
                    });
                }
                catch (Exception ex)
                {
                    RunOnUi(() =>
                    {
                        StatusText = $"檢查實際模型失敗：{ex.Message}";
                        MessageBox.Show($"檢查實際模型失敗：{ex.Message}", "實際模型檢查", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        private bool FilterRoomsPredicate(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (!(obj is RoomItemViewModel item)) return false;

            return Contains(item.Number, SearchText) ||
                   Contains(item.Name, SearchText) ||
                   Contains(item.FloorFinish, SearchText) ||
                   Contains(item.WallFinish, SearchText) ||
                   Contains(item.CeilingFinish, SearchText) ||
                   Contains(item.BaseboardFinish, SearchText) ||
                   Contains(item.DetectedFloorFinish, SearchText) ||
                   Contains(item.DetectedWallFinish, SearchText) ||
                   Contains(item.DetectedCeilingFinish, SearchText) ||
                   Contains(item.DetectedBaseboardFinish, SearchText);
        }

        private static bool Contains(string source, string keyword)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateStatusText()
        {
            StatusText = $"專案共 {Rooms.Count} 間房間，目前已編輯 {Rooms.Count(r => r.IsModified)} 間的裝修材質。";
        }

        private void RunOnUi(Action action)
        {
            if (action == null) return;

            if (_window.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _window.Dispatcher.Invoke(action);
            }
        }

        private void OnApplyBatch()
        {
            var targets = GetActiveTargets();
            if (!targets.Any())
            {
                MessageBox.Show("目前清單中沒有可套用的房間。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string value = BatchValue ?? string.Empty;
            foreach (RoomItemViewModel room in targets)
            {
                if (BatchTarget == 0) room.FloorFinish = value;
                else if (BatchTarget == 1) room.WallFinish = value;
                else if (BatchTarget == 2) room.CeilingFinish = value;
                else if (BatchTarget == 3) room.BaseboardFinish = value;
            }

            UpdateStatusText();
        }

        private List<RoomItemViewModel> GetActiveTargets()
        {
            var selected = Rooms.Where(r => r.IsSelected).ToList();
            return selected.Any() ? selected : FilteredRooms.Cast<RoomItemViewModel>().ToList();
        }

        private void OnSave()
        {
            var modifiedItems = Rooms.Where(r => r.IsModified).ToList();
            if (!modifiedItems.Any())
            {
                _window.Close();
                return;
            }

            _revitEvents.Raise(_ =>
            {
                try
                {
                    using (Transaction transaction = new Transaction(_doc, "批次配置房間裝修材"))
                    {
                        transaction.Start();

                        foreach (RoomItemViewModel item in modifiedItems)
                        {
                            if (!(_doc.GetElement(item.Id) is Room room)) continue;

                            SetParamValue(room, BuiltInParameter.ROOM_FINISH_FLOOR, item.FloorFinish);
                            SetParamValue(room, BuiltInParameter.ROOM_FINISH_WALL, item.WallFinish);
                            SetParamValue(room, BuiltInParameter.ROOM_FINISH_CEILING, item.CeilingFinish);
                            SetFirstWritableRoomParameter(room, item.BaseboardFinish, "踢腳板", "踢腳板裝修", "Baseboard");
                        }

                        transaction.Commit();
                    }

                    MessageBox.Show("房間裝修材質配置更新成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    _window.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"寫入 Revit 房間參數時發生錯誤：{ex.Message}\n交易已自動撤銷。", "更新錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private static void SetParamValue(Room room, BuiltInParameter bip, string value)
        {
            Parameter p = room.get_Parameter(bip);
            if (p != null && !p.IsReadOnly)
            {
                p.Set(value ?? string.Empty);
            }
        }

        private static void SetFirstWritableRoomParameter(Room room, string value, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter p = room.LookupParameter(name);
                if (p == null || p.IsReadOnly) continue;
                p.Set(value ?? string.Empty);
                return;
            }
        }

        private void LoadDefaultConfigs()
        {
            _finishConfigs.Clear();
            AddDefaultConfig("F1", "Floor", 70, -70, "1:2水泥砂漿防水粉刷+外裝地磚9x29cm±(窯燒花崗石車道磚)", "戶外汽車坡道", "");
            AddDefaultConfig("F2", "Floor", 70, -70, "1:2水泥砂漿防水粉刷+外裝地磚9x29cm±(窯燒花崗石車道磚)", "戶外機車坡道", "");
            AddDefaultConfig("F3", "Floor", 5, 0, "整體粉光+面塗5㎜TH環氧樹脂耐磨地坪", "斜坡車道(室內)", "");
            AddDefaultConfig("F4", "Floor", 5, 0, "整體粉光+面塗3㎜TH環氧樹脂石英砂耐磨地坪", "機房、停車區", "");
            AddDefaultConfig("F4a", "Floor", 5, 0, "整體粉光+面塗3㎜TH環氧樹脂耐磨地坪", "B2F垃圾暫存室", "");
            AddDefaultConfig("F5", "Floor", 0, 0, "踏階貼專用止滑PVC地磚(含止滑條)", "RC樓梯", "");
            AddDefaultConfig("F5a", "Floor", 0, 0, "踏階貼專用止滑PVC地磚(含止滑條)", "鋼構樓梯(實驗棟10F、B2F冰水主機房)", "");
            AddDefaultConfig("F6", "Floor", 60, -60, "高分子防水膜+1:2水泥砂漿防水粉刷+內裝地磚60x60cm±(止滑瓷磚，色另定)", "通風井、廁所、茶水間、更衣室", "");
            AddDefaultConfig("F7", "Floor", 60, -60, "1:2水泥砂漿防水粉刷+面鋪60*60㎝止滑瓷磚，色另定", "走道(易潮濕空間)", "");
            AddDefaultConfig("F8", "Floor", 60, -60, "1:2水泥砂漿+防水膜+面鋪石材(黑/灰色系)", "戶外公共", "");
            AddDefaultConfig("F9", "Floor", 60, -60, "1:3水泥砂漿+面鋪石材(黑/灰色系)", "客梯廳、室內公共、貴賓室", "");
            AddDefaultConfig("F9a", "Floor", 60, -60, "1:3水泥砂漿+面鋪石材(黑/灰色系)", "鋼構樓梯", "");
            AddDefaultConfig("F10", "Floor", 65, 0, "1:2水泥砂漿高分子防水層塗佈+塑木地坪", "陽台", "");
            AddDefaultConfig("F11", "Floor", 45, -100, "除塵地墊", "風除室", "");
            AddDefaultConfig("F12", "Floor", 3, 0, "耐磨硬化地板", "儲藏室、庫房、貨梯廳", "");
            AddDefaultConfig("F13", "Floor", 150, 150, "整體粉光+高架地板(300kgf/㎡,H=15㎝)/面舖方塊地毯", "辦公室", "");
            AddDefaultConfig("F14", "Floor", 20, -20, "整體粉光+自平水泥+滿鋪捲裝PVC地坪", "走道、客貨梯廳、排煙室、樓梯平台", "");
            AddDefaultConfig("F15", "Floor", 10, 0, "整體粉光+自平水泥+方塊地毯", "會議室/簡報室", "");
            AddDefaultConfig("F16", "Floor", 400, 400, "整體粉光+高架地板(500kgf/㎡,H=40㎝)/面舖方塊PVC地坪", "中控室、控制室、電腦機房", "");
            AddDefaultConfig("F17", "Floor", 200, 200, "整體粉光+高架地板(500kgf/㎡,H=20㎝)/面舖方塊PVC地坪", "電力室、資訊機房", "");
            AddDefaultConfig("F18", "Floor", 10, 0, "整體粉光+自平水泥+滿鋪捲裝PVC地坪(導角10㎝)", "P1、P2實驗室、實驗室走廊", "");
            AddDefaultConfig("F19", "Floor", 10, 0, "整體粉光+自平水泥+耐酸鹼滿鋪捲裝PVC地坪(導角10㎝)", "P3實驗室(含走道)", "");
            AddDefaultConfig("F20", "Floor", 6, 0, "整體粉光+環氧樹脂地坪(導角15㎝)(依特殊裝修及設備規格)", "洗滌室", "");
            AddDefaultConfig("F21", "Floor", 6, 0, "整體粉光+荷重型環氧樹脂地坪(導角15㎝)(依特殊裝修及設備規格)", "GMP、動物區", "");
            AddDefaultConfig("F22", "Floor", 60, -58.5, "防水隔熱層+1:3水泥砂漿+瓷質止滑地磚20x20㎝", "屋頂平台", "");
            AddDefaultConfig("F23", "Floor", 45, -100, "防水隔熱層+1:3水泥砂漿", "屋突平台", "");
            AddDefaultConfig("F24", "Floor", 160, -160, "浮動地板(室外)", "冷卻水塔", "");
            AddDefaultConfig("F24a", "Floor", 160, -160, "浮動地板(室內)", "發電機房、自設變電站", "");
            AddDefaultConfig("F24b", "Floor", 160, -160, "浮動地板(室內)", "冰水主機房", "");
            AddDefaultConfig("F25", "Floor", 110, -450, "耐磨硬化地板+冷凍底板", "冷凍庫、冷藏庫", "");
            AddDefaultConfig("F26", "Floor", 0, 0, "金屬格柵", "貓道", "");
            AddDefaultConfig("F27", "Floor", 30, -50, "1:2水泥砂漿防水粉刷+無毒彈性防水膜+貼20x20㎝瓷質釉面磚", "RC水箱(內)", "");
            AddDefaultConfig("F28", "Floor", 15, 0, "拍漿整平", "實驗棟5樓", "");
            AddDefaultConfig("C1", "Ceiling", 38, 0, "襯夾板模霧面清潔磨平", "", "");
            AddDefaultConfig("C2", "Ceiling", 38, 0, "襯夾板模露面清潔整平+水泥漆噴塗(標側及樑底1:3水泥砂漿粉光),色另定", "", "");
            AddDefaultConfig("C3", "Ceiling", 38, 0, "襯夾板模露面清潔整平+表面批土刷乳膠漆", "", "");
            AddDefaultConfig("C4", "Ceiling", 38, 0, "襯夾板模清潔整平", "實驗棟:冷藏室", "");
            AddDefaultConfig("C6", "Ceiling", 38, 0, "明架玻纖天花板(600*600+10mm)", "一般實驗室", "");
            AddDefaultConfig("C7", "Ceiling", 38, 0, "明架礦纖天花板(600*600+10mm)", "辦公室、儲藏室、貨梯廳", "");
            AddDefaultConfig("C8", "Ceiling", 38, 0, "明架玻纖天花板(防潮)(600*600+10mm)", "洗滌室", "");
            AddDefaultConfig("C9", "Ceiling", 41, 0, "半明架礦纖天花板(600*600+10mm)", "會議室、簡報室", "");
            AddDefaultConfig("C11", "Ceiling", 42, 0, "暗架矽酸鈣板天花+表面批土刷乳膠漆", "走道(易潮溼空間)、茶水間、廁所、樓層平台外", "");
            AddDefaultConfig("C13", "Ceiling", 45, 0, "暗架矽酸鈣板天花+矽利康填縫+表面批土刷環氧樹脂漆(半光)", "動物區", "");
            AddDefaultConfig("C15", "Ceiling", 38, 0, "面貼防火吸音材", "發電機房、空調機房、進排風機房", "");
            AddDefaultConfig("C17", "Ceiling", 38, 0, "1:2水泥砂漿防水粉刷+抿石子(戶外)", "戶外樓梯", "");
            AddDefaultConfig("C18", "Ceiling", 80, 0, "冷凍庫板天花", "冷藏室、冷凍室", "");
            AddDefaultConfig("C19", "Ceiling", 82, 0, "製藥無塵室庫板天花", "GMP", "");
            AddDefaultConfig("C20", "Ceiling", 82, 0, "不鏽鋼庫板天花+矽利康填縫", "P3實驗室", "");
            AddDefaultConfig("C21", "Ceiling", 82, 0, "氟碳烤漆庫板天花", "VHP實驗室", "");
            AddDefaultConfig("C22", "Ceiling", 82, 0, "樑底及樑側1:3水泥粉光(Deck板露面)", "實驗棟4.5.11樓", "");
            AddDefaultConfig("C23", "Ceiling", 82, 0, "樑底及樑側1:3水泥粉光+乳膠漆(Deck板露面不需刷漆),色另定", "實驗棟樓梯", "");
            AddDefaultConfig("W1", "Wall", 13, 0, "1:3水泥砂漿粉光/表面批土刷乳膠漆(色另定)", "停車場、客貨梯廳、地下層樓梯間", "");
            AddDefaultConfig("W2", "Wall", 25, 0, "1:3水泥砂漿粉光", "鋼瓶室 註:W3不含批土", "");
            AddDefaultConfig("W3", "Wall", 13, 0, "1:3水泥砂漿粉光/表面批土刷水性水泥漆", "電氣機房(W3a:輕隔間牆體/AB膠填縫/批土整平/刷水性水泥漆)", "");
            AddDefaultConfig("W4", "Wall", 25, 0, "面貼防火吸音材", "發電機房、空調機房、進排風機房", "");
            AddDefaultConfig("W5", "Wall", 30, 0, "1:2水泥砂漿防水粉刷+抿石子(車道,色另定)", "車道(內、外)", "");
            AddDefaultConfig("W6", "Wall", 30, 0, "1:2水泥砂漿防水粉刷+抿石子(戶外)", "通風井、坡道、戶外樓梯", "");
            AddDefaultConfig("W7", "Wall", 23, 0, "面塗防水膜H=180cm+1:2水泥砂漿防水粉刷+內裝壁磚30*60cm+(石英磚)", "廁所、茶水間(W7a:輕隔間墻體/AB膠填縫批土+內裝壁磚30*60cm土(石英磚)(防水膜H=180cm)", "");
            AddDefaultConfig("W9", "Wall", 3, 0, "輕隔間:表面批土面刷乳膠漆(色另定)", "辦公室、客貨梯廳、貨梯廳、樓梯間、走道、P2實驗室", "");
            AddDefaultConfig("W10", "Wall", 3, 0, "輕隔間:表面環氧樹脂漆", "P2實驗室(洗滌室、噴藥室)", "");
            AddDefaultConfig("W11", "Wall", 1, 0, "1:3水泥砂漿粉光+環氧樹脂漆(漆層厚10mil)(依特殊裝修及設備規格)", "P3實驗室、動物區、實驗棟卸貨區(W11a:輕隔間牆體/AB膠填填縫批土+環氧樹脂漆(漆層厚10mil)", "");
            AddDefaultConfig("W12", "Wall", 50, 0, "石材,乾式吊掛(淺灰色系·戶外)", "半户外", "");
            AddDefaultConfig("W14", "Wall", 50, 0, "金屬鋁包板", "穿廊、走道", "");
            AddDefaultConfig("W15", "Wall", 15, 0, "1:2水泥砂漿防水粉刷", "陽台、屋頂平台", "");
            AddDefaultConfig("W17", "Wall", 100, 0, "冷凍庫板", "冷藏室、冷凍室", "");
            AddDefaultConfig("W18", "Wall", 50, 0, "5公分外層不鏽鋼、內層PU庫板", "P3實驗室", "");
            AddDefaultConfig("W19", "Wall", 50, 0, "製藥無塵室庫板", "製程室", "");
            AddDefaultConfig("W20", "Wall", 3, 0, "輕隔間:表面批土面", "實驗棟5樓", "");
            AddDefaultConfig("B1", "Baseboard", 3, 1200, "1:3水泥砂漿粉光+表面刷反光漆(色另定)H=120cm", "停車場", "");
            AddDefaultConfig("B2", "Baseboard", 5.5, 100, "PVC發泡踢腳H=10cm(平面貼合)", "辦公室、研究室、貴賓室", "");
            AddDefaultConfig("B3", "Baseboard", 3, 1200, "材料同牆面,色另定。RC牆:1:3水泥砂漿粉光+表面刷乳膠漆 H=120cm;輕隔間:表面批土刷乳膠漆 H=120cm", "貨梯廳、庫房、儲藏室(B3a:輕 隔間+AB膠填縫批土+刷乳膠漆 H=120cm)", "");
            AddDefaultConfig("B4", "Baseboard", 2, 100, "1:3水泥砂漿粉光+滿舗捲裝PVC地坪上牆10m", "客貨梯梯廳", "");
            AddDefaultConfig("B5", "Baseboard", 3, 100, "刷EPOXY漆+10cm高,色同地坪", "機房", "");
            AddDefaultConfig("B6", "Baseboard", 5.5, 100, "油漆踢腳+10cm高·色另定", "樓梯", "");
            AddDefaultConfig("B7", "Baseboard", 3, 1800, "高分子防水膜H=180cm+1:2水泥砂漿防水粉水刷+內裝壁磚60*H30cm+-(止滑瓷磚,色另定)", "廁所、清潔間、茶水間", "");
            AddDefaultConfig("B8", "Baseboard", 3, 60, "不銹鋼毛絲面踢腳H=6cm", "梯廳/走道", "");
        }

        private void AddDefaultConfig(string code, string type, double thickness, double heightOrOffset, string material, string space, string note)
        {
            _finishConfigs[code] = new FinishConfigItem
            {
                Code = code,
                Type = type,
                Thickness = thickness,
                HeightOrOffset = heightOrOffset,
                Material = material,
                Space = space,
                Note = note
            };
        }

        private async void OnSyncCloud()
        {
            if (string.IsNullOrWhiteSpace(CloudUrl))
            {
                MessageBox.Show("請輸入 CSV 或 Google Sheet 發布連結。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusText = "正在同步雲端材質表...";
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    string csvData = await client.GetStringAsync(CloudUrl);
                    ParseCsvConfigs(csvData);
                }

                NotifyConfigCodeLists();
                StatusText = $"已同步 {_finishConfigs.Count} 筆材質配置。需要比對模型時請按「檢查實際模型」。";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步雲端材質表失敗：{ex.Message}", "同步失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadDefaultConfigs();
                NotifyConfigCodeLists();
                UpdateStatusText();
            }
        }

        private void ParseCsvConfigs(string csvData)
        {
            if (string.IsNullOrWhiteSpace(csvData)) return;

            var parsed = new Dictionary<string, FinishConfigItem>(StringComparer.OrdinalIgnoreCase);
            var lines = csvData.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return;

            string[] headers = SplitCsvLine(lines[0]).Select(h => h.Trim()).ToArray();
            for (int i = 1; i < lines.Length; i++)
            {
                string[] values = SplitCsvLine(lines[i]);
                string code = ReadCsvValue(headers, values, "Code", "代號", "編號", "Revit房間參數填值", "Revit類型備註");
                string type = NormalizeType(ReadCsvValue(headers, values, "Type", "類型"));
                string material = ReadCsvValue(headers, values, "Material", "材質", "材料名稱");

                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(type)) continue;

                parsed[code] = new FinishConfigItem
                {
                    Code = code.Trim(),
                    Type = type,
                    Thickness = ParseDouble(ReadCsvValue(headers, values, "Thickness", "厚度", "模型厚度(mm)", "模型厚度")),
                    HeightOrOffset = ParseDouble(ReadCsvValue(headers, values, "HeightOrOffset", "高度", "偏移", "高度或偏移(mm)", "高度/偏移(mm)", "高度或偏移")),
                    Material = material,
                    Space = ReadCsvValue(headers, values, "Space", "使用空間"),
                    Note = ReadCsvValue(headers, values, "Note", "備註")
                };
            }

            if (parsed.Count == 0) return;

            _finishConfigs.Clear();
            foreach (var pair in parsed)
            {
                _finishConfigs[pair.Key] = pair.Value;
            }
        }

        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (char ch in line)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }

        private static string ReadCsvValue(string[] headers, string[] values, params string[] names)
        {
            for (int i = 0; i < headers.Length && i < values.Length; i++)
            {
                if (names.Any(n => headers[i].Equals(n, StringComparison.OrdinalIgnoreCase)))
                {
                    return values[i].Trim();
                }
            }

            return string.Empty;
        }

        private static string NormalizeType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return string.Empty;
            string value = type.Trim();
            if (value.Contains("地")) return "Floor";
            if (value.Contains("牆") || value.Contains("墙")) return "Wall";
            if (value.Contains("天")) return "Ceiling";
            if (value.Contains("踢") || value.Contains("Baseboard")) return "Baseboard";
            return value;
        }

        private static double ParseDouble(string value)
        {
            return double.TryParse(value, out double result) ? result : 0.0;
        }

        private void OnDownloadTemplate()
        {
            var dialog = new SaveFileDialog
            {
                Title = "下載材質配置範本",
                Filter = "Excel 活頁簿 (*.xlsx)|*.xlsx",
                FileName = "專案裝修材料管理表.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(_window) != true) return;

            string templatePath = FindMaterialTemplatePath();
            if (string.IsNullOrEmpty(templatePath))
            {
                MessageBox.Show("找不到內建 Excel 範本檔，請重新安裝或更新外掛。", "範本不存在", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            File.Copy(templatePath, dialog.FileName, true);
            StatusText = $"已輸出範本：{dialog.FileName}";
        }

        private static string FindMaterialTemplatePath()
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string[] candidates =
            {
                Path.Combine(assemblyDir ?? string.Empty, "Resources", "Templates", "RoomFinishMaterialTemplate.xlsx"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Templates", "RoomFinishMaterialTemplate.xlsx"),
                Path.Combine(@"D:\Room Tile Local 3 System", "DevelopmentTools.Addin", "Resources", "Templates", "RoomFinishMaterialTemplate.xlsx")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private void OnGenerateFinishes()
        {
            var targets = GetActiveTargets();
            if (!targets.Any())
            {
                MessageBox.Show("目前清單中沒有可生成裝修模型的房間。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _revitEvents.Raise(_ =>
            {
                try
                {
                    using (Transaction transaction = new Transaction(_doc, "生成房間裝修模型"))
                    {
                        transaction.Start();

                        foreach (RoomItemViewModel item in targets)
                        {
                            if (!(_doc.GetElement(item.Id) is Room room)) continue;

                            FinishGeometryGenerator.GenerateRoomFinishes(
                                _doc,
                                room,
                                GetConfig(item.FloorFinish, "Floor"),
                                GetConfig(item.WallFinish, "Wall"),
                                GetConfig(item.CeilingFinish, "Ceiling"),
                                GetConfig(item.BaseboardFinish, "Baseboard"),
                                JointRelation);
                        }

                        transaction.Commit();
                    }

                    RefreshAllDetectedMaterials();
                    StatusText = $"已為 {targets.Count} 間房間生成裝修模型。";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"生成裝修模型失敗：{ex.Message}", "生成失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private FinishConfigItem GetConfig(string code, string expectedType)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            if (!_finishConfigs.TryGetValue(code.Trim(), out FinishConfigItem config)) return null;
            return config.Type.Equals(expectedType, StringComparison.OrdinalIgnoreCase) ? config : null;
        }

        private void OnNavigateToRoom(object parameter)
        {
            if (!(parameter is RoomItemViewModel vm)) return;

            _revitEvents.Raise(_ =>
            {
                try
                {
                    Room room = _doc.GetElement(vm.Id) as Room;
                    if (room == null) return;

                    View3D view3D = GetOrCreateFinishReviewView3D();
                    if (view3D == null) return;

                    BoundingBoxXYZ roomBox = room.get_BoundingBox(null);
                    if (roomBox != null)
                    {
                        using (Transaction transaction = new Transaction(_doc, "設定房間裝修調整視圖"))
                        {
                            transaction.Start();
                            view3D.IsSectionBoxActive = true;
                            view3D.SetSectionBox(CreatePaddedBox(roomBox, 3.0));
                            transaction.Commit();
                        }
                    }

                    _uiDoc.ActiveView = view3D;
                    _uiDoc.Selection.SetElementIds(new List<ElementId> { vm.Id });
                    StatusText = $"已切換到 RTS_房間裝修調整_3D，並選取房間 {vm.Number}。";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"開啟 3D 調整視圖失敗：{ex.Message}", "3D 調整視圖", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private View3D GetOrCreateFinishReviewView3D()
        {
            const string viewName = "RTS_房間裝修調整_3D";

            View3D existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            ViewFamilyType viewFamilyType = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

            if (viewFamilyType == null)
            {
                MessageBox.Show("找不到可建立 3D 視圖的 ViewFamilyType。", "3D 調整視圖", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            using (Transaction transaction = new Transaction(_doc, "建立房間裝修調整 3D 視圖"))
            {
                transaction.Start();
                View3D view3D = View3D.CreateIsometric(_doc, viewFamilyType.Id);
                view3D.Name = viewName;
                transaction.Commit();
                return view3D;
            }
        }

        private static BoundingBoxXYZ CreatePaddedBox(BoundingBoxXYZ source, double paddingFeet)
        {
            return new BoundingBoxXYZ
            {
                Min = new XYZ(source.Min.X - paddingFeet, source.Min.Y - paddingFeet, source.Min.Z - 1.0),
                Max = new XYZ(source.Max.X + paddingFeet, source.Max.Y + paddingFeet, source.Max.Z + paddingFeet)
            };
        }

        private void OnModelCodeChangeRequested(RoomItemViewModel vm, string role, string code)
        {
            if (vm == null || string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(code)) return;
            if (code.Contains("未建置") || code.Contains("尚未檢查")) return;

            _revitEvents.Raise(_ =>
            {
                try
                {
                    Room room = _doc.GetElement(vm.Id) as Room;
                    if (room == null) return;

                    int updatedCount;
                    using (Transaction transaction = new Transaction(_doc, $"更新實際模型類型備註 {vm.Number} {role}"))
                    {
                        transaction.Start();
                        updatedCount = ApplyTypeCommentToRoomFinishes(room, role, code.Trim());
                        transaction.Commit();
                    }

                    DetectRoomMaterials(room, vm);
                    StatusText = updatedCount > 0
                        ? $"已將房間 {vm.Number} 的 {RoleDisplayName(role)} 實際模型類型備註更新為 {code}。"
                        : $"房間 {vm.Number} 找不到可更新的 {RoleDisplayName(role)} 實際模型。";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"更新實際模型類型備註失敗：{ex.Message}", "實際模型更正", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private int ApplyTypeCommentToRoomFinishes(Room room, string role, string code)
        {
            int updatedCount = 0;
            var updatedTypeIds = new HashSet<ElementId>();

            foreach (Element element in GetRoomFinishElements(room, role))
            {
                ElementId typeId = element.GetTypeId();
                if (typeId == ElementId.InvalidElementId || updatedTypeIds.Contains(typeId)) continue;

                ElementType elementType = _doc.GetElement(typeId) as ElementType;
                if (WriteTypeCommentCode(elementType, code))
                {
                    updatedTypeIds.Add(typeId);
                    updatedCount++;
                }
            }

            return updatedCount;
        }

        private IEnumerable<Element> GetRoomFinishElements(Room room, string role)
        {
            var results = new List<Element>();
            var taggedResults = GetTaggedFinishElementsInRoom(room, role).ToList();
            if (taggedResults.Count > 0) return taggedResults;

            try
            {
                SpatialElementGeometryCalculator calculator = new SpatialElementGeometryCalculator(_doc);
                SpatialElementGeometryResults geometryResults = calculator.CalculateSpatialElementGeometry(room);
                Solid roomSolid = geometryResults.GetGeometry();

                foreach (Face roomFace in roomSolid.Faces)
                {
                    XYZ normal = roomFace.ComputeNormal(new UV(0.5, 0.5));
                    foreach (SpatialElementBoundarySubface faceInfo in geometryResults.GetBoundaryFaceInfo(roomFace))
                    {
                        LinkElementId boundaryLinkId = faceInfo.SpatialBoundaryElement;
                        if (boundaryLinkId == null || boundaryLinkId.HostElementId == ElementId.InvalidElementId) continue;

                        Element hostElement = _doc.GetElement(boundaryLinkId.HostElementId);
                        if (hostElement == null) continue;

                        if (role.Equals("Floor", StringComparison.OrdinalIgnoreCase) && hostElement is Floor && normal.Z < -0.5)
                        {
                            results.Add(hostElement);
                        }
                        else if (role.Equals("Ceiling", StringComparison.OrdinalIgnoreCase) && normal.Z > 0.5)
                        {
                            if (hostElement is Floor || IsCeilingElement(hostElement)) results.Add(hostElement);
                        }
                        else if (hostElement is Wall wall)
                        {
                            bool isBaseboard = IsBaseboardWall(wall);
                            if (role.Equals("Baseboard", StringComparison.OrdinalIgnoreCase) && isBaseboard)
                                results.Add(hostElement);
                            else if (role.Equals("Wall", StringComparison.OrdinalIgnoreCase) && !isBaseboard)
                                results.Add(hostElement);
                        }
                    }
                }
            }
            catch
            {
                if (role.Equals("Wall", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("Baseboard", StringComparison.OrdinalIgnoreCase))
                {
                    AddBoundaryWalls(room, role, results);
                }
            }

            return results;
        }

        private IEnumerable<Element> GetTaggedFinishElementsInRoom(Room room, string role, IReadOnlyList<Element> candidates = null)
        {
            string targetRole = NormalizeGeneratedRole(role);
            if (room == null || string.IsNullOrWhiteSpace(targetRole)) return Enumerable.Empty<Element>();

            BoundingBoxXYZ roomBox = GetRoomPaddedBox(room);
            if (roomBox == null) return Enumerable.Empty<Element>();

            var source = candidates ?? GetTaggedFinishCandidates();
            var results = new List<Element>();

            foreach (Element element in source)
            {
                if (!TryReadGeneratedFinishTag(element, out string elementRole, out _)) continue;
                if (!targetRole.Equals(elementRole, StringComparison.OrdinalIgnoreCase)) continue;

                BoundingBoxXYZ elementBox = element.get_BoundingBox(null);
                if (elementBox == null) continue;
                if (BoxesIntersect(roomBox, elementBox)) results.Add(element);
            }

            return results;
        }

        private IReadOnlyList<Element> GetTaggedFinishCandidates()
        {
            var candidates = new List<Element>();
            var seen = new HashSet<ElementId>();
            BuiltInCategory[] categories =
            {
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Ceilings
            };

            foreach (BuiltInCategory category in categories)
            {
                foreach (Element element in new FilteredElementCollector(_doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType())
                {
                    if (element == null || !seen.Add(element.Id)) continue;
                    if (TryReadGeneratedFinishTag(element, out _, out _))
                    {
                        candidates.Add(element);
                    }
                }
            }

            return candidates;
        }

        private static BoundingBoxXYZ GetRoomPaddedBox(Room room)
        {
            BoundingBoxXYZ source = room?.get_BoundingBox(null);
            if (source == null) return null;

            return new BoundingBoxXYZ
            {
                Min = new XYZ(source.Min.X - 0.2, source.Min.Y - 0.2, source.Min.Z - 1.0),
                Max = new XYZ(source.Max.X + 0.2, source.Max.Y + 0.2, source.Max.Z + 1.0)
            };
        }

        private static bool BoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null) return false;

            return a.Min.X <= b.Max.X &&
                   a.Max.X >= b.Min.X &&
                   a.Min.Y <= b.Max.Y &&
                   a.Max.Y >= b.Min.Y &&
                   a.Min.Z <= b.Max.Z &&
                   a.Max.Z >= b.Min.Z;
        }

        private static bool TryReadGeneratedFinishTag(Element element, out string role, out string code)
        {
            role = null;
            code = null;
            if (element == null) return false;

            Parameter commentParam = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (commentParam == null || !commentParam.HasValue) return false;

            string comments = commentParam.AsString();
            if (string.IsNullOrWhiteSpace(comments)) return false;

            string rawRole = ReadTagValue(comments, "Role:");
            string rawCode = ReadTagValue(comments, "Code:");
            role = NormalizeGeneratedRole(rawRole);
            code = string.IsNullOrWhiteSpace(rawCode) ? null : rawCode.Trim();

            return !string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(code);
        }

        private static string ReadTagValue(string text, string prefix)
        {
            int start = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;

            start += prefix.Length;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;

            int end = start;
            while (end < text.Length)
            {
                char ch = text[end];
                if (ch == '|' || ch == ',' || ch == ';' || char.IsWhiteSpace(ch)) break;
                end++;
            }

            return end > start ? text.Substring(start, end - start).Trim() : null;
        }

        private static string NormalizeGeneratedRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role)) return null;

            string value = role.Trim();
            if (value.Equals("Floor", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("FloorFinish", StringComparison.OrdinalIgnoreCase))
                return "Floor";

            if (value.Equals("Wall", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("WallFinish", StringComparison.OrdinalIgnoreCase))
                return "Wall";

            if (value.Equals("Ceiling", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("CeilingFinish", StringComparison.OrdinalIgnoreCase))
                return "Ceiling";

            if (value.Equals("Baseboard", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("BaseboardFinish", StringComparison.OrdinalIgnoreCase))
                return "Baseboard";

            return value;
        }

        private void AddBoundaryWalls(Room room, string role, List<Element> results)
        {
            var boundarySegments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
            if (boundarySegments == null) return;

            foreach (IList<BoundarySegment> loop in boundarySegments)
            {
                foreach (BoundarySegment segment in loop)
                {
                    if (segment.ElementId == ElementId.InvalidElementId) continue;
                    Wall wall = _doc.GetElement(segment.ElementId) as Wall;
                    if (wall == null) continue;

                    bool isBaseboard = IsBaseboardWall(wall);
                    if (role.Equals("Baseboard", StringComparison.OrdinalIgnoreCase) && isBaseboard)
                        results.Add(wall);
                    else if (role.Equals("Wall", StringComparison.OrdinalIgnoreCase) && !isBaseboard)
                        results.Add(wall);
                }
            }
        }

        private static bool IsCeilingElement(Element element)
        {
            return element.Category != null &&
                   element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Ceilings;
        }

        private static bool IsBaseboardWall(Wall wall)
        {
            if (wall == null) return false;

            Parameter comment = wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            string text = comment != null && comment.HasValue ? comment.AsString() ?? string.Empty : string.Empty;
            if (text.IndexOf("BaseboardFinish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Role:Baseboard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            Parameter height = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            double value = height != null ? height.AsDouble() : 0.0;
            return value > 0 && value < 350.0 / 304.8;
        }

        private static bool WriteTypeCommentCode(ElementType elementType, string code)
        {
            if (elementType == null || string.IsNullOrWhiteSpace(code)) return false;

            Parameter typeComments = elementType.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS);
            if (typeComments == null || typeComments.IsReadOnly) return false;

            typeComments.Set(code.Trim());
            return true;
        }

        private void RefreshAllDetectedMaterials()
        {
            IReadOnlyList<Element> taggedFinishCandidates = GetTaggedFinishCandidates();

            foreach (RoomItemViewModel vm in Rooms)
            {
                if (_doc.GetElement(vm.Id) is Room room)
                {
                    DetectRoomMaterials(room, vm, taggedFinishCandidates);
                }
            }

            RunOnUi(NotifyConfigCodeLists);
        }

        private void DetectRoomMaterials(Room room, RoomItemViewModel vm, IReadOnlyList<Element> taggedFinishCandidates = null)
        {
            string floor = "未建置地板";
            string wall = "未建置牆面";
            string ceiling = "未建置天花板";
            string baseboard = "未建置踢腳板";

            var floorCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var wallCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ceilingCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var baseboardCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddGeneratedFinishCodes(room, floorCodes, wallCodes, ceilingCodes, baseboardCodes, taggedFinishCandidates);

            try
            {
                SpatialElementGeometryCalculator calculator = new SpatialElementGeometryCalculator(_doc);
                SpatialElementGeometryResults results = calculator.CalculateSpatialElementGeometry(room);
                Solid roomSolid = results.GetGeometry();

                foreach (Face roomFace in roomSolid.Faces)
                {
                    XYZ normal = roomFace.ComputeNormal(new UV(0.5, 0.5));
                    foreach (SpatialElementBoundarySubface faceInfo in results.GetBoundaryFaceInfo(roomFace))
                    {
                        LinkElementId boundaryLinkId = faceInfo.SpatialBoundaryElement;
                        if (boundaryLinkId == null || boundaryLinkId.HostElementId == ElementId.InvalidElementId) continue;

                        Element hostElement = _doc.GetElement(boundaryLinkId.HostElementId);
                        if (hostElement == null) continue;

                        if (hostElement is Floor floorElement)
                        {
                            string code = GetFinishTypeCode(floorElement.FloorType);
                            if (string.IsNullOrEmpty(code))
                                code = GetFinishMaterialName(floorElement.FloorType.GetCompoundStructure());

                            if (string.IsNullOrEmpty(code)) continue;
                            if (normal.Z < -0.5) floorCodes.Add(code);
                            else if (normal.Z > 0.5) ceilingCodes.Add(code);
                        }
                        else if (hostElement is Wall wallElement)
                        {
                            string code = GetFinishTypeCode(wallElement.WallType);
                            if (string.IsNullOrEmpty(code))
                                code = GetFinishMaterialName(wallElement.WallType.GetCompoundStructure());

                            if (string.IsNullOrEmpty(code)) continue;
                            if (IsBaseboardWall(wallElement)) baseboardCodes.Add(code);
                            else wallCodes.Add(code);
                        }
                        else if (IsCeilingElement(hostElement))
                        {
                            string code = GetFinishTypeCode(_doc.GetElement(hostElement.GetTypeId()) as ElementType);
                            if (!string.IsNullOrEmpty(code)) ceilingCodes.Add(code);
                        }
                    }
                }

            }
            catch
            {
                var boundarySegments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                if (boundarySegments != null)
                {
                    foreach (IList<BoundarySegment> loop in boundarySegments)
                    {
                        foreach (BoundarySegment segment in loop)
                        {
                            if (segment.ElementId == ElementId.InvalidElementId) continue;
                            Wall boundaryWall = _doc.GetElement(segment.ElementId) as Wall;
                            if (boundaryWall == null) continue;

                            string code = GetFinishTypeCode(boundaryWall.WallType);
                            if (!string.IsNullOrEmpty(code)) wallCodes.Add(code);
                        }
                    }
                }

            }

            if (floorCodes.Count > 0) floor = string.Join(", ", floorCodes);
            if (wallCodes.Count > 0) wall = string.Join(", ", wallCodes);
            if (ceilingCodes.Count > 0) ceiling = string.Join(", ", ceilingCodes);
            if (baseboardCodes.Count > 0) baseboard = string.Join(", ", baseboardCodes);

            SetDetectedFinishesOnUi(vm, floor, wall, ceiling, baseboard);
        }

        private void SetDetectedFinishesOnUi(RoomItemViewModel vm, string floor, string wall, string ceiling, string baseboard)
        {
            if (vm == null) return;
            RunOnUi(() => vm.SetDetectedFinishes(floor, wall, ceiling, baseboard));
        }

        private void AddGeneratedFinishCodes(
            Room room,
            HashSet<string> floorCodes,
            HashSet<string> wallCodes,
            HashSet<string> ceilingCodes,
            HashSet<string> baseboardCodes,
            IReadOnlyList<Element> taggedFinishCandidates)
        {
            var source = taggedFinishCandidates ?? GetTaggedFinishCandidates();
            BoundingBoxXYZ roomBox = GetRoomPaddedBox(room);
            if (roomBox == null) return;

            foreach (Element element in source)
            {
                if (!TryReadGeneratedFinishTag(element, out string role, out string code)) continue;

                BoundingBoxXYZ elementBox = element.get_BoundingBox(null);
                if (!BoxesIntersect(roomBox, elementBox)) continue;

                string typeCode = GetFinishTypeCode(_doc.GetElement(element.GetTypeId()) as ElementType);
                if (!string.IsNullOrWhiteSpace(typeCode)) code = typeCode;

                if (role.Equals("Floor", StringComparison.OrdinalIgnoreCase))
                    floorCodes.Add(code);
                else if (role.Equals("Wall", StringComparison.OrdinalIgnoreCase))
                    wallCodes.Add(code);
                else if (role.Equals("Ceiling", StringComparison.OrdinalIgnoreCase))
                    ceilingCodes.Add(code);
                else if (role.Equals("Baseboard", StringComparison.OrdinalIgnoreCase))
                    baseboardCodes.Add(code);
            }
        }

        private string GetFinishMaterialName(CompoundStructure compoundStructure)
        {
            if (compoundStructure == null) return null;

            for (int i = 0; i < compoundStructure.LayerCount; i++)
            {
                MaterialFunctionAssignment function = compoundStructure.GetLayerFunction(i);
                if (function != MaterialFunctionAssignment.Finish1 &&
                    function != MaterialFunctionAssignment.Finish2)
                {
                    continue;
                }

                string materialName = GetMaterialName(compoundStructure.GetMaterialId(i));
                if (!string.IsNullOrEmpty(materialName)) return materialName;
            }

            return compoundStructure.LayerCount > 0
                ? GetMaterialName(compoundStructure.GetMaterialId(0))
                : null;
        }

        private string GetMaterialName(ElementId materialId)
        {
            if (materialId == ElementId.InvalidElementId) return null;
            return (_doc.GetElement(materialId) as Material)?.Name;
        }

        private IEnumerable<string> GetConfigCodes(string type)
        {
            var codes = _finishConfigs.Values
                .Where(c => c != null && c.Type != null && c.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Code);

            IEnumerable<string> detectedCodes = Enumerable.Empty<string>();
            if (Rooms != null)
            {
                detectedCodes = Rooms.Select(r => GetDetectedByType(r, type))
                    .SelectMany(SplitDetectedCodes);
            }

            return codes.Concat(detectedCodes)
                .Where(c => !string.IsNullOrWhiteSpace(c) && !c.Contains("未建置") && !c.Contains("尚未檢查"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetDetectedByType(RoomItemViewModel room, string type)
        {
            if (type.Equals("Floor", StringComparison.OrdinalIgnoreCase)) return room.DetectedFloorFinish;
            if (type.Equals("Wall", StringComparison.OrdinalIgnoreCase)) return room.DetectedWallFinish;
            if (type.Equals("Ceiling", StringComparison.OrdinalIgnoreCase)) return room.DetectedCeilingFinish;
            if (type.Equals("Baseboard", StringComparison.OrdinalIgnoreCase)) return room.DetectedBaseboardFinish;
            return string.Empty;
        }

        private static IEnumerable<string> SplitDetectedCodes(string detected)
        {
            if (string.IsNullOrWhiteSpace(detected)) return Enumerable.Empty<string>();
            return detected.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim());
        }

        private void NotifyConfigCodeLists()
        {
            OnPropertyChanged(nameof(FloorConfigCodes));
            OnPropertyChanged(nameof(WallConfigCodes));
            OnPropertyChanged(nameof(CeilingConfigCodes));
            OnPropertyChanged(nameof(BaseboardConfigCodes));
        }

        private static string GetFinishTypeCode(ElementType elementType)
        {
            if (elementType == null) return null;

            try
            {
                Parameter typeComments = elementType.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS);
                if (typeComments == null || !typeComments.HasValue) return null;

                string raw = typeComments.AsString();
                if (string.IsNullOrWhiteSpace(raw)) return null;

                string value = raw.Trim();
                int codeIndex = value.IndexOf("Code:", StringComparison.OrdinalIgnoreCase);
                if (codeIndex >= 0)
                {
                    value = value.Substring(codeIndex + "Code:".Length).Trim();
                    int stopIndex = value.IndexOfAny(new[] { '|', ',', ';', '\r', '\n', '\t', ' ' });
                    if (stopIndex >= 0)
                    {
                        value = value.Substring(0, stopIndex).Trim();
                    }
                }
                else
                {
                    // 若無 Code: 標籤，取第一段作為代號 (容許後方帶有空白說明文字)
                    int spaceIndex = value.IndexOf(' ');
                    if (spaceIndex > 0)
                    {
                        value = value.Substring(0, spaceIndex).Trim();
                    }
                }

                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        private static string RoleDisplayName(string role)
        {
            if (role.Equals("Floor", StringComparison.OrdinalIgnoreCase)) return "地板";
            if (role.Equals("Wall", StringComparison.OrdinalIgnoreCase)) return "牆面";
            if (role.Equals("Ceiling", StringComparison.OrdinalIgnoreCase)) return "天花板";
            if (role.Equals("Baseboard", StringComparison.OrdinalIgnoreCase)) return "踢腳板";
            return role;
        }

        public void Dispose()
        {
            _revitEvents?.Dispose();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

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

    public class RelayCommandParam : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommandParam(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
