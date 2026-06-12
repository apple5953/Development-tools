using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Autodesk.Revit.UI;

namespace DevelopmentTools.UI
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _statusText = "就緒。請選擇操作：";

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private double _tileWidth = 300;
        public double TileWidth
        {
            get => _tileWidth;
            set { _tileWidth = value; OnPropertyChanged(); }
        }

        private double _tileHeight = 600;
        public double TileHeight
        {
            get => _tileHeight;
            set { _tileHeight = value; OnPropertyChanged(); }
        }

        private double _tileThickness = 10;
        public double TileThickness
        {
            get => _tileThickness;
            set { _tileThickness = value; OnPropertyChanged(); }
        }

        private double _jointWidth = 3;
        public double JointWidth
        {
            get => _jointWidth;
            set { _jointWidth = value; OnPropertyChanged(); }
        }

        // 由 App 注入
        public ExternalEvent SyncExternalEvent { get; set; }
        public TileSyncEventHandler SyncHandler { get; set; }

        public ICommand PickFaceCommand               { get; }
        public ICommand ConfirmMaterialPatCommand     { get; }
        public ICommand Generate3DTilesCommand        { get; }
        public ICommand Generate3DFloorTilesCommand   { get; }
        public ICommand ChangeLocalTileMaterialCommand { get; }
        public ICommand ConvertToEditableCommand      { get; }
        public ICommand CalculatePlaneQuantityCommand { get; }
        public ICommand Calculate3DQuantityCommand    { get; }
        public ICommand AddJointWallsCommand          { get; }
        public ICommand AddJointFloorsCommand         { get; }
        public ICommand DeleteTilesCommand            { get; }
        public ICommand CreateScheduleCommand         { get; }
        public ICommand ExportExcelCommand            { get; }
        public ICommand OpenHelpCommand               { get; }
 
        public MainViewModel(ExternalCommandData commandData)
        {
            PickFaceCommand               = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.PickFaceAndPat,         "等待點選面..."));
            ConfirmMaterialPatCommand     = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.ConfirmMaterialPat,    "等待點選面以確認材質..."));
            Generate3DTilesCommand        = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.Generate3DTiles,       "等待點選已確認牆面以生成 3D 牆磚..."));
            Generate3DFloorTilesCommand   = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.Generate3DFloorTiles,  "等待點選已確認地坪以生成 3D 地磚..."));
            ChangeLocalTileMaterialCommand = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.ChangeLocalTileMaterial, "等待選取要更換材質 of the 3D 磁磚..."));
            ConvertToEditableCommand      = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.ConvertToEditable,      "等待選擇磁磚進行可編輯轉換..."));
            CalculatePlaneQuantityCommand = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.CalculatePlaneQuantity, "等待多選已確認面計算平面數量..."));
            Calculate3DQuantityCommand    = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.Calculate3DQuantity,    "等待多選房間元件以進行3D實體統計..."));
            AddJointWallsCommand          = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.AddJointParamToWalls,    "正在加入裝修牆縫隙參數..."));
            AddJointFloorsCommand         = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.AddJointParamToFloors,   "正在加入裝修地板縫隙參數..."));
            DeleteTilesCommand            = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.DeleteTiles,             "清除中..."));
            CreateScheduleCommand         = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.CreateSchedule,          "正在建立 Revit 明細表..."));
            ExportExcelCommand            = new RelayCommand(() => Raise(TileSyncEventHandler.Operation.ExportExcel,             "正在準備匯出統計數據..."));
            OpenHelpCommand               = new RelayCommand(OpenHelp);
        }

        private void OpenHelp()
        {
            try
            {
                // 嘗試尋找專案根目錄的 README.md
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string currentDir = System.IO.Path.GetDirectoryName(assemblyPath);
                
                // 向上搜尋 README.md，最深搜尋 4 層
                string readmePath = null;
                string tempDir = currentDir;
                for (int i = 0; i < 4; i++)
                {
                    if (string.IsNullOrEmpty(tempDir)) break;
                    string checkPath = System.IO.Path.Combine(tempDir, "README.md");
                    if (System.IO.File.Exists(checkPath))
                    {
                        readmePath = checkPath;
                        break;
                    }
                    tempDir = System.IO.Path.GetDirectoryName(tempDir);
                }

                if (readmePath != null && System.IO.File.Exists(readmePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = readmePath,
                        UseShellExecute = true
                    });
                    StatusText = "✓ 已成功開啟說明文件 README.md。";
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"開啟 README.md 失敗: {ex.Message}");
            }

            // 如果找不到 README.md，彈出 TaskDialog 顯示新手快速指南
            ShowHelpTaskDialog();
        }

        private void ShowHelpTaskDialog()
        {
            string title = DevelopmentTools.Core.LanguageManager.Instance["Tut_TileSys_Title"];
            string content = DevelopmentTools.Core.LanguageManager.Instance["Tut_TileSys_Content"];
            TaskDialog td = new TaskDialog(title)
            {
                TitleAutoPrefix = false,
                MainInstruction = title,
                MainContent = content,
                CommonButtons = TaskDialogCommonButtons.Close
            };
            td.Show();
        }

        private void Raise(TileSyncEventHandler.Operation op, string waitMsg)
        {
            if (SyncExternalEvent == null || SyncHandler == null)
            {
                StatusText = "錯誤：ExternalEvent 未初始化，請重新開啟外掛。";
                return;
            }
            SyncHandler.ViewModel = this;
            SyncHandler.CurrentOperation = op;
            SyncExternalEvent.Raise();
            StatusText = waitMsg;
        }

        // Dispatcher-safe 回寫（從 Handler 的執行緒呼叫）
        public void SetStatus(string text)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => StatusText = text);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

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
            add    => System.Windows.Input.CommandManager.RequerySuggested += value;
            remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
        }
    }
}
