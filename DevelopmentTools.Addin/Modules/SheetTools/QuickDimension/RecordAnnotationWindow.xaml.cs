using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.DB;
using DevelopmentTools.Core;

namespace DevelopmentTools.Modules.SheetTools.QuickDimension
{
    public partial class RecordAnnotationWindow : Window, INotifyPropertyChanged
    {
        private readonly Document _doc;

        public ObservableCollection<SheetItem> Sheets { get; } = new ObservableCollection<SheetItem>();
        public List<DimensionType> DimensionTypes { get; }

        private ViewItem _selectedViewItem;
        public ViewItem SelectedViewItem
        {
            get => _selectedViewItem;
            set
            {
                _selectedViewItem = value;
                OnPropertyChanged();
                OnViewItemSelectionChanged();
            }
        }

        private string _viewTemplateName = "無樣板 (儲存於視圖主體)";
        public string ViewTemplateName
        {
            get => _viewTemplateName;
            set { _viewTemplateName = value; OnPropertyChanged(); }
        }

        public RecordAnnotationWindow(Document doc, View activeView)
        {
            InitializeComponent();
            _doc = doc;
            DataContext = this;

            // 1. 取得線性標註型式
            DimensionTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear)
                .OrderBy(t => t.Name)
                .ToList();

            // 2. 載入圖紙與視圖
            LoadSheetsAndViews();
        }

        private void LoadSheetsAndViews()
        {
            Sheets.Clear();

            var sheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            foreach (var sheet in sheets)
            {
                var sheetItem = new SheetItem
                {
                    SheetName = sheet.Name,
                    SheetNumber = sheet.SheetNumber
                };

                var viewIds = sheet.GetAllPlacedViews();
                foreach (var vId in viewIds)
                {
                    View view = _doc.GetElement(vId) as View;
                    if (view == null || view.IsTemplate) continue;

                    var viewItem = new ViewItem { ViewElement = view };
                    
                    View targetLoadView = view;
                    if (view.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        View templateView = _doc.GetElement(view.ViewTemplateId) as View;
                        if (templateView != null)
                        {
                            targetLoadView = templateView;
                        }
                    }

                    string json = ViewAnnotationStorageManager.LoadConfig(targetLoadView);
                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            var config = System.Text.Json.JsonSerializer.Deserialize<ViewAnnotationConfig>(json, options);
                            if (config != null)
                            {
                                if (config.Logics != null && config.Logics.Any())
                                {
                                    foreach (var savedLogic in config.Logics)
                                    {
                                        var matched = viewItem.Logics.FirstOrDefault(l => IsSameLogic(l, savedLogic));
                                        if (matched != null)
                                        {
                                            matched.IsEnabled = savedLogic.IsEnabled;
                                            matched.DimensionTypeName = savedLogic.DimensionTypeName;
                                            matched.OffsetDistance = savedLogic.OffsetDistance;
                                            matched.PrefixText = savedLogic.PrefixText ?? string.Empty;
                                            matched.SuffixText = savedLogic.SuffixText ?? string.Empty;
                                        }
                                    }
                                }
                                else
                                {
                                    // 舊格式相容：若沒有 Logics 則填入舊屬性到第一個相符的
                                    var legacyLogic = viewItem.Logics.FirstOrDefault(l => l.TaggingMode == config.TaggingMode);
                                    if (legacyLogic != null)
                                    {
                                        legacyLogic.IsEnabled = true;
                                        legacyLogic.DimensionTypeName = config.DimensionTypeName;
                                        legacyLogic.OffsetDistance = config.OffsetDistance;
                                        legacyLogic.PrefixText = config.PrefixText ?? string.Empty;
                                        legacyLogic.SuffixText = config.SuffixText ?? string.Empty;
                                    }
                                }
                                viewItem.HasConfig = true;
                            }
                        }
                        catch { }
                    }

                    // 綁定各個 Logic 的 ComboBox 預設項
                    foreach (var logic in viewItem.Logics)
                    {
                        logic.SelectedDimensionType = DimensionTypes.FirstOrDefault(t => t.Name == logic.DimensionTypeName) 
                            ?? DimensionTypes.FirstOrDefault(t => t.Name.Contains("標註")) 
                            ?? DimensionTypes.FirstOrDefault();
                    }

                    sheetItem.Views.Add(viewItem);
                }

