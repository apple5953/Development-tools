using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.SheetTransfer.Models;
using DevelopmentTools.Modules.SheetTransfer.Services;
using DevelopmentTools.UI;

namespace DevelopmentTools.Modules.SheetTransfer.ViewModels
{
    public class SheetTransferViewModel : INotifyPropertyChanged
    {
        private UIApplication _uiapp;
        private Document _sourceDoc;
        private Document _targetDoc;

        public SheetTransferViewModel(UIApplication uiapp)
        {
            _uiapp = uiapp;
            _targetDoc = uiapp.ActiveUIDocument.Document;

            BrowseCommand = new RelayCommand(ExecuteBrowse);
            TransferCommand = new RelayCommand(ExecuteTransfer, CanExecuteTransfer);

            AssetGroups = new ObservableCollection<TransferAssetGroup>
            {
                new TransferAssetGroup { GroupName = "Project Parameters" },
                new TransferAssetGroup { GroupName = "View Templates" },
                new TransferAssetGroup { GroupName = "Project Info & Symbols" },
                new TransferAssetGroup { GroupName = "Sheets" },
                new TransferAssetGroup { GroupName = "Drafting Views" },
                new TransferAssetGroup { GroupName = "Legends" },
                new TransferAssetGroup { GroupName = "Schedules" }
            };

            foreach (var group in AssetGroups)
            {
                group.PropertyChanged += (s, e) => {
                    if (e.PropertyName == nameof(TransferAssetGroup.IsChecked))
                        OnPropertyChanged(nameof(CanTransfer));
                };
            }

            LoadOpenProjects();
        }

        // ── Properties ──────────────────────────────────────────────────────
        private string _sourceFilePath;
        public string SourceFilePath
        {
            get => _sourceFilePath;
            set { _sourceFilePath = value; OnPropertyChanged(); }
        }

        private string _sourceProjectName;
        public string SourceProjectName
        {
            get => _sourceProjectName;
            set { _sourceProjectName = value; OnPropertyChanged(); }
        }

        private string _sourceRevitVersion;
        public string SourceRevitVersion
        {
            get => _sourceRevitVersion;
            set { _sourceRevitVersion = value; OnPropertyChanged(); }
        }

