using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.UI;

namespace DevelopmentTools
{
    public class App : IExternalApplication
    {
        private TileUpdater _updater;

        // 全域持有，供 ShowControlPanelCommand 和 MainWindow 使用
        public static TileSyncEventHandler EventHandler { get; private set; }
        public static ExternalEvent SyncEvent { get; private set; }

        // 持有視窗單例，避免重複開啟
        private static MainWindow _window;
        private static MainViewModel _viewModel;

        private static readonly System.Collections.Generic.HashSet<string> _activeDocs = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Log(string msg)
        {
            try
            {
                string logPath = Path.Combine(Path.GetTempPath(), "RevitAddinLog.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}");
            }
            catch { }
        }

        public static bool IsSyncingOrLoading(Document doc)
        {
            if (doc == null) return false;
            string path = GetDocPathSafe(doc);
            if (string.IsNullOrEmpty(path)) return false;
            lock (_activeDocs)
            {
                bool active = _activeDocs.Contains(path);
                Log($"IsSyncingOrLoading check for '{path}': {active} (Active count: {_activeDocs.Count})");
                return active;
            }
        }

        private static string GetDocPathSafe(Document doc)
        {
            if (doc == null) return null;
            try
            {
                string path = doc.PathName;
                if (string.IsNullOrEmpty(path))
                {
                    path = doc.Title;
                }
                return path;
            }
            catch
            {
                return doc.GetHashCode().ToString();
            }
        }