                if (sheetItem.Views.Any())
                {
                    Sheets.Add(sheetItem);
                }
            }
        }


        private static bool IsSameLogic(AnnotationLogicItem current, AnnotationLogicItem saved)
        {
            if (current == null || saved == null) return false;
            if (!string.IsNullOrWhiteSpace(current.ModeKey) && !string.IsNullOrWhiteSpace(saved.ModeKey))
            {
                return string.Equals(current.ModeKey, saved.ModeKey, StringComparison.OrdinalIgnoreCase);
            }
            return current.TaggingMode == saved.TaggingMode;
        }
        private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is ViewItem viewItem)
            {
                SelectedViewItem = viewItem;
            }
            else
            {
                SelectedViewItem = null;
            }
        }

        private void OnViewItemSelectionChanged()
        {
            if (SelectedViewItem == null)
            {
                ConfigPanel.IsEnabled = false;
                ViewTemplateName = "無樣板 (儲存於視圖主體)";
                return;
            }

            ConfigPanel.IsEnabled = true;

            if (SelectedViewItem.ViewElement.ViewTemplateId != ElementId.InvalidElementId)
            {
                View templateView = _doc.GetElement(SelectedViewItem.ViewElement.ViewTemplateId) as View;
                ViewTemplateName = templateView != null ? templateView.Name : "已指定樣板 (但找不到)";
            }
            else
            {
                ViewTemplateName = "無樣板 (將直接儲存於此視圖主體)";
            }
        }

        private void OnAutoDetectAll(object sender, RoutedEventArgs e)
        {
            int detectedCount = 0;
            var detectedViews = new List<ViewItem>();

            foreach (var sheet in Sheets)
            {
                foreach (var viewItem in sheet.Views)
                {
                    if (AutoDetectViewAnnotations(viewItem))
                    {
                        detectedViews.Add(viewItem);
                        detectedCount++;
                    }
                }
            }

            if (detectedViews.Any())
            {
                try
                {
                    using (Transaction t = new Transaction(_doc, "自動儲存偵測之標註邏輯"))
                    {
                        t.Start();
                        foreach (var viewItem in detectedViews)
                        {
                            var config = new ViewAnnotationConfig
                            {
                                Logics = viewItem.Logics.Select(l => new AnnotationLogicItem
                                {
                                    TaggingMode = l.TaggingMode,
                                    ModeKey = l.ModeKey,
                                    Name = l.Name,
                                    IsEnabled = l.IsEnabled,
                                    DimensionTypeName = l.DimensionTypeName,
                                    OffsetDistance = l.OffsetDistance,
                                    PrefixText = l.PrefixText,
                                    SuffixText = l.SuffixText
                                }).ToList(),
                                AutoApplyOnTransfer = true
                            };

                            string json = System.Text.Json.JsonSerializer.Serialize(config);
                            
                            View targetSaveView = viewItem.ViewElement;
                            if (viewItem.ViewElement.ViewTemplateId != ElementId.InvalidElementId)
                            {
                                View templateView = _doc.GetElement(viewItem.ViewElement.ViewTemplateId) as View;
                                if (templateView != null)
                                {
                                    targetSaveView = templateView;
                                }
                            }

                            ViewAnnotationStorageManager.SaveConfig(targetSaveView, json);
                            viewItem.HasConfig = true;
                        }
                        t.Commit();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"自動儲存失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            MessageBox.Show($"自動分析並儲存完成！共偵測並自動儲存了 {detectedCount} 個視圖的多重標註邏輯與樣式，轉移時將直接套用。若有特定視圖需要微調，可在右側面板修改後手動儲存。", "分析並自動儲存完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool AutoDetectViewAnnotations(ViewItem item)
        {
            try
            {
                var dimensions = new FilteredElementCollector(_doc, item.ViewElement.Id)
                    .OfClass(typeof(Dimension))
                    .Cast<Dimension>()
                    .ToList();

                bool detectedAny = false;
                string detectedLogString = "";

                // 我們對視圖中所有尺寸標註進行全面分析，判定是否有符合的標註類型
                foreach (var dim in dimensions)
                {
                    if (dim.References == null) continue;
                    foreach (Reference r in dim.References)
                    {
                        Element el = _doc.GetElement(r.ElementId);
                        if (el == null) continue;

                        int catId = el.Category?.Id.IntegerValue ?? 0;
                        if (catId == (int)BuiltInCategory.OST_StructuralColumns || catId == (int)BuiltInCategory.OST_Columns)
                        {
                            var logic = item.Logics.FirstOrDefault(l => l.TaggingMode == 0);
                            if (logic != null && !logic.IsEnabled)
                            {
                                logic.IsEnabled = true;
                                logic.DimensionTypeName = dim.DimensionType?.Name;
                                logic.SelectedDimensionType = DimensionTypes.FirstOrDefault(t => t.Name == dim.DimensionType?.Name) ?? logic.SelectedDimensionType;
                                detectedAny = true;
                                detectedLogString += "柱、";
                            }
                            break;
                        }
                        if (catId == (int)BuiltInCategory.OST_Doors || catId == (int)BuiltInCategory.OST_Windows)
                        {
                            var logic = item.Logics.FirstOrDefault(l => l.TaggingMode == 3);
                            if (logic != null && !logic.IsEnabled)
                            {
                                logic.IsEnabled = true;
                                logic.DimensionTypeName = dim.DimensionType?.Name;
                                logic.SelectedDimensionType = DimensionTypes.FirstOrDefault(t => t.Name == dim.DimensionType?.Name) ?? logic.SelectedDimensionType;
                                detectedAny = true;
                                detectedLogString += "門窗開口、";
                            }
                            break;
                        }
                        if (el is Wall)
                        {
                            var logic = item.Logics.FirstOrDefault(l => l.TaggingMode == 1);
                            if (logic != null && !logic.IsEnabled)
                            {
                                logic.IsEnabled = true;
                                logic.DimensionTypeName = dim.DimensionType?.Name;
                                logic.SelectedDimensionType = DimensionTypes.FirstOrDefault(t => t.Name == dim.DimensionType?.Name) ?? logic.SelectedDimensionType;
                                detectedAny = true;
                                detectedLogString += "牆體中心、";
                            }
                            break;
                        }
                    }
                }

                var roomTags = new FilteredElementCollector(_doc, item.ViewElement.Id)
                    .OfCategory(BuiltInCategory.OST_RoomTags)
                    .WhereElementIsNotElementType()
                    .Any();
                if (roomTags)
                {
                    var logic = item.Logics.FirstOrDefault(l => string.Equals(l.ModeKey, "rooms.tags", StringComparison.OrdinalIgnoreCase) || l.TaggingMode == 100);
                    if (logic != null && !logic.IsEnabled)
                    {
                        logic.IsEnabled = true;
                        detectedAny = true;
                        detectedLogString += "房間標籤、";
                    }
                }

                if (detectedAny)
                {
                    item.DetectedMode = 99; // 代表偵測到多重邏輯
                    item.DetectedLogicText = $"偵測到：{detectedLogString.TrimEnd('、')}";
                    item.IsSelected = true;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void OnSaveAllSelected(object sender, RoutedEventArgs e)
        {
            var selectedViews = Sheets.SelectMany(s => s.Views).Where(v => v.IsSelected).ToList();
            if (!selectedViews.Any())
            {
                MessageBox.Show("請先在清單中勾選要儲存的視圖。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (Transaction t = new Transaction(_doc, "批次儲存視圖標註邏輯"))
                {
                    t.Start();
                    foreach (var viewItem in selectedViews)
                    {
                        var config = new ViewAnnotationConfig
                        {
                            Logics = viewItem.Logics.Select(l => new AnnotationLogicItem
                            {
                                TaggingMode = l.TaggingMode,
                                ModeKey = l.ModeKey,
                                Name = l.Name,
                                IsEnabled = l.IsEnabled,
                                DimensionTypeName = l.DimensionTypeName,
                                OffsetDistance = l.OffsetDistance,
                                PrefixText = l.PrefixText,
                                SuffixText = l.SuffixText
                            }).ToList(),
                            AutoApplyOnTransfer = true
                        };

                        string json = System.Text.Json.JsonSerializer.Serialize(config);
                        
                        View targetSaveView = viewItem.ViewElement;
                        if (viewItem.ViewElement.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            View templateView = _doc.GetElement(viewItem.ViewElement.ViewTemplateId) as View;
                            if (templateView != null)
                            {
                                targetSaveView = templateView;
                            }
                        }

                        ViewAnnotationStorageManager.SaveConfig(targetSaveView, json);
                        viewItem.HasConfig = true;
                    }
                    t.Commit();
                }

                MessageBox.Show($"已成功儲存 {selectedViews.Count} 個視圖的標註邏輯！此邏輯將跟隨視圖一同轉移。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDeleteSelected(object sender, RoutedEventArgs e)
        {
            var selectedViews = Sheets.SelectMany(s => s.Views).Where(v => v.IsSelected).ToList();
            if (!selectedViews.Any())
            {
                MessageBox.Show("請先在清單中勾選要清除設定的視圖。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"確定要清除選取之 {selectedViews.Count} 個視圖的標註邏輯設定嗎？", "確認清除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                using (Transaction t = new Transaction(_doc, "清除視圖標註邏輯"))
                {
                    t.Start();
                    foreach (var viewItem in selectedViews)
                    {
                        View targetSaveView = viewItem.ViewElement;
                        if (viewItem.ViewElement.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            View templateView = _doc.GetElement(viewItem.ViewElement.ViewTemplateId) as View;
                            if (templateView != null)
                            {
                                targetSaveView = templateView;
                            }
                        }

                        ViewAnnotationStorageManager.RemoveConfig(targetSaveView);
                        viewItem.HasConfig = false;
                        viewItem.DetectedLogicText = "未偵測到標註";
                        
                        // 重置邏輯狀態
                        foreach (var l in viewItem.Logics)
                        {
                            l.IsEnabled = false;
                        }
                    }
                    t.Commit();
                }

                MessageBox.Show("已清除選定視圖的標註邏輯設定。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"清除失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class SheetItem
    {
        public string SheetName { get; set; }
        public string SheetNumber { get; set; }
        public List<ViewItem> Views { get; set; } = new List<ViewItem>();
    }

    public class ViewItem : INotifyPropertyChanged
    {
        private View _viewElement;
        public View ViewElement
        {
            get => _viewElement;
            set
            {
                _viewElement = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ViewName));
                InitializeLogicsForView();
            }
        }
        public string ViewName => ViewElement?.Name ?? string.Empty;

        private string _detectedLogicText = "未偵測到標註";
        public string DetectedLogicText
        {
            get => _detectedLogicText;
            set { _detectedLogicText = value; OnPropertyChanged(); }
        }

        public int DetectedMode { get; set; } = -1;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public ObservableCollection<AnnotationLogicItem> Logics { get; } = new ObservableCollection<AnnotationLogicItem>();

        private bool _hasConfig;
        public bool HasConfig
        {
            get => _hasConfig;
            set { _hasConfig = value; OnPropertyChanged(); }
        }

        public ViewItem()
        {
            InitializeLogicsForView();
        }

        private void InitializeLogicsForView()
        {
            Logics.Clear();
            QuickAnnotationProfileDefinition profile = QuickAnnotationModeRegistry.GetProfileForView(ViewElement);
            foreach (var mode in QuickAnnotationModeRegistry.GetModesForProfile(profile, ViewElement, manualOnly: false))
            {
                Logics.Add(new AnnotationLogicItem
                {
                    TaggingMode = mode.TaggingMode,
                    ModeKey = mode.ModeKey,
                    Name = mode.Name
                });
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class AnnotationLogicItem : INotifyPropertyChanged
    {
        public int TaggingMode { get; set; }
        public string ModeKey { get; set; }
        public string Name { get; set; }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public string DimensionTypeName { get; set; }

        private DimensionType _selectedDimensionType;
        public DimensionType SelectedDimensionType
        {
            get => _selectedDimensionType;
            set
            {
                _selectedDimensionType = value;
                OnPropertyChanged();
                if (value != null)
                {
                    DimensionTypeName = value.Name;
                }
            }
        }

        private double _offsetDistance = 1000;
        public double OffsetDistance
        {
            get => _offsetDistance;
            set { _offsetDistance = value; OnPropertyChanged(); }
        }

        private string _prefixText = string.Empty;
        public string PrefixText
        {
            get => _prefixText;
            set { _prefixText = value; OnPropertyChanged(); }
        }

        private string _suffixText = string.Empty;
        public string SuffixText
        {
            get => _suffixText;
            set { _suffixText = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class DetectedModeColorConverter : IValueConverter
    {
        public static readonly DetectedModeColorConverter Instance = new DetectedModeColorConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int mode && mode >= 0)
            {
                return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A6E3A1"));
            }
            return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7D8590"));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new BoolToVisibilityConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ViewAnnotationConfig
    {
        public int TaggingMode { get; set; }
        public string ModeKey { get; set; }
        public string DimensionTypeName { get; set; }
        public double OffsetDistance { get; set; }
        public string PrefixText { get; set; }
        public string SuffixText { get; set; }
        public bool AutoApplyOnTransfer { get; set; }

        // 多重邏輯清單
        public List<AnnotationLogicItem> Logics { get; set; }
    }
}

