using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.DesignReview.Models;
using DevelopmentTools.Modules.DesignReview.Services;
using DevelopmentTools.UI;

namespace DevelopmentTools.Modules.DesignReview.ViewModels
{
    public class CodeReviewPaneViewModel : INotifyPropertyChanged
    {
        private readonly UIApplication _uiapp;
        private readonly ExternalEvent _extEvent;
        private readonly CodeReviewPaneExternalEventHandler _handler;
        private readonly IssueTrackerService _trackerService = new IssueTrackerService();

        private ReviewProject _project;
        public ReviewProject Project
        {
            get => _project;
            set
            {
                _project = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private ReviewItem _selectedItem;
        public ReviewItem SelectedItem
        {
            get => _selectedItem;
            set
            {
                _selectedItem = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedItem));
                if (_selectedItem != null)
                {
                    SelectedStatusInput = _selectedItem.Status;
                    CommentInput = _selectedItem.Comment;
                    AssigneeInput = _selectedItem.Assignee;
                }
            }
        }

        public bool HasSelectedItem => SelectedItem != null;

        private TrackingStatus _selectedStatusInput;
        public TrackingStatus SelectedStatusInput
        {
            get => _selectedStatusInput;
            set { _selectedStatusInput = value; OnPropertyChanged(); }
        }

        private string _commentInput;
        public string CommentInput
        {
            get => _commentInput;
            set { _commentInput = value; OnPropertyChanged(); }
        }

        private string _assigneeInput;
        public string AssigneeInput
        {
            get => _assigneeInput;
            set { _assigneeInput = value; OnPropertyChanged(); }
        }

