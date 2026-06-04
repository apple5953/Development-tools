using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.SheetTools.SheetViewPlacer
{
    /// <summary>
    /// 樹狀圖節點 ViewModel
    /// </summary>
    public class TreeItemViewModel : INotifyPropertyChanged
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        
        /// <summary>
        /// 節點類型: "SheetGroup", "Sheet", "UnplacedGroup", "ViewTypeGroup", "View", "Schedule"
        /// </summary>
        public string Type { get; set; }
        public string ViewTypeStr { get; set; }
        public string SheetNumber { get; set; }

        public bool IsGroup => Type == "SheetGroup" || Type == "UnplacedGroup" || Type == "ViewTypeGroup" || Type == "Sheet";
        public bool IsView => Type == "View" || Type == "Schedule";

        private string _rawName;
        public string RawName
        {
            get => _rawName;
            set { _rawName = value; OnPropertyChanged(); }
        }

        private string _editText;
        public string EditText
        {
            get => _editText;
            set { _editText = value; OnPropertyChanged(); }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set { _isEditing = value; OnPropertyChanged(); }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public TreeItemViewModel Parent { get; set; }
        public ObservableCollection<TreeItemViewModel> Children { get; } = new ObservableCollection<TreeItemViewModel>();

        public TreeItemViewModel()
        {
            _isExpanded = true;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 下拉選單項目 ViewModel
    /// </summary>
    public class ComboboxItemViewModel : INotifyPropertyChanged
    {
        public ElementId Id { get; }
        public string Name { get; }

        public ComboboxItemViewModel(ElementId id, string name)
        {
            Id = id;
            Name = name;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 圖紙視圖排版主 ViewModel
    /// </summary>
    public class SheetViewPlacerViewModel : INotifyPropertyChanged
    {
        private readonly Document _doc;
        public Document Doc => _doc;
        private Window _window;

        public ObservableCollection<TreeItemViewModel> TreeItems { get; } = new ObservableCollection<TreeItemViewModel>();
        public ObservableCollection<ComboboxItemViewModel> TitleBlocks { get; } = new ObservableCollection<ComboboxItemViewModel>();
        public ObservableCollection<ComboboxItemViewModel> ViewportTypes { get; } = new ObservableCollection<ComboboxItemViewModel>();

        private ComboboxItemViewModel _selectedTitleBlock;
        public ComboboxItemViewModel SelectedTitleBlock
        {
            get => _selectedTitleBlock;
            set { _selectedTitleBlock = value; OnPropertyChanged(); }
        }

        private ComboboxItemViewModel _selectedViewportType;
        public ComboboxItemViewModel SelectedViewportType
        {
            get => _selectedViewportType;
            set { _selectedViewportType = value; OnPropertyChanged(); }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        private ObservableCollection<PlacedViewItemViewModel> _placedViews = new ObservableCollection<PlacedViewItemViewModel>();
        public ObservableCollection<PlacedViewItemViewModel> PlacedViews
        {
            get => _placedViews;
            set { _placedViews = value; OnPropertyChanged(); }
        }

        private TreeItemViewModel _selectedSheetNode;
        public TreeItemViewModel SelectedSheetNode
        {
            get => _selectedSheetNode;
            set
            {
                _selectedSheetNode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedSheet));
                OnPropertyChanged(nameof(ShowInstructions));
                LoadPlacedViewsForSelectedSheet();
            }
        }

        public bool HasSelectedSheet => SelectedSheetNode != null;
        public bool ShowInstructions => SelectedSheetNode == null;

        // 快取未過濾的原始樹狀圖
        private TreeItemViewModel _rawSheetsGroup;
        private TreeItemViewModel _rawUnplacedGroup;

        public SheetViewPlacerViewModel(Document doc, Window window)
        {
            _doc = doc;
            _window = window;

            LoadComboboxes();
            LoadData();
        }

        /// <summary>
        /// 載入下拉選單數據 (圖框與視埠類型)
        /// </summary>
        private void LoadComboboxes()
        {
            TitleBlocks.Clear();
            ViewportTypes.Clear();

            // 1. 載入圖框類型
            var tbs = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .OrderBy(x => x.Name)
                .ToList();

            TitleBlocks.Add(new ComboboxItemViewModel(ElementId.InvalidElementId, "<無圖框 - 建立空白圖紙>"));
            foreach (var tb in tbs)
            {
                TitleBlocks.Add(new ComboboxItemViewModel(tb.Id, $"{tb.FamilyName}: {tb.Name}"));
            }
            SelectedTitleBlock = TitleBlocks.FirstOrDefault();

            // 2. 載入視埠類型 (Viewport Types)
            var vps = new FilteredElementCollector(_doc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .Where(x => (x.FamilyName != null && (x.FamilyName.Equals("Viewport", StringComparison.OrdinalIgnoreCase) 
                                                  || x.FamilyName.Equals("視埠", StringComparison.OrdinalIgnoreCase)
                                                  || x.FamilyName.Equals("視口", StringComparison.OrdinalIgnoreCase)))
                         || (x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Viewports))
                .OrderBy(x => x.Name)
                .ToList();

            foreach (var vp in vps)
            {
                ViewportTypes.Add(new ComboboxItemViewModel(vp.Id, vp.Name));
            }

            // 嘗試選取預設的視埠類型
            SelectedViewportType = ViewportTypes.FirstOrDefault();
        }

        private static readonly Dictionary<ViewType, int> ViewTypeOrder = new Dictionary<ViewType, int>
        {
            { ViewType.FloorPlan, 1 },
            { ViewType.CeilingPlan, 2 },
            { ViewType.ThreeD, 3 },
            { ViewType.Elevation, 4 },
            { ViewType.Section, 5 },
            { ViewType.Rendering, 6 },
            { ViewType.DraftingView, 7 },
            { ViewType.Legend, 8 },
            { ViewType.Schedule, 9 }
        };

        private int GetViewTypeOrder(ViewType type)
        {
            return ViewTypeOrder.TryGetValue(type, out var val) ? val : 99;
        }

        /// <summary>
        /// 載入樹狀圖拓撲結構
        /// </summary>
        public void LoadData()
        {
            TreeItems.Clear();

            // 1. 取得所有圖紙
            var sheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .ToList();

            // 2. 統計已被放置的視圖 Id (包含明細表) 並對應到圖紙編號
            var viewToSheetNumbers = new Dictionary<ElementId, List<string>>();
            var viewports = new FilteredElementCollector(_doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .ToList();

            foreach (var vp in viewports)
            {
                var sheet = _doc.GetElement(vp.SheetId) as ViewSheet;
                if (sheet != null)
                {
                    if (!viewToSheetNumbers.TryGetValue(vp.ViewId, out var sheetNums))
                    {
                        sheetNums = new List<string>();
                        viewToSheetNumbers[vp.ViewId] = sheetNums;
                    }
                    if (!sheetNums.Contains(sheet.SheetNumber))
                        sheetNums.Add(sheet.SheetNumber);
                }
            }

            var scheduleInstances = new FilteredElementCollector(_doc)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>()
                .ToList();

            foreach (var si in scheduleInstances)
            {
                var sheet = _doc.GetElement(si.OwnerViewId) as ViewSheet;
                if (sheet != null)
                {
                    if (!viewToSheetNumbers.TryGetValue(si.ScheduleId, out var sheetNums))
                    {
                        sheetNums = new List<string>();
                        viewToSheetNumbers[si.ScheduleId] = sheetNums;
                    }
                    if (!sheetNums.Contains(sheet.SheetNumber))
                        sheetNums.Add(sheet.SheetNumber);
                }
            }

            // 3. 建立「圖紙清單」根節點
            _rawSheetsGroup = new TreeItemViewModel
            {
                Name = "圖紙清單 (Sheets)",
                Type = "SheetGroup"
            };

            foreach (var sheet in sheets)
            {
                var sheetNode = new TreeItemViewModel
                {
                    Id = sheet.Id,
                    Name = $"[{sheet.SheetNumber}] {sheet.Name}",
                    RawName = sheet.Name,
                    EditText = sheet.Name,
                    SheetNumber = sheet.SheetNumber,
                    Type = "Sheet",
                    Parent = _rawSheetsGroup
                };

                var sheetChildrenList = new List<TreeItemViewModel>();

                // 3.1 載入此圖紙上的普通視埠視圖
                var vpsOnSheet = viewports.Where(vp => vp.SheetId == sheet.Id).ToList();
                foreach (var vp in vpsOnSheet)
                {
                    var view = _doc.GetElement(vp.ViewId) as View;
                    if (view == null) continue;

                    sheetChildrenList.Add(new TreeItemViewModel
                    {
                        Id = view.Id,
                        Name = $"[{GetViewTypeName(view.ViewType)}] {view.Name}",
                        RawName = view.Name,
                        EditText = view.Name,
                        Type = "View",
                        ViewTypeStr = GetViewTypeName(view.ViewType),
                        Parent = sheetNode
                    });
                }

                // 3.2 載入此圖紙上的明細表視圖
                var sisOnSheet = scheduleInstances.Where(si => si.OwnerViewId == sheet.Id).ToList();
                foreach (var si in sisOnSheet)
                {
                    var sched = _doc.GetElement(si.ScheduleId) as ViewSchedule;
                    if (sched == null || sched.IsTitleblockRevisionSchedule) continue;

                    sheetChildrenList.Add(new TreeItemViewModel
                    {
                        Id = sched.Id,
                        Name = $"[明細表] {sched.Name}",
                        RawName = sched.Name,
                        EditText = sched.Name,
                        Type = "Schedule",
                        ViewTypeStr = "明細表",
                        Parent = sheetNode
                    });
                }

                // 依專案瀏覽器邏輯排序圖紙內的視圖
                var sortedSheetChildren = sheetChildrenList
                    .OrderBy(item => {
                        var view = _doc.GetElement(item.Id) as View;
                        return view != null ? GetViewTypeOrder(view.ViewType) : 99;
                    })
                    .ThenBy(item => item.RawName);

                foreach (var childNode in sortedSheetChildren)
                {
                    sheetNode.Children.Add(childNode);
                }

                _rawSheetsGroup.Children.Add(sheetNode);
            }

            // 4. 建立「所有視圖」根節點
            _rawUnplacedGroup = new TreeItemViewModel
            {
                Name = "所有視圖 (All Views)",
                Type = "UnplacedGroup"
            };

            var allViews = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .ToList();

            var placeableViews = allViews.Where(v => IsPlaceableView(v)).ToList();

            // 依視圖類型分組
            var groupedViews = placeableViews.GroupBy(v => v.ViewType).OrderBy(g => GetViewTypeOrder(g.Key));
            foreach (var group in groupedViews)
            {
                var viewTypeGroupNode = new TreeItemViewModel
                {
                    Name = $"{GetViewTypeName(group.Key)} ({group.Count()})",
                    Type = "ViewTypeGroup",
                    Parent = _rawUnplacedGroup
                };

                foreach (var view in group.OrderBy(v => v.Name))
                {
                    string displayName = view.Name;
                    if (viewToSheetNumbers.TryGetValue(view.Id, out var sheetNums) && sheetNums.Count > 0)
                    {
                        displayName = $"{view.Name} [已放置於 {string.Join(", ", sheetNums)}]";
                    }

                    viewTypeGroupNode.Children.Add(new TreeItemViewModel
                    {
                        Id = view.Id,
                        Name = displayName,
                        RawName = view.Name,
                        EditText = view.Name,
                        Type = view is ViewSchedule ? "Schedule" : "View",
                        ViewTypeStr = GetViewTypeName(view.ViewType),
                        Parent = viewTypeGroupNode
                    });
                }

                _rawUnplacedGroup.Children.Add(viewTypeGroupNode);
            }

            // 加入顯示樹
            TreeItems.Add(_rawSheetsGroup);
            TreeItems.Add(_rawUnplacedGroup);

            ApplyFilter();
        }

        /// <summary>
        /// 套用即時關鍵字搜尋過濾
        /// </summary>
        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                // 無搜尋字串，還原顯示
                ResetVisibility(TreeItems);
                return;
            }

            string keyword = SearchText.Trim().ToLower();

            // 對「圖紙清單」與「所有視圖」下的節點進行過濾
            FilterNode(_rawSheetsGroup, keyword);
            FilterNode(_rawUnplacedGroup, keyword);
        }

        private bool FilterNode(TreeItemViewModel node, string keyword)
        {
            bool anyChildVisible = false;

            foreach (var child in node.Children)
            {
                if (child.Type == "Sheet" || child.Type == "ViewTypeGroup")
                {
                    // 檢查子節點 (視圖)
                    bool matchAnySubChild = false;
                    foreach (var subChild in child.Children)
                    {
                        bool isMatch = (subChild.Name ?? "").ToLower().Contains(keyword);
                        subChild.IsSelected = false; // 取消選取高亮
                        if (isMatch) matchAnySubChild = true;
                    }

                    bool parentMatch = (child.Name ?? "").ToLower().Contains(keyword);
                    if (parentMatch || matchAnySubChild)
                    {
                        child.IsExpanded = true;
                        anyChildVisible = true;
                    }
                }
            }

            return anyChildVisible;
        }

        private void ResetVisibility(ObservableCollection<TreeItemViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                node.IsExpanded = true;
                ResetVisibility(node.Children);
            }
        }

        /// <summary>
        /// 判斷視圖是否可放置至圖紙
        /// </summary>
        private bool IsPlaceableView(View view)
        {
            if (view == null || view.IsTemplate) return false;
            
            // 排除圖紙本身
            if (view is ViewSheet) return false;

            if (view is ViewSchedule sched && sched.IsTitleblockRevisionSchedule) return false;

            // 支持的視圖類型
            var t = view.ViewType;
            return t == ViewType.FloorPlan ||
                   t == ViewType.CeilingPlan ||
                   t == ViewType.Elevation ||
                   t == ViewType.Section ||
                   t == ViewType.ThreeD ||
                   t == ViewType.DraftingView ||
                   t == ViewType.Legend ||
                   t == ViewType.Schedule ||
                   t == ViewType.Rendering;
        }

        /// <summary>
        /// 取得視圖類型的中文名稱
        /// </summary>
        private string GetViewTypeName(ViewType type)
        {
            switch (type)
            {
                case ViewType.FloorPlan: return "平面圖";
                case ViewType.CeilingPlan: return "天花板平面圖";
                case ViewType.Elevation: return "立面圖";
                case ViewType.Section: return "剖面圖";
                case ViewType.ThreeD: return "3D 視圖";
                case ViewType.DraftingView: return "繪圖視圖";
                case ViewType.Legend: return "圖例";
                case ViewType.Schedule: return "明細表";
                case ViewType.Rendering: return "彩現";
                default: return type.ToString();
            }
        }

        #region Revit API 寫入操作 (Transaction)

        /// <summary>
        /// 將未放置的視圖放入圖紙中 (若已放置在其他圖紙，則會自動移轉位置)
        /// </summary>
        public bool PlaceViewOnSheet(ElementId viewId, ElementId targetSheetId)
        {
            var view = _doc.GetElement(viewId) as View;
            var sheet = _doc.GetElement(targetSheetId) as ViewSheet;
            if (view == null || sheet == null) return false;

            // 檢查是否為普通視圖，且已經放置在其他圖紙上，若是則自動執行移轉
            if (!(view is ViewSchedule))
            {
                var existingVp = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .FirstOrDefault(vp => vp.ViewId == view.Id);

                if (existingVp != null)
                {
                    if (existingVp.SheetId == targetSheetId)
                    {
                        return true;
                    }
                    return MoveViewBetweenSheets(view.Id, existingVp.SheetId, targetSheetId);
                }
            }

            try
            {
                using (Transaction trans = new Transaction(_doc, "放置視圖至圖紙"))
                {
                    trans.Start();

                    XYZ location = CalculateViewportLocation(sheet);

                    if (view is ViewSchedule schedule)
                    {
                        ScheduleSheetInstance.Create(_doc, sheet.Id, schedule.Id, location);
                    }
                    else
                    {
                        if (!Viewport.CanAddViewToSheet(_doc, sheet.Id, view.Id))
                        {
                            MessageBox.Show("此視圖無法放置在此圖紙上！\n請確定它是否已放置於其他圖紙，或其類型是否支援多張圖紙放置。", 
                                "放置失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
                            trans.RollBack();
                            return false;
                        }

                        Viewport vp = Viewport.Create(_doc, sheet.Id, view.Id, location);
                        if (SelectedViewportType != null && SelectedViewportType.Id != ElementId.InvalidElementId)
                        {
                            vp.ChangeTypeId(SelectedViewportType.Id);
                        }
                    }

                    trans.Commit();
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"放置視圖失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 重新命名 Revit 元素 (圖紙或視圖)
        /// </summary>
        public bool RenameElement(ElementId id, string newName)
        {
            if (id == null || id == ElementId.InvalidElementId) return false;
            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("名稱不可為空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var elem = _doc.GetElement(id);
            if (elem == null) return false;

            try
            {
                using (Transaction trans = new Transaction(_doc, "重新命名元素"))
                {
                    trans.Start();
                    
                    if (elem is ViewSheet sheet)
                    {
                        sheet.Name = newName;
                    }
                    else if (elem is View view)
                    {
                        view.Name = newName;
                    }
                    
                    trans.Commit();
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重新命名失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 在兩張圖紙之間移動視圖 (跨圖紙調整)
        /// </summary>
        public bool MoveViewBetweenSheets(ElementId viewId, ElementId sourceSheetId, ElementId targetSheetId)
        {
            if (sourceSheetId == targetSheetId) return false;

            var view = _doc.GetElement(viewId) as View;
            var targetSheet = _doc.GetElement(targetSheetId) as ViewSheet;
            if (view == null || targetSheet == null) return false;

            try
            {
                using (Transaction trans = new Transaction(_doc, "跨圖紙移轉視圖"))
                {
                    trans.Start();

                    // 1. 尋找並刪除在原圖紙上的視埠/明細表實例
                    if (view is ViewSchedule)
                    {
                        var existingSched = new FilteredElementCollector(_doc)
                            .OfClass(typeof(ScheduleSheetInstance))
                            .Cast<ScheduleSheetInstance>()
                            .FirstOrDefault(si => si.ScheduleId == view.Id && si.OwnerViewId == sourceSheetId);
                        
                        if (existingSched != null)
                        {
                            _doc.Delete(existingSched.Id);
                        }
                    }
                    else
                    {
                        var existingVp = new FilteredElementCollector(_doc)
                            .OfClass(typeof(Viewport))
                            .Cast<Viewport>()
                            .FirstOrDefault(vp => vp.ViewId == view.Id && vp.SheetId == sourceSheetId);

                        if (existingVp != null)
                        {
                            _doc.Delete(existingVp.Id);
                        }
                    }

                    // 2. 重新在新圖紙上建立視埠/明細表
                    XYZ location = CalculateViewportLocation(targetSheet);

                    if (view is ViewSchedule schedule)
                    {
                        ScheduleSheetInstance.Create(_doc, targetSheet.Id, schedule.Id, location);
                    }
                    else
                    {
                        if (!Viewport.CanAddViewToSheet(_doc, targetSheet.Id, view.Id))
                        {
                            MessageBox.Show("此視圖無法放置在此圖紙上！", "放置失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
                            trans.RollBack();
                            return false;
                        }

                        Viewport vp = Viewport.Create(_doc, targetSheet.Id, view.Id, location);
                        if (SelectedViewportType != null && SelectedViewportType.Id != ElementId.InvalidElementId)
                        {
                            vp.ChangeTypeId(SelectedViewportType.Id);
                        }
                    }

                    trans.Commit();
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"移轉視圖失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 從圖紙中移除視圖 (刪除視埠)
        /// </summary>
        public bool RemoveViewFromSheet(ElementId viewId, ElementId sheetId)
        {
            var view = _doc.GetElement(viewId) as View;
            if (view == null) return false;

            try
            {
                using (Transaction trans = new Transaction(_doc, "移除圖紙視圖"))
                {
                    trans.Start();

                    if (view is ViewSchedule)
                    {
                        var existingSched = new FilteredElementCollector(_doc)
                            .OfClass(typeof(ScheduleSheetInstance))
                            .Cast<ScheduleSheetInstance>()
                            .FirstOrDefault(si => si.ScheduleId == view.Id && si.OwnerViewId == sheetId);
                        
                        if (existingSched != null)
                        {
                            _doc.Delete(existingSched.Id);
                        }
                    }
                    else
                    {
                        var existingVp = new FilteredElementCollector(_doc)
                            .OfClass(typeof(Viewport))
                            .Cast<Viewport>()
                            .FirstOrDefault(vp => vp.ViewId == view.Id && vp.SheetId == sheetId);

                        if (existingVp != null)
                        {
                            _doc.Delete(existingVp.Id);
                        }
                    }

                    trans.Commit();
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"移除視圖失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 一鍵新建一張圖紙
        /// </summary>
        public bool CreateNewSheet(string number, string name)
        {
            if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("圖紙編號與名稱不可為空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // 檢查編號唯一性
            bool numberExists = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Any(s => s.SheetNumber.Equals(number, StringComparison.OrdinalIgnoreCase));

            if (numberExists)
            {
                MessageBox.Show($"圖紙編號 [{number}] 已在專案中存在！請換一個編號。", "編號重複", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                using (Transaction trans = new Transaction(_doc, "新建圖紙"))
                {
                    trans.Start();

                    ElementId tbId = SelectedTitleBlock != null ? SelectedTitleBlock.Id : ElementId.InvalidElementId;
                    ViewSheet sheet = ViewSheet.Create(_doc, tbId);
                    if (sheet != null)
                    {
                        sheet.SheetNumber = number;
                        sheet.Name = name;
                    }

                    trans.Commit();
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"建立圖紙失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 計算新視埠在圖紙上的置入位置，避免與現有視埠完美重疊
        /// </summary>
        private XYZ CalculateViewportLocation(ViewSheet sheet)
        {
            var viewports = new FilteredElementCollector(_doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Where(vp => vp.SheetId == sheet.Id)
                .ToList();

            int count = viewports.Count;
            if (count == 0)
            {
                return new XYZ(0, 0, 0); // 置中
            }

            // 每多一個視埠，就往右上方稍微偏移 0.15 呎，方便使用者手動拉開
            double offset = count * 0.15;
            return new XYZ(offset, offset, 0);
        }

        /// <summary>
        /// 自動推算下一個推薦的圖紙編號 (依據專案最大編號後綴遞增)
        /// </summary>
        public string SuggestNextSheetNumber()
        {
            var sheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            if (sheets.Count == 0) return "A-101";

            // 尋找最大編號
            var sortedNumbers = sheets
                .Select(s => s.SheetNumber)
                .OrderBy(n => n)
                .ToList();

            string maxNum = sortedNumbers.LastOrDefault();
            if (string.IsNullOrEmpty(maxNum)) return "A-101";

            // 尋找最後部分的數字進行加一
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(maxNum, @"\d+$");
            if (match.Success)
            {
                string numStr = match.Value;
                int val = int.Parse(numStr) + 1;
                string format = new string('0', numStr.Length);
                string prefix = maxNum.Substring(0, maxNum.Length - numStr.Length);
                return prefix + val.ToString(format);
            }

            return maxNum + "-1";
        }

        /// <summary>
        /// 載入目前選中圖紙上的所有已放置視圖與明細表
        /// </summary>
        public void LoadPlacedViewsForSelectedSheet()
        {
            PlacedViews.Clear();
            if (SelectedSheetNode == null || SelectedSheetNode.Type != "Sheet") return;

            var sheetId = SelectedSheetNode.Id;

            // 1. 取得普通視圖 Viewports
            var viewportsOnSheet = new FilteredElementCollector(_doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Where(vp => vp.SheetId == sheetId)
                .ToList();

            foreach (var vp in viewportsOnSheet)
            {
                var view = _doc.GetElement(vp.ViewId) as View;
                if (view == null) continue;

                XYZ center = vp.GetBoxCenter();
                PlacedViews.Add(new PlacedViewItemViewModel
                {
                    ViewId = view.Id,
                    ElementId = vp.Id,
                    Name = $"[{GetViewTypeName(view.ViewType)}] {view.Name}",
                    Type = "Viewport",
                    XMm = Math.Round(center.X * 304.8, 1),
                    YMm = Math.Round(center.Y * 304.8, 1)
                });
            }

            // 2. 取得明細表 ScheduleSheetInstances
            var scheduleInstancesOnSheet = new FilteredElementCollector(_doc)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>()
                .Where(si => si.OwnerViewId == sheetId)
                .ToList();

            foreach (var si in scheduleInstancesOnSheet)
            {
                var sched = _doc.GetElement(si.ScheduleId) as ViewSchedule;
                if (sched == null || sched.IsTitleblockRevisionSchedule) continue;

                XYZ point = si.Point;
                PlacedViews.Add(new PlacedViewItemViewModel
                {
                    ViewId = sched.Id,
                    ElementId = si.Id,
                    Name = $"[明細表] {sched.Name}",
                    Type = "Schedule",
                    XMm = Math.Round(point.X * 304.8, 1),
                    YMm = Math.Round(point.Y * 304.8, 1)
                });
            }
        }

        /// <summary>
        /// 調整單一已放置視圖的位置
        /// </summary>
        public bool UpdatePlacedViewPosition(PlacedViewItemViewModel item, double newXMm, double newYMm)
        {
            var elem = _doc.GetElement(item.ElementId);
            if (elem == null) return false;

            double xFeet = newXMm / 304.8;
            double yFeet = newYMm / 304.8;

            try
            {
                using (Transaction trans = new Transaction(_doc, "調整視圖位置"))
                {
                    trans.Start();
                    if (elem is Viewport vp)
                    {
                        vp.SetBoxCenter(new XYZ(xFeet, yFeet, 0));
                    }
                    else if (elem is ScheduleSheetInstance si)
                    {
                        si.Point = new XYZ(xFeet, yFeet, 0);
                    }
                    trans.Commit();
                }

                item.XMm = Math.Round(newXMm, 1);
                item.YMm = Math.Round(newYMm, 1);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新位置失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 微調/偏移多個視圖的位置
        /// </summary>
        public void OffsetPlacedViews(IEnumerable<PlacedViewItemViewModel> items, double dxMm, double dyMm)
        {
            if (items == null || !items.Any()) return;

            try
            {
                using (Transaction trans = new Transaction(_doc, "微調視圖位置"))
                {
                    trans.Start();
                    foreach (var item in items)
                    {
                        var elem = _doc.GetElement(item.ElementId);
                        if (elem == null) continue;

                        double newXMm = item.XMm + dxMm;
                        double newYMm = item.YMm + dyMm;
                        double xFeet = newXMm / 304.8;
                        double yFeet = newYMm / 304.8;

                        if (elem is Viewport vp)
                        {
                            vp.SetBoxCenter(new XYZ(xFeet, yFeet, 0));
                        }
                        else if (elem is ScheduleSheetInstance si)
                        {
                            si.Point = new XYZ(xFeet, yFeet, 0);
                        }

                        item.XMm = Math.Round(newXMm, 1);
                        item.YMm = Math.Round(newYMm, 1);
                    }
                    trans.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"微調位置失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 對齊多個選中的視圖
        /// </summary>
        public void AlignPlacedViews(IEnumerable<PlacedViewItemViewModel> items, string alignType)
        {
            if (items == null || items.Count() < 2)
            {
                MessageBox.Show("請至少選擇兩個視圖進行對齊！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                double targetVal = 0;
                switch (alignType)
                {
                    case "Left":
                        targetVal = items.Min(x => x.XMm);
                        break;
                    case "Right":
                        targetVal = items.Max(x => x.XMm);
                        break;
                    case "Top":
                        targetVal = items.Max(x => x.YMm);
                        break;
                    case "Bottom":
                        targetVal = items.Min(x => x.YMm);
                        break;
                    case "HCenter":
                        targetVal = items.Average(x => x.YMm);
                        break;
                    case "VCenter":
                        targetVal = items.Average(x => x.XMm);
                        break;
                }

                using (Transaction trans = new Transaction(_doc, "對齊視圖"))
                {
                    trans.Start();
                    foreach (var item in items)
                    {
                        var elem = _doc.GetElement(item.ElementId);
                        if (elem == null) continue;

                        double newXMm = item.XMm;
                        double newYMm = item.YMm;

                        if (alignType == "Left" || alignType == "Right" || alignType == "VCenter")
                        {
                            newXMm = targetVal;
                        }
                        else
                        {
                            newYMm = targetVal;
                        }

                        double xFeet = newXMm / 304.8;
                        double yFeet = newYMm / 304.8;

                        if (elem is Viewport vp)
                        {
                            vp.SetBoxCenter(new XYZ(xFeet, yFeet, 0));
                        }
                        else if (elem is ScheduleSheetInstance si)
                        {
                            si.Point = new XYZ(xFeet, yFeet, 0);
                        }

                        item.XMm = Math.Round(newXMm, 1);
                        item.YMm = Math.Round(newYMm, 1);
                    }
                    trans.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"對齊失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 自動將選中的視圖按網格排列
        /// </summary>
        public void ArrangeViewsInGrid(IEnumerable<PlacedViewItemViewModel> items, int rows, int cols, double gapXMm, double gapYMm, double startXMm, double startYMm)
        {
            if (items == null || !items.Any()) return;
            if (rows <= 0 || cols <= 0) return;

            var itemList = items.ToList();

            try
            {
                using (Transaction trans = new Transaction(_doc, "網格排列視圖"))
                {
                    trans.Start();

                    for (int i = 0; i < itemList.Count; i++)
                    {
                        var item = itemList[i];
                        var elem = _doc.GetElement(item.ElementId);
                        if (elem == null) continue;

                        int r = i / cols;
                        int c = i % cols;

                        if (r >= rows) break;

                        // Revit 的 Y 軸向上，向下折行 Y 遞減
                        double newXMm = startXMm + c * gapXMm;
                        double newYMm = startYMm - r * gapYMm;

                        double xFeet = newXMm / 304.8;
                        double yFeet = newYMm / 304.8;

                        if (elem is Viewport vp)
                        {
                            vp.SetBoxCenter(new XYZ(xFeet, yFeet, 0));
                        }
                        else if (elem is ScheduleSheetInstance si)
                        {
                            si.Point = new XYZ(xFeet, yFeet, 0);
                        }

                        item.XMm = Math.Round(newXMm, 1);
                        item.YMm = Math.Round(newYMm, 1);
                    }

                    trans.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"網格排列失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