        private static void OnDocOpening(object sender, Autodesk.Revit.DB.Events.DocumentOpeningEventArgs e)
        {
            try
            {
                string path = e.PathName;
                Log($"OnDocOpening: '{path}'");
                if (!string.IsNullOrEmpty(path))
                {
                    lock (_activeDocs) { _activeDocs.Add(path); }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in OnDocOpening: {ex.Message}");
            }
        }

        private static void OnDocOpened(object sender, Autodesk.Revit.DB.Events.DocumentOpenedEventArgs e)
        {
            try
            {
                string path = e.Document != null ? GetDocPathSafe(e.Document) : null;
                Log($"OnDocOpened: '{path}', Status: {e.Status}");
                if (!string.IsNullOrEmpty(path))
                {
                    lock (_activeDocs) { _activeDocs.Remove(path); }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in OnDocOpened: {ex.Message}");
            }
        }

        private static void OnDocSyncing(object sender, Autodesk.Revit.DB.Events.DocumentSynchronizingWithCentralEventArgs e)
        {
            try
            {
                string path = e.Document != null ? GetDocPathSafe(e.Document) : null;
                Log($"OnDocSyncing: '{path}'");
                if (!string.IsNullOrEmpty(path))
                {
                    lock (_activeDocs) { _activeDocs.Add(path); }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in OnDocSyncing: {ex.Message}");
            }
        }

        private static void OnDocSynced(object sender, Autodesk.Revit.DB.Events.DocumentSynchronizedWithCentralEventArgs e)
        {
            try
            {
                string path = e.Document != null ? GetDocPathSafe(e.Document) : null;
                Log($"OnDocSynced: '{path}', Status: {e.Status}");
                if (!string.IsNullOrEmpty(path))
                {
                    lock (_activeDocs) { _activeDocs.Remove(path); }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in OnDocSynced: {ex.Message}");
            }
        }

        private static void OnDocReloading(object sender, Autodesk.Revit.DB.Events.DocumentReloadingLatestEventArgs e)
        {
            try
            {
                string path = e.Document != null ? GetDocPathSafe(e.Document) : null;
                Log($"OnDocReloading: '{path}'");
                if (!string.IsNullOrEmpty(path))
                {
                    lock (_activeDocs) { _activeDocs.Add(path); }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in OnDocReloading: {ex.Message}");
            }
        }

        private static void OnDocReloaded(object sender, Autodesk.Revit.DB.Events.DocumentReloadedLatestEventArgs e)
        {
            try
            {
                string path = e.Document != null ? GetDocPathSafe(e.Document) : null;
                Log($"OnDocReloaded: '{path}', Status: {e.Status}");
                if (!string.IsNullOrEmpty(path))
                {
                    lock (_activeDocs) { _activeDocs.Remove(path); }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in OnDocReloaded: {ex.Message}");
            }
        }

        private static bool _hasCheckedUpdateThisSession = false;

        private static void TriggerSilentUpdateCheck()
        {
            if (_hasCheckedUpdateThisSession) return;
            _hasCheckedUpdateThisSession = true;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000);
                    var result = await UpdateManager.CheckForUpdatesAsync();
                    if (result.HasUpdate)
                    {
                        TaskDialog td = new TaskDialog("遠端更新提示");
                        td.MainInstruction = $"發現新版本 v{result.LatestVersion}！";
                        td.MainContent = $"目前本機版本: v{result.CurrentVersion}\n\n更新內容:\n{result.ReleaseNotes}\n\n是否下載新版並準備安裝？";
                        td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                        td.DefaultButton = TaskDialogResult.Yes;

                        if (td.Show() == TaskDialogResult.Yes)
                        {
                            await UpdateManager.StartUpdateProcessAsync(result.Manifest);
                        }
                    }
                }
                catch (Exception ex)
                {
                    UpdateLogger.Log("背景自動檢查更新失敗", ex);
                }
            });
        }

        public static void ShowOrActivateWindow(ExternalCommandData commandData)
        {
            TriggerSilentUpdateCheck();

            if (_window != null && _window.IsLoaded)
            {
                _window.Activate();
                _window.Focus();
                return;
            }

            var uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            // 檢查是否啟用 Google 授權驗證
            if (GoogleAuthManager.IsAuthEnabled())
            {
                // 啟動非同步驗證，防止卡死 Revit 主執行緒
                Task.Run(async () =>
                {
                    try
                    {
                        bool isAuthorized = await GoogleAuthManager.VerifyAccessAsync("Tiling", "磁磚鋪設系統");
                        if (!isAuthorized) return;

                        uiDispatcher.Invoke(() =>
                        {
                            DoShowWindow(commandData);
                        });
                    }
                    catch (Exception ex)
                    {
                        uiDispatcher.Invoke(() =>
                        {
                            TaskDialog.Show("驗證錯誤", $"驗證過程發生異常：{ex.Message}");
                        });
                    }
                });
            }
            else
            {
                DoShowWindow(commandData);
            }
        }

        private static void DoShowWindow(ExternalCommandData commandData)
        {
            if (_window == null || !_window.IsLoaded)
            {
                _viewModel = new MainViewModel(commandData);
                _viewModel.SyncExternalEvent = SyncEvent;
                _viewModel.SyncHandler = EventHandler;

                _window = new MainWindow(_viewModel);

                // 設定 Revit 主視窗為 Owner，讓視窗在 Revit 上方顯示
                var helper = new System.Windows.Interop.WindowInteropHelper(_window);
                helper.Owner = commandData.Application.MainWindowHandle;

                _window.Closed += (s, e) => { _window = null; _viewModel = null; };
                _window.Show(); // Modeless，不阻塞 Revit
            }
            else
            {
                _window.Activate();
                _window.Focus();
            }
        }

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 1. 建立 ExternalEvent（在 OnStartup 建立，才能在整個 session 存活）
                EventHandler = new TileSyncEventHandler();
                SyncEvent = ExternalEvent.Create(EventHandler);

                // 2. 建立 Ribbon 頁籤
                string tabName = "Development tools";
                application.CreateRibbonTab(tabName);
                
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                // Panel 1: 系統管理
                RibbonPanel adminPanel = application.CreateRibbonPanel(tabName, "系統管理");
                
                PushButtonData loginBtn = new PushButtonData(
                    "GoogleLogin", "Google 登入",
                    assemblyPath, "DevelopmentTools.Commands.LoginCommand");
                loginBtn.ToolTip = "【Google 登入 - 快速指南】\n\n" +
                                   "1. 點擊按鈕，會自動開啟網頁瀏覽器。\n" +
                                   "2. 請在網頁中登入您的 Google 帳號進行權限認證。\n" +
                                   "3. 顯示成功後回到 Revit，外掛各功能將自動解鎖開通。";
                loginBtn.LongDescription = "【Google 登入 - 安全與權限詳細說明】\n\n" +
                                           "■ 帳號白名單：登入後，系統會至後台雲端比對管理員設定的允許名單。\n" +
                                           "■ 功能細粒度控制：不同的 Google 帳號可分別授權開通不同模組（如磁磚、粉刷、樓板等）。若有功能點擊後顯示「未授權」，請向系統管理員提出開通申請。\n" +
                                           "■ 安全保障：外掛採用 Google 官方 OAuth 2.0 驗證協議，絕不會收集或儲存您的密碼，僅確認您的使用授權狀態。";
                
                PushButtonData feedbackBtn = new PushButtonData(
                    "SystemFeedback", "問題與反饋",
                    assemblyPath, "DevelopmentTools.Commands.FeedbackCommand");
                feedbackBtn.ToolTip = "【問題與反饋 - 快速指南】\n\n" +
                                      "1. 點擊打開問題回報與優化反饋視窗。\n" +
                                      "2. 輸入您遇到的錯誤描述、截圖網址或功能優化建議。\n" +
                                      "3. 點擊「提交」即可傳送給作者。";
                feedbackBtn.LongDescription = "【問題與反饋 - 常見反饋引導】\n\n" +
                                              "■ 錯誤回報提示：若外掛產生錯誤或閃退，建議提供完整的錯誤說明。您也可以點擊查看日誌，將 RevitAddinLog.txt 提供給開發小組排查。\n" +
                                              "■ 功能提案：若有任何希望新增的指令、優化操作流程的想法，都歡迎直接寫在反饋中，我們將於後續版本評估加入。";

                PushButtonData updateBtn = new PushButtonData(
                    "CheckForUpdates", "檢查更新",
                    assemblyPath, "DevelopmentTools.Commands.CheckUpdateCommand");
                updateBtn.ToolTip = "【檢查更新 - 快速指南】\n\n" +
                                    "1. 點擊按鈕，系統會線上檢查是否有發佈最新版本。\n" +
                                    "2. 若有新版，會彈出視窗顯示更新明細內容。\n" +
                                    "3. 點選「確認安裝」後會自動下載並在重啟 Revit 時完成升級。";
                updateBtn.LongDescription = "【檢查更新 - 升級與防毒說明】\n\n" +
                                            "■ 自動比對：程式會檢查本機版號與雲端最新配置，確保您隨時可以使用最穩定、修正了 Bug 的新外掛。\n" +
                                            "■ 靜默檢測：每次點開排版主視窗時，後台執行緒都會自動做一次無感的更新檢測。\n" +
                                            "■ 防毒信任：更新時需要透過獨立的 Updater 覆蓋安裝，若被防毒軟體攔截，請選擇「信任並允許執行」。";

                adminPanel.AddItem(loginBtn);
                adminPanel.AddItem(feedbackBtn);
                adminPanel.AddItem(updateBtn);

                // Panel 2: 空間排版
                RibbonPanel layoutPanel = application.CreateRibbonPanel(tabName, "空間排版");
                
                PushButtonData mainBtn = new PushButtonData(
                    "ShowControlPanel", "磁磚鋪設系統",
                    assemblyPath, "DevelopmentTools.Commands.ShowControlPanelCommand");
                mainBtn.ToolTip = "【磁磚鋪設系統 - 快速指南】\n\n" +
                                  "1. 點擊按鈕開啟磁磚設計浮動主面板。\n" +
                                  "2. 在專案中點選您的目標房間 (Room)。\n" +
                                  "3. 在面板中設定磁磚規格 (如 60x60、30x60) 與起磚對齊起點。\n" +
                                  "4. 系統會自動在房間地板與牆面繪製磁磚線。";
                mainBtn.LongDescription = "【磁磚鋪設系統 - 進階使用提示】\n\n" +
                                          "■ 排磚原理：程式會取得 Room 牆體的最外側裝修面，並直接在表面 Paint .pat 填充線，不需手動繪製。\n" +
                                          "■ 設計微調：可在面板中微調起磚原點偏移量 X/Y，或調整整體排磚旋轉角度，以避開碎磚或對齊特定縫隙。\n" +
                                          "■ 動態關聯：外掛在專案中註冊了 DMU (Dynamic Model Update)，當房間的牆壁被手動拖動時，面域內的磁磚會自動重新計算，無須人工手動重做。";
                layoutPanel.AddItem(mainBtn);

                PushButtonData elevationBtn = new PushButtonData(
                    "TileElevationGenerator", "展開圖生成",
                    assemblyPath, "DevelopmentTools.Commands.DT_TileElevationGeneratorCommand");
                elevationBtn.ToolTip = "【展開圖生成 - 快速指南】\n\n" +
                                       "1. 點開按鈕，選取地板 (Floor) 或多面牆體。\n" +
                                       "2. 點擊面板中的「1. 分析幾何」取得牆體資訊。\n" +
                                       "3. 依序執行建立視圖、套用樣板與命名（視角會自動朝著牆面外看）。\n" +
                                       "4. 點選「5. 建立圖紙」，各個立面圖會在圖紙上自動以「等高、左右無縫拼接」方式對齊排好！";
                elevationBtn.LongDescription = "【展開圖生成 - 拼接與對齊細節】\n\n" +
                                               "■ 無縫對接技術：排版至圖紙時，系統會自動在背景隱藏 Section 視圖中的標高、軸線、剖面線等干擾符號，以便抓取純淨的 CropBox 邊界尺寸進行完美貼合排版。\n" +
                                               "■ 高低差自動補償：讀取各牆面底部的 Level 標高高程，在圖紙 Y 軸對齊時自動進行偏移量扣除，確保所有立面圖的樓面線 (Floor Line) 處於同一條水平線上。\n" +
                                               "■ 剖面方向：生成剖面線視角預設是從樓板中心點朝牆體表面向外看。";
                layoutPanel.AddItem(elevationBtn);

                // Panel 3: 粉刷裝修
                RibbonPanel finishPanel = application.CreateRibbonPanel(tabName, "粉刷裝修");

                PushButtonData wallFinishBtn = new PushButtonData(
                    "WallFinishTool", "自動粉刷",
                    assemblyPath, "DevelopmentTools.Commands.WallFinishCommand");
                wallFinishBtn.ToolTip = "【自動粉刷 - 快速指南】\n\n" +
                                        "1. 點擊此按鈕進入選取模式。\n" +
                                        "2. 在 Revit 視圖中點選一個或多個目標房間 (Room)。\n" +
                                        "3. 程式會分析房間牆面範圍，自動在表面長出實體厚度與材質的粉刷層牆體。";
                wallFinishBtn.LongDescription = "【自動粉刷 - 進階生成原理說明】\n\n" +
                                                "■ 牆體生成：分析房間的 Finish 邊界，在牆面外側快速繪製並長出具有厚度的薄牆。您可以自訂粉刷厚度（如 15mm）與對應的 Revit 材料（如水泥砂漿）。\n" +
                                                "■ 門窗精準洞口扣減：外掛會自動偵測牆上的 Window 窗與 Door 門，分析其原有的洞口形狀，在生成的粉刷層實體中自動扣除重疊的洞口，確保明細表工程量計算精準無誤。";
                finishPanel.AddItem(wallFinishBtn);

                // Panel 4: DT｜樓板工具
                RibbonPanel floorPanel = application.CreateRibbonPanel(tabName, "DT｜樓板工具");

                PushButtonData floorSnapBtn = new PushButtonData(
                    "FloorSnapToRoom", "樓板貼房間",
                    assemblyPath, "DevelopmentTools.Commands.Cmd_FloorSnapToRoom");
                floorSnapBtn.ToolTip = "【樓板貼房間 - 快速指南】\n\n" +
                                       "1. 先預選樓板，或者點按鈕後依 Revit 提示手動選取樓板（選完必須點左上角綠色欄的「完成 (Finish)」）。\n" +
                                       "2. 程式會自動搜尋同樓層對應房間，取得房間完成面邊界。\n" +
                                       "3. 樓板草圖直線邊界會像磁鐵般自動平移、貼齊牆面完成面，並重新計算拐角交點以保持草圖閉合不報錯。";
                floorSnapBtn.LongDescription = "【樓板貼房間 - 進階吸附與幾何控制】\n\n" +
                                               "■ 幾何對齊原理：分析樓板所在的 Room 房間完成面（Finish Boundary）。針對草圖中與牆面平行、距離在 MaxSnapDistance (預設 300 mm) 內且有一定重疊長度的直線段進行平移對齊。\n" +
                                               "■ 端點重建：移動後，程式會針對所有相鄰的直線在 2D XY 平面上重新求無限延伸線的交點，重設其端點。對於圓弧 (Arc)，則會保持其曲率半徑，僅微調其相鄰端點，確保草圖 100% 閉合。\n" +
                                               "■ 獨立防崩潰控制：框選多個樓板時，每塊樓板都會使用獨立的事務群組 (TransactionGroup) 處理。若有部分樓板因為草圖本身太亂或自交而對齊失敗，程式會單獨 Rollback 該樓板並提供報告，其餘成功的樓板依然會照常套用。";
                floorPanel.AddItem(floorSnapBtn);

                // 3. 註冊 DMU Updater
                _updater = new TileUpdater(application.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_updater);

                ElementClassFilter classFilter = new ElementClassFilter(typeof(FamilyInstance));
                ElementCategoryFilter categoryFilter = new ElementCategoryFilter(BuiltInCategory.OST_GenericModel);
                LogicalAndFilter gmFilter = new LogicalAndFilter(classFilter, categoryFilter);

                UpdaterRegistry.AddTrigger(_updater.GetUpdaterId(), gmFilter, Element.GetChangeTypeAny());

                // 訂閱開檔/同步中央/讀取最新變更事件，以控制 Updater 避開這些高載入狀態
                application.ControlledApplication.DocumentOpening += OnDocOpening;
                application.ControlledApplication.DocumentOpened += OnDocOpened;
                application.ControlledApplication.DocumentSynchronizingWithCentral += OnDocSyncing;
                application.ControlledApplication.DocumentSynchronizedWithCentral += OnDocSynced;
                application.ControlledApplication.DocumentReloadingLatest += OnDocReloading;
                application.ControlledApplication.DocumentReloadedLatest += OnDocReloaded;

                return Result.Succeeded;
            }
            catch (Exception)
            {
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                SyncEvent?.Dispose();
                if (_updater != null)
                    UpdaterRegistry.UnregisterUpdater(_updater.GetUpdaterId());

                // 取消訂閱事件
                application.ControlledApplication.DocumentOpening -= OnDocOpening;
                application.ControlledApplication.DocumentOpened -= OnDocOpened;
                application.ControlledApplication.DocumentSynchronizingWithCentral -= OnDocSyncing;
                application.ControlledApplication.DocumentSynchronizedWithCentral -= OnDocSynced;
                application.ControlledApplication.DocumentReloadingLatest -= OnDocReloading;
                application.ControlledApplication.DocumentReloadedLatest -= OnDocReloaded;
            }
            catch { }
            return Result.Succeeded;
        }
    }
}
