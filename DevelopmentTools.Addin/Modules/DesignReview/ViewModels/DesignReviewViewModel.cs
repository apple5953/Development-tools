using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.DesignReview.Models;
using DevelopmentTools.Modules.DesignReview.Services;
using DevelopmentTools.UI;
using System.Text.Json;

namespace DevelopmentTools.Modules.DesignReview.ViewModels
{
    // ── Navigation Item Model ───────────────────────────────────────────
    public class NavigationItem
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Description { get; set; }
        public Func<UIApplication, ExternalEvent, DesignReviewExternalEventHandler, SubReviewViewModelBase> CreateViewModel { get; set; }

        private SubReviewViewModelBase _cachedViewModel;
        public SubReviewViewModelBase GetOrCreateViewModel(UIApplication uiapp, ExternalEvent ev, DesignReviewExternalEventHandler h)
        {
            if (_cachedViewModel == null)
            {
                _cachedViewModel = CreateViewModel(uiapp, ev, h);
            }
            return _cachedViewModel;
        }

        public void ClearCache() => _cachedViewModel = null;
    }

    // ── Main Controller ViewModel ────────────────────────────────────────
    public class DesignReviewViewModel : INotifyPropertyChanged
    {
        private readonly UIApplication _uiapp;
        private readonly ExternalEvent _externalEvent;
        private readonly DesignReviewExternalEventHandler _handler;

        public DesignReviewViewModel(UIApplication uiapp)
        {
            _uiapp = uiapp;
            _handler = new DesignReviewExternalEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);

            InitializeNavigation();
        }

        public ObservableCollection<NavigationItem> NavigationItems { get; } = new ObservableCollection<NavigationItem>();

        private NavigationItem _selectedNavigationItem;
        public NavigationItem SelectedNavigationItem
        {
            get => _selectedNavigationItem;
            set
            {
                if (_selectedNavigationItem != value)
                {
                    _selectedNavigationItem = value;
                    OnPropertyChanged();
                    if (_selectedNavigationItem != null)
                    {
                        CurrentSubViewModel = _selectedNavigationItem.GetOrCreateViewModel(_uiapp, _externalEvent, _handler);
                        
                        // 若切換到總體檢討報告，主動刷新卡片狀態
                        if (CurrentSubViewModel is OverviewReviewViewModel overviewVm)
                        {
                            overviewVm.RefreshCardStatuses();
                        }
                    }
                }
            }
        }

        private SubReviewViewModelBase _currentSubViewModel;
        public SubReviewViewModelBase CurrentSubViewModel
        {
            get => _currentSubViewModel;
            set { _currentSubViewModel = value; OnPropertyChanged(); }
        }

        private string _statusMessage = "請在左側選單選擇檢核項目以開始";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private void InitializeNavigation()
        {
            // 0. 中華民國建築技術規則庫
            NavigationItems.Add(new NavigationItem
            {
                Icon = "📜",
                Name = "中華民國建築技術規則庫",
                Description = "逐條檢索台灣建築技術規則法條，動態檢查元件參數健康度與進行參數合規診斷。",
                CreateViewModel = (app, ev, h) => new RegulatoryCodesCatalogViewModel(app, ev, h)
            });

            // 1. 總體檢討報告
            NavigationItems.Add(new NavigationItem
            {
                Icon = "📊",
                Name = "總體檢討報告",
                Description = "查看 6 大幾何檢討項目的模型健康度、前置參數缺失與最新檢核狀態摘要。",
                CreateViewModel = (app, ev, h) => new OverviewReviewViewModel(app, ev, h, this)
            });

            // 2. 避難步行距離檢討
            NavigationItems.Add(new NavigationItem
            {
                Icon = "🚶‍♂️",
                Name = "避難步行距離檢討",
                Description = "計算房間最遠點至安全出口之真實避難長度，確保不超出法規 30m 限制。",
                CreateViewModel = (app, ev, h) => new EscapeDistanceReviewViewModel(app, ev, h)
            });

            // 3. 樓梯級高級深安全尺寸
            NavigationItems.Add(new NavigationItem
            {
                Icon = "📐",
                Name = "樓梯級高級深安全尺寸",
                Description = "驗證各樓梯之 Actual Riser Height 與 Actual Tread Depth 是否符合國家標準。",
                CreateViewModel = (app, ev, h) => new StairDimensionReviewViewModel(app, ev, h)
            });

            // 4. 無障礙迴轉圓空間檢核
            NavigationItems.Add(new NavigationItem
            {
                Icon = "♿",
                Name = "無障礙迴轉圓空間檢核",
                Description = "對無障礙空間執行最大內切圓空間演算法，驗證是否符合 1.5m 迴轉直徑限制。",
                CreateViewModel = (app, ev, h) => new WheelchairSpaceReviewViewModel(app, ev, h)
            });

            // 5. 梁下與機電管線結構淨高檢核
            NavigationItems.Add(new NavigationItem
            {
                Icon = "📏",
                Name = "梁下與機電管線結構淨高檢核",
                Description = "利用射線碰撞掃描各房間實質淨高，篩選出淨高低於 2.1m 的衝突區域。",
                CreateViewModel = (app, ev, h) => new NetHeightReviewViewModel(app, ev, h)
            });

            // 6. 管線穿梁牆套管遺漏檢核
            NavigationItems.Add(new NavigationItem
            {
                Icon = "🔧",
                Name = "管線穿梁牆套管遺漏檢核",
                Description = "篩選所有穿越剪力牆、結構梁的風/水/線管線，檢核其是否遺漏配置穿套管並一鍵生成。",
                CreateViewModel = (app, ev, h) => new SleevePenetrationReviewViewModel(app, ev, h)
            });

            // 7. 房間裝修幾何一致性檢核
            NavigationItems.Add(new NavigationItem
            {
                Icon = "🏠",
                Name = "房間裝修幾何一致性檢核",
                Description = "比對 Room 物件的裝修材料參數與實體地板、天花板的實際繪製情況，確保圖資相符。",
                CreateViewModel = (app, ev, h) => new RoomFinishReviewViewModel(app, ev, h)
            });

            // 預設選取中華民國建築技術規則庫
            SelectedNavigationItem = NavigationItems.FirstOrDefault();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Base Class for Sub ViewModels ────────────────────────────────────
    public abstract class SubReviewViewModelBase : INotifyPropertyChanged
    {
        protected readonly UIApplication Uiapp;
        protected readonly ExternalEvent ExternalEvent;
        protected readonly DesignReviewExternalEventHandler Handler;

        protected SubReviewViewModelBase(UIApplication uiapp, ExternalEvent externalEvent, DesignReviewExternalEventHandler handler)
        {
            Uiapp = uiapp;
            ExternalEvent = externalEvent;
            Handler = handler;

            RunCheckCommand = new RelayCommand(ExecuteRunCheck, CanExecuteRunCheck);
            ShowIssueIn3DCommand = new RelayCommand(ExecuteShowIssueIn3D, CanExecuteShowIssueIn3D);
            AutoFixCommand = new RelayCommand(ExecuteAutoFix, CanExecuteAutoFix);

            RunPreCheckCommand = new RelayCommand(ExecuteRunPreCheck, CanExecuteRunPreCheck);
            SwitchToResultsCommand = new RelayCommand(ExecuteSwitchToResults);
            SwitchToPreCheckCommand = new RelayCommand(ExecuteSwitchToPreCheck);
            SelectMissingElementsCommand = new RelayCommand(ExecuteSelectMissingElements, CanExecuteSelectMissingElements);
        }

        // ── 法規說明與診斷資訊屬性 ──
        private string _regRuleName;
        public string RegRuleName
        {
            get => _regRuleName;
            set { _regRuleName = value; OnPropertyChanged(); }
        }

        private string _regRuleDescription;
        public string RegRuleDescription
        {
            get => _regRuleDescription;
            set { _regRuleDescription = value; OnPropertyChanged(); }
        }

        private string _requiredComponentsText;
        public string RequiredComponentsText
        {
            get => _requiredComponentsText;
            set { _requiredComponentsText = value; OnPropertyChanged(); }
        }

        private string _requiredParametersText;
        public string RequiredParametersText
        {
            get => _requiredParametersText;
            set { _requiredParametersText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PreCheckItem> PreCheckItems { get; } = new ObservableCollection<PreCheckItem>();

        private int _selectedSubTabIndex = 0; // 0: 法規與前置健康度檢查, 1: 檢核結果列表
        public int SelectedSubTabIndex
        {
            get => _selectedSubTabIndex;
            set { _selectedSubTabIndex = value; OnPropertyChanged(); }
        }

        private PreCheckItem _selectedPreCheckItem;
        public PreCheckItem SelectedPreCheckItem
        {
            get => _selectedPreCheckItem;
            set
            {
                _selectedPreCheckItem = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedPreCheckItem));
            }
        }

        public bool HasSelectedPreCheckItem => SelectedPreCheckItem != null && SelectedPreCheckItem.MissingElementIds != null && SelectedPreCheckItem.MissingElementIds.Any();

        private string _latestCheckResultSummary = "⚪ 未執行";
        public string LatestCheckResultSummary
        {
            get => _latestCheckResultSummary;
            set { _latestCheckResultSummary = value; OnPropertyChanged(); }
        }

        // ── 原始狀態與命令 ──
        private bool _isChecking;
        public bool IsChecking
        {
            get => _isChecking;
            set
            {
                _isChecking = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotChecking));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsNotChecking => !IsChecking;

        private string _statusMessage = "尚未執行檢核";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private ReviewIssue _selectedIssue;
        public ReviewIssue SelectedIssue
        {
            get => _selectedIssue;
            set
            {
                _selectedIssue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedIssue));
            }
        }

        public bool HasSelectedIssue => SelectedIssue != null;

        public ICommand RunCheckCommand { get; }
        public ICommand ShowIssueIn3DCommand { get; }
        public ICommand AutoFixCommand { get; }

        public ICommand RunPreCheckCommand { get; }
        public ICommand SwitchToResultsCommand { get; }
        public ICommand SwitchToPreCheckCommand { get; }
        public ICommand SelectMissingElementsCommand { get; }

        protected virtual bool CanExecuteRunCheck() => !IsChecking;
        protected abstract void ExecuteRunCheck();

        protected virtual bool CanExecuteShowIssueIn3D() => SelectedIssue != null && SelectedIssue.Location != null;

        protected virtual void ExecuteShowIssueIn3D()
        {
            if (SelectedIssue == null || SelectedIssue.Location == null) return;

            XYZ loc = SelectedIssue.Location;
            ElementId targetElemId = SelectedIssue.ElementId;

            Handler.RequestAction(uiapp =>
            {
                Document doc = uiapp.ActiveUIDocument.Document;

                View3D view3d = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => v.Name == "DesignReview_3D");

                using (Transaction t = new Transaction(doc, "建立設計審查 3D 視圖"))
                {
                    t.Start();
                    if (view3d == null)
                    {
                        ViewFamilyType viewFamilyType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                        view3d = View3D.CreateIsometric(doc, viewFamilyType.Id);
                        view3d.Name = "DesignReview_3D";
                    }

                    view3d.IsSectionBoxActive = true;
                    BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                    sectionBox.Min = loc - new XYZ(6, 6, 6);
                    sectionBox.Max = loc + new XYZ(6, 6, 6);
                    view3d.SetSectionBox(sectionBox);

                    t.Commit();
                }

                uiapp.ActiveUIDocument.ActiveView = view3d;

                if (targetElemId != ElementId.InvalidElementId)
                {
                    uiapp.ActiveUIDocument.Selection.SetElementIds(new List<ElementId> { targetElemId });
                    try { uiapp.ActiveUIDocument.ShowElements(targetElemId); } catch { }
                }
            });

            ExternalEvent.Raise();
        }

        protected virtual bool CanExecuteAutoFix() => false;
        protected virtual void ExecuteAutoFix() { }

        protected virtual bool CanExecuteRunPreCheck() => !IsChecking;
        protected virtual void ExecuteRunPreCheck() { }

        protected virtual void ExecuteSwitchToResults()
        {
            SelectedSubTabIndex = 1;
            RunCheckCommand.Execute(null);
        }

        protected virtual void ExecuteSwitchToPreCheck()
        {
            SelectedSubTabIndex = 0;
        }

        protected virtual bool CanExecuteSelectMissingElements() => HasSelectedPreCheckItem;
        protected virtual void ExecuteSelectMissingElements()
        {
            if (SelectedPreCheckItem == null || SelectedPreCheckItem.MissingElementIds == null || !SelectedPreCheckItem.MissingElementIds.Any()) return;

            var ids = SelectedPreCheckItem.MissingElementIds;
            Handler.RequestAction(uiapp =>
            {
                uiapp.ActiveUIDocument.Selection.SetElementIds(ids);
                try { uiapp.ActiveUIDocument.ShowElements(ids.First()); } catch { }
            });
            ExternalEvent.Raise();
        }

        protected void LoadRuleFromCatalog(string codeId)
        {
            try
            {
                string assemblyFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string jsonPath = System.IO.Path.Combine(assemblyFolder, "Resources", "ArchitectureCodes.json");
                if (!System.IO.File.Exists(jsonPath)) jsonPath = System.IO.Path.Combine(assemblyFolder, "ArchitectureCodes.json");

                if (System.IO.File.Exists(jsonPath))
                {
                    string json = System.IO.File.ReadAllText(jsonPath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var rules = JsonSerializer.Deserialize<List<JsonCodeRule>>(json, options);
                    var jr = rules.FirstOrDefault(x => x.CodeId == codeId);
                    if (jr != null)
                    {
                        RegRuleName = $"{jr.Section} {jr.RuleName}";
                        RegRuleDescription = jr.Description;
                        RequiredComponentsText = string.Join("\n", jr.ApplicableCategories.Select(c => $"• 品類: {c.Replace("OST_", "")}"));
                        RequiredParametersText = string.Join("\n", jr.RequiredParameters.Select(p => $"• 參數: {p}"));
                    }
                }
            }
            catch {}
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── JSON 法規模型 ──
    public class JsonCodeRule
    {
        public string CodeId { get; set; }
        public string Chapter { get; set; }
        public string Section { get; set; }
        public string RuleName { get; set; }
        public string Country { get; set; }
        public List<string> ApplicableCategories { get; set; }
        public List<string> RequiredParameters { get; set; }
        public string Description { get; set; }
        public string CheckMethod { get; set; }
        public string LogicExpression { get; set; }
    }

    // ── 📜 Regulatory Codes Catalog Sub ViewModel ────────────────────
    public class RegulatoryCodesCatalogViewModel : SubReviewViewModelBase
    {
        public ObservableCollection<RegulatoryCodeRule> Rules { get; } = new ObservableCollection<RegulatoryCodeRule>();
        
        private RegulatoryCodeRule _selectedRule;
        public RegulatoryCodeRule SelectedRule
        {
            get => _selectedRule;
            set
            {
                _selectedRule = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedRule));
                if (_selectedRule != null)
                {
                    LoadPreCheckItemsForRule(_selectedRule);
                }
            }
        }

        public bool HasSelectedRule => SelectedRule != null;

        private List<JsonCodeRule> _jsonRules = new List<JsonCodeRule>();

        public RegulatoryCodesCatalogViewModel(UIApplication uiapp, ExternalEvent externalEvent, DesignReviewExternalEventHandler handler)
            : base(uiapp, externalEvent, handler)
        {
            RegRuleName = "📜 中華民國建築技術規則與輔助法規庫";
            RegRuleDescription = "呈現台灣《建築技術規則》設計施工編核心條文（與美/日對照法條）。系統會自動針對每一條法規動態檢查模型中對應元件與參數的健康完備度。針對「參數合規檢查」類法規，可直接在下方執行快速診斷。";
            
            LoadJsonRules();
            ExecuteRunPreCheck();
        }

        private void LoadJsonRules()
        {
            try
            {
                string assemblyFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string jsonPath = System.IO.Path.Combine(assemblyFolder, "Resources", "ArchitectureCodes.json");
                if (!System.IO.File.Exists(jsonPath))
                {
                    jsonPath = System.IO.Path.Combine(assemblyFolder, "ArchitectureCodes.json");
                }

                if (System.IO.File.Exists(jsonPath))
                {
                    string json = System.IO.File.ReadAllText(jsonPath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _jsonRules = JsonSerializer.Deserialize<List<JsonCodeRule>>(json, options);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"載入法規 JSON 異常: {ex.Message}";
            }
        }

        protected override void ExecuteRunPreCheck()
        {
            IsChecking = true;
            StatusMessage = "執行全法規參數健康度掃描中...";
            Rules.Clear();
            PreCheckItems.Clear();

            var doc = Uiapp.ActiveUIDocument.Document;

            try
            {
                foreach (var jr in _jsonRules)
                {
                    var rule = new RegulatoryCodeRule
                    {
                        CodeId = jr.CodeId,
                        Chapter = jr.Chapter,
                        Section = jr.Section,
                        RuleName = jr.RuleName,
                        Country = jr.Country,
                        Description = jr.Description,
                        CheckMethod = jr.CheckMethod,
                        RunStatus = "⚪ 未執行"
                    };

                    DiagnoseRule(doc, jr, rule);
                    Rules.Add(rule);
                }

                StatusMessage = $"健康度掃描完成。共載入 {Rules.Count} 條法規條款。";
                if (Rules.Any()) SelectedRule = Rules.First();
            }
            catch (Exception ex)
            {
                StatusMessage = $"掃描異常: {ex.Message}";
            }
            finally
            {
                IsChecking = false;
            }
        }

        private void DiagnoseRule(Document doc, JsonCodeRule jr, RegulatoryCodeRule rule)
        {
            bool hasAnyElements = false;
            bool missingParams = false;

            foreach (var catStr in jr.ApplicableCategories)
            {
                BuiltInCategory bic = ParseCategory(catStr);
                if (bic == BuiltInCategory.INVALID) continue;

                var elements = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (elements.Any())
                {
                    hasAnyElements = true;
                    foreach (var param in jr.RequiredParameters)
                    {
                        var lackingElems = elements.Where(e => !HasParameter(e, param)).ToList();
                        if (lackingElems.Any())
                        {
                            missingParams = true;
                        }
                    }
                }
            }

            if (!hasAnyElements)
            {
                rule.HealthStatus = "❌ 缺少元件";
            }
            else if (missingParams)
            {
                rule.HealthStatus = "⚠️ 參數不完備";
            }
            else
            {
                rule.HealthStatus = "✅ 參數齊備";
            }
        }

        private void LoadPreCheckItemsForRule(RegulatoryCodeRule rule)
        {
            PreCheckItems.Clear();
            var doc = Uiapp.ActiveUIDocument.Document;

            var jr = _jsonRules.FirstOrDefault(x => x.CodeId == rule.CodeId);
            if (jr == null) return;

            foreach (var catStr in jr.ApplicableCategories)
            {
                BuiltInCategory bic = ParseCategory(catStr);
                if (bic == BuiltInCategory.INVALID) continue;

                string categoryName = catStr.Replace("OST_", "");
                
                var elements = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (!elements.Any())
                {
                    PreCheckItems.Add(new PreCheckItem
                    {
                        TargetName = $"品類: {categoryName}",
                        Status = "❌ 嚴重缺失 (數量為 0)",
                        Description = $"模型中未放置任何 {categoryName} 元件。請先在專案中配置此類元件方可檢討。",
                        IsCritical = true
                    });
                }
                else
                {
                    PreCheckItems.Add(new PreCheckItem
                    {
                        TargetName = $"品類: {categoryName}",
                        Status = $"✅ 正常 (已偵測到 {elements.Count} 個元件)",
                        Description = $"模型中已配置 {categoryName} 元件，可進行檢核。",
                        IsCritical = false
                    });

                    foreach (var paramName in jr.RequiredParameters)
                    {
                        var lacking = elements.Where(e => !HasParameter(e, paramName)).ToList();
                        if (lacking.Any())
                        {
                            PreCheckItems.Add(new PreCheckItem
                            {
                                TargetName = $"必要參數: {paramName}",
                                Status = $"⚠️ 警告 ({lacking.Count} 個元件缺少參數)",
                                Description = $"部分 {categoryName} 元件缺少「{paramName}」參數，將導致法規比對時數據缺失。",
                                IsCritical = false,
                                MissingElementIds = lacking.Select(e => e.Id).ToList()
                            });
                        }
                        else
                        {
                            PreCheckItems.Add(new PreCheckItem
                            {
                                TargetName = $"必要參數: {paramName}",
                                Status = "✅ 正常 (所有元件皆已填寫)",
                                Description = $"所有 {categoryName} 元件均已具備「{paramName}」參數設定。",
                                IsCritical = false
                            });
                        }
                    }
                }
            }
        }

        private BuiltInCategory ParseCategory(string catName)
        {
            if (Enum.TryParse<BuiltInCategory>(catName, out var result)) return result;
            if (!catName.StartsWith("OST_") && Enum.TryParse<BuiltInCategory>("OST_" + catName, out var res2)) return res2;
            return BuiltInCategory.INVALID;
        }

        private bool HasParameter(Element elem, string paramName)
        {
            Parameter p = elem.LookupParameter(paramName);
            if (p != null && p.HasValue)
            {
                if (p.StorageType == StorageType.Double && p.AsDouble() != 0) return true;
                if (p.StorageType == StorageType.String && !string.IsNullOrEmpty(p.AsString())) return true;
                if (p.StorageType == StorageType.Integer && p.AsInteger() != 0) return true;
                if (p.StorageType == StorageType.ElementId && p.AsElementId() != ElementId.InvalidElementId) return true;
            }

            ElementId typeId = elem.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                Element typeElem = elem.Document.GetElement(typeId);
                if (typeElem != null)
                {
                    Parameter tp = typeElem.LookupParameter(paramName);
                    if (tp != null && tp.HasValue)
                    {
                        if (tp.StorageType == StorageType.Double && tp.AsDouble() != 0) return true;
                        if (tp.StorageType == StorageType.String && !string.IsNullOrEmpty(tp.AsString())) return true;
                        if (tp.StorageType == StorageType.Integer && tp.AsInteger() != 0) return true;
                        if (tp.StorageType == StorageType.ElementId && tp.AsElementId() != ElementId.InvalidElementId) return true;
                    }
                }
            }

            return false;
        }

        protected override void ExecuteRunCheck()
        {
            if (SelectedRule == null) return;
            IsChecking = true;
            StatusMessage = $"正在執行 {SelectedRule.RuleName} 參數合規檢測...";

            var doc = Uiapp.ActiveUIDocument.Document;
            var jr = _jsonRules.FirstOrDefault(x => x.CodeId == SelectedRule.CodeId);
            
            Handler.RequestAction(uiapp =>
            {
                try
                {
                    var results = new List<ReviewIssue>();
                    if (jr != null && jr.CheckMethod == "參數合規檢查")
                    {
                        foreach (var catStr in jr.ApplicableCategories)
                        {
                            BuiltInCategory bic = ParseCategory(catStr);
                            if (bic == BuiltInCategory.INVALID) continue;

                            var elements = new FilteredElementCollector(doc)
                                .OfCategory(bic)
                                .WhereElementIsNotElementType()
                                .ToList();

                            foreach (var elem in elements)
                            {
                                if (jr.CodeId == "TW_Design_38") // 陽台欄杆高度
                                {
                                    Parameter p = elem.LookupParameter("Height") ?? elem.LookupParameter("欄杆高度") ?? elem.LookupParameter("高度");
                                    if (p != null && p.HasValue)
                                    {
                                        double heightMeter = p.AsDouble() * 0.3048;
                                        if (heightMeter < 1.1 && heightMeter > 0.1)
                                        {
                                            results.Add(new ReviewIssue
                                            {
                                                IssueId = Guid.NewGuid().ToString(),
                                                RuleName = jr.RuleName,
                                                Severity = Severity.Error,
                                                Description = $"陽台欄杆高度不足！實際高度為 {heightMeter:F2}m (法規要求 >= 1.1m)。",
                                                ElementId = elem.Id,
                                                ElementName = elem.Name,
                                                LevelName = doc.GetElement(elem.LevelId)?.Name ?? "未指定樓層",
                                                Location = elem.get_BoundingBox(null)?.Min
                                            });
                                        }
                                    }
                                }
                                else if (jr.CodeId == "TW_Design_76") // 防火門耐火時間
                                {
                                    Parameter p = elem.LookupParameter("防火時效") ?? elem.LookupParameter("耐火時間");
                                    if (p != null && p.HasValue)
                                    {
                                        string fireVal = p.AsString() ?? p.AsValueString();
                                        if (string.IsNullOrEmpty(fireVal) || (!fireVal.Contains("1") && !fireVal.Contains("2") && !fireVal.Contains("甲")))
                                        {
                                            results.Add(new ReviewIssue
                                            {
                                                IssueId = Guid.NewGuid().ToString(),
                                                RuleName = jr.RuleName,
                                                Severity = Severity.Error,
                                                Description = $"防火門耐火時效不符標準！參數設定為「{fireVal}」(要求甲種防火門或耐火 1hr 以上)。",
                                                ElementId = elem.Id,
                                                ElementName = elem.Name,
                                                LevelName = doc.GetElement(elem.LevelId)?.Name ?? "未指定樓層",
                                                Location = (elem.Location as LocationPoint)?.Point
                                            });
                                        }
                                    }
                                }
                                else if (jr.CodeId == "TW_Design_92") // 走廊淨寬
                                {
                                    Parameter p = elem.LookupParameter("Width") ?? elem.LookupParameter("廊道淨寬") ?? elem.LookupParameter("寬度");
                                    if (p != null && p.HasValue)
                                    {
                                        double wVal = p.AsDouble() * 0.3048;
                                        if (wVal < 1.2 && wVal > 0.1)
                                        {
                                            results.Add(new ReviewIssue
                                            {
                                                IssueId = Guid.NewGuid().ToString(),
                                                RuleName = jr.RuleName,
                                                Severity = Severity.Error,
                                                Description = $"走廊淨寬不足！實際寬度為 {wVal:F2}m (法規要求走廊最窄不得小於 1.2m)。",
                                                ElementId = elem.Id,
                                                ElementName = elem.Name,
                                                LevelName = doc.GetElement(elem.LevelId)?.Name ?? "未指定樓層"
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsChecking = false;
                        if (jr != null && jr.CheckMethod == "自動幾何運算")
                        {
                            SelectedRule.RunStatus = "請直接由左側對應幾何檢核頁面執行運算";
                        }
                        else if (jr != null && jr.CheckMethod == "參數合規檢查")
                        {
                            SelectedRule.RunStatus = results.Any() ? $"🔴 發現 {results.Count} 處異常" : "✅ 符合法規";
                        }
                        else
                        {
                            SelectedRule.RunStatus = "需人工目視審查";
                        }
                        StatusMessage = $"檢核完成。結果: {SelectedRule.RunStatus}";
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsChecking = false;
                        StatusMessage = $"合規檢測出錯: {ex.Message}";
                    });
                }
            });
            ExternalEvent.Raise();
        }
    }

    // ── 1. Overview / Dashboard Sub ViewModel ──────────────────────────
    public class ReviewSummaryCard : INotifyPropertyChanged
    {
        private readonly SubReviewViewModelBase _subVm;

        public ReviewSummaryCard(SubReviewViewModelBase subVm, string icon, string name, string description, ICommand goToCommand)
        {
            _subVm = subVm ?? throw new ArgumentNullException(nameof(subVm));
            Icon = icon;
            Name = name;
            Description = description;
            GoToCommand = goToCommand;

            // 訂閱子 ViewModel 的變更以即時更新卡片狀態
            _subVm.PropertyChanged += OnSubVmPropertyChanged;

            UpdateProperties();

            RunCheckCommand = new RelayCommand(() =>
            {
                if (_subVm.RunCheckCommand.CanExecute(null))
                {
                    _subVm.RunCheckCommand.Execute(null);
                }
            }, () => !_subVm.IsChecking);
        }

        private void OnSubVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SubReviewViewModelBase.LatestCheckResultSummary) ||
                e.PropertyName == nameof(SubReviewViewModelBase.IsChecking) ||
                e.PropertyName == "PreCheckItems")
            {
                UpdateProperties();
            }
        }

        public void UpdateProperties()
        {
            string health = "✅ 健康";
            if (_subVm.PreCheckItems.Any(p => p.IsCritical)) health = "❌ 嚴重缺失";
            else if (_subVm.PreCheckItems.Any(p => p.Status.Contains("⚠️"))) health = "⚠️ 參數不完備";

            HealthStatus = $"健康度: {health}";
            CheckStatus = $"最新檢核: {_subVm.LatestCheckResultSummary}";
            IsChecking = _subVm.IsChecking;
        }

        public string Name { get; set; }
        public string Icon { get; set; }
        public string Description { get; set; }

        private string _healthStatus;
        public string HealthStatus
        {
            get => _healthStatus;
            set { _healthStatus = value; OnPropertyChanged(); }
        }

        private string _checkStatus;
        public string CheckStatus
        {
            get => _checkStatus;
            set { _checkStatus = value; OnPropertyChanged(); }
        }

        private bool _isChecking;
        public bool IsChecking
        {
            get => _isChecking;
            set { _isChecking = value; OnPropertyChanged(); }
        }

        public string DetectElements { get; set; }
        public string CheckCriteria { get; set; }

        public string ActionText { get; set; } = "前往此項檢討 ➔";
        public ICommand GoToCommand { get; set; }
        public ICommand RunCheckCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class OverviewReviewViewModel : SubReviewViewModelBase
    {
        private readonly DesignReviewViewModel _mainVm;
        public ObservableCollection<ReviewSummaryCard> Cards { get; } = new ObservableCollection<ReviewSummaryCard>();

        public OverviewReviewViewModel(UIApplication uiapp, ExternalEvent externalEvent, DesignReviewExternalEventHandler handler, DesignReviewViewModel mainVm)
            : base(uiapp, externalEvent, handler)
        {
            _mainVm = mainVm;
            RegRuleName = "BIM 事前設計審查與健康度報告";
            RegRuleDescription = "本系統提供 6 大事前檢檢與幾何診斷功能。此看板顯示目前模型的參數完備健康度與最新檢討結果統計。請點選卡片或左側目錄進入各細項檢查。";
            
            RefreshCardStatuses();
        }

        public void RefreshCardStatuses()
        {
            Cards.Clear();
            foreach (var item in _mainVm.NavigationItems.Skip(2)) // 排除法規庫和Overview自己
            {
                var subVm = item.GetOrCreateViewModel(Uiapp, ExternalEvent, Handler);

                string detectElements = "";
                string checkCriteria = "";

                if (subVm is EscapeDistanceReviewViewModel)
                {
                    detectElements = "房間 (Rooms)、門 (Doors)";
                    checkCriteria = "計算房間最遠點至出口之避難步行長度，不得超出法規 30m 限制。";
                }
                else if (subVm is StairDimensionReviewViewModel)
                {
                    detectElements = "樓梯 (Stairs)";
                    checkCriteria = "驗證各樓梯之 Actual Riser Height 與 Actual Tread Depth 是否符合國家標準。";
                }
                else if (subVm is WheelchairSpaceReviewViewModel)
                {
                    detectElements = "房間 (Rooms)";
                    checkCriteria = "篩選無障礙命名房間，執行最大內切圓空間演算法，驗證是否滿足 1.5m 迴轉直徑限制。";
                }
                else if (subVm is NetHeightReviewViewModel)
                {
                    detectElements = "房間 (Rooms)、樓板、結構梁、風管、水管、線槽";
                    checkCriteria = "於房間內執行 3D 射線干涉碰撞，掃描實質淨高，找出淨高低於 2.1m 的衝突區域。";
                }
                else if (subVm is SleevePenetrationReviewViewModel)
                {
                    detectElements = "風/水/線管線、剪力牆、結構梁、套管家族 (Sleeve)";
                    checkCriteria = "比對管路與結構牆梁碰撞，檢核交接處是否遺漏配置穿套管元件。";
                }
                else if (subVm is RoomFinishReviewViewModel)
                {
                    detectElements = "房間 (Rooms)、地板 (Floors)、天花板 (Ceilings)";
                    checkCriteria = "比對房間裝修參數 (Floor/Ceiling Finish) 與實際繪製的地板、天花板實體是否一致。";
                }

                var card = new ReviewSummaryCard(
                    subVm,
                    item.Icon,
                    item.Name,
                    item.Description,
                    new RelayCommand(() =>
                    {
                        _mainVm.SelectedNavigationItem = item;
                    }))
                {
                    DetectElements = detectElements,
                    CheckCriteria = checkCriteria
                };
                Cards.Add(card);
            }
        }

        protected override void ExecuteRunCheck()
        {
            IsChecking = true;
            StatusMessage = "重新整理所有項目健康度中...";

            try
            {
                foreach (var item in _mainVm.NavigationItems.Skip(2))
                {
                    var subVm = item.GetOrCreateViewModel(Uiapp, ExternalEvent, Handler);
                    subVm.RunPreCheckCommand.Execute(null);
                }
                RefreshCardStatuses();
                StatusMessage = "健康度整理完成。";
            }
            catch (Exception ex)
            {
                StatusMessage = $"整理異常: {ex.Message}";
            }
            finally
            {
                IsChecking = false;
            }
        }
    }

    // ── 2. Escape Distance Sub ViewModel ───────────────────────────────
    public class EscapeDistanceReviewViewModel : SubReviewViewModelBase
    {
        public ObservableCollection<EscapeDistanceIssue> Issues { get; } = new ObservableCollection<EscapeDistanceIssue>();

        public EscapeDistanceReviewViewModel(UIApplication uiapp, ExternalEvent externalEvent, DesignReviewExternalEventHandler handler)
            : base(uiapp, externalEvent, handler)
        {
            LoadRuleFromCatalog("TW_Design_95");
            ExecuteRunPreCheck();
        }

        protected override void ExecuteRunPreCheck()
        {
            PreCheckItems.Clear();
            var doc = Uiapp.ActiveUIDocument.Document;

            var rooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            var roomCheck = new PreCheckItem
            {
                TargetName = "房間元件 (Rooms)",
                Status = rooms.Any() ? $"✅ 正常 (已偵測到 {rooms.Count} 個房間)" : "❌ 嚴重缺失 (未偵測到任何房間)",
                Description = rooms.Any() ? "模型已具備房間空間劃分。" : "模型中無有效房間，避難距離檢核將無法執行。",
                IsCritical = !rooms.Any()
            };
            PreCheckItems.Add(roomCheck);

            var doors = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_Doors)
                .Cast<FamilyInstance>()
                .ToList();

            var doorCheck = new PreCheckItem
            {
                TargetName = "門元件 (Doors)",
                Status = doors.Any() ? $"✅ 正常 (已偵測到 {doors.Count} 個門)" : "❌ 嚴重缺失 (未偵測到任何門)",
                Description = doors.Any() ? "模型已放置門元件。" : "模型中無門元件，避難路徑將無法找到出口。",
                IsCritical = !doors.Any()
            };
            PreCheckItems.Add(doorCheck);

            var invalidDoors = new List<FamilyInstance>();
            if (doors.Any())
            {
                foreach (var door in doors)
                {
                    if (door.FromRoom == null && door.ToRoom == null)
                    {
                        invalidDoors.Add(door);
                    }
                }
            }

            var fromToRoomCheck = new PreCheckItem
            {
                TargetName = "門的房間關聯 (FromRoom/ToRoom)",
                Status = !invalidDoors.Any() ? "✅ 正常 (所有門均正確關聯房間)" : $"⚠️ 警告 ({invalidDoors.Count} 個門缺少房間關聯)",
                Description = !invalidDoors.Any() 
                    ? "所有門皆可做為合法的避難出口。" 
                    : "部分門未正確放置於房間邊界，導致 FromRoom 與 ToRoom 為空。這些門將無法被尋路演算法識別為出口，可能導致避難長度超標或計算失敗。",
                IsCritical = false,
                MissingElementIds = invalidDoors.Select(d => d.Id).ToList()
            };
            PreCheckItems.Add(fromToRoomCheck);
        }

        protected override void ExecuteRunCheck()
        {
            IsChecking = true;
            StatusMessage = "計算避難長度中...";
            Issues.Clear();

            Handler.RequestAction(uiapp =>
            {
                try
                {
                    var service = new DesignReviewService(uiapp.ActiveUIDocument.Document);
                    var results = service.CheckEscapeDistance();

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var issue in results) Issues.Add(issue);
                        IsChecking = false;
                        LatestCheckResultSummary = Issues.Any() ? $"🔴 發現 {Issues.Count} 處超標" : "✅ 符合法規";
                        StatusMessage = $"檢核完成。共發現 {Issues.Count} 個避難步行距離超標房間。";
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsChecking = false;
                        StatusMessage = $"錯誤: {ex.Message}";
                    });
                }
            });
            ExternalEvent.Raise();
        }
    }

    // ── 3. Stair Dimension Sub ViewModel ───────────────────────────────
    public class StairDimensionReviewViewModel : SubReviewViewModelBase
    {
        public ObservableCollection<ReviewIssue> Issues { get; } = new ObservableCollection<ReviewIssue>();

        public StairDimensionReviewViewModel(UIApplication uiapp, ExternalEvent externalEvent, DesignReviewExternalEventHandler handler)
            : base(uiapp, externalEvent, handler)
        {
            LoadRuleFromCatalog("TW_Design_33");
            ExecuteRunPreCheck();
        }

        protected override void ExecuteRunPreCheck()
        {
            PreCheckItems.Clear();
            var doc = Uiapp.ActiveUIDocument.Document;

            var stairs = new FilteredElementCollector(doc)
                .OfClass(typeof(Stairs))
                .Cast<Stairs>()
                .ToList();

            var stairCheck = new PreCheckItem
            {
                TargetName = "樓梯元件 (Stairs)",
                Status = stairs.Any() ? $"✅ 正常 (已偵測到 {stairs.Count} 個樓梯)" : "❌ 嚴重缺失 (未偵測到任何樓梯)",
                Description = stairs.Any() ? "模型中包含樓梯元件。" : "未偵測到樓梯，無法執行級高級深安全尺寸檢討。",
                IsCritical = !stairs.Any()
            };
            PreCheckItems.Add(stairCheck);

            var invalidStairs = new List<Stairs>();
            foreach (var stair in stairs)
            {
                double r = stair.ActualRiserHeight;
                double t = stair.ActualTreadDepth;
                if (r <= 0 || t <= 0)
                {
                    invalidStairs.Add(stair);
                }
            }

            var paramCheck = new PreCheckItem
            {
                TargetName = "樓梯級高級深參數",
                Status = !invalidStairs.Any() ? "✅ 正常 (級高級深參數正常)" : $"⚠️ 警告 ({invalidStairs.Count} 個樓梯缺少有效尺寸)",
                Description = !invalidStairs.Any() ? "所有樓梯尺寸參數均已填寫。" : "部分樓梯元件的實際級高或實際級深為 0，將無法正確進行法規比對。",
                IsCritical = false,
                MissingElementIds = invalidStairs.Select(s => s.Id).ToList()
            };
            PreCheckItems.Add(paramCheck);
        }

        protected override void ExecuteRunCheck()
        {
            IsChecking = true;
            StatusMessage = "檢查樓梯級高級深安全尺寸...";
            Issues.Clear();

            Handler.RequestAction(uiapp =>
            {
                try
                {
                    var service = new DesignReviewService(uiapp.ActiveUIDocument.Document);
                    var results = service.CheckStairDimensions();

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var issue in results) Issues.Add(issue);
                        IsChecking = false;
                        LatestCheckResultSummary = Issues.Any() ? $"🔴 發現 {Issues.Count} 處不合規" : "✅ 符合法規";
                        StatusMessage = $"檢核完成。共發現 {Issues.Count} 處不合規樓梯。";
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsChecking = false;
                        StatusMessage = $"錯誤: {ex.Message}";
                    });
                }
            });
            ExternalEvent.Raise();
        }
    }

    // ── 4. Wheelchair Space Sub ViewModel ──────────────────────────────
    public class WheelchairSpaceReviewViewModel : SubReviewViewModelBase
    {
        public ObservableCollection<ReviewIssue> Issues { get; } = new ObservableCollection<ReviewIssue>();

        public WheelchairSpaceReviewViewModel(UIApplication uiapp, ExternalEvent externalEvent, DesignReviewExternalEventHandler handler)
            : base(uiapp, externalEvent, handler)
        {
            LoadRuleFromCatalog("TW_Accessibility_5");
            ExecuteRunPreCheck();
        }

        protected override void ExecuteRunPreCheck()
        {
            PreCheckItems.Clear();
            var doc = Uiapp.ActiveUIDocument.Document;

            var rooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            var roomCheck = new PreCheckItem
            {
                TargetName = "房間空間元件 (Rooms)",
                Status = rooms.Any() ? $"✅ 正常 (已偵測到 {rooms.Count} 個房間)" : "❌ 嚴重缺失 (未偵測到任何房間)",
                Description = rooms.Any() ? "模型中包含房間空間。" : "無房間元件，無法執行輪椅迴轉空間檢算。",
                IsCritical = !rooms.Any()
            };
            PreCheckItems.Add(roomCheck);

            var barrierFreeRooms = new List<Room>();
            foreach (var r in rooms)
            {
                string name = r.Name;
                if (name.Contains("無障礙") || name.Contains("多功能") || name.Contains("殘障") || name.Contains("Barrier-Free"))
                {
                    barrierFreeRooms.Add(r);
                }
            }

            var keywordCheck = new PreCheckItem
            {
                TargetName = "無障礙關鍵字命名房間",
                Status = barrierFreeRooms.Any() ? $"✅ 正常 (找到 {barrierFreeRooms.Count} 個無障礙空間)" : "⚠️ 警告 (未找到任何命名含「無障礙/多功能」的房間)",
                Description = barrierFreeRooms.Any() 
                    ? "已識別出要進行迴轉圓檢索的無障礙空間。" 
                    : "系統依據房間名稱關鍵字（無障礙、多功能、殘障、Barrier-Free）來執行檢索。若無符合此名稱的房間，檢討將不會產生 any 數據。請檢查房間命名。",
                IsCritical = false,
                MissingElementIds = rooms.Select(r => r.Id).ToList()
            };
            PreCheckItems.Add(keywordCheck);
        }

        protected override void ExecuteRunCheck()
        {
            IsChecking = true;
            StatusMessage = "計算無障礙空間內切圓...";
            Issues.Clear();

            Handler.RequestAction(uiapp =>
            {
                try
                {
                    var service = new DesignReviewService(uiapp.ActiveUIDocument.Document);
                    var results = service.CheckWheelchairSpace();

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var issue in results) Issues.Add(issue);
                        IsChecking = false;
                        LatestCheckResultSummary = Issues.Any() ? $"🔴 發現 {Issues.Count} 處不符標準" : "✅ 符合法規";
                        StatusMessage = $"檢核完成。共發現 {Issues.Count} 處無障礙空間不符標準。";
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsChecking = false;
                        StatusMessage = $"錯誤: {ex.Message}";
                    });
                }
            });
            ExternalEvent.Raise();
        }
    }

    // ── 5. Net Height Sub ViewModel ──────────────────────────────
    public class NetHeightReviewViewModel : SubReviewViewModelBase
    {
        public ObservableCollection<NetHeightIssue> Issues { get; } = new ObservableCollection<NetHeightIssue>();

        public NetHeightReviewViewModel(UIApplication uiapp, ExternalEvent externalEvent, DesignReviewExternalEventHandler handler)
            : base(uiapp, externalEvent, handler)
        {
            LoadRuleFromCatalog("TW_Design_267");
            ExecuteRunPreCheck();
        }

        protected override void ExecuteRunPreCheck()
        {
            PreCheckItems.Clear();
            var doc = Uiapp.ActiveUIDocument.Document;

            View3D view3d = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);

            var viewCheck = new PreCheckItem
            {
                TargetName = "非樣板 3D 視圖 (View3D)",
                Status = view3d != null ? $"✅ 正常 (已載入 {view3d.Name})" : "❌ 嚴重缺失 (找不到任何可用 3D 視圖)",
                Description = view3d != null ? "可用於射線干涉計算的 3D 視圖存在。" : "淨高檢測使用 `ReferenceIntersector` 射線法，必須在專案中至少包含一個非視圖樣板的預設 3D 視圖，否則無法運行。",
                IsCritical = view3d == null
            };
            PreCheckItems.Add(viewCheck);

            var rooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            var roomCheck = new PreCheckItem
            {
                TargetName = "房間元件 (Rooms)",
                Status = rooms.Any() ? $"✅ 正常 (已偵測到 {rooms.Count} 個房間)" : "❌ 嚴重缺失 (未偵測到任何房間)",
                Description = rooms.Any() ? "房間元件正常。" : "沒有房間元件，淨高檢測將無檢索範圍。",
                IsCritical = !rooms.Any()
            };
            PreCheckItems.Add(roomCheck);

            var structuralFraming = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType().ToList();
            var floors = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType().ToList();
            var ducts = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType().ToList();
            var pipes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves).WhereElementIsNotElementType().ToList();

            bool hasCollidables = structuralFraming.Any() || floors.Any() || ducts.Any() || pipes.Any();

            var collidableCheck = new PreCheckItem
            {
                TargetName = "結構與機電實體元件 (梁/板/風管/水管)",
                Status = hasCollidables 
                    ? $"✅ 正常 (梁:{structuralFraming.Count}, 板:{floors.Count}, 風管:{ducts.Count}, 水管:{pipes.Count})" 
                    : "⚠️ 警告 (模型中無結構或機電管路)",
                Description = hasCollidables ? "模型中具備可進行射線檢測的實體目標。" : "專案中沒有任何結構梁、樓板或 MEP 管線，淨高檢測將不會有任何結果。",
                IsCritical = false
            };
            PreCheckItems.Add(collidableCheck);
        }

        protected override void ExecuteRunCheck()
        {
            IsChecking = true;
            StatusMessage = "射線干涉掃描房間淨高中...";
            Issues.Clear();

            Handler.RequestAction(uiapp =>
            {
                try
                {
                    var service = new DesignReviewService(uiapp.ActiveUIDocument.Document);
                    var results = service.CheckNetHeight();

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var issue in results) Issues.Add(issue);
                        IsChecking = false;
                        LatestCheckResultSummary = Issues.Any() ? $"🔴 發現 {Issues.Count} 處淨高不足" : "✅ 符合法規";
                        StatusMessage = $"檢核完成。共發現 {Issues.Count} 個區域淨高低於 2.1m。";
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsChecking = false;
                        StatusMessage = $"錯誤: {ex.Message}";
                    });
                }
            });
            ExternalEvent.Raise();
        }
    }

    // ── 6. Sleeve Penetration Sub ViewModel ────────────────────────────
    public class SleevePenetrationReviewViewModel : SubReviewViewModelBase
    {
        public ObservableCollection<SleevePenetrationIssue> Issues { get; } = new ObservableCollection<SleevePenetrationIssue>();

        public SleevePenetrationReviewViewModel(UIApplication uiapp, ExternalEvent externalEvent, DesignReviewExternalEventHandler handler)
            : base(uiapp, externalEvent, handler)
        {
            LoadRuleFromCatalog("MEP_Sleeve_Clash");
            ExecuteRunPreCheck();
        }

        protected override void ExecuteRunPreCheck()
        {
            PreCheckItems.Clear();
            var doc = Uiapp.ActiveUIDocument.Document;

            var walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w => w.StructuralUsage != StructuralWallUsage.NonBearing)
                .ToList();

            var beams = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .Cast<FamilyInstance>()
                .ToList();

            var structCheck = new PreCheckItem
            {
                TargetName = "結構主體元件 (剪力牆與結構梁)",
                Status = (walls.Any() || beams.Any()) ? $"✅ 正常 (剪力牆:{walls.Count}, 梁:{beams.Count})" : "❌ 嚴重缺失 (未偵測到結構牆梁)",
                Description = (walls.Any() || beams.Any()) ? "存在可進行穿透檢測的結構體。" : "專案中無任何剪力牆或結構梁，無法進行穿牆梁套管檢測。",
                IsCritical = !(walls.Any() || beams.Any())
            };
            PreCheckItems.Add(structCheck);

            var ducts = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType().ToList();
            var pipes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves).WhereElementIsNotElementType().ToList();
            var trays = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray).WhereElementIsNotElementType().ToList();

            var mepCheck = new PreCheckItem
            {
                TargetName = "機電管線元件 (風/水管與線槽)",
                Status = (ducts.Any() || pipes.Any() || trays.Any()) ? $"✅ 正常 (風管:{ducts.Count}, 水管:{pipes.Count}, 線槽:{trays.Count})" : "❌ 嚴重缺失 (無任何機電管線)",
                Description = (ducts.Any() || pipes.Any() || trays.Any()) ? "機電管路齊備。" : "專案中無任何風管、水管或線槽，無法執行穿梁牆碰撞計算。",
                IsCritical = !(ducts.Any() || pipes.Any() || trays.Any())
            };
            PreCheckItems.Add(mepCheck);

            FamilySymbol sleeveSymbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x.Name.Contains("套管") || x.Family.Name.Contains("套管") || x.Name.Contains("Sleeve"));

            var familyCheck = new PreCheckItem
            {
                TargetName = "預留孔套管元件家族 (Sleeve Family)",
                Status = sleeveSymbol != null ? $"✅ 正常 (已載入: {sleeveSymbol.Family.Name})" : "⚠️ 警告 (找不到套管家族)",
                Description = sleeveSymbol != null 
                    ? "專案已載入套管家族，可正常使用「一鍵生成套管」功能。" 
                    : "專案中找不到命名中含有「套管」或「Sleeve」的家族。若未載入套管家族，檢核時將無法執行「一鍵自動修復/放置套管」！請預先載入對應之機械設備套管家族。",
                IsCritical = false
            };
            PreCheckItems.Add(familyCheck);
        }

        protected override void ExecuteRunCheck()
        {
            IsChecking = true;
            StatusMessage = "掃描穿越結構體管線中...";
            Issues.Clear();

            Handler.RequestAction(uiapp =>
            {
                try
                {
                    var service = new DesignReviewService(uiapp.ActiveUIDocument.Document);
                    var results = service.CheckSleevePenetrations();

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var issue in results) Issues.Add(issue);
                        IsChecking = false;
                        LatestCheckResultSummary = Issues.Any() ? $"🔴 發現 {Issues.Count} 處套管遺漏" : "✅ 符合法規";
                        StatusMessage = $"檢核完成。共發現 {Issues.Count} 處穿梁牆套管遺漏。";
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsChecking = false;
                        StatusMessage = $"錯誤: {ex.Message}";
                    });
                }
            });
            ExternalEvent.Raise();
        }

        protected override bool CanExecuteAutoFix() => SelectedIssue != null;

        protected override void ExecuteAutoFix()
        {
            if (SelectedIssue == null) return;

            XYZ loc = SelectedIssue.Location;
            ElementId mepId = SelectedIssue.ElementId;

            Handler.RequestAction(uiapp =>
            {
                Document doc = uiapp.ActiveUIDocument.Document;

                FamilySymbol sleeveSymbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(x => x.Name.Contains("套管") || x.Family.Name.Contains("套管") || x.Name.Contains("Sleeve"));

                if (sleeveSymbol == null)
                {
                    TaskDialog.Show("錯誤", "專案中找不到命名包含「套管」或「Sleeve」的家族型別，請先載入對應家族。");
                    return;
                }

                using (Transaction t = new Transaction(doc, "自動放置穿梁牆套管"))
                {
                    t.Start();

                    if (!sleeveSymbol.IsActive)
                    {
                        sleeveSymbol.Activate();
                    }

                    FamilyInstance sleeveInstance = doc.Create.NewFamilyInstance(loc, sleeveSymbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    
                    Element mepElem = doc.GetElement(mepId);
                    if (mepElem != null)
                    {
                        Parameter diameterParam = mepElem.LookupParameter("Diameter") ?? mepElem.LookupParameter("管徑") ?? mepElem.LookupParameter("Size");
                        if (diameterParam != null && diameterParam.HasValue)
                        {
                            Parameter sleeveDiameter = sleeveInstance.LookupParameter("Diameter") ?? sleeveInstance.LookupParameter("套管管徑") ?? sleeveInstance.LookupParameter("外徑");
                            if (sleeveDiameter != null && !sleeveDiameter.IsReadOnly)
                            {
                                sleeveDiameter.Set(diameterParam.AsDouble() + (1.0 / 12.0)); 
                            }
                        }
                    }

                    t.Commit();
                }

                TaskDialog.Show("成功", "套管元件已成功自動生成放置於交接碰撞點。");

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var currentSelected = SelectedIssue as SleevePenetrationIssue;
                    if (currentSelected != null)
                    {
                        Issues.Remove(currentSelected);
                    }
                    SelectedIssue = null;
                });
            });

            ExternalEvent.Raise();
        }
    }

    // ── 7. Room Finish Sub ViewModel ──────────────────────────────────
    public class RoomFinishReviewViewModel : SubReviewViewModelBase
    {
        public ObservableCollection<ReviewIssue> Issues { get; } = new ObservableCollection<ReviewIssue>();

        public RoomFinishReviewViewModel(UIApplication uiapp, ExternalEvent externalEvent, DesignReviewExternalEventHandler handler)
            : base(uiapp, externalEvent, handler)
        {
            LoadRuleFromCatalog("ROOM_Finish_Consistency");
            ExecuteRunPreCheck();
        }

        protected override void ExecuteRunPreCheck()
        {
            PreCheckItems.Clear();
            var doc = Uiapp.ActiveUIDocument.Document;

            var rooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            var roomCheck = new PreCheckItem
            {
                TargetName = "房間空間元件 (Rooms)",
                Status = rooms.Any() ? $"✅ 正常 (已偵測到 {rooms.Count} 個房間)" : "❌ 嚴重缺失 (無房間元件)",
                Description = rooms.Any() ? "房間元件正常。" : "沒有房間空間元件，裝修一致性檢討將無對象。",
                IsCritical = !rooms.Any()
            };
            PreCheckItems.Add(roomCheck);

            var roomsWithFinishes = new List<Room>();
            foreach (var r in rooms)
            {
                string f = r.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR)?.AsString();
                string c = r.get_Parameter(BuiltInParameter.ROOM_FINISH_CEILING)?.AsString();
                if (!string.IsNullOrEmpty(f) || !string.IsNullOrEmpty(c))
                {
                    roomsWithFinishes.Add(r);
                }
            }

            var paramCheck = new PreCheckItem
            {
                TargetName = "房間裝修材料參數 (Floor/Ceiling Finish)",
                Status = roomsWithFinishes.Any() ? $"✅ 正常 (已設定 {roomsWithFinishes.Count} 間房間的裝修參數)" : "⚠️ 警告 (所有房間裝修參數皆為空)",
                Description = roomsWithFinishes.Any() 
                    ? "模型中有部分房間已定義裝修參數，可進行幾何一致性比對。" 
                    : "所有房間的 Floor Finish 或 Ceiling Finish 參數皆為空。若無填寫參數，檢討將無法比對實體地板與天花板的繪製狀態，結果將會是空的。請先在房間屬性中輸入裝修材料設定。",
                IsCritical = false,
                MissingElementIds = rooms.Select(r => r.Id).ToList()
            };
            PreCheckItems.Add(paramCheck);

            var floors = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType().ToList();
            var ceilings = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Ceilings).WhereElementIsNotElementType().ToList();

            var physicalCheck = new PreCheckItem
            {
                TargetName = "地板與天花板實體元件 (Floors/Ceilings)",
                Status = (floors.Any() || ceilings.Any()) ? $"✅ 正常 (地板:{floors.Count}, 天花板:{ceilings.Count})" : "⚠️ 警告 (無地板與天花板實體)",
                Description = (floors.Any() || ceilings.Any()) ? "實體元件存在。" : "模型中未繪製任何地板與天花板，將會被診斷為全部缺失。請確認實體模型是否已繪製。",
                IsCritical = false
            };
            PreCheckItems.Add(physicalCheck);
        }

        protected override void ExecuteRunCheck()
        {
            IsChecking = true;
            StatusMessage = "核對 Room 裝修幾何一致性中...";
            Issues.Clear();

            Handler.RequestAction(uiapp =>
            {
                try
                {
                    var service = new DesignReviewService(uiapp.ActiveUIDocument.Document);
                    var results = service.CheckRoomFinishes();

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var issue in results) Issues.Add(issue);
                        IsChecking = false;
                        LatestCheckResultSummary = Issues.Any() ? $"🔴 發現 {Issues.Count} 處裝修衝突" : "✅ 符合法規";
                        StatusMessage = $"檢核完成。共發現 {Issues.Count} 處空間裝修資訊邏輯衝突。";
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsChecking = false;
                        StatusMessage = $"錯誤: {ex.Message}";
                    });
                }
            });
            ExternalEvent.Raise();
        }
    }

    /// <summary>
    /// 通用 Revit ExternalEvent 處理器
    /// </summary>
    public class DesignReviewExternalEventHandler : IExternalEventHandler
    {
        private Action<UIApplication> _pendingAction;

        public void RequestAction(Action<UIApplication> action)
        {
            _pendingAction = action;
        }

        public void Execute(UIApplication uiapp)
        {
            try
            {
                _pendingAction?.Invoke(uiapp);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Revit Event 異常", ex.Message);
            }
            finally
            {
                _pendingAction = null;
            }
        }

        public string GetName() => "DesignReviewExternalEventHandler";
    }
}
