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
                new TransferAssetGroup { GroupName = "Sheets" },
                new TransferAssetGroup { GroupName = "Drafting Views" },
                new TransferAssetGroup { GroupName = "Legends" },
                new TransferAssetGroup { GroupName = "Schedules" }
            };

            foreach(var group in AssetGroups)
            {
                group.PropertyChanged += (s, e) => {
                    if (e.PropertyName == nameof(TransferAssetGroup.IsChecked))
                        OnPropertyChanged(nameof(CanTransfer));
                };
            }
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

        public bool CanTransfer => !IsAnalyzing && !IsTransferring && _sourceDoc != null;

        // ── Commands ────────────────────────────────────────────────────────
        public ICommand BrowseCommand { get; }
        public ICommand TransferCommand { get; }

        private async void ExecuteBrowse(object obj)
        {
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

        private async Task AnalyzeSourceProjectAsync(string filePath)
        {
            IsAnalyzing = true;
            StatusMessage = "正在背景開啟來源專案 (這可能需要幾分鐘的時間)...";

            try
            {
                await Task.Run(() =>
                {
                    if (_sourceDoc != null)
                    {
                        try { _sourceDoc.Close(false); } catch { }
                    }

                    ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
                    OpenOptions options = new OpenOptions { DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets };
                    _sourceDoc = _uiapp.Application.OpenDocumentFile(modelPath, options);
                });

                if (_sourceDoc == null)
                {
                    StatusMessage = "無法開啟來源檔案";
                    IsAnalyzing = false;
                    return;
                }

                SourceProjectName = _sourceDoc.ProjectInformation.Name;
                SourceRevitVersion = _sourceDoc.Application.VersionName;

                StatusMessage = "正在掃描可轉移資產...";

                var analyzer = new SheetTransferAnalyzer(_sourceDoc);
                var assets = await Task.Run(() => analyzer.AnalyzeAssets());

                // Update UI
                App.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var group in AssetGroups) group.Assets.Clear();

                    foreach (var asset in assets)
                    {
                        asset.PropertyChanged += (s, e) => {
                            if (e.PropertyName == nameof(TransferAsset.IsSelected))
                            {
                                OnPropertyChanged(nameof(CanTransfer));
                            }
                        };

                        if (asset.Type == AssetType.Sheet) AssetGroups[0].Assets.Add(asset);
                        else if (asset.Type == AssetType.DraftingView) AssetGroups[1].Assets.Add(asset);
                        else if (asset.Type == AssetType.Legend) AssetGroups[2].Assets.Add(asset);
                        else if (asset.Type == AssetType.Schedule) AssetGroups[3].Assets.Add(asset);
                    }
                });

                StatusMessage = "掃描完成。請選擇要轉移的項目。";
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

        private bool CanExecuteTransfer(object obj)
        {
            return CanTransfer;
        }

        private async void ExecuteTransfer(object obj)
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
                    _sourceDoc.Close(false);
                    _sourceDoc = null;
                }
                catch { }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
