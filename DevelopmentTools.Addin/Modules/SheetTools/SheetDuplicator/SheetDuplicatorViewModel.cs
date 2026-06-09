using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;

namespace DevelopmentTools.Modules.SheetTools.SheetDuplicator
{
    public class SheetDuplicatorViewModel : INotifyPropertyChanged
    {
        private readonly ExternalCommandData _commandData;
        private readonly Document _doc;
        
        public Action RequestClose { get; set; }

        private string _sheetPrefix;
        public string SheetPrefix
        {
            get => _sheetPrefix;
            set { _sheetPrefix = value; OnPropertyChanged(); }
        }

        public List<ViewSheet> MatchedSheets { get; private set; } = new List<ViewSheet>();

        public ObservableCollection<ViewItem> AnalyzedViews { get; private set; } = new ObservableCollection<ViewItem>();
        
        public ObservableCollection<LevelItem> TargetLevels { get; private set; } = new ObservableCollection<LevelItem>();

        private string _statusText = "準備就緒";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public ICommand AnalyzeCommand { get; }
        public ICommand GenerateCommand { get; }
        public ICommand CloseCommand { get; }

        public SheetDuplicatorViewModel(ExternalCommandData commandData)
        {
            _commandData = commandData;
            _doc = commandData.Application.ActiveUIDocument.Document;

            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            foreach (var lvl in levels)
            {
                TargetLevels.Add(new LevelItem { Level = lvl, IsSelected = false });
            }

            AnalyzeCommand = new RelayCommand(OnAnalyze);
            GenerateCommand = new RelayCommand(OnGenerate);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        private void OnAnalyze()
        {
            AnalyzedViews.Clear();
            MatchedSheets.Clear();
            
            if (string.IsNullOrWhiteSpace(SheetPrefix))
            {
                StatusText = "請輸入圖紙編號前綴";
                return;
            }

            MatchedSheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.SheetNumber.StartsWith(SheetPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (MatchedSheets.Count == 0)
            {
                StatusText = $"找不到任何以 {SheetPrefix} 開頭的圖紙";
                return;
            }

            foreach (var sheet in MatchedSheets)
            {
                // 取得圖紙上所有的 Viewports
                var viewportIds = sheet.GetAllViewports();
                foreach (var vpId in viewportIds)
                {
                    var vp = _doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;

                    var view = _doc.GetElement(vp.ViewId) as View;
                    if (view == null) continue;

                    string viewTypeStr = view.ViewType.ToString();
                    string levelName = view.GenLevel?.Name ?? "-";
                    string templateName = "-";
                    if (view.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        var template = _doc.GetElement(view.ViewTemplateId);
                        if (template != null) templateName = template.Name;
                    }

                    AnalyzedViews.Add(new ViewItem
                    {
                        SourceSheetNumber = sheet.SheetNumber,
                        SourceSheetId = sheet.Id,
                        ViewId = view.Id,
                        ViewportId = vp.Id,
                        ViewName = view.Name,
                        ViewType = viewTypeStr,
                        LevelName = levelName,
                        TemplateName = templateName,
                        Location = vp.GetBoxCenter(),
                        ViewportTypeId = vp.GetTypeId(),
                        OriginalView = view
                    });
                }

                // ScheduleSheetInstances
                var schedules = new FilteredElementCollector(_doc, sheet.Id)
                    .OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>();

                foreach (var sch in schedules)
                {
                    AnalyzedViews.Add(new ViewItem
                    {
                        SourceSheetNumber = sheet.SheetNumber,
                        SourceSheetId = sheet.Id,
                        ViewId = sch.ScheduleId,
                        ViewportId = sch.Id,
                        ViewName = sch.Name,
                        ViewType = "Schedule",
                        LevelName = "-",
                        TemplateName = "-",
                        Location = sch.Point,
                        ViewportTypeId = ElementId.InvalidElementId,
                        OriginalView = null,
                        IsSchedule = true
                    });
                }
            }

            StatusText = $"共分析 {MatchedSheets.Count} 張圖紙，找到 {AnalyzedViews.Count} 個視圖/明細表。";
        }

        private void OnGenerate()
        {
            var selectedLevels = TargetLevels.Where(x => x.IsSelected).ToList();
            if (selectedLevels.Count == 0)
            {
                MessageBox.Show("請至少選擇一個目標樓層！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MatchedSheets.Count == 0)
            {
                MessageBox.Show("請先輸入前綴並點擊分析，至少需找到一張圖紙！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var externalEventHandler = new SheetDuplicatorEventHandler
            {
                Action = app =>
                {
                    using (Transaction tx = new Transaction(_doc, "DT: 圖紙逐層量化開圖"))
                    {
                        tx.Start();

                        int sheetCount = 0;
                        foreach (var targetLvl in selectedLevels)
                        {
                            foreach (var sourceSheet in MatchedSheets)
                            {
                                ViewSheet newSheet = null;
                                ElementId tbId = ElementId.InvalidElementId;
                                var tbs = new FilteredElementCollector(_doc, sourceSheet.Id)
                                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                    .WhereElementIsNotElementType()
                                    .Cast<FamilyInstance>()
                                    .ToList();
                                if (tbs.Count > 0) tbId = tbs[0].GetTypeId();

                                if (tbId != ElementId.InvalidElementId)
                                    newSheet = ViewSheet.Create(_doc, tbId);
                                else
                                    newSheet = ViewSheet.Create(_doc, ElementId.InvalidElementId);

                                newSheet.Name = sourceSheet.Name + $" ({targetLvl.Level.Name})";
                                sheetCount++;

                                var viewsForThisSheet = AnalyzedViews.Where(v => v.SourceSheetId == sourceSheet.Id).ToList();

                                foreach (var item in viewsForThisSheet)
                                {
                                    try
                                    {
                                        if (item.IsSchedule)
                                        {
                                            ScheduleSheetInstance.Create(_doc, newSheet.Id, item.ViewId, item.Location);
                                        }
                                        else if (item.OriginalView != null && item.OriginalView.ViewType == ViewType.Legend)
                                        {
                                            var vp = Viewport.Create(_doc, newSheet.Id, item.ViewId, item.Location);
                                            if (item.ViewportTypeId != ElementId.InvalidElementId)
                                                vp.ChangeTypeId(item.ViewportTypeId);
                                        }
                                        else if (item.OriginalView != null && 
                                                (item.OriginalView.ViewType == ViewType.FloorPlan || 
                                                 item.OriginalView.ViewType == ViewType.CeilingPlan ||
                                                 item.OriginalView.ViewType == ViewType.AreaPlan ||
                                                 item.OriginalView.ViewType == ViewType.EngineeringPlan))
                                        {
                                            ElementId viewFamilyTypeId = _doc.GetElement(item.OriginalView.GetTypeId())?.Id;
                                            if (viewFamilyTypeId != null)
                                            {
                                                ViewPlan newPlan = ViewPlan.Create(_doc, viewFamilyTypeId, targetLvl.Level.Id);
                                                newPlan.Name = item.OriginalView.Name + $"_{targetLvl.Level.Name}_{Guid.NewGuid().ToString().Substring(0,4)}";
                                                
                                                if (item.OriginalView.ViewTemplateId != ElementId.InvalidElementId)
                                                {
                                                    newPlan.ViewTemplateId = item.OriginalView.ViewTemplateId;
                                                }

                                                var vp = Viewport.Create(_doc, newSheet.Id, newPlan.Id, item.Location);
                                                if (item.ViewportTypeId != ElementId.InvalidElementId)
                                                    vp.ChangeTypeId(item.ViewportTypeId);
                                            }
                                        }
                                        else if (item.OriginalView != null)
                                        {
                                            // 剖面圖或詳圖每個樓層不一樣，直接留空
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        // 忽略放置失敗
                                    }
                                }
                            }
                        }

                        tx.Commit();
                        MessageBox.Show($"成功複製了 {sheetCount} 張圖紙！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                        StatusText = $"成功建立了 {sheetCount} 張目標圖紙。";
                    }
                }
            };

            var externalEvent = ExternalEvent.Create(externalEventHandler);
            externalEvent.Raise();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class SheetDuplicatorEventHandler : IExternalEventHandler
    {
        public Action<UIApplication> Action { get; set; }

        public void Execute(UIApplication app)
        {
            Action?.Invoke(app);
        }

        public string GetName() => "SheetDuplicatorEventHandler";
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
    }

    public class ViewItem
    {
        public string SourceSheetNumber { get; set; }
        public ElementId SourceSheetId { get; set; }
        public ElementId ViewId { get; set; }
        public ElementId ViewportId { get; set; }
        public string ViewName { get; set; }
        public string ViewType { get; set; }
        public string LevelName { get; set; }
        public string TemplateName { get; set; }
        public XYZ Location { get; set; }
        public ElementId ViewportTypeId { get; set; }
        public View OriginalView { get; set; }
        public bool IsSchedule { get; set; } = false;
    }

    public class LevelItem : INotifyPropertyChanged
    {
        public Level Level { get; set; }
        public string Name => Level.Name;
        public string ElevationInfo => (Level.Elevation * 304.8).ToString("0.0") + " mm";
        
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
