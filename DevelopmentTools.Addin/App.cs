using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.AIAssistant;
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

        // AI 助手
        private static AIAssistantWindow _aiWindow;
        private static AISelectionHandler _aiSelectionHandler;
        private static ExternalEvent _aiSelectionEvent;

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

        public static void ShowOrActivateAIAssistant(ExternalCommandData commandData)
        {
            if (_aiWindow != null && _aiWindow.IsLoaded)
            {
                _aiWindow.Activate();
                _aiWindow.Focus();
                return;
            }

            _aiWindow = new AIAssistantWindow(_aiSelectionEvent, _aiSelectionHandler);
            var helper = new System.Windows.Interop.WindowInteropHelper(_aiWindow);
            helper.Owner = commandData.Application.MainWindowHandle;
            _aiWindow.Closed += (s, e) => { _aiWindow = null; };
            _aiWindow.Show();
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

                // AI 助手 ExternalEvent
                _aiSelectionHandler = new AISelectionHandler();
                _aiSelectionEvent = ExternalEvent.Create(_aiSelectionHandler);

                // 2. 建立 Ribbon 頁籤
                string tabName = "Development tools";
                application.CreateRibbonTab(tabName);
                
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                string originalPath = new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath;

                // Panel 1: 系統管理
                RibbonPanel adminPanel = application.CreateRibbonPanel(tabName, "系統管理");
                
                PushButtonData loginBtn = new PushButtonData(
                    "GoogleLogin", "Google 登入",
                    assemblyPath, "DevelopmentTools.Commands.LoginCommand");
                loginBtn.LargeImage = CreateDynamicIcon("🔑", "#4285F4", "#FFFFFF");
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
                feedbackBtn.LargeImage = CreateDynamicIcon("💬", "#34A853", "#FFFFFF");
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
                updateBtn.LargeImage = CreateDynamicIcon("🔄", "#EA4335", "#FFFFFF");
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

                // Panel 1.5: 語言切換 (Added next to 系統管理)
                
                PulldownButtonData langDropdownData = new PulldownButtonData("LanguageDropdown", "語言 / Language");
                langDropdownData.ToolTip = "切換系統語言 / Switch Language / 言語を切り替える";
                
                string langDllPath = "";
                try { langDllPath = System.Reflection.Assembly.GetExecutingAssembly().Location; } catch { }
                if (string.IsNullOrEmpty(langDllPath) || !System.IO.Directory.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(langDllPath), "Resources")))
                {
                    try { langDllPath = new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase).LocalPath; } catch { }
                }
                if (string.IsNullOrEmpty(langDllPath) || !System.IO.Directory.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(langDllPath), "Resources")))
                {
                    langDllPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "DevelopmentTools", "App", "dummy.dll");
                }
                
                string iconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(langDllPath), "Resources", "RibbonIcons", "language.png");
                if (System.IO.File.Exists(iconPath))
                {
                    langDropdownData.LargeImage = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath));
                }
                else
                {
                    langDropdownData.LargeImage = CreateDynamicIcon("🌍", "#3498DB", "#FFFFFF");
                }

                PulldownButton langDropdown = adminPanel.AddItem(langDropdownData) as PulldownButton;

                PushButtonData langZhBtn = new PushButtonData("LanguageZH", "繁體中文", assemblyPath, "DevelopmentTools.Commands.Cmd_LanguageZH");
                PushButtonData langEnBtn = new PushButtonData("LanguageEN", "English", assemblyPath, "DevelopmentTools.Commands.Cmd_LanguageEN");
                PushButtonData langJaBtn = new PushButtonData("LanguageJA", "日本語", assemblyPath, "DevelopmentTools.Commands.Cmd_LanguageJA");

                langDropdown.AddPushButton(langZhBtn);
                langDropdown.AddPushButton(langEnBtn);
                langDropdown.AddPushButton(langJaBtn);

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
                mainBtn.LargeImage = CreateDynamicIcon("🟦", "#2980B9", "#FFFFFF");
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
                elevationBtn.LargeImage = CreateDynamicIcon("📐", "#8E44AD", "#FFFFFF");
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
                wallFinishBtn.LargeImage = CreateDynamicIcon("🏗", "#27AE60", "#FFFFFF");
                finishPanel.AddItem(wallFinishBtn);

                PushButtonData roomFinishConfigBtn = new PushButtonData(
                    "RoomFinishConfigurator", "房間裝修配置",
                    assemblyPath, "DevelopmentTools.Commands.Cmd_RoomFinishConfigurator");
                roomFinishConfigBtn.ToolTip = "【房間裝修配置 - 快速指南】\n\n" +
                                              "1. 點擊按鈕開啟配置視窗，系統會列出專案中所有的房間 (Rooms)。\n" +
                                              "2. 您可以直接在 DataGrid 清單中編輯各個房間的地板、牆面與天花板裝修欄位。\n" +
                                              "3. 使用左側的「批量填入」面板，可一鍵套用規格名稱至多個房間。\n" +
                                              "4. 點擊「確定更新」寫回 Revit，作為自動粉刷/排磚等工具的生成依據。";
                roomFinishConfigBtn.LongDescription = "【房間裝修配置 - 數據關聯說明】\n\n" +
                                                      "■ 參數對應：直接讀寫 Revit Room 元件內建的地板裝修 (Base Finish)、牆面裝修 (Wall Finish) 與天花板裝修 (Ceiling Finish) 屬性。\n" +
                                                      "■ 自動化工作流：透過本工具在房間中預先填入好裝修編號或材質（如: T01 地磚、P01 乳膠漆），後續其他裝修生成工具在分析房間時，即可自動識別並生成相應厚度與材質的實體牆/板/填充線，實現 BIM 智慧化裝修自動化。";
                roomFinishConfigBtn.LargeImage = CreateDynamicIcon("🏠", "#16A085", "#FFFFFF");
                finishPanel.AddItem(roomFinishConfigBtn);

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
                floorSnapBtn.LargeImage = CreateDynamicIcon("⬇", "#D35400", "#FFFFFF");
                floorPanel.AddItem(floorSnapBtn);

                // Panel 5: DT｜圖紙工具
                RibbonPanel sheetPanel = application.CreateRibbonPanel(tabName, "圖紙工具");

                PushButtonData sheetRenameBtn = new PushButtonData(
                    "BatchSheetRename", "圖紙批次更名",
                    assemblyPath, "DevelopmentTools.Commands.Cmd_BatchSheetRenamer");
                sheetRenameBtn.ToolTip = "【圖紙批次更名 - 快速指南】\n\n" +
                                         "1. 點擊按鈕開啟批次更名視窗。\n" +
                                         "2. 視窗會列出目前專案中所有的圖紙 (Sheets)。\n" +
                                         "3. 您可以直接在清單的格子中修改圖紙編號或圖紙名稱。\n" +
                                         "4. 按下「確定更新」即可一次套用所有變更。";
                sheetRenameBtn.LongDescription = "【圖紙批次更名 - 衝突防護與批次功能說明】\n\n" +
                                                 "■ 衝突防護機制：Revit 原生要求圖紙編號必須唯一。如果您想對調兩張圖紙的編號（如 A:001->002，B:002->001），本工具會自動偵測衝突並採用「兩步暫存修改法」（先暫時加上隨機或 Temp 後綴，再統一修改為目標編號），完美避開 Revit 限制。\n" +
                                                 "■ 批次修改與預覽：支援即時編輯，提供直觀的介面，防止因手動一個個修改圖紙屬性而造成的繁瑣操作與編號衝突。";
                sheetRenameBtn.LargeImage = CreateDynamicIcon("📝", "#2C3E50", "#FFFFFF");
                sheetPanel.AddItem(sheetRenameBtn);

                PushButtonData viewCreateBtn = new PushButtonData(
                    "QuickViewCreator", "快速開圖套樣板",
                    assemblyPath, "DevelopmentTools.Commands.Cmd_QuickViewCreator");
                viewCreateBtn.ToolTip = "【快速開圖套樣板 - 快速指南】\n\n" +
                                        "1. 點擊按鈕開啟開圖視窗。\n" +
                                        "2. 在左側勾選一個或多個來源樓層平面圖。\n" +
                                        "3. 在右側勾選一個或多個要套用的視圖樣板。\n" +
                                        "4. 選擇複製模式（如複製詳圖），並可選勾選同步自動建立圖紙。\n" +
                                        "5. 點擊「一鍵開圖」即可自動批次複製、命名、套用樣板與放置圖紙。";
                viewCreateBtn.LongDescription = "【快速開圖套樣板 - 複製與圖紙自動化說明】\n\n" +
                                                "■ 複製機制：支援「僅複製結構 (Duplicate)」、「複製詳圖 (With Detailing)」與「建立相依 (As Dependent)」三種模式，完美應對不同的出圖需求。\n" +
                                                "■ 命名防碰撞：新視圖名稱為 [來源視圖名稱]-[樣板名稱]。若名稱已存在，系統會自動遞增後綴 (如 _1, _2) 避免 Revit 衝突崩潰。\n" +
                                                "■ 一鍵自動出圖：若勾選「同步建立圖紙」，系統會自動採用專案圖框為新視圖建立對齊圖紙，並自動將視圖放置到圖紙正中央，實現開圖到排版的一鍵完成。";
                viewCreateBtn.LargeImage = CreateDynamicIcon("🖼", "#C0392B", "#FFFFFF");
                sheetPanel.AddItem(viewCreateBtn);

                PushButtonData viewPlaceBtn = new PushButtonData(
                    "SheetViewPlacer", "圖紙視圖排版",
                    assemblyPath, "DevelopmentTools.Commands.Cmd_SheetViewPlacer");
                viewPlaceBtn.ToolTip = "【圖紙視圖排版 - 快速指南】\n\n" +
                                       "1. 點擊按鈕開啟拖曳排版主視窗。\n" +
                                       "2. 在左側樹狀清單中展開「未放置視圖」。\n" +
                                       "3. 將目標視圖拖曳 (Drag & Drop) 放至上方的特定圖紙上即可置入。\n" +
                                       "4. 可跨圖紙相互拖曳搬移，或點選「新建圖紙」一鍵加開圖紙。";
                viewPlaceBtn.LongDescription = "【圖紙視圖排版 - 拖放移轉與防重疊說明】\n\n" +
                                               "■ 智能跨圖紙搬移：由於 Revit 限制普通視圖只能放置在單一圖紙上，當您跨圖紙拖曳時，系統會自動在後台刪除舊圖紙的 Viewport 並於新圖紙重建，避免 Revit 異常。\n" +
                                               "■ 位置自動偏移：置入新視圖時，若圖紙已有視窗，系統會自動套用微幅偏移以防完全重疊遮擋，並會自動套用下拉選定的視埠樣式。\n" +
                                               "■ 支援明細表與圖例：除了一般平面/剖立面/3D 視圖外，亦支援將明細表 (Schedule) 等可多重置入的視圖進行拖放排版。";
                viewPlaceBtn.LargeImage = CreateDynamicIcon("📋", "#1A5276", "#FFFFFF");
                sheetPanel.AddItem(viewPlaceBtn);

                PushButtonData sheetDuplicatorBtn = new PushButtonData(
                    "SheetDuplicator", DevelopmentTools.Core.LanguageManager.Instance["Ribbon_Btn_SheetDuplicator"],
                    assemblyPath, "DevelopmentTools.Commands.Cmd_SheetDuplicator");
                sheetDuplicatorBtn.ToolTip = DevelopmentTools.Core.LanguageManager.Instance["Ribbon_TT_SheetDuplicator"];
                // 暫時使用同一個 icon，或者如果有 sheet-duplicator.png 的話
                sheetDuplicatorBtn.LargeImage = CreateDynamicIcon("📋", "#1A5276", "#FFFFFF");
                sheetPanel.AddItem(sheetDuplicatorBtn);

                // Panel 6: DT｜標註工具
                RibbonPanel tagPanel = application.CreateRibbonPanel(tabName, "標註工具");

                PushButtonData quickDimBtn = new PushButtonData(
                    "QuickDimension", "快速尺寸標註",
                    assemblyPath, "DevelopmentTools.Commands.Cmd_QuickDimension");
                quickDimBtn.ToolTip = "【快速尺寸標註 - 快速指南】\n\n" +
                                      "1. 點擊按鈕開啟標註工具箱。\n" +
                                      "2. 選擇您要使用的標註模式（柱中心、牆中心、開口邊到邊）。\n" +
                                      "3. 點擊「開始選取並標註」，依 Revit 提示在視圖中點選目標元件。\n" +
                                      "4. 按下 Esc 完成選取，尺寸線便會自動在預設偏移位置生成。";
                quickDimBtn.LongDescription = "【快速尺寸標註 - 標註原理與幾何提取說明】\n\n" +
                                              "■ 柱中心對齊：自動讀取結構柱/建築柱內部的 Center 幾何面參照，依柱子排列方向（水平或垂直）建立橫向或縱向的尺寸標註。\n" +
                                              "■ 牆中心對齊：藉由牆體定位中心線直接提取 Reference，自動在平行牆體中線建立連續的對齊標註。\n" +
                                              "■ 開口邊到邊：讀取門、窗、洞口元件內部的 Left 與 Right 強參照面，實現開口淨寬與間距的快速一鍵尺寸標註。";
                quickDimBtn.LargeImage = CreateDynamicIcon("📏", "#6C3483", "#FFFFFF");
                tagPanel.AddItem(quickDimBtn);

                // Panel 7: AI 助手
                RibbonPanel aiPanel = application.CreateRibbonPanel(tabName, LanguageManager.Instance["Ribbon_Panel_AI"]);

                PushButtonData aiAssistantBtn = new PushButtonData(
                    "AIAssistant", LanguageManager.Instance["Ribbon_Btn_AI"],
                    assemblyPath, "DevelopmentTools.Commands.Cmd_AIAssistant");
                aiAssistantBtn.ToolTip = LanguageManager.Instance["Ribbon_TT_AI"];
                aiAssistantBtn.LargeImage = CreateDynamicIcon("🤖", "#6E40C9", "#FFFFFF");
                aiPanel.AddItem(aiAssistantBtn);

                // Panel 8: Document Tools
                RibbonPanel docToolsPanel = application.CreateRibbonPanel(tabName, LanguageManager.Instance["Ribbon_Panel_DocTools"]);

                PushButtonData sheetTransferBtn = new PushButtonData(
                    "SheetTransfer", LanguageManager.Instance["Ribbon_Btn_SheetTransfer"],
                    assemblyPath, "DevelopmentTools.Commands.Cmd_SheetTransfer");
                sheetTransferBtn.ToolTip = LanguageManager.Instance["Ribbon_TT_SheetTransfer"];
                // 找不到 icon 先用 emoji fallback 即可
                sheetTransferBtn.LargeImage = CreateDynamicIcon("📦", "#0052CC", "#FFFFFF");
                docToolsPanel.AddItem(sheetTransferBtn);



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
                application.ControlledApplication.ApplicationInitialized += (s, e) =>
                {
                    try
                    {
                        DevelopmentTools.Commands.Cmd_ChangeLanguageBase.UpdateRibbonTexts();
                    }
                    catch { }

                    // 每次 Revit 啟動就檢查更新，不再等使用者點特定按鈕
                    TriggerSilentUpdateCheck();
                };

                return Result.Succeeded;
            }
            catch (Exception)
            {
                return Result.Failed;
            }
        }

        
        private void ApplyRibbonIcon(Autodesk.Revit.UI.PushButtonData btnData, string iconFileName)
        {
            try
            {
                string dllPath = "";
                try { dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location; } catch { }

                if (string.IsNullOrEmpty(dllPath) || !System.IO.Directory.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dllPath), "Resources")))
                {
                    try { dllPath = new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase).LocalPath; } catch { }
                }

                if (string.IsNullOrEmpty(dllPath) || !System.IO.Directory.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dllPath), "Resources")))
                {
                    dllPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "DevelopmentTools", "App", "dummy.dll");
                }

                string iconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dllPath), "Resources", "RibbonIcons", iconFileName);
                bool iconLoaded = false;
                if (System.IO.File.Exists(iconPath))
                {
                    try
                    {
                        var bmp = new System.Windows.Media.Imaging.BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new System.Uri(iconPath, System.UriKind.Absolute);
                        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        btnData.LargeImage = bmp;
                        iconLoaded = true;
                    }
                    catch { }
                }
                
                if (!iconLoaded)
                {
                    // PNG 不存在或讀取失敗時動態畫 fallback 圖示，讓按鈕永遠不會空白
                    var fallbackMap = new System.Collections.Generic.Dictionary<string, (string emoji, string bg)>
                    {
                        { "tile-layout.png",         ("🟦", "#2980B9") },
                        { "tile-elevation.png",      ("📐", "#8E44AD") },
                        { "wall-finish.png",         ("🏗", "#27AE60") },
                        { "room-finish-config.png",  ("🏠", "#16A085") },
                        { "floor-snap-to-room.png",  ("⬇", "#D35400") },
                        { "batch-sheet-renamer.png", ("📝", "#2C3E50") },
                        { "quick-view-creator.png",  ("🖼", "#C0392B") },
                        { "sheet-view-placer.png",   ("📋", "#1A5276") },
                        { "quick-dimension.png",     ("📏", "#6C3483") },
                        { "sheet-transfer.png",      ("📦", "#0052CC") },
                    };
                    if (fallbackMap.TryGetValue(iconFileName, out var info))
                    {
                        btnData.LargeImage = CreateDynamicIcon(info.emoji, info.bg, "#FFFFFF");
                    }
                }
            }
            catch { }
        }


        
        private System.Windows.Media.Imaging.BitmapImage CreateDynamicIcon(string text, string bgColorHex, string fgColorHex)
        {
            try
            {
                var width = 32;
                var height = 32;
                var drawingVisual = new System.Windows.Media.DrawingVisual();
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    var bgBrush = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFrom(bgColorHex);
                    drawingContext.DrawRectangle(bgBrush, null, new System.Windows.Rect(0, 0, width, height));

                    var typeFace = new System.Windows.Media.Typeface("Segoe UI Emoji");
                    var formattedText = new System.Windows.Media.FormattedText(
                        text,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        typeFace,
                        18,
                        (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFrom(fgColorHex),
                        System.Windows.Media.VisualTreeHelper.GetDpi(drawingVisual).PixelsPerDip);

                    drawingContext.DrawText(formattedText, new System.Windows.Point((width - formattedText.Width) / 2, (height - formattedText.Height) / 2));
                }

                var renderTarget = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                renderTarget.Render(drawingVisual);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(renderTarget));

                using (var memoryStream = new System.IO.MemoryStream())
                {
                    encoder.Save(memoryStream);
                    var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = memoryStream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                    return bitmapImage;
                }
            }
            catch
            {
                return null;
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

