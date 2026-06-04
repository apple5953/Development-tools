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

namespace DevelopmentTools.Modules.SheetTools.QuickViewCreator
{
    /// <summary>
    /// 專案瀏覽器樹狀結構節點 ViewModel
    /// </summary>
    public class ViewTreeItemViewModel : INotifyPropertyChanged
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; } // "Group" or "View"
        public string ViewTypeStr { get; set; }

        public bool IsGroup => Type == "Group";
        public bool IsView => Type == "View";

        private bool _isUpdatingSelection = false;
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();

                if (Type == "Group" && !_isUpdatingSelection)
                {
                    _isUpdatingSelection = true;
                    foreach (var child in Children)
                    {
                        child.IsSelected = _isSelected;
                    }
                    _isUpdatingSelection = false;
                }

                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ViewTreeItemViewModel> Children { get; } = new ObservableCollection<ViewTreeItemViewModel>();

        public event EventHandler SelectionChanged;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 視圖樣板規則 ViewModel
    /// </summary>
    public class TemplateRuleViewModel : INotifyPropertyChanged
    {
        public ElementId Id { get; }
        public string Name { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); RuleChanged?.Invoke(this, EventArgs.Empty); }
        }

        private string _suffix;
        public string Suffix
        {
            get => _suffix;
            set { _suffix = value; OnPropertyChanged(); RuleChanged?.Invoke(this, EventArgs.Empty); }
        }

        public event EventHandler RuleChanged;

        public TemplateRuleViewModel(View template)
        {
            Id = template.Id;
            Name = template.Name;
            _isSelected = false;
            _suffix = $" - {template.Name}";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 即時開圖與更名預覽項目 ViewModel
    /// </summary>
    public class ViewPreviewItemViewModel : INotifyPropertyChanged
    {
        public ElementId SourceViewId { get; set; }
        public string SourceViewName { get; set; }
        public ElementId TemplateId { get; set; }
        public string TemplateName { get; set; }

        private string _targetViewName;
        public string TargetViewName
        {
            get => _targetViewName;
            set { _targetViewName = value; OnPropertyChanged(); }
        }

        private bool _createSheet;
        public bool CreateSheet
        {
            get => _createSheet;
            set { _createSheet = value; OnPropertyChanged(); }
        }

        private string _sheetNumber;
        public string SheetNumber
        {
            get => _sheetNumber;
            set { _sheetNumber = value; OnPropertyChanged(); }
        }

        private string _sheetName;
        public string SheetName
        {
            get => _sheetName;
            set { _sheetName = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 快速開圖與套樣板工具的主 ViewModel
    /// </summary>
    public class QuickViewCreatorViewModel : INotifyPropertyChanged
    {
        private readonly Document _doc;
        private readonly Window _window;
        private readonly List<ViewPlan> _allViews;
        private readonly HashSet<ElementId> _selectedSourceViewIds = new HashSet<ElementId>();
        private bool _isBatchUpdating = false;

        public ObservableCollection<ViewTreeItemViewModel> TreeItems { get; } = new ObservableCollection<ViewTreeItemViewModel>();
        public ObservableCollection<TemplateRuleViewModel> Templates { get; } = new ObservableCollection<TemplateRuleViewModel>();
        public ObservableCollection<ViewPreviewItemViewModel> PreviewItems { get; } = new ObservableCollection<ViewPreviewItemViewModel>();

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                RebuildTree();
            }
        }

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private int _duplicateMode = 1; // 預設為複製詳圖 (With Detailing)
        public int DuplicateMode
        {
            get => _duplicateMode;
            set { _duplicateMode = value; OnPropertyChanged(); }
        }

        private bool _createSheets = false;
        public bool CreateSheets
        {
            get => _createSheets;
            set
            {
                _createSheets = value;
                OnPropertyChanged();
                foreach (var item in PreviewItems)
                {
                    item.CreateSheet = _createSheets;
                }
            }
        }

        // 命令
        public ICommand CreateViewsCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ToggleSelectAllViewsCommand { get; }

        private bool _allViewsSelected;
        public bool AllViewsSelected
        {
            get => _allViewsSelected;
            set
            {
                _allViewsSelected = value;
                OnPropertyChanged();
                _isBatchUpdating = true;
                foreach (var groupNode in TreeItems)
                {
                    groupNode.IsSelected = _allViewsSelected;
                    foreach (var child in groupNode.Children)
                    {
                        child.IsSelected = _allViewsSelected;
                        if (_allViewsSelected)
                            _selectedSourceViewIds.Add(child.Id);
                        else
                            _selectedSourceViewIds.Remove(child.Id);
                    }
                }
                _isBatchUpdating = false;
                UpdatePreviewList();
            }
        }

        public QuickViewCreatorViewModel(Document doc, Window window)
        {
            _doc = doc;
            _window = window;

            // 1. 取得專案內所有平面視圖 (排除樣板)
            _allViews = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate)
                .ToList();

            RebuildTree();

            // 2. 取得專案內所有平面視圖樣板
            var viewTemplates = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan))
                .OrderBy(v => v.Name)
                .ToList();

            foreach (var template in viewTemplates)
            {
                var rule = new TemplateRuleViewModel(template);
                rule.RuleChanged += OnTemplateRuleChanged;
                Templates.Add(rule);
            }

            // 3. 初始化命令
            CreateViewsCommand = new RelayCommand(OnCreateViews);
            CancelCommand = new RelayCommand(() => _window.Close());
            ToggleSelectAllViewsCommand = new RelayCommand(() => AllViewsSelected = !AllViewsSelected);

            UpdateStatusText();
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

        private void RebuildTree()
        {
            TreeItems.Clear();

            // 根據 SearchText 過濾平面圖
            var filtered = _allViews.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string kw = SearchText.Trim().ToLower();
                filtered = filtered.Where(v => v.Name.ToLower().Contains(kw));
            }

            // 依 ViewType 分組
            var grouped = filtered.GroupBy(v => v.ViewType).OrderBy(g => GetViewTypeOrder(g.Key));

            _isBatchUpdating = true;
            foreach (var group in grouped)
            {
                var groupNode = new ViewTreeItemViewModel
                {
                    Name = $"{GetViewTypeName(group.Key)} ({group.Count()})",
                    Type = "Group",
                    ViewTypeStr = GetViewTypeName(group.Key)
                };

                groupNode.SelectionChanged += OnTreeItemSelectionChanged;

                foreach (var v in group.OrderBy(v => v.Name))
                {
                    bool isSelected = _selectedSourceViewIds.Contains(v.Id);
                    var viewNode = new ViewTreeItemViewModel
                    {
                        Id = v.Id,
                        Name = v.Name,
                        Type = "View",
                        ViewTypeStr = GetViewTypeName(v.ViewType),
                        IsSelected = isSelected
                    };

                    viewNode.SelectionChanged += OnTreeItemSelectionChanged;
                    groupNode.Children.Add(viewNode);
                }

                TreeItems.Add(groupNode);
            }
            _isBatchUpdating = false;
        }

        private void OnTreeItemSelectionChanged(object sender, EventArgs e)
        {
            if (_isBatchUpdating) return;

            if (sender is ViewTreeItemViewModel item)
            {
                if (item.Type == "View")
                {
                    if (item.IsSelected)
                        _selectedSourceViewIds.Add(item.Id);
                    else
                        _selectedSourceViewIds.Remove(item.Id);
                }
                else if (item.Type == "Group")
                {
                    _isBatchUpdating = true;
                    foreach (var child in item.Children)
                    {
                        child.IsSelected = item.IsSelected;
                        if (item.IsSelected)
                            _selectedSourceViewIds.Add(child.Id);
                        else
                            _selectedSourceViewIds.Remove(child.Id);
                    }
                    _isBatchUpdating = false;
                }
            }
            UpdatePreviewList();
        }

        private void OnTemplateRuleChanged(object sender, EventArgs e)
        {
            if (sender is TemplateRuleViewModel rule)
            {
                foreach (var item in PreviewItems.Where(i => i.TemplateId == rule.Id))
                {
                    var srcView = _doc.GetElement(item.SourceViewId) as ViewPlan;
                    if (srcView != null)
                    {
                        item.TargetViewName = srcView.Name + rule.Suffix;
                        item.SheetName = item.TargetViewName;
                    }
                }
            }
            UpdatePreviewList();
        }

        private void UpdatePreviewList()
        {
            var selectedTemplates = Templates.Where(t => t.IsSelected).ToList();

            // 建立目前需要的 (SourceViewId, TemplateId) 組合的鍵
            var desiredKeys = new HashSet<(ElementId SourceId, ElementId TempId)>();
            foreach (var srcId in _selectedSourceViewIds)
            {
                foreach (var temp in selectedTemplates)
                {
                    desiredKeys.Add((srcId, temp.Id));
                }
            }

            // 1. 移除不符合選取的預覽項目
            var toRemove = PreviewItems.Where(item => !desiredKeys.Contains((item.SourceViewId, item.TemplateId))).ToList();
            foreach (var item in toRemove)
            {
                PreviewItems.Remove(item);
            }

            // 2. 新增沒有的預覽項目
            string suggestSheetNo = SuggestNextSheetNumber();
            int sheetOffset = 0;

            foreach (var key in desiredKeys)
            {
                bool exists = PreviewItems.Any(item => item.SourceViewId == key.SourceId && item.TemplateId == key.TempId);
                if (!exists)
                {
                    var srcView = _doc.GetElement(key.SourceId) as ViewPlan;
                    var template = _doc.GetElement(key.TempId) as View;
                    if (srcView == null || template == null) continue;

                    var rule = Templates.FirstOrDefault(t => t.Id == key.TempId);
                    string suffix = rule?.Suffix ?? $" - {template.Name}";
                    string targetName = srcView.Name + suffix;

                    string sheetNo = IncrementSheetNumber(suggestSheetNo, sheetOffset);
                    sheetOffset++;

                    PreviewItems.Add(new ViewPreviewItemViewModel
                    {
                        SourceViewId = key.SourceId,
                        SourceViewName = srcView.Name,
                        TemplateId = key.TempId,
                        TemplateName = template.Name,
                        TargetViewName = targetName,
                        CreateSheet = CreateSheets,
                        SheetNumber = sheetNo,
                        SheetName = targetName
                    });
                }
            }

            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            int selectedV = _selectedSourceViewIds.Count;
            int selectedT = Templates.Count(t => t.IsSelected);
            StatusText = $"已選取 {selectedV} 張平面圖、{selectedT} 個視圖樣板。預計產生 {PreviewItems.Count} 張圖說。";
        }

        private string SuggestNextSheetNumber()
        {
            var sheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            if (sheets.Count == 0) return "A-101";

            var sortedNumbers = sheets
                .Select(s => s.SheetNumber)
                .OrderBy(n => n)
                .ToList();

            string maxNum = sortedNumbers.LastOrDefault();
            if (string.IsNullOrEmpty(maxNum)) return "A-101";

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

        private string IncrementSheetNumber(string startNumber, int offset)
        {
            if (offset == 0) return startNumber;

            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(startNumber, @"\d+$");
            if (match.Success)
            {
                string numStr = match.Value;
                int val = int.Parse(numStr) + offset;
                string format = new string('0', numStr.Length);
                string prefix = startNumber.Substring(0, startNumber.Length - numStr.Length);
                return prefix + val.ToString(format);
            }

            return startNumber + "-" + offset;
        }

        private bool IsViewNameExists(string name)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSheetNumberExists(string number)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Any(s => s.SheetNumber.Equals(number, StringComparison.OrdinalIgnoreCase));
        }

        private void OnCreateViews()
        {
            if (!PreviewItems.Any())
            {
                MessageBox.Show("預覽清單為空，請先選取來源平面圖與套用樣板。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (PreviewItems.Any(item => string.IsNullOrWhiteSpace(item.TargetViewName)))
            {
                MessageBox.Show("目標視圖名稱不可為空！請檢查預覽清單。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (PreviewItems.Any(item => item.CreateSheet && (string.IsNullOrWhiteSpace(item.SheetNumber) || string.IsNullOrWhiteSpace(item.SheetName))))
            {
                MessageBox.Show("圖紙編號與圖紙名稱不可為空！請檢查預覽清單。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ViewDuplicateOption dupOption = ViewDuplicateOption.WithDetailing;
            if (DuplicateMode == 0) dupOption = ViewDuplicateOption.Duplicate;
            else if (DuplicateMode == 2) dupOption = ViewDuplicateOption.AsDependent;

            int createdCount = 0;
            int sheetCount = 0;

            try
            {
                using (Transaction trans = new Transaction(_doc, "批次開圖與套樣板"))
                {
                    trans.Start();

                    ElementId titleBlockId = ElementId.InvalidElementId;
                    var titleBlock = new FilteredElementCollector(_doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsElementType()
                        .Cast<FamilySymbol>()
                        .FirstOrDefault();
                    if (titleBlock != null)
                    {
                        titleBlockId = titleBlock.Id;
                    }

                    foreach (var item in PreviewItems)
                    {
                        var srcView = _doc.GetElement(item.SourceViewId) as ViewPlan;
                        if (srcView == null) continue;

                        // 1. 複製視圖
                        ElementId newViewId = srcView.Duplicate(dupOption);
                        var newView = _doc.GetElement(newViewId) as View;

                        if (newView != null)
                        {
                            // 2. 重新命名 (防撞處理)
                            string baseName = item.TargetViewName;
                            string finalName = baseName;
                            int collisionCount = 1;
                            while (IsViewNameExists(finalName))
                            {
                                finalName = $"{baseName}_{collisionCount}";
                                collisionCount++;
                            }
                            newView.Name = finalName;

                            // 3. 套用視圖樣板
                            newView.ViewTemplateId = item.TemplateId;
                            createdCount++;

                            // 4. 同步建立圖紙
                            if (item.CreateSheet)
                            {
                                ViewSheet sheet = ViewSheet.Create(_doc, titleBlockId);
                                if (sheet != null)
                                {
                                    sheet.Name = item.SheetName;

                                    string baseSheetNo = item.SheetNumber;
                                    string finalSheetNo = baseSheetNo;
                                    int sheetCollisionCount = 1;
                                    while (IsSheetNumberExists(finalSheetNo))
                                    {
                                        finalSheetNo = $"{baseSheetNo}_{sheetCollisionCount}";
                                        sheetCollisionCount++;
                                    }
                                    sheet.SheetNumber = finalSheetNo;

                                    // 置入視圖至圖紙中心
                                    if (Viewport.CanAddViewToSheet(_doc, sheet.Id, newView.Id))
                                    {
                                        Viewport.Create(_doc, sheet.Id, newView.Id, XYZ.Zero);
                                    }
                                    sheetCount++;
                                }
                            }
                        }
                    }

                    trans.Commit();
                }

                string msg = $"✓ 批次建立完成！\n\n共新建了 {createdCount} 張圖說視圖。";
                if (sheetCount > 0)
                {
                    msg += $"\n共新建了 {sheetCount} 張對應圖紙並完成自動排版。";
                }
                MessageBox.Show(msg, "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                _window.DialogResult = true;
                _window.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"批次開圖過程中發生錯誤：{ex.Message}\n交易已撤銷。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// RelayCommand 類別
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
