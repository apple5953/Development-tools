using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.ProjectStandards.Cloud.ModelResolver;
using DevelopmentTools.ProjectStandards.Domain.Dependency;
using DevelopmentTools.ProjectStandards.Domain.Diff;
using DevelopmentTools.ProjectStandards.Domain.Models;
using DevelopmentTools.ProjectStandards.Domain.Services;
using DevelopmentTools.ProjectStandards.Domain.Snapshots;
using DevelopmentTools.ProjectStandards.Revit2024.ExternalEvents;
using DevelopmentTools.ProjectStandards.Revit2024.Scanner;

namespace DevelopmentTools.ProjectStandards.UI.ViewModels
{
    public class ModelItemViewModel : INotifyPropertyChanged
    {
        private string _name;
        private ModelSourceType _sourceType;
        private string _sourceDescription;
        private bool _isSelected;
        private bool _canSync;
        private string _status;
        private bool _isReadOnlySnapshot;

        public string Name
        {
            get { return _name; }
            set { _name = value; OnPropertyChanged("Name"); }
        }

        public string FilePath { get; set; }

        public ModelSourceType SourceType
        {
            get { return _sourceType; }
            set
            {
                _sourceType = value;
                OnPropertyChanged("SourceType");
                OnPropertyChanged("SourceIcon");
            }
        }

        public string SourceDescription
        {
            get { return _sourceDescription; }
            set { _sourceDescription = value; OnPropertyChanged("SourceDescription"); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; OnPropertyChanged("IsSelected"); }
        }

        public bool CanSync
        {
            get { return _canSync; }
            set { _canSync = value; OnPropertyChanged("CanSync"); }
        }

        public bool IsReadOnlySnapshot
        {
            get { return _isReadOnlySnapshot; }
            set { _isReadOnlySnapshot = value; OnPropertyChanged("IsReadOnlySnapshot"); }
        }

        public string Status
        {
            get { return _status; }
            set { _status = value; OnPropertyChanged("Status"); }
        }

        public string SourceIcon
        {
            get
            {
                switch (SourceType)
                {
                    case ModelSourceType.RevitCloudModel:
                        return "☁ 即時雲端模型 (Live Cloud)";
                    case ModelSourceType.RevitCloudWorksharedModel:
                        return "☁ 雲端協同模型 (Cloud Workshared)";
                    case ModelSourceType.DesktopConnectorPublishedSnapshot:
                        return "🔒 發布快照 (唯讀比對)";
                    case ModelSourceType.DesktopConnectorFile:
                        return "📁 DOCS 檔案";
                    case ModelSourceType.NetworkFile:
                        return "🌐 區網伺服器";
                    default:
                        return "📄 本機檔案 (Local)";
                }
            }
        }

