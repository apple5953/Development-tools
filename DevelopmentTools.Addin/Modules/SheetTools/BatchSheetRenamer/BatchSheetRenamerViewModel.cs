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

namespace DevelopmentTools.Modules.SheetTools.BatchSheetRenamer
{
    /// <summary>
    /// 代表單張圖紙的 ViewModel，用於 DataGrid 展示與編輯
    /// </summary>
    public class SheetItemViewModel : INotifyPropertyChanged
    {
        public ElementId Id { get; }
        public string OriginalNumber { get; }
        public string OriginalName { get; }

        private string _sheetNumber;
        public string SheetNumber
        {
            get => _sheetNumber;
            set
            {
                if (_sheetNumber != value)
                {
                    _sheetNumber = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsModified));
                }
            }
        }

        private string _sheetName;
        public string SheetName
        {
            get => _sheetName;
            set
            {
                if (_sheetName != value)
                {
                    _sheetName = value;
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

        public bool IsModified => SheetNumber != OriginalNumber || SheetName != OriginalName;

        public SheetItemViewModel(ViewSheet sheet)
        {
            Id = sheet.Id;
            OriginalNumber = sheet.SheetNumber;
            OriginalName = sheet.Name;
            _sheetNumber = sheet.SheetNumber;
            _sheetName = sheet.Name;
            _isSelected = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 圖紙批次更名工具的主 ViewModel
    /// </summary>
    public class BatchSheetRenamerViewModel : INotifyPropertyChanged
    {
        private readonly Document _doc;
        private readonly Window _window;

        public ObservableCollection<SheetItemViewModel> Sheets { get; }
        public ICollectionView FilteredSheets { get; }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilteredSheets.Refresh();
            }
        }

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // 批次尋找取代
        public int FindReplaceTarget { get; set; } = 0; // 0: 編號, 1: 名稱
        private string _findText;
        public string FindText
        {
            get => _findText;
            set { _findText = value; OnPropertyChanged(); }
        }
        private string _replaceText;
        public string ReplaceText
        {
            get => _replaceText;
            set { _replaceText = value; OnPropertyChanged(); }
        }

        // 批次加首尾綴
        public int PrefixSuffixTarget { get; set; } = 0; // 0: 編號, 1: 名稱
        private string _prefixText;
        public string PrefixText
        {
            get => _prefixText;
            set { _prefixText = value; OnPropertyChanged(); }
        }
        private string _suffixText;
        public string SuffixText
        {
            get => _suffixText;
            set { _suffixText = value; OnPropertyChanged(); }
        }

        // 自動編號重排
        private string _startNumber = "A-101";
        public string StartNumber
        {
            get => _startNumber;
            set { _startNumber = value; OnPropertyChanged(); }
        }
        private int _increment = 1;
        public int Increment
        {
            get => _increment;
            set { _increment = value; OnPropertyChanged(); }
        }

        // 命令
        public ICommand ApplyFindReplaceCommand { get; }
        public ICommand ApplyPrefixSuffixCommand { get; }
        public ICommand ApplyAutoReindexCommand { get; }
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
                foreach (var s in FilteredSheets.Cast<SheetItemViewModel>())
                {
                    s.IsSelected = _allSelected;
                }
            }
        }

        public BatchSheetRenamerViewModel(Document doc, Window window)
        {
            _doc = doc;
            _window = window;

            // 1. 取得專案所有圖紙並載入
            var sheetsInDoc = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .Select(s => new SheetItemViewModel(s))
                .ToList();

            Sheets = new ObservableCollection<SheetItemViewModel>(sheetsInDoc);
            FilteredSheets = CollectionViewSource.GetDefaultView(Sheets);
            FilteredSheets.Filter = FilterSheetsPredicate;

            // 2. 初始化命令
            ApplyFindReplaceCommand = new RelayCommand(OnApplyFindReplace);
            ApplyPrefixSuffixCommand = new RelayCommand(OnApplyPrefixSuffix);
            ApplyAutoReindexCommand = new RelayCommand(OnApplyAutoReindex);
            SaveCommand = new RelayCommand(OnSave);
            CancelCommand = new RelayCommand(() => _window.Close());
            ToggleSelectAllCommand = new RelayCommand(() => AllSelected = !AllSelected);

            UpdateStatusText();
        }

        private bool FilterSheetsPredicate(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (!(obj is SheetItemViewModel item)) return false;

            return item.SheetNumber.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.SheetName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.OriginalNumber.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.OriginalName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateStatusText()
        {
            int total = Sheets.Count;
            int modified = Sheets.Count(s => s.IsModified);
            StatusText = $"專案共 {total} 張圖紙，目前已修改 {modified} 張圖紙。";
        }

        // 批次尋找取代
        private void OnApplyFindReplace()
        {
            if (string.IsNullOrEmpty(FindText))
            {
                MessageBox.Show("請輸入要尋找的文字。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var targets = GetActiveTargets();
            if (!targets.Any()) return;

            foreach (var s in targets)
            {
                if (FindReplaceTarget == 0) // 編號
                {
                    s.SheetNumber = s.SheetNumber.Replace(FindText, ReplaceText ?? "");
                }
                else // 名稱
                {
                    s.SheetName = s.SheetName.Replace(FindText, ReplaceText ?? "");
                }
            }

            UpdateStatusText();
        }

        // 批次加首尾綴
        private void OnApplyPrefixSuffix()
        {
            if (string.IsNullOrEmpty(PrefixText) && string.IsNullOrEmpty(SuffixText))
            {
                MessageBox.Show("請輸入要加入的前綴或後綴。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var targets = GetActiveTargets();
            if (!targets.Any()) return;

            foreach (var s in targets)
            {
                if (PrefixSuffixTarget == 0) // 編號
                {
                    s.SheetNumber = (PrefixText ?? "") + s.SheetNumber + (SuffixText ?? "");
                }
                else // 名稱
                {
                    s.SheetName = (PrefixText ?? "") + s.SheetName + (SuffixText ?? "");
                }
            }

            UpdateStatusText();
        }

        // 自動編號重排
        private void OnApplyAutoReindex()
        {
            var targets = GetActiveTargets();
            if (!targets.Any()) return;

            var (prefix, startNum, padLength) = ParseStartNumber(StartNumber);
            int currentNum = startNum;

            foreach (var s in targets)
            {
                string formattedNum = prefix + currentNum.ToString().PadLeft(padLength, '0');
                s.SheetNumber = formattedNum;
                currentNum += Increment;
            }

            UpdateStatusText();
        }

        private (string Prefix, int Number, int PadLength) ParseStartNumber(string input)
        {
            if (string.IsNullOrEmpty(input))
                return ("", 1, 1);

            int i = input.Length - 1;
            while (i >= 0 && char.IsDigit(input[i]))
            {
                i--;
            }

            string numberStr = input.Substring(i + 1);
            string prefix = input.Substring(0, i + 1);

            if (int.TryParse(numberStr, out int num))
            {
                return (prefix, num, numberStr.Length);
            }
            else
            {
                return (input, 1, 1);
            }
        }

        private List<SheetItemViewModel> GetActiveTargets()
        {
            // 如果有勾選圖紙，只套用勾選的
            var selected = Sheets.Where(s => s.IsSelected).ToList();
            if (selected.Any()) return selected;

            // 否則套用目前列表過濾顯示的所有圖紙
            return FilteredSheets.Cast<SheetItemViewModel>().ToList();
        }

        // 保存更新至 Revit
        private void OnSave()
        {
            // 1. 驗證新編號是否有重複（目前清單內）
            var numGroup = Sheets.GroupBy(s => s.SheetNumber).Where(g => g.Count() > 1).ToList();
            if (numGroup.Any())
            {
                string duplicateNums = string.Join(", ", numGroup.Select(g => g.Key));
                MessageBox.Show($"更新失敗：圖紙編號不能重複！\n重複的編號有：{duplicateNums}", "驗證錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 2. 驗證是否與專案中「未出現在清單中」的圖紙編號重複 (正常情況下 FilteredElementCollector 會讀出全部，但以防萬一)
            // 取得當前修改過的圖紙所指定的目標編號，若這些編號已被其他未受影響的圖紙佔用，則不合法
            var modifiedItems = Sheets.Where(s => s.IsModified).ToList();
            if (!modifiedItems.Any())
            {
                _window.DialogResult = true;
                _window.Close();
                return;
            }

            // 3. 進入 Revit Transaction 執行兩步修改法
            try
            {
                using (Transaction trans = new Transaction(_doc, "批次更名與重排圖紙"))
                {
                    trans.Start();

                    // 篩選出有修改編號的
                    var numModified = modifiedItems.Where(i => i.SheetNumber != i.OriginalNumber).ToList();

                    // 階段一：修改為臨時編號，避開 Revit SheetNumber 唯一性衝突
                    var tempMappings = new List<(ViewSheet Sheet, string TargetNumber)>();
                    foreach (var item in numModified)
                    {
                        if (_doc.GetElement(item.Id) is ViewSheet sheet)
                        {
                            // 臨時編號後綴
                            string tempNum = item.SheetNumber + "_temp_" + Guid.NewGuid().ToString().Substring(0, 8);
                            sheet.SheetNumber = tempNum;
                            tempMappings.Add((sheet, item.SheetNumber));
                        }
                    }

                    // 階段二：修改為最終的目標編號
                    foreach (var map in tempMappings)
                    {
                        map.Sheet.SheetNumber = map.TargetNumber;
                    }

                    // 階段三：修改圖紙名稱 (無唯一性限制)
                    var nameModified = modifiedItems.Where(i => i.SheetName != i.OriginalName).ToList();
                    foreach (var item in nameModified)
                    {
                        if (_doc.GetElement(item.Id) is ViewSheet sheet)
                        {
                            sheet.Name = item.SheetName;
                        }
                    }

                    trans.Commit();
                }

                MessageBox.Show("✓ 圖紙資訊批次更新成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                _window.DialogResult = true;
                _window.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新圖紙時發生錯誤：{ex.Message}\n已自動還原交易變更。", "更新錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// RelayCommand 簡化版類別，用於 ViewModel 命令繫結
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