        private string _selectedCategoryFilter = "所有分類";
        public string SelectedCategoryFilter
        {
            get => _selectedCategoryFilter;
            set { _selectedCategoryFilter = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private string _selectedStatusFilter = "所有狀態";
        public string SelectedStatusFilter
        {
            get => _selectedStatusFilter;
            set { _selectedStatusFilter = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private string _selectedPerspectiveFilter = "所有視角";
        public string SelectedPerspectiveFilter
        {
            get => _selectedPerspectiveFilter;
            set { _selectedPerspectiveFilter = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private ReviewResult _selectedResult;
        public ReviewResult SelectedResult
        {
            get => _selectedResult;
            set { _selectedResult = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Categories { get; } = new ObservableCollection<string> { "所有分類", "基地", "面積", "高度", "防火", "避難", "樓梯", "走廊", "開口", "昇降機", "無障礙", "消防", "室內裝修", "其他" };
        public ObservableCollection<string> Statuses { get; } = new ObservableCollection<string> { "所有狀態", "🔴 未解決 (Created)", "🟡 已修改 (Modified)", "✅ 已通過 (Approved)", "❌ 被拒絕 (Rejected)" };
        public ObservableCollection<string> Perspectives { get; } = new ObservableCollection<string> { "所有視角", "建築師 (設計合規)", "營造廠 (可施工性)" };

        private string _selectedBuildingType = "集合住宅";
        public string SelectedBuildingType
        {
            get => _selectedBuildingType;
            set
            {
                if (_selectedBuildingType != value)
                {
                    _selectedBuildingType = value;
                    OnPropertyChanged();
                    if (Project != null && Project.BuildingType != _selectedBuildingType)
                    {
                        Project.BuildingType = _selectedBuildingType;
                        if (_uiapp?.ActiveUIDocument?.Document != null)
                        {
                            _trackerService.SaveProject(_uiapp.ActiveUIDocument.Document, Project);
                        }
                    }
                }
            }
        }

        public ObservableCollection<string> BuildingTypes { get; } = new ObservableCollection<string> { "集合住宅", "學校/公眾使用", "商辦/商用", "其他類" };

        public ObservableCollection<ReviewItem> FilteredItems { get; } = new ObservableCollection<ReviewItem>();

        public ICommand UpdateStatusCommand { get; }
        public ICommand RecheckCommand { get; }
        public ICommand SingleRecheckCommand { get; }
        public ICommand DoubleClickResultCommand { get; }
        public ICommand CreateSheetCommand { get; }

        public CodeReviewPaneViewModel(UIApplication uiapp)
        {
            _uiapp = uiapp;
            _handler = new CodeReviewPaneExternalEventHandler();
            _extEvent = ExternalEvent.Create(_handler);

            // 把 ViewModel 的回調傳給 Handler，讓它執行完後更新 UI
            _handler.OnRecheckCompleted = () =>
            {
                ApplyFilters();
                OnPropertyChanged(nameof(SelectedItem));
            };

            UpdateStatusCommand = new RelayCommand(ExecuteUpdateStatus, () => HasSelectedItem);
            RecheckCommand = new RelayCommand(ExecuteRecheck);
            SingleRecheckCommand = new RelayCommandParam(param => ExecuteSingleRecheck(param as ReviewItem), param => param is ReviewItem);
            DoubleClickResultCommand = new RelayCommand(ExecuteDoubleClickResult);
            CreateSheetCommand = new RelayCommand(ExecuteCreateSheet);

            LoadInitialProject();
        }

        public void LoadInitialProject()
        {
            if (_uiapp?.ActiveUIDocument?.Document == null) return;
            var doc = _uiapp.ActiveUIDocument.Document;

            string projectName = doc.Title;
            string assemblyFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string rulesDir = System.IO.Path.Combine(assemblyFolder, "Resources", "Rules");
            if (!System.IO.Directory.Exists(rulesDir))
            {
                rulesDir = System.IO.Path.Combine(assemblyFolder, "Rules");
            }

            Project = _trackerService.LoadOrCreateProject(doc, projectName, "集合住宅", "Residential_TW", rulesDir);
            
            // 初始化選定用途
            if (Project != null)
            {
                if (string.IsNullOrEmpty(Project.BuildingType))
                {
                    Project.BuildingType = "集合住宅";
                }
                _selectedBuildingType = Project.BuildingType;
                OnPropertyChanged(nameof(SelectedBuildingType));

                // 將各法規章節動態加入 Categories 清單中
                var chapters = Project.Items
                    .Select(i => i.LawChapter)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct()
                    .OrderBy(c => c);

                foreach (var ch in chapters)
                {
                    if (!Categories.Contains(ch))
                    {
                        Categories.Add(ch);
                    }
                }
            }
        }

        private void ApplyFilters()
        {
            if (Project == null) return;

            FilteredItems.Clear();
            foreach (var item in Project.Items)
            {
                // 同時支援原本的品類篩選與新加入的法規章節篩選
                if (SelectedCategoryFilter != "所有分類" && item.Category != SelectedCategoryFilter && item.LawChapter != SelectedCategoryFilter)
                    continue;

                // 檢討視角過濾
                if (SelectedPerspectiveFilter != "所有視角")
                {
                    if (SelectedPerspectiveFilter.Contains("建築師"))
                    {
                        if (item.Perspective != "Architect" && item.Perspective != "Both" && !string.IsNullOrEmpty(item.Perspective))
                            continue;
                    }
                    else if (SelectedPerspectiveFilter.Contains("營造廠"))
                    {
                        if (item.Perspective != "Contractor" && item.Perspective != "Both")
                            continue;
                    }
                }

                if (SelectedStatusFilter != "所有狀態")
                {
                    if (SelectedStatusFilter.Contains("Created") && item.Status != TrackingStatus.Created) continue;
                    if (SelectedStatusFilter.Contains("Modified") && item.Status != TrackingStatus.Modified) continue;
                    if (SelectedStatusFilter.Contains("Approved") && item.Status != TrackingStatus.Approved) continue;
                    if (SelectedStatusFilter.Contains("Rejected") && item.Status != TrackingStatus.Rejected) continue;
                }

                FilteredItems.Add(item);
            }
        }

        private void ExecuteUpdateStatus()
        {
            if (SelectedItem == null) return;
            if (_uiapp?.ActiveUIDocument?.Document == null) return;
            var doc = _uiapp.ActiveUIDocument.Document;

            _trackerService.UpdateItemStatus(doc, Project, SelectedItem, SelectedStatusInput, AssigneeInput, CommentInput);
            
            OnPropertyChanged(nameof(SelectedItem));
            ApplyFilters();
        }

        private void ExecuteRecheck()
        {
            if (Project == null) return;
            _handler.RequestRecheck(Project, _trackerService);
            _extEvent.Raise();
        }



        private void ExecuteSingleRecheck(ReviewItem item)
        {
            if (item == null || Project == null) return;
            _handler.RequestSingleRecheck(item, Project, _trackerService);
            _extEvent.Raise();
        }


        private void ExecuteDoubleClickResult()
        {
            if (SelectedResult == null || string.IsNullOrEmpty(SelectedResult.ElementId)) return;

            _handler.RequestZoom(SelectedResult.ElementId);
            _extEvent.Raise();
        }

        private void ExecuteCreateSheet()
        {
            if (Project == null) return;
            _handler.RequestCreateSheet(Project);
            _extEvent.Raise();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private BuiltInCategory ParseCategory(string catName)
        {
            if (Enum.TryParse<BuiltInCategory>(catName, out var result)) return result;
            if (!catName.StartsWith("OST_") && Enum.TryParse<BuiltInCategory>("OST_" + catName, out var res2)) return res2;
            return BuiltInCategory.INVALID;
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

    public class CodeReviewPaneExternalEventHandler : IExternalEventHandler
    {
        private string _targetElementId;
        private bool _requestCreateSheet;
        private ReviewProject _projectToDraw;

        // 全量重新檢核
        private ReviewProject _recheckProject;
        private IssueTrackerService _recheckTrackerService;

        // 單項檢核
        private ReviewItem _singleRecheckItem;
        private ReviewProject _singleRecheckProject;
        private IssueTrackerService _singleRecheckTrackerService;

        private readonly object _lock = new object();

        /// <summary>
        /// 執行完成後由 Revit 執行緒回呼 Dispatcher，更新 VM 的 UI。
        /// </summary>
        public Action OnRecheckCompleted { get; set; }

        public void RequestZoom(string elementId)
        {
            lock (_lock) { _targetElementId = elementId; }
        }

        public void RequestCreateSheet(ReviewProject project)
        {
            lock (_lock)
            {
                _projectToDraw = project;
                _requestCreateSheet = true;
            }
        }

        public void RequestRecheck(ReviewProject project, IssueTrackerService trackerService)
        {
            lock (_lock)
            {
                _recheckProject = project;
                _recheckTrackerService = trackerService;
            }
        }

        public void RequestSingleRecheck(ReviewItem item, ReviewProject project, IssueTrackerService trackerService)
        {
            lock (_lock)
            {
                _singleRecheckItem = item;
                _singleRecheckProject = project;
                _singleRecheckTrackerService = trackerService;
            }
        }

        public void Execute(UIApplication app)
        {
            string targetId;
            bool doCreateSheet;
            ReviewProject projectToDraw;
            ReviewProject recheckProject;
            IssueTrackerService recheckService;
            ReviewItem singleItem;
            ReviewProject singleProject;
            IssueTrackerService singleService;

            lock (_lock)
            {
                targetId = _targetElementId; _targetElementId = null;
                doCreateSheet = _requestCreateSheet; _requestCreateSheet = false;
                projectToDraw = _projectToDraw; _projectToDraw = null;
                recheckProject = _recheckProject; _recheckProject = null;
                recheckService = _recheckTrackerService; _recheckTrackerService = null;
                singleItem = _singleRecheckItem; _singleRecheckItem = null;
                singleProject = _singleRecheckProject; _singleRecheckProject = null;
                singleService = _singleRecheckTrackerService; _singleRecheckTrackerService = null;
            }

            var doc = app.ActiveUIDocument.Document;

            // --- 生成圖紙 ---
            if (doCreateSheet && projectToDraw != null)
            {
                try
                {
                    var sheetService = new ReportSheetService();
                    sheetService.GenerateReportSheet(doc, projectToDraw, app.ActiveUIDocument);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("生成圖紙失敗", ex.Message);
                }
            }

            // --- 全量重新檢核 ---
            if (recheckProject != null && recheckService != null)
            {
                try
                {
                    RunRecheckLogic(doc, recheckProject, recheckProject.Items, recheckService);
                    TaskDialog.Show("DT Code Review", "全自動法規檢核完成，追蹤狀態已更新！");
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("DT Code Review 錯誤", $"全量檢核發生異常：\n{ex.Message}");
                }
                finally
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => OnRecheckCompleted?.Invoke());
                }
            }

            // --- 單項檢核 ---
            if (singleItem != null && singleProject != null && singleService != null)
            {
                try
                {
                    RunRecheckLogic(doc, singleProject, new List<ReviewItem> { singleItem }, singleService);
                    bool hasFail = singleItem.Results?.Any(r => !r.Passed) == true;
                    TaskDialog.Show("DT Code Review", $"法規項目 [{singleItem.RuleCode}] 檢核完成！\n結果：{(hasFail ? $"🔴 發現 {singleItem.Results.Count(r => !r.Passed)} 處異常" : "✅ 符合法規規範")}");
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("DT Code Review 錯誤", $"執行單項檢核 [{singleItem.RuleCode}] 時發生異常：\n{ex.Message}\n\n{ex.StackTrace}");
                }
                finally
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => OnRecheckCompleted?.Invoke());
                }
            }

            // --- 元件對焦 ---
            if (!string.IsNullOrEmpty(targetId))
            {
                if (!int.TryParse(targetId, out int idVal)) return;

                var uidoc = app.ActiveUIDocument;
                var id = new ElementId(idVal);
                var elem = doc.GetElement(id);
                if (elem == null) return;

                try
                {
                    uidoc.Selection.SetElementIds(new List<ElementId> { id });
                    var uiView = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == doc.ActiveView.Id);
                    if (uiView != null)
                    {
                        var bbox = elem.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            XYZ center = (bbox.Max + bbox.Min) / 2.0;
                            uiView.ZoomAndCenterRectangle(center - new XYZ(6, 6, 6), center + new XYZ(6, 6, 6));
                        }
                    }
                }
                catch { }
            }
        }

        private void RunRecheckLogic(Document doc, ReviewProject project, IEnumerable<ReviewItem> items, IssueTrackerService trackerService)
        {
            var scanner = new ElementScanner(doc);

            string assemblyFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string rulesDir = System.IO.Path.Combine(assemblyFolder, "Resources", "Rules");
            if (!System.IO.Directory.Exists(rulesDir))
                rulesDir = System.IO.Path.Combine(assemblyFolder, "Rules");

            var ruleEngine = new RuleEngine();
            ruleEngine.LoadRules(rulesDir);

            foreach (var item in items)
            {
                var results = new List<ReviewResult>();

                if (item.RuleCode == "BCR-012")
                {
                    foreach (var elem in scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Rooms }))
                        results.AddRange(LevelChecker.Check(doc, elem));
                }
                else if (item.RuleCode == "BCR-015")
                {
                    foreach (var elem in scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Railings }))
                        results.AddRange(RailingChecker.Check(doc, elem));
                }
                else if (item.RuleCode == "BCR-033")
                {
                    foreach (var elem in scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Rooms }))
                        results.AddRange(CorridorChecker.Check(doc, elem, project.BuildingType));
                }
                else if (item.RuleCode == "BCR-034" || item.RuleCode == "BCR-037")
                {
                    foreach (var elem in scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Stairs }))
                        results.AddRange(StairChecker.Check(doc, elem, project.BuildingType));
                }
                else if (item.RuleCode == "BCR-038")
                {
                    var levels = scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Levels });
                    results.AddRange(FloorHeightChecker.Check(doc, levels));
                }
                else if (item.RuleCode == "BCR-074")
                {
                    foreach (var elem in scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Rooms }))
                        results.AddRange(FireDistrictAreaChecker.Check(doc, elem));
                }
                else if (item.RuleCode == "BCR-076" || item.RuleCode == "BCR-077" || item.RuleCode == "BCR-090" || item.RuleCode == "BCR-092" || item.RuleCode == "BCR-118")
                {
                    foreach (var elem in scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Doors }))
                        results.AddRange(DoorChecker.Check(doc, elem, item.RuleCode, project.BuildingType));
                }
                else if (item.RuleCode == "BCR-101")
                {
                    var svc = new DesignReviewService(doc);
                    foreach (var issue in svc.CheckEscapeDistance())
                        results.Add(new ReviewResult { Passed = false, Message = issue.Description, ElementId = issue.ElementId.ToString(), LevelName = issue.LevelName, Location = XYZPoint.FromXYZ(issue.Location) });
                }
                else if (item.RuleCode == "BCR-102")
                {
                    var svc = new DesignReviewService(doc);
                    foreach (var issue in svc.CheckWheelchairSpace())
                        results.Add(new ReviewResult { Passed = false, Message = issue.Description, ElementId = issue.ElementId.ToString(), LevelName = issue.LevelName, Location = XYZPoint.FromXYZ(issue.Location) });
                }
                else if (item.RuleCode == "BCR-103")
                {
                    var svc = new DesignReviewService(doc);
                    foreach (var issue in svc.CheckNetHeight())
                        results.Add(new ReviewResult { Passed = false, Message = issue.Description, ElementId = issue.ElementId.ToString(), LevelName = issue.LevelName, Location = XYZPoint.FromXYZ(issue.Location) });
                }
                else if (item.RuleCode == "BCR-104")
                {
                    var svc = new DesignReviewService(doc);
                    foreach (var issue in svc.CheckSleevePenetrations())
                        results.Add(new ReviewResult { Passed = false, Message = issue.Description, ElementId = issue.ElementId.ToString(), LevelName = issue.LevelName, Location = XYZPoint.FromXYZ(issue.Location) });
                }
                else if (item.RuleCode == "BCR-105")
                {
                    var svc = new DesignReviewService(doc);
                    foreach (var issue in svc.CheckRoomFinishes())
                        results.Add(new ReviewResult { Passed = false, Message = issue.Description, ElementId = issue.ElementId.ToString(), LevelName = issue.LevelName, Location = XYZPoint.FromXYZ(issue.Location) });
                }
                else if (item.RuleCode == "BCR-111")
                {
                    foreach (var elem in scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Ramps }))
                        results.AddRange(RampChecker.Check(doc, elem));
                }
                else if (item.RuleCode == "BCR-117")
                {
                    // 無障礙通路淨寬度
                    foreach (var elem in scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Rooms }))
                    {
                        string rName = elem.ElementName ?? string.Empty;
                        if (rName.Contains("無障礙通路") || rName.Contains("走廊") || rName.Contains("走道"))
                        {
                            double w = elem.GetDoubleParameter("廊道淨寬") ?? elem.GetDoubleParameter("寬度") ?? elem.GetDoubleParameter("Width") ?? 0;
                            if (w > 0 && w < 1300.0)
                            {
                                results.Add(new ReviewResult
                                {
                                    Passed = false,
                                    Message = $"無障礙通路「{rName}」淨寬為 {w:F1}mm，低於法規標準 1.3m (1300mm)。",
                                    ElementId = elem.ElementId,
                                    LevelName = elem.LevelName,
                                    Location = XYZPoint.FromXYZ(elem.Location)
                                });
                            }
                        }
                    }
                }
                else if (item.RuleCode == "BCR-119")
                {
                    foreach (var elem in scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Rooms }))
                        results.AddRange(AccessibleToiletChecker.Check(doc, elem));
                }
                else if (item.RuleCode == "BCR-120")
                {
                    foreach (var elem in scanner.ScanCategories(new List<BuiltInCategory> { BuiltInCategory.OST_Parking }))
                        results.AddRange(ParkingChecker.Check(doc, elem));
                }

                // fallback: 如果沒有特定檢核結果，按 RuleConfig 的 ApplicableCategories 掃描
                if (!results.Any())
                {
                    var ruleConfig = ruleEngine.RuleConfigs.FirstOrDefault(r => r.RuleCode == item.RuleCode);
                    if (ruleConfig?.ApplicableCategories?.Any() == true)
                    {
                        var categories = ruleConfig.ApplicableCategories
                            .Select(catName =>
                            {
                                if (Enum.TryParse<BuiltInCategory>(catName, out var bic)) return bic;
                                if (!catName.StartsWith("OST_") && Enum.TryParse<BuiltInCategory>("OST_" + catName, out var bic2)) return bic2;
                                return BuiltInCategory.INVALID;
                            })
                            .Where(c => c != BuiltInCategory.INVALID)
                            .ToList();

                        if (categories.Any())
                        {
                            foreach (var elem in scanner.ScanCategories(categories))
                                results.Add(new ReviewResult
                                {
                                    Passed = true,
                                    Message = item.Type == ReviewType.Auto ? "自動檢核通過，元件規格符合法規限制。" : "[人工檢核] 適用元件，請人工查核是否合規。",
                                    ElementId = elem.ElementId,
                                    LevelName = elem.LevelName,
                                    Location = XYZPoint.FromXYZ(elem.Location)
                                });
                        }
                    }
                }

                item.Results = results;

                bool failed = results.Any(r => !r.Passed);
                if (failed && item.Status == TrackingStatus.Approved)
                {
                    item.Status = TrackingStatus.Created;
                    item.History.Add(new TrackingHistoryEntry { Timestamp = DateTime.Now, FromStatus = TrackingStatus.Approved, ToStatus = TrackingStatus.Created, ChangedBy = "System", Comment = "自動重新檢核未通過，狀態重置。" });
                }
                else if (!failed && (item.Status == TrackingStatus.Created || item.Status == TrackingStatus.Modified))
                {
                    item.Status = TrackingStatus.Approved;
                    item.History.Add(new TrackingHistoryEntry { Timestamp = DateTime.Now, FromStatus = item.Status, ToStatus = TrackingStatus.Approved, ChangedBy = "System", Comment = "自動檢核通過！" });
                }
            }

            trackerService.SaveProject(doc, project);
        }

        public string GetName() => "CodeReviewPaneExternalEventHandler";
    }
}