        public Document DocumentRef { get; set; }
        public ProjectStandardsSnapshot Snapshot { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propName));
        }
    }

    public class ProjectStandardsViewModel : INotifyPropertyChanged
    {
        private readonly UIApplication _uiApp;
        private readonly ExternalEvent _externalEvent;
        private readonly SimpleExternalEventHandler _eventHandler;

        private readonly IModelSourceResolver _sourceResolver;
        private readonly ICanonicalizationService _canonicalizer;
        private readonly ISignatureService _signatureService;
        private readonly IDiffEngine _diffEngine;
        private readonly IDependencyResolver _dependencyResolver;
        private readonly RevitStandardsScanner _scanner;

        private ModelItemViewModel _masterModel;
        private CategoryDiffSummary _selectedCategory;
        private DiffItem _selectedDiffItem;
        private string _statusMessage;
        private string _dryRunSummary;

        public ObservableCollection<ModelItemViewModel> TargetModels { get; set; }
        public ObservableCollection<CategoryDiffSummary> CategorySummaries { get; set; }
        public ObservableCollection<DiffItem> CurrentDiffItems { get; set; }
        public ObservableCollection<FieldDiff> CurrentFieldDiffs { get; set; }
        public ObservableCollection<SyncOperation> PlannedOperations { get; set; }

        public ModelItemViewModel MasterModel
        {
            get { return _masterModel; }
            set { _masterModel = value; OnPropertyChanged("MasterModel"); }
        }

        public CategoryDiffSummary SelectedCategory
        {
            get { return _selectedCategory; }
            set
            {
                _selectedCategory = value;
                OnPropertyChanged("SelectedCategory");
                FilterDiffItemsByCategory();
            }
        }

        public DiffItem SelectedDiffItem
        {
            get { return _selectedDiffItem; }
            set
            {
                _selectedDiffItem = value;
                OnPropertyChanged("SelectedDiffItem");
                UpdateFieldDiffs();
            }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { _statusMessage = value; OnPropertyChanged("StatusMessage"); }
        }

        public string DryRunSummary
        {
            get { return _dryRunSummary; }
            set { _dryRunSummary = value; OnPropertyChanged("DryRunSummary"); }
        }

        public ICommand ScanAndCompareCommand { get; set; }
        public ICommand DryRunCommand { get; set; }
        public ICommand AddTargetFileCommand { get; set; }

        private StandardsDiffResult _lastDiffResult;

        public ICommand SelectAllCategoriesCommand { get; set; }
        public ICommand UnselectAllCategoriesCommand { get; set; }
        public ICommand SelectAllItemsCommand { get; set; }
        public ICommand UnselectAllItemsCommand { get; set; }
        public ICommand SelectDiffOnlyItemsCommand { get; set; }
        public ICommand ApplySyncCommand { get; set; }

        public ProjectStandardsViewModel(UIApplication uiApp, ExternalEvent externalEvent, SimpleExternalEventHandler eventHandler)
        {
            _uiApp = uiApp;
            _externalEvent = externalEvent;
            _eventHandler = eventHandler;

            _sourceResolver = new ModelSourceResolver();
            _canonicalizer = new CanonicalizationService();
            _signatureService = new SignatureService(_canonicalizer);
            _diffEngine = new DiffEngine(_canonicalizer, _signatureService);
            _dependencyResolver = new DependencyResolver();
            _scanner = new RevitStandardsScanner();

            TargetModels = new ObservableCollection<ModelItemViewModel>();
            CategorySummaries = new ObservableCollection<CategoryDiffSummary>();
            CurrentDiffItems = new ObservableCollection<DiffItem>();
            CurrentFieldDiffs = new ObservableCollection<FieldDiff>();
            PlannedOperations = new ObservableCollection<SyncOperation>();

            ScanAndCompareCommand = new RelayCommand(p => ExecuteScanAndCompare());
            DryRunCommand = new RelayCommand(p => ExecuteDryRun(), p => _lastDiffResult != null && _lastDiffResult.TotalDifferences > 0);
            ApplySyncCommand = new RelayCommand(p => ExecuteApplySync(), p => _lastDiffResult != null && _lastDiffResult.Items.Any(i => i.IsSelectedForSync && i.Status != DiffStatus.Equal && i.Status != DiffStatus.Conflict));
            AddTargetFileCommand = new RelayCommand(p => ExecuteAddTargetFile());

            SelectAllCategoriesCommand = new RelayCommand(p => ExecuteSelectAllCategories(true));
            UnselectAllCategoriesCommand = new RelayCommand(p => ExecuteSelectAllCategories(false));
            SelectAllItemsCommand = new RelayCommand(p => ExecuteSelectAllCurrentItems(true));
            UnselectAllItemsCommand = new RelayCommand(p => ExecuteSelectAllCurrentItems(false));
            SelectDiffOnlyItemsCommand = new RelayCommand(p => ExecuteSelectDiffOnlyCurrentItems());

            InitializeFromCurrentSession();
        }

        private void InitializeFromCurrentSession()
        {
            var activeDoc = _uiApp.ActiveUIDocument != null ? _uiApp.ActiveUIDocument.Document : null;
            if (activeDoc != null)
            {
                var masterDesc = _sourceResolver.ResolveAsync(new ModelReference
                {
                    RawPath = activeDoc.PathName,
                    DisplayName = activeDoc.Title,
                    KnownIsCloudModel = activeDoc.IsModelInCloud,
                    KnownIsWorkshared = activeDoc.IsWorkshared
                }).Result;

                MasterModel = new ModelItemViewModel
                {
                    Name = activeDoc.Title,
                    FilePath = activeDoc.PathName,
                    SourceType = masterDesc.SourceType,
                    SourceDescription = masterDesc.FormattedSourceDescription,
                    CanSync = masterDesc.CanSynchronize,
                    DocumentRef = activeDoc,
                    Status = "基準模型 (作用中)"
                };

                foreach (Document doc in _uiApp.Application.Documents)
                {
                    if (doc.Title != activeDoc.Title)
                    {
                        var targetDesc = _sourceResolver.ResolveAsync(new ModelReference
                        {
                            RawPath = doc.PathName,
                            DisplayName = doc.Title,
                            KnownIsCloudModel = doc.IsModelInCloud,
                            KnownIsWorkshared = doc.IsWorkshared
                        }).Result;

                        bool isLinkedDoc = doc.IsLinked;
                        string status = isLinkedDoc ? "🔗 專案連結檔 (比對/同步)" : "已就緒 (已開啟檔案)";
                        string sourceDesc = isLinkedDoc ? "🔗 專案連結檔 (Linked Model)" : targetDesc.FormattedSourceDescription;

                        TargetModels.Add(new ModelItemViewModel
                        {
                            Name = doc.Title,
                            FilePath = doc.PathName,
                            SourceType = targetDesc.SourceType,
                            SourceDescription = sourceDesc,
                            IsSelected = !isLinkedDoc,
                            CanSync = true,
                            IsReadOnlySnapshot = false,
                            DocumentRef = doc,
                            Status = status
                        });
                    }
                }
            }

            StatusMessage = "系統就緒。請點擊【🔍 掃描並比對所有標準】開始進行專案標準差異分析。";
        }

        private void ExecuteSelectAllCategories(bool select)
        {
            foreach (var cat in CategorySummaries)
            {
                cat.IsSelected = select;
            }
            if (_lastDiffResult != null)
            {
                foreach (var item in _lastDiffResult.Items)
                {
                    item.IsSelectedForSync = select;
                }
            }
        }

        private void ExecuteSelectAllCurrentItems(bool select)
        {
            foreach (var item in CurrentDiffItems)
            {
                item.IsSelectedForSync = select;
            }
        }

        private void ExecuteSelectDiffOnlyCurrentItems()
        {
            foreach (var item in CurrentDiffItems)
            {
                item.IsSelectedForSync = (item.Status != DiffStatus.Equal && item.Status != DiffStatus.Conflict);
            }
        }

        private void ExecuteAddTargetFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Revit 專案檔案 (*.rvt)|*.rvt";
            dlg.Title = "選取要比對的目標 RVT 專案檔案";
            if (dlg.ShowDialog() == true)
            {
                var desc = _sourceResolver.ResolveAsync(new ModelReference
                {
                    RawPath = dlg.FileName,
                    DisplayName = Path.GetFileName(dlg.FileName)
                }).Result;

                TargetModels.Add(new ModelItemViewModel
                {
                    Name = Path.GetFileName(dlg.FileName),
                    FilePath = dlg.FileName,
                    SourceType = desc.SourceType,
                    SourceDescription = desc.FormattedSourceDescription,
                    IsSelected = true,
                    CanSync = desc.CanWrite,
                    IsReadOnlySnapshot = desc.IsPublishedSnapshot,
                    Status = "外部檔案 (已就緒)"
                });
            }
        }

        private void ExecuteScanAndCompare()
        {
            StatusMessage = "正在掃描基準與目標模型專案標準 (Revit API Context)...";

            _eventHandler.SetAction(app =>
            {
                if (MasterModel == null || MasterModel.DocumentRef == null)
                {
                    StatusMessage = "錯誤：尚未選取有效的基準模型。";
                    return;
                }

                // 1. Scan Master
                var masterDescriptor = _sourceResolver.ResolveAsync(new ModelReference
                {
                    RawPath = MasterModel.DocumentRef.PathName,
                    DisplayName = MasterModel.DocumentRef.Title,
                    KnownIsCloudModel = MasterModel.DocumentRef.IsModelInCloud,
                    KnownIsWorkshared = MasterModel.DocumentRef.IsWorkshared
                }).Result;

                MasterModel.Snapshot = _scanner.Scan(MasterModel.DocumentRef, masterDescriptor);
                MasterModel.Status = string.Format("已完成掃描 ({0} 參數, {1} 樣板, {2} 族群元件)",
                    MasterModel.Snapshot.Parameters.Count, MasterModel.Snapshot.ViewTemplates.Count, MasterModel.Snapshot.FamilyTypes.Count);

                // 2. Scan Targets
                var selectedTarget = TargetModels.FirstOrDefault(t => t.IsSelected);
                if (selectedTarget == null)
                {
                    StatusMessage = "請先至少勾選一個要比對的目標模型。";
                    return;
                }

                ProjectStandardsSnapshot targetSnapshot = null;
                if (selectedTarget.DocumentRef != null)
                {
                    var targetDesc = _sourceResolver.ResolveAsync(new ModelReference
                    {
                        RawPath = selectedTarget.DocumentRef.PathName,
                        DisplayName = selectedTarget.DocumentRef.Title,
                        KnownIsCloudModel = selectedTarget.DocumentRef.IsModelInCloud,
                        KnownIsWorkshared = selectedTarget.DocumentRef.IsWorkshared
                    }).Result;
                    targetSnapshot = _scanner.Scan(selectedTarget.DocumentRef, targetDesc);
                    selectedTarget.Status = string.Format("已完成掃描 ({0} 參數, {1} 樣板, {2} 族群元件)",
                        targetSnapshot.Parameters.Count, targetSnapshot.ViewTemplates.Count, targetSnapshot.FamilyTypes.Count);
                }
                else if (!string.IsNullOrEmpty(selectedTarget.FilePath) && File.Exists(selectedTarget.FilePath))
                {
                    var bgDoc = app.Application.OpenDocumentFile(selectedTarget.FilePath);
                    var targetDesc = _sourceResolver.ResolveAsync(new ModelReference
                    {
                        RawPath = selectedTarget.FilePath,
                        DisplayName = selectedTarget.Name
                    }).Result;
                    targetSnapshot = _scanner.Scan(bgDoc, targetDesc);
                    bgDoc.Close(false);
                    selectedTarget.Status = string.Format("已完成掃描 ({0} 參數, {1} 樣板, {2} 族群元件)",
                        targetSnapshot.Parameters.Count, targetSnapshot.ViewTemplates.Count, targetSnapshot.FamilyTypes.Count);
                }

                selectedTarget.Snapshot = targetSnapshot;

                // 3. Diff Engine Comparison
                _lastDiffResult = _diffEngine.Compare(MasterModel.Snapshot, selectedTarget.Snapshot);

                Action updateUiAction = new Action(() =>
                {
                    CategorySummaries.Clear();
                    foreach (var summary in _lastDiffResult.CategorySummaries.Values)
                    {
                        summary.PropertyChanged += OnCategorySummaryPropertyChanged;
                        CategorySummaries.Add(summary);
                    }

                    if (CategorySummaries.Count > 0)
                    {
                        SelectedCategory = CategorySummaries[0];
                    }

                    StatusMessage = string.Format("比對完成！共比對 {0} 個標準類別，發現 {1} 項差異 (包含 {2} 項嚴重衝突)。",
                        _lastDiffResult.CategorySummaries.Count, _lastDiffResult.TotalDifferences, _lastDiffResult.CriticalConflictCount);
                    DryRunSummary = "點擊【步驟一：執行 Dry Run 模擬】可檢查相依性並將待同步項目標註為黃色待確認狀態。";
                });

                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(updateUiAction);
                }
                else
                {
                    updateUiAction();
                }
            });

            _externalEvent.Raise();
        }

        private void OnCategorySummaryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsSelected")
            {
                var cat = sender as CategoryDiffSummary;
                if (cat != null && _lastDiffResult != null)
                {
                    var items = _lastDiffResult.Items.Where(i => i.AdapterKey == cat.CategoryKey || i.CategoryName == cat.CategoryDisplayName);
                    foreach (var item in items)
                    {
                        item.IsSelectedForSync = cat.IsSelected;
                    }
                }
            }
        }

        private void FilterDiffItemsByCategory()
        {
            CurrentDiffItems.Clear();
            if (_lastDiffResult == null || SelectedCategory == null) return;

            var items = _lastDiffResult.Items.Where(i => i.AdapterKey == SelectedCategory.CategoryKey || i.CategoryName == SelectedCategory.CategoryDisplayName || i.CategoryName == SelectedCategory.CategoryKey);
            foreach (var item in items)
            {
                CurrentDiffItems.Add(item);
            }

            if (CurrentDiffItems.Count > 0)
            {
                SelectedDiffItem = CurrentDiffItems[0];
            }
        }

        private void UpdateFieldDiffs()
        {
            CurrentFieldDiffs.Clear();
            if (SelectedDiffItem == null || SelectedDiffItem.FieldDiffs == null) return;

            foreach (var fd in SelectedDiffItem.FieldDiffs)
            {
                CurrentFieldDiffs.Add(fd);
            }
        }

        public class CustomDuplicateTypeNamesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }

        private void ExecuteDryRun()
        {
            if (_lastDiffResult == null) return;

            int candidateCount = 0;
            int conflictCount = 0;

            foreach (var item in _lastDiffResult.Items)
            {
                if (!item.IsSelectedForSync)
                {
                    item.ConfirmationState = "未勾選同步";
                    item.RowBackground = "#FFFFFF";
                    item.SyncMessage = "";
                    continue;
                }

                if (item.Status == DiffStatus.Equal)
                {
                    item.ConfirmationState = "完全相同 (略過)";
                    item.RowBackground = "#F8FAFC";
                    item.SyncMessage = "內容已與基準一致";
                }
                else if (item.Status == DiffStatus.Conflict)
                {
                    conflictCount++;
                    item.ConfirmationState = "衝突已阻斷 🚫";
                    item.RowBackground = "#FEE2E2"; // Light red
                    item.SyncMessage = item.ConflictReason ?? "嚴重衝突：系統已主動阻斷自動同步";
                }
                else
                {
                    candidateCount++;
                    item.ConfirmationState = "待確認 (Dry Run 通過)";
                    item.RowBackground = "#FEF3C7"; // Light amber/yellow
                    item.SyncMessage = "依賴拓撲檢查通過，待點擊【步驟二】確認同步";
                }
            }

            DryRunSummary = string.Format("【DRY RUN 模擬完成】共勾選 {0} 項待同步標準，{1} 項嚴重衝突已阻斷。請審閱上方黃色【待確認】細項後，點擊【步驟二：確認並執行同步】。",
                candidateCount, conflictCount);
            StatusMessage = "Dry Run 模擬完成！可同步項目已標示為黃色【待確認】狀態。";
        }

        private void ExecuteApplySync()
        {
            if (_lastDiffResult == null) return;

            var itemsToSync = _lastDiffResult.Items.Where(i => i.IsSelectedForSync && i.Status != DiffStatus.Equal && i.Status != DiffStatus.Conflict).ToList();
            if (itemsToSync.Count == 0)
            {
                StatusMessage = "未選取任何需要同步的項目。請在上方勾選欲同步之標準細項。";
                return;
            }

            StatusMessage = string.Format("正在執行專案標準同步調和 (共 {0} 項)...", itemsToSync.Count);

            _eventHandler.SetAction(app =>
            {
                try
                {
                    var masterDoc = MasterModel != null ? MasterModel.DocumentRef : null;
                    var selectedTarget = TargetModels.FirstOrDefault(t => t.IsSelected);

                    if (masterDoc == null || selectedTarget == null)
                    {
                        StatusMessage = "錯誤：無法取得基準模型或目標模型 Document 參考。";
                        return;
                    }

                    Document targetDoc = selectedTarget.DocumentRef;
                    bool openedBackgroundDoc = false;
                    string targetFilePath = selectedTarget.FilePath;

                    // If targetDoc is a linked document (cannot open transaction directly on linked instance in Revit)
                    if (targetDoc != null && targetDoc.IsLinked)
                    {
                        if (!string.IsNullOrEmpty(targetDoc.PathName) && File.Exists(targetDoc.PathName))
                        {
                            targetFilePath = targetDoc.PathName;
                            targetDoc = null; // Will open as primary background doc below
                        }
                        else
                        {
                            StatusMessage = "錯誤：目標模型為專案連結檔 (Linked File)。Revit 禁止直接對連結記憶體實體開啟交易。請在 Revit 中將該 RVT 檔案單獨開啟為主要專案後，再執行同步。";
                            return;
                        }
                    }

                    if (targetDoc == null && !string.IsNullOrEmpty(targetFilePath) && File.Exists(targetFilePath))
                    {
                        targetDoc = app.Application.OpenDocumentFile(targetFilePath);
                        openedBackgroundDoc = true;
                    }

                    if (targetDoc == null)
                    {
                        StatusMessage = "錯誤：無法取得或開啟目標模型的 Document 實體。";
                        return;
                    }

                    int successCount = 0;
                    int failCount = 0;

                    var copyOptions = new CopyPasteOptions();
                    copyOptions.SetDuplicateTypeNamesHandler(new CustomDuplicateTypeNamesHandler());

                    using (var trans = new Transaction(targetDoc, "Multi-RVT 專案標準同步調和"))
                    {
                        trans.Start();

                        foreach (var item in itemsToSync)
                        {
                            try
                            {
                                bool applied = SyncSingleItem(masterDoc, targetDoc, item, copyOptions);
                                if (applied)
                                {
                                    successCount++;
                                    item.ConfirmationState = "已完成同步 ✓";
                                    item.RowBackground = "#DCFCE7"; // Light green
                                    item.SyncMessage = "成功寫入目標模型";
                                    item.Status = DiffStatus.Equal;
                                }
                                else
                                {
                                    item.ConfirmationState = "略過";
                                    item.SyncMessage = "未執行任何變更 (目標已具備或無可異動屬性)";
                                }
                            }
                            catch (Exception ex)
                            {
                                failCount++;
                                item.ConfirmationState = "同步失敗 ✕";
                                item.RowBackground = "#FEE2E2"; // Light red
                                item.SyncMessage = string.Format("錯誤: {0}", ex.Message);
                            }
                        }

                        trans.Commit();
                    }

                    if (openedBackgroundDoc)
                    {
                        try
                        {
                            targetDoc.Save();
                            targetDoc.Close(false);

                            // Reload links in master document
                            var linkTypes = new FilteredElementCollector(masterDoc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>();
                            foreach (var lt in linkTypes)
                            {
                                try
                                {
                                    if (RevitLinkType.IsLoaded(masterDoc, lt.Id))
                                    {
                                        lt.Reload();
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }

                    _lastDiffResult.RecomputeSummaries();

                    Action updateUiAction = new Action(() =>
                    {
                        CategorySummaries.Clear();
                        foreach (var summary in _lastDiffResult.CategorySummaries.Values)
                        {
                            summary.PropertyChanged += OnCategorySummaryPropertyChanged;
                            CategorySummaries.Add(summary);
                        }

                        StatusMessage = string.Format("同步執行完成！成功同步 {0} 項，失敗 {1} 項至目標模型 [{2}]。",
                            successCount, failCount, selectedTarget.Name);
                        DryRunSummary = string.Format("同步成果：{0} 項已標記為【已完成同步 ✓】。", successCount);
                    });

                    if (System.Windows.Application.Current != null)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(updateUiAction);
                    }
                    else
                    {
                        updateUiAction();
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = string.Format("同步過程發生錯誤：{0}", ex.Message);
                }
            });

            _externalEvent.Raise();
        }

        private static void CopyElementParameters(Element source, Element target)
        {
            if (source == null || target == null) return;
            foreach (Parameter srcParam in source.Parameters)
            {
                if (srcParam == null || srcParam.IsReadOnly || !srcParam.HasValue) continue;
                if (srcParam.Definition == null) continue;

                Parameter tgtParam = target.get_Parameter(srcParam.Definition);
                if (tgtParam == null)
                {
                    tgtParam = target.LookupParameter(srcParam.Definition.Name);
                }

                if (tgtParam != null && !tgtParam.IsReadOnly)
                {
                    try
                    {
                        switch (srcParam.StorageType)
                        {
                            case StorageType.Double:
                                tgtParam.Set(srcParam.AsDouble());
                                break;
                            case StorageType.Integer:
                                tgtParam.Set(srcParam.AsInteger());
                                break;
                            case StorageType.String:
                                tgtParam.Set(srcParam.AsString() ?? string.Empty);
                                break;
                        }
                    }
                    catch { }
                }
            }
        }

        private bool SyncSingleItem(Document masterDoc, Document targetDoc, DiffItem item, CopyPasteOptions options)
        {
            string name = item.MasterIdentity != null ? item.MasterIdentity.DisplayName : "";

            // 1. Workset
            if (item.AdapterKey == "Workset" && targetDoc.IsWorkshared)
            {
                var existing = new FilteredWorksetCollector(targetDoc).OfKind(WorksetKind.UserWorkset).ToWorksets();
                if (!existing.Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    Workset.Create(targetDoc, name);
                    return true;
                }
                return true;
            }

            // 2. Level
            if (item.AdapterKey == "Level")
            {
                var targetLvl = new FilteredElementCollector(targetDoc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(l => l.Name == name);
                var masterLvl = new FilteredElementCollector(masterDoc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(l => l.Name == name);
                if (targetLvl == null && masterLvl != null)
                {
                    var newLvl = Level.Create(targetDoc, masterLvl.Elevation);
                    try { newLvl.Name = masterLvl.Name; } catch { }
                    return true;
                }
                else if (targetLvl != null && masterLvl != null)
                {
                    targetLvl.Elevation = masterLvl.Elevation;
                    return true;
                }
            }

            // 3. Line Pattern
            if (item.AdapterKey == "LinePattern")
            {
                var masterLp = new FilteredElementCollector(masterDoc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
                    .FirstOrDefault(lp => lp.Name == name);
                if (masterLp != null)
                {
                    var targetLp = new FilteredElementCollector(targetDoc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
                        .FirstOrDefault(lp => lp.Name == name);
                    if (targetLp == null)
                    {
                        ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterLp.Id }, targetDoc, Transform.Identity, options);
                        return true;
                    }
                    else
                    {
                        var lpDef = masterLp.GetLinePattern();
                        if (lpDef != null)
                        {
                            targetLp.SetLinePattern(lpDef);
                            return true;
                        }
                    }
                }
            }

            // 4. Line Style (OST_Lines Subcategories)
            if (item.AdapterKey == "LineStyle")
            {
                var masterLinesCat = masterDoc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                var targetLinesCat = targetDoc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (masterLinesCat != null && targetLinesCat != null)
                {
                    Category masterSub = masterLinesCat.SubCategories != null ? masterLinesCat.SubCategories.get_Item(name) : null;
                    if (masterSub != null)
                    {
                        Category targetSub = targetLinesCat.SubCategories != null ? targetLinesCat.SubCategories.get_Item(name) : null;
                        if (targetSub == null)
                        {
                            targetSub = targetDoc.Settings.Categories.NewSubcategory(targetLinesCat, name);
                        }

                        if (targetSub != null)
                        {
                            targetSub.LineColor = masterSub.LineColor;
                            var weight = masterSub.GetLineWeight(GraphicsStyleType.Projection);
                            if (weight.HasValue) targetSub.SetLineWeight(weight.Value, GraphicsStyleType.Projection);

                            var patId = masterSub.GetLinePatternId(GraphicsStyleType.Projection);
                            if (patId != ElementId.InvalidElementId)
                            {
                                var masterPat = masterDoc.GetElement(patId) as LinePatternElement;
                                if (masterPat != null)
                                {
                                    var targetPat = new FilteredElementCollector(targetDoc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
                                        .FirstOrDefault(lp => lp.Name == masterPat.Name);
                                    if (targetPat == null)
                                    {
                                        var copied = ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterPat.Id }, targetDoc, Transform.Identity, options);
                                        if (copied != null && copied.Count > 0) targetPat = targetDoc.GetElement(copied.First()) as LinePatternElement;
                                    }
                                    if (targetPat != null)
                                    {
                                        targetSub.SetLinePatternId(targetPat.Id, GraphicsStyleType.Projection);
                                    }
                                }
                            }
                            return true;
                        }
                    }
                }
            }

            // 5. Fill Pattern
            if (item.AdapterKey == "FillPattern")
            {
                string fpName = name.Contains("::") ? name.Split(new[] { "::" }, StringSplitOptions.None)[1] : name;
                var masterFp = new FilteredElementCollector(masterDoc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.Name == fpName || fp.Name == name);
                if (masterFp != null)
                {
                    var targetFp = new FilteredElementCollector(targetDoc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                        .FirstOrDefault(fp => fp.Name == fpName || fp.Name == name);
                    if (targetFp == null)
                    {
                        ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterFp.Id }, targetDoc, Transform.Identity, options);
                        return true;
                    }
                    return true;
                }
            }

            // 6. Object Style
            if (item.AdapterKey == "ObjectStyle")
            {
                foreach (Category mc in masterDoc.Settings.Categories)
                {
                    if (mc.Name == name)
                    {
                        Category tc = null;
                        try
                        {
                            tc = targetDoc.Settings.Categories.get_Item((BuiltInCategory)mc.Id.IntegerValue);
                        }
                        catch { }
                        if (tc == null) tc = targetDoc.Settings.Categories.get_Item(mc.Name);

                        if (tc != null)
                        {
                            tc.LineColor = mc.LineColor;
                            var projWt = mc.GetLineWeight(GraphicsStyleType.Projection);
                            if (projWt.HasValue) tc.SetLineWeight(projWt.Value, GraphicsStyleType.Projection);
                            var cutWt = mc.GetLineWeight(GraphicsStyleType.Cut);
                            if (cutWt.HasValue) tc.SetLineWeight(cutWt.Value, GraphicsStyleType.Cut);
                            return true;
                        }
                    }
                }
            }

            // 7. View Filter
            if (item.AdapterKey == "ViewFilter")
            {
                var masterF = new FilteredElementCollector(masterDoc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => f.Name == name);
                if (masterF != null)
                {
                    var targetF = new FilteredElementCollector(targetDoc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                        .FirstOrDefault(f => f.Name == name);
                    if (targetF == null)
                    {
                        ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterF.Id }, targetDoc, Transform.Identity, options);
                        return true;
                    }
                    else
                    {
                        var cats = masterF.GetCategories();
                        if (cats != null && cats.Count > 0)
                        {
                            targetF.SetCategories(cats);
                        }
                        var elemFilter = masterF.GetElementFilter();
                        if (elemFilter != null)
                        {
                            targetF.SetElementFilter(elemFilter);
                        }
                        return true;
                    }
                }
            }

            // 8. View Template
            if (item.AdapterKey == "ViewTemplate")
            {
                var masterVt = new FilteredElementCollector(masterDoc).OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && v.Name == name);
                if (masterVt != null)
                {
                    var targetVt = new FilteredElementCollector(targetDoc).OfClass(typeof(View)).Cast<View>()
                        .FirstOrDefault(v => v.IsTemplate && v.Name == name);

                    if (targetVt == null)
                    {
                        var copiedIds = ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterVt.Id }, targetDoc, Transform.Identity, options);
                        if (copiedIds != null && copiedIds.Count > 0)
                        {
                            var copiedView = targetDoc.GetElement(copiedIds.First()) as View;
                            if (copiedView != null && copiedView.Name != masterVt.Name)
                            {
                                try { copiedView.Name = masterVt.Name; } catch { }
                            }
                            return true;
                        }
                    }
                    else
                    {
                        try { targetVt.Scale = masterVt.Scale; } catch { }
                        try { targetVt.Discipline = masterVt.Discipline; } catch { }
                        try { targetVt.DetailLevel = masterVt.DetailLevel; } catch { }
                        CopyElementParameters(masterVt, targetVt);

                        var masterFilters = masterVt.GetFilters();
                        if (masterFilters != null)
                        {
                            foreach (var fId in masterFilters)
                            {
                                var mf = masterDoc.GetElement(fId) as ParameterFilterElement;
                                if (mf != null)
                                {
                                    var tf = new FilteredElementCollector(targetDoc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                                        .FirstOrDefault(f => f.Name == mf.Name);
                                    if (tf == null)
                                    {
                                        var copiedFilterIds = ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { mf.Id }, targetDoc, Transform.Identity, options);
                                        if (copiedFilterIds != null && copiedFilterIds.Count > 0)
                                        {
                                            tf = targetDoc.GetElement(copiedFilterIds.First()) as ParameterFilterElement;
                                        }
                                    }

                                    if (tf != null)
                                    {
                                        if (!targetVt.IsFilterApplied(tf.Id))
                                        {
                                            targetVt.AddFilter(tf.Id);
                                        }
                                        var overrides = masterVt.GetFilterOverrides(fId);
                                        if (overrides != null)
                                        {
                                            targetVt.SetFilterOverrides(tf.Id, overrides);
                                        }
                                        targetVt.SetFilterVisibility(tf.Id, masterVt.GetFilterVisibility(fId));
                                    }
                                }
                            }
                        }
                        return true;
                    }
                }
            }

            // 9. Text Note Type
            if (item.AdapterKey == "TextType")
            {
                var masterTt = new FilteredElementCollector(masterDoc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                    .FirstOrDefault(t => t.Name == name);
                if (masterTt != null)
                {
                    var targetTt = new FilteredElementCollector(targetDoc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                        .FirstOrDefault(t => t.Name == name);
                    if (targetTt == null)
                    {
                        ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterTt.Id }, targetDoc, Transform.Identity, options);
                        return true;
                    }
                    else
                    {
                        CopyElementParameters(masterTt, targetTt);
                        return true;
                    }
                }
            }

            // 10. Dimension Type
            if (item.AdapterKey == "DimensionType")
            {
                var masterDt = new FilteredElementCollector(masterDoc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                    .FirstOrDefault(d => d.Name == name);
                if (masterDt != null)
                {
                    var targetDt = new FilteredElementCollector(targetDoc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                        .FirstOrDefault(d => d.Name == name);
                    if (targetDt == null)
                    {
                        ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterDt.Id }, targetDoc, Transform.Identity, options);
                        return true;
                    }
                    else
                    {
                        CopyElementParameters(masterDt, targetDt);
                        return true;
                    }
                }
            }

            // 11. Schedules
            if (item.AdapterKey == "Schedule")
            {
                var masterSched = new FilteredElementCollector(masterDoc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                    .FirstOrDefault(s => !s.IsTemplate && s.Name == name);
                if (masterSched != null)
                {
                    var targetSched = new FilteredElementCollector(targetDoc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                        .FirstOrDefault(s => !s.IsTemplate && s.Name == name);
                    if (targetSched == null)
                    {
                        ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterSched.Id }, targetDoc, Transform.Identity, options);
                        return true;
                    }
                    else
                    {
                        CopyElementParameters(masterSched, targetSched);
                        return true;
                    }
                }
            }

            // 12. Family Types, Annotation Symbols, and System Family Types
            if (item.AdapterKey.StartsWith("Family"))
            {
                string famName = "";
                string typeName = name;
                int colonIdx = name.IndexOf(" : ");
                if (colonIdx >= 0)
                {
                    famName = name.Substring(0, colonIdx).Trim();
                    typeName = name.Substring(colonIdx + 3).Trim();
                }

                ElementType masterElem = null;
                var allSymbols = new FilteredElementCollector(masterDoc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>();
                masterElem = allSymbols.FirstOrDefault(f => f.Name == typeName && (string.IsNullOrEmpty(famName) || f.FamilyName == famName || f.FamilyName.IndexOf(famName, StringComparison.OrdinalIgnoreCase) >= 0));
                if (masterElem == null && !string.IsNullOrEmpty(famName))
                {
                    masterElem = allSymbols.FirstOrDefault(f => f.Name == typeName);
                }

                if (masterElem == null)
                {
                    var allTypes = new FilteredElementCollector(masterDoc).WhereElementIsElementType().Cast<ElementType>();
                    masterElem = allTypes.FirstOrDefault(e => e.Name == typeName && (string.IsNullOrEmpty(famName) || e.FamilyName == famName || e.FamilyName.IndexOf(famName, StringComparison.OrdinalIgnoreCase) >= 0));
                    if (masterElem == null)
                    {
                        masterElem = allTypes.FirstOrDefault(e => e.Name == typeName || e.Name == name);
                    }
                }

                if (masterElem != null)
                {
                    ElementType targetElem = null;
                    if (masterElem is FamilySymbol)
                    {
                        var targetSymbols = new FilteredElementCollector(targetDoc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>();
                        targetElem = targetSymbols.FirstOrDefault(f => f.Name == typeName && (string.IsNullOrEmpty(famName) || f.FamilyName == famName || f.FamilyName.IndexOf(famName, StringComparison.OrdinalIgnoreCase) >= 0));
                    }
                    else
                    {
                        var targetTypes = new FilteredElementCollector(targetDoc).WhereElementIsElementType().Cast<ElementType>();
                        targetElem = targetTypes.FirstOrDefault(e => e.Name == typeName && (string.IsNullOrEmpty(famName) || e.FamilyName == famName || e.FamilyName.IndexOf(famName, StringComparison.OrdinalIgnoreCase) >= 0));
                        if (targetElem == null)
                        {
                            targetElem = targetTypes.FirstOrDefault(e => e.Name == typeName || e.Name == name);
                        }
                    }

                    if (targetElem == null)
                    {
                        ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterElem.Id }, targetDoc, Transform.Identity, options);
                        return true;
                    }
                    else
                    {
                        CopyElementParameters(masterElem, targetElem);
                        return true;
                    }
                }
            }

            // 13. Project Parameter
            if (item.AdapterKey == "ProjectParameter")
            {
                var iterator = masterDoc.ParameterBindings.ForwardIterator();
                while (iterator.MoveNext())
                {
                    var def = iterator.Key;
                    var binding = iterator.Current as ElementBinding;
                    if (def != null && def.Name == name && binding != null)
                    {
                        CategorySet catSet = targetDoc.Application.Create.NewCategorySet();
                        if (binding.Categories != null)
                        {
                            foreach (Category c in binding.Categories)
                            {
                                try
                                {
                                    var tc = targetDoc.Settings.Categories.get_Item(c.Name);
                                    if (tc != null) catSet.Insert(tc);
                                }
                                catch { }
                            }
                        }

                        ElementBinding newBinding = binding is InstanceBinding
                            ? (ElementBinding)targetDoc.Application.Create.NewInstanceBinding(catSet)
                            : (ElementBinding)targetDoc.Application.Create.NewTypeBinding(catSet);

                        try
                        {
                            targetDoc.ParameterBindings.Insert(def, newBinding, def.GetGroupTypeId());
                            return true;
                        }
                        catch
                        {
                            try
                            {
                                targetDoc.ParameterBindings.ReInsert(def, newBinding, def.GetGroupTypeId());
                                return true;
                            }
                            catch { }
                        }
                    }
                }
            }

            // 14. Legend Views (圖例視圖)
            if (item.AdapterKey == "Legend")
            {
                var masterLegend = new FilteredElementCollector(masterDoc).OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => v.ViewType == ViewType.Legend && !v.IsTemplate && v.Name == name);
                if (masterLegend != null)
                {
                    ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterLegend.Id }, targetDoc, Transform.Identity, options);
                    return true;
                }
            }

            // 15. Sheets & Viewports (圖紙與圖面佈局)
            if (item.AdapterKey == "Sheet")
            {
                string sheetNum = name.Contains(" - ") ? name.Split(new[] { " - " }, StringSplitOptions.None)[0].Trim() : name.Trim();
                var masterSheet = new FilteredElementCollector(masterDoc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .FirstOrDefault(s => s.SheetNumber == sheetNum || string.Format("{0} - {1}", s.SheetNumber, s.Name) == name);

                if (masterSheet != null)
                {
                    var targetSheet = new FilteredElementCollector(targetDoc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                        .FirstOrDefault(s => s.SheetNumber == masterSheet.SheetNumber);

                    if (targetSheet == null)
                    {
                        ElementId titleBlockTypeId = ElementId.InvalidElementId;
                        var tbElem = new FilteredElementCollector(masterDoc, masterSheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).FirstOrDefault();
                        if (tbElem != null)
                        {
                            var masterTbSym = masterDoc.GetElement(tbElem.GetTypeId()) as FamilySymbol;
                            if (masterTbSym != null)
                            {
                                var targetTbSym = new FilteredElementCollector(targetDoc).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                                    .FirstOrDefault(t => t.FamilyName == masterTbSym.FamilyName && t.Name == masterTbSym.Name);
                                if (targetTbSym != null)
                                {
                                    titleBlockTypeId = targetTbSym.Id;
                                }
                                else
                                {
                                    var copied = ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterTbSym.Id }, targetDoc, Transform.Identity, options);
                                    if (copied != null && copied.Count > 0)
                                    {
                                        titleBlockTypeId = copied.First();
                                    }
                                }
                            }
                        }

                        if (titleBlockTypeId != ElementId.InvalidElementId)
                        {
                            targetSheet = ViewSheet.Create(targetDoc, titleBlockTypeId);
                        }
                        else
                        {
                            var anyTb = new FilteredElementCollector(targetDoc).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol)).FirstOrDefault();
                            if (anyTb != null)
                            {
                                targetSheet = ViewSheet.Create(targetDoc, anyTb.Id);
                            }
                        }
                    }

                    if (targetSheet != null)
                    {
                        if (targetSheet.SheetNumber != masterSheet.SheetNumber) targetSheet.SheetNumber = masterSheet.SheetNumber;
                        if (targetSheet.Name != masterSheet.Name) targetSheet.Name = masterSheet.Name;

                        var vpIds = masterSheet.GetAllViewports();
                        if (vpIds != null)
                        {
                            var targetExistingVpViewIds = new HashSet<ElementId>(targetSheet.GetAllViewports().Select(id => ((Viewport)targetDoc.GetElement(id)).ViewId));

                            foreach (var vpId in vpIds)
                            {
                                var mvp = masterDoc.GetElement(vpId) as Viewport;
                                if (mvp != null)
                                {
                                    var mv = masterDoc.GetElement(mvp.ViewId) as View;
                                    if (mv != null)
                                    {
                                        var tv = new FilteredElementCollector(targetDoc).OfClass(typeof(View)).Cast<View>()
                                            .FirstOrDefault(v => !v.IsTemplate && v.Name == mv.Name);

                                        if (tv != null && !targetExistingVpViewIds.Contains(tv.Id))
                                        {
                                            if (Viewport.CanAddViewToSheet(targetDoc, targetSheet.Id, tv.Id))
                                            {
                                                Viewport.Create(targetDoc, targetSheet.Id, tv.Id, mvp.GetBoxCenter());
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        var schedInstances = new FilteredElementCollector(masterDoc, masterSheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>();
                        foreach (var ssi in schedInstances)
                        {
                            var msched = masterDoc.GetElement(ssi.ScheduleId) as ViewSchedule;
                            if (msched != null)
                            {
                                var tsched = new FilteredElementCollector(targetDoc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                                    .FirstOrDefault(s => !s.IsTemplate && s.Name == msched.Name);
                                if (tsched != null)
                                {
                                    bool alreadyPlaced = new FilteredElementCollector(targetDoc, targetSheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                                        .Any(inst => inst.ScheduleId == tsched.Id);
                                    if (!alreadyPlaced)
                                    {
                                        ScheduleSheetInstance.Create(targetDoc, targetSheet.Id, tsched.Id, ssi.Point);
                                    }
                                }
                            }
                        }

                        return true;
                    }
                }
            }

            // 16. Project Browser Organization (專案瀏覽器組織)
            if (item.AdapterKey == "BrowserOrganization")
            {
                string orgName = name.Contains("::") ? name.Split(new[] { "::" }, StringSplitOptions.None)[1].Trim() : name.Trim();
                var masterBo = new FilteredElementCollector(masterDoc).OfClass(typeof(BrowserOrganization)).Cast<BrowserOrganization>()
                    .FirstOrDefault(bo => bo.Name == orgName || string.Format("{0}::{1}", "Views", bo.Name) == name || string.Format("{0}::{1}", "Sheets", bo.Name) == name || string.Format("{0}::{1}", "Schedules", bo.Name) == name);

                if (masterBo != null)
                {
                    var targetBo = new FilteredElementCollector(targetDoc).OfClass(typeof(BrowserOrganization)).Cast<BrowserOrganization>()
                        .FirstOrDefault(bo => bo.Name == masterBo.Name || bo.Name == orgName);

                    if (targetBo == null)
                    {
                        try
                        {
                            var copied = ElementTransformUtils.CopyElements(masterDoc, new List<ElementId> { masterBo.Id }, targetDoc, Transform.Identity, options);
                            if (copied != null && copied.Count > 0)
                            {
                                return true;
                            }
                        }
                        catch { }
                    }
                    return true;
                }
            }

            return false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propName));
        }
    }
}
