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
using Autodesk.Revit.UI;

namespace DevelopmentTools.Modules.SheetTools.QuickViewCreator
{
    /// <summary>
    /// 平面視圖展示 ViewModel
    /// </summary>
    public class ViewItemViewModel : INotifyPropertyChanged
    {
        public ElementId Id { get; }
        public string Name { get; }
        public string ViewTypeStr { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public ViewItemViewModel(ViewPlan view)
        {
            Id = view.Id;
            Name = view.Name;
            ViewTypeStr = view.ViewType == ViewType.FloorPlan ? "樓層平面" : "天花板平面";
            _isSelected = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 視圖樣板展示 ViewModel
    /// </summary>
    public class TemplateItemViewModel : INotifyPropertyChanged
    {
        public ElementId Id { get; }
        public string Name { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public TemplateItemViewModel(View template)
        {
            Id = template.Id;
            Name = template.Name;
            _isSelected = false;
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

        public ObservableCollection<ViewItemViewModel> Views { get; }
        public ICollectionView FilteredViews { get; }

        public ObservableCollection<TemplateItemViewModel> Templates { get; }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilteredViews.Refresh();
            }
        }

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // 複製模式：0 = 僅複製, 1 = 複製詳圖, 2 = 建立相依
        private int _duplicateMode = 1; // 預設為複製詳圖 (With Detailing)
        public int DuplicateMode
        {
            get => _duplicateMode;
            set { _duplicateMode = value; OnPropertyChanged(); }
        }

        // 同步建立圖紙
        private bool _createSheets = false;
        public bool CreateSheets
        {
            get => _createSheets;
            set { _createSheets = value; OnPropertyChanged(); }
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
                foreach (var v in FilteredViews.Cast<ViewItemViewModel>())
                {
                    v.IsSelected = _allViewsSelected;
                }
            }
        }

        public QuickViewCreatorViewModel(Document doc, Window window)
        {
            _doc = doc;
            _window = window;

            // 1. 取得專案內所有平面視圖 (排除樣板)
            var planViews = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate)
                .OrderBy(v => v.Name)
                .Select(v => new ViewItemViewModel(v))
                .ToList();

            Views = new ObservableCollection<ViewItemViewModel>(planViews);
            FilteredViews = CollectionViewSource.GetDefaultView(Views);
            FilteredViews.Filter = FilterViewsPredicate;

            foreach (var v in planViews)
            {
                v.PropertyChanged += (s, e) => {
                    if (e.PropertyName == nameof(ViewItemViewModel.IsSelected)) UpdateStatusText();
                };
            }

            // 2. 取得專案內所有平面視圖樣板
            var viewTemplates = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan))
                .OrderBy(v => v.Name)
                .Select(v => new TemplateItemViewModel(v))
                .ToList();

            Templates = new ObservableCollection<TemplateItemViewModel>(viewTemplates);

            foreach (var t in viewTemplates)
            {
                t.PropertyChanged += (s, e) => {
                    if (e.PropertyName == nameof(TemplateItemViewModel.IsSelected)) UpdateStatusText();
                };
            }

            // 3. 初始化命令
            CreateViewsCommand = new RelayCommand(OnCreateViews);
            CancelCommand = new RelayCommand(() => _window.Close());
            ToggleSelectAllViewsCommand = new RelayCommand(() => AllViewsSelected = !AllViewsSelected);

            UpdateStatusText();
        }

        private bool FilterViewsPredicate(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (!(obj is ViewItemViewModel item)) return false;

            return item.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateStatusText()
        {
            int selectedV = Views.Count(v => v.IsSelected);
            int selectedT = Templates.Count(t => t.IsSelected);
            StatusText = $"已勾選 {selectedV} 張平面圖、{selectedT} 個視圖樣板。預計產生 {selectedV * selectedT} 張圖說。";
        }

        public void TriggerSelectionChange()
        {
            UpdateStatusText();
        }

        // 執行批次複製與套樣板
        private void OnCreateViews()
        {
            var selectedViews = Views.Where(v => v.IsSelected).ToList();
            var selectedTemplates = Templates.Where(t => t.IsSelected).ToList();

            if (!selectedViews.Any())
            {
                MessageBox.Show("請至少選擇一張平面圖作為來源。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!selectedTemplates.Any())
            {
                MessageBox.Show("請至少選擇一個視圖樣板。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 確定複製模式
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

                    // 取得 TitleBlock (用於建立圖紙)
                    ElementId titleBlockId = ElementId.InvalidElementId;
                    if (CreateSheets)
                    {
                        var titleBlock = new FilteredElementCollector(_doc)
                            .OfCategory(BuiltInCategory.OST_TitleBlocks)
                            .WhereElementIsNotElementType()
                            .FirstOrDefault();
                        if (titleBlock != null)
                        {
                            titleBlockId = titleBlock.Id;
                        }
                    }

                    foreach (var viewItem in selectedViews)
                    {
                        var srcView = _doc.GetElement(viewItem.Id) as ViewPlan;
                        if (srcView == null) continue;

                        foreach (var tempItem in selectedTemplates)
                        {
                            var template = _doc.GetElement(tempItem.Id) as View;
                            if (template == null) continue;

                            // 1. 複製視圖
                            ElementId newViewId = srcView.Duplicate(dupOption);
                            var newView = _doc.GetElement(newViewId) as View;

                            if (newView != null)
                            {
                                // 2. 重新命名 (防撞處理)
                                string baseName = $"{srcView.Name}-{template.Name}";
                                string finalName = baseName;
                                int collisionCount = 1;
                                while (IsViewNameExists(finalName))
                                {
                                    finalName = $"{baseName}_{collisionCount}";
                                    collisionCount++;
                                }
                                newView.Name = finalName;

                                // 3. 套用視圖樣板
                                newView.ViewTemplateId = template.Id;
                                createdCount++;

                                // 4. 是否建立圖紙
                                if (CreateSheets)
                                {
                                    ViewSheet sheet = ViewSheet.Create(_doc, titleBlockId);
                                    if (sheet != null)
                                    {
                                        sheet.Name = finalName;
                                        // 自動生成不重複編號
                                        sheet.SheetNumber = GenerateUniqueSheetNumber();
                                        
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
                    }

                    trans.Commit();
                }

                string msg = $"✓ 批次建立完成！\n\n共新建了 {createdCount} 張圖說視圖。";
                if (CreateSheets)
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

        private bool IsViewNameExists(string name)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private string GenerateUniqueSheetNumber()
        {
            string prefix = "A-";
            int startNum = 101;
            while (true)
            {
                string numStr = prefix + startNum.ToString();
                bool exists = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Any(s => s.SheetNumber.Equals(numStr, StringComparison.OrdinalIgnoreCase));

                if (!exists) return numStr;
                startNum++;
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