        private bool _isAnalyzing;
        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set { _isAnalyzing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanTransfer)); }
        }

        private bool _isTransferring;
        public bool IsTransferring
        {
            get => _isTransferring;
            set { _isTransferring = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanTransfer)); }
        }

        private string _statusMessage = "準備就緒";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private string _reportText = "";
        public string ReportText
        {
            get => _reportText;
            set { _reportText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TransferAssetGroup> AssetGroups { get; }

        private ObservableCollection<string> _openProjects = new ObservableCollection<string>();
        public ObservableCollection<string> OpenProjects => _openProjects;

        private string _selectedOpenProject;
        public string SelectedOpenProject
        {
            get => _selectedOpenProject;
            set
            {
                if (_selectedOpenProject != value)
                {
                    _selectedOpenProject = value;
                    OnPropertyChanged();
                    if (!string.IsNullOrEmpty(_selectedOpenProject))
                    {
                        OnOpenProjectSelected(_selectedOpenProject);
                    }
                }
            }
        }

        public bool IsSourceFromOpenedDoc { get; private set; } = false;

        private TransferAsset _selectedAsset;
        public TransferAsset SelectedAsset
        {
            get => _selectedAsset;
            set
            {
                _selectedAsset = value;
                OnPropertyChanged();
                UpdateActiveDiffDetails();
            }
        }

        private ObservableCollection<ParameterDiffItem> _activeDiffDetails = new ObservableCollection<ParameterDiffItem>();
        public ObservableCollection<ParameterDiffItem> ActiveDiffDetails => _activeDiffDetails;

        public bool CanTransfer => !IsAnalyzing && !IsTransferring && _sourceDoc != null;

        // ── Commands ────────────────────────────────────────────────────────
        public ICommand BrowseCommand { get; }
        public ICommand TransferCommand { get; }

        private void LoadOpenProjects()
        {
            _openProjects.Clear();
            foreach (Document doc in _uiapp.Application.Documents)
            {
                if (doc.IsFamilyDocument) continue;
                if (doc.PathName == _targetDoc.PathName) continue;

                string name = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
                _openProjects.Add(name);
            }
        }

        private async void OnOpenProjectSelected(string projectNameOrPath)
        {
            Document foundDoc = null;
            foreach (Document doc in _uiapp.Application.Documents)
            {
                string name = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
                if (name == projectNameOrPath)
                {
                    foundDoc = doc;
                    break;
                }
            }

            if (foundDoc != null)
            {
                IsSourceFromOpenedDoc = true;
                SourceFilePath = string.IsNullOrEmpty(foundDoc.PathName) ? foundDoc.Title : foundDoc.PathName;
                await AnalyzeSourceProjectAsync(foundDoc);
            }
        }

        private async void ExecuteBrowse()
        {
            SelectedOpenProject = null;
            IsSourceFromOpenedDoc = false;

            System.Windows.Forms.OpenFileDialog ofd = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "Revit Project Files (*.rvt)|*.rvt",
                Title = "選擇來源專案檔案"
            };

            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SourceFilePath = ofd.FileName;
                await AnalyzeSourceProjectAsync(SourceFilePath);
            }
        }

        private async Task AnalyzeSourceProjectAsync(Document sourceDoc)
        {
            IsAnalyzing = true;
            StatusMessage = "正在讀取專案資料與進行差異比對...";

            try
            {
                _sourceDoc = sourceDoc;
                SourceProjectName = _sourceDoc.ProjectInformation.Name;
                SourceRevitVersion = _sourceDoc.Application.VersionName;

                StatusMessage = "正在比對分析兩邊專案資產差異...";

                var analyzer = new SheetTransferAnalyzer(_sourceDoc, _targetDoc);
                var assets = await Task.Run(() => analyzer.AnalyzeAssets());

                // Update UI
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var group in AssetGroups) group.Assets.Clear();

                    foreach (var asset in assets)
                    {
                        // 差異與新項目預設勾選，完全一致的項目預設不勾選
                        asset.IsSelected = (asset.Comparison != AssetComparison.Identical);

                        asset.PropertyChanged += (s, e) => {
                            if (e.PropertyName == nameof(TransferAsset.IsSelected))
                            {
                                OnPropertyChanged(nameof(CanTransfer));
                            }
                        };

                        if (asset.Type == AssetType.ProjectParameter) AssetGroups[0].Assets.Add(asset);
                        else if (asset.Type == AssetType.ViewTemplate) AssetGroups[1].Assets.Add(asset);
                        else if (asset.Type == AssetType.ProjectInfoAndSymbol) AssetGroups[2].Assets.Add(asset);
                        else if (asset.Type == AssetType.Sheet) AssetGroups[3].Assets.Add(asset);
                        else if (asset.Type == AssetType.DraftingView) AssetGroups[4].Assets.Add(asset);
                        else if (asset.Type == AssetType.Legend) AssetGroups[5].Assets.Add(asset);
                        else if (asset.Type == AssetType.Schedule) AssetGroups[6].Assets.Add(asset);
                    }
                });

                StatusMessage = "分析比對完成。有差異與缺失項目已預設勾選。";
            }
            catch (Exception ex)
            {
                StatusMessage = $"分析失敗: {ex.Message}";
            }
            finally
            {
                IsAnalyzing = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task AnalyzeSourceProjectAsync(string filePath)
        {
            IsAnalyzing = true;
            StatusMessage = "正在背景開啟來源專案 (這可能需要幾分鐘的時間)...";
            IsSourceFromOpenedDoc = false;

            try
            {
                Document openedDoc = null;
                await Task.Run(() =>
                {
                    if (_sourceDoc != null && !IsSourceFromOpenedDoc)
                    {
                        try { _sourceDoc.Close(false); } catch { }
                    }

                    ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
                    OpenOptions options = new OpenOptions { DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets };
                    openedDoc = _uiapp.Application.OpenDocumentFile(modelPath, options);
                });

                if (openedDoc == null)
                {
                    StatusMessage = "無法開啟來源檔案";
                    IsAnalyzing = false;
                    return;
                }

                await AnalyzeSourceProjectAsync(openedDoc);
            }
            catch (Exception ex)
            {
                StatusMessage = $"開啟分析失敗: {ex.Message}";
                IsAnalyzing = false;
            }
        }

        private bool CanExecuteTransfer()
        {
            return CanTransfer;
        }

        private async void ExecuteTransfer()
        {
            IsTransferring = true;
            StatusMessage = "正在執行轉移...";
            ReportText = "";

            var selectedAssets = new System.Collections.Generic.List<TransferAsset>();
            foreach (var group in AssetGroups)
            {
                foreach (var asset in group.Assets)
                {
                    if (asset.IsSelected) selectedAssets.Add(asset);
                }
            }

            if (selectedAssets.Count == 0)
            {
                StatusMessage = "請先選擇至少一個要轉移的項目";
                IsTransferring = false;
                return;
            }

            try
            {
                var service = new SheetTransferService(_sourceDoc, _targetDoc);
                var report = await Task.Run(() => service.TransferAssets(selectedAssets));
                ReportText = report;
                StatusMessage = "轉移完成";

                // 轉移完成後，自動重新比對以刷新介面狀態
                if (_sourceDoc != null)
                {
                    await AnalyzeSourceProjectAsync(_sourceDoc);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"轉移發生嚴重錯誤: {ex.Message}";
            }
            finally
            {
                IsTransferring = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public void CloseSourceDocument()
        {
            if (_sourceDoc != null)
            {
                try
                {
                    if (!IsSourceFromOpenedDoc)
                    {
                        _sourceDoc.Close(false);
                    }
                    _sourceDoc = null;
                }
                catch { }
            }
        }

        private void UpdateActiveDiffDetails()
        {
            _activeDiffDetails.Clear();
            if (_selectedAsset != null && _selectedAsset.DiffDetails != null)
            {
                foreach (var diff in _selectedAsset.DiffDetails)
                {
                    _activeDiffDetails.Add(diff);
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
