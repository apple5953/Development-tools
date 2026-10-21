using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BEAI.QueryHost;
using BEAI.QueryHost.Commands;
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
        private static AICommandHandler _aiCommandHandler;
        private static ExternalEvent _aiCommandEvent;

        // 事前檢核視窗
        private static Modules.DesignReview.Views.DesignReviewWindow _designReviewWindow;

        // Code Review Window & ViewModel
        private static System.Windows.Window _codeReviewWindow;
        private static Modules.DesignReview.ViewModels.CodeReviewPaneViewModel _codeReviewPaneViewModel;
        // Adaptive Region Dockable Pane UI
        public static DevelopmentTools.Modules.AdaptiveSectionBox.Views.RegionManagerDockablePane RegionPane { get; private set; }

        // Adaptive Region Async Handler (防止被 GC 回收)
        public static DevelopmentTools.Modules.AdaptiveSectionBox.Interaction.AdaptiveRegionExternalEventHandler AdaptiveRegionHandler { get; private set; }
        private static readonly System.Collections.Generic.HashSet<string> _activeDocs = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);


        /// <summary>建立的 Ribbon 面板與其語系字串鍵，供切換語言時更新標題。</summary>
        public static readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<RibbonPanel, string>> TrackedPanels = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<RibbonPanel, string>>();


        /// <summary>Revit 主執行緒的 Dispatcher（OnStartup 時記下），背景工作要顯示對話框時用它切回主執行緒。</summary>
        public static System.Windows.Threading.Dispatcher UiDispatcher { get; private set; }

        /// <summary>在 Revit 主執行緒顯示 TaskDialog；目前已在主執行緒就直接顯示。</summary>
        public static void ShowDialogOnUi(string title, string text)
        {
            var d = UiDispatcher;
            if (d == null || d.CheckAccess()) { TaskDialog.Show(title, text); return; }
            d.BeginInvoke(new Action(() => TaskDialog.Show(title, text)));
        }

        public static void Log(string msg)
        {
            try
            {
                string dllDir = Path.GetDirectoryName(typeof(App).Assembly.Location);
                string logPath = Path.Combine(dllDir, "RevitAddinLog.txt");
                // 記錄檔超過 5 MB 時改名保留一份，避免大型模型長期使用後無限變大
                var info = new FileInfo(logPath);
                if (info.Exists && info.Length > 5 * 1024 * 1024)
                {
                    string old = logPath + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(logPath, old);
                }
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
                        // TaskDialog 只能在 Revit 主執行緒顯示；這裡在背景執行緒，必須切回主執行緒
                        bool accepted = false;
                        var dispatcher = UiDispatcher;
                        if (dispatcher == null) return;
                        dispatcher.Invoke(() =>
                        {
                            TaskDialog td = new TaskDialog("遠端更新提示");
                            td.MainInstruction = $"發現新版本 v{result.LatestVersion}！";
                            td.MainContent = $"目前本機版本: v{result.CurrentVersion}\n\n更新內容:\n{result.ReleaseNotes}\n\n是否下載新版並準備安裝？";
                            td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                            td.DefaultButton = TaskDialogResult.Yes;
                            accepted = td.Show() == TaskDialogResult.Yes;
                        });

                        if (accepted)
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

        public static void ShowCodeReviewPane(UIApplication uiapp)
        {
            try
            {
                if (_codeReviewWindow != null && _codeReviewWindow.IsLoaded)
                {
                    _codeReviewWindow.Activate();
                    _codeReviewWindow.Focus();
                    return;
                }

                if (_codeReviewPaneViewModel == null)
                {
                    Log("[CodeReview] Creating new ViewModel");
                    _codeReviewPaneViewModel = new Modules.DesignReview.ViewModels.CodeReviewPaneViewModel(uiapp);
                    Log("[CodeReview] ViewModel created OK");
                }
                else
                {
                    Log("[CodeReview] Reusing existing ViewModel, calling LoadInitialProject");
                    _codeReviewPaneViewModel.LoadInitialProject();
                }

                var page = new Modules.DesignReview.Views.CodeReviewPanePage();
                page.SetViewModel(_codeReviewPaneViewModel);
                Log("[CodeReview] Page created and VM bound");

                _codeReviewWindow = new System.Windows.Window
                {
                    Title = "BCR 法規檢討與追蹤",
                    Width = 380,
                    Height = 750,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(7, 9, 14)),
                    Content = page
                };

                var helper = new System.Windows.Interop.WindowInteropHelper(_codeReviewWindow);
                helper.Owner = uiapp.MainWindowHandle;

                _codeReviewWindow.Closed += (s, e) => { _codeReviewWindow = null; };
                _codeReviewWindow.Show();
                Log("[CodeReview] Window shown");
            }
            catch (Exception ex)
            {
                Log($"[CodeReview] ShowCodeReviewPane EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                Autodesk.Revit.UI.TaskDialog.Show("法規檢討錯誤", $"開啟法規檢討視窗時發生錯誤：\n{ex.Message}");
            }
        }

        public static void ShowOrActivateDesignReview(ExternalCommandData commandData)
        {
            if (_designReviewWindow != null && _designReviewWindow.IsLoaded)
            {
                _designReviewWindow.Activate();
                _designReviewWindow.Focus();
                return;
            }

            var vm = new Modules.DesignReview.ViewModels.DesignReviewViewModel(commandData.Application);
            _designReviewWindow = new Modules.DesignReview.Views.DesignReviewWindow(vm);

            var helper = new System.Windows.Interop.WindowInteropHelper(_designReviewWindow);
            helper.Owner = commandData.Application.MainWindowHandle;
            _designReviewWindow.Closed += (s, e) => { _designReviewWindow = null; };
            _designReviewWindow.Show();
        }

        public static void ShowOrActivateAIAssistant(ExternalCommandData commandData)
        {
            if (_aiWindow != null && _aiWindow.IsLoaded)
            {
                _aiWindow.Activate();
                _aiWindow.Focus();
                return;
            }

            _aiWindow = new AIAssistantWindow(_aiSelectionEvent, _aiSelectionHandler, _aiCommandEvent, _aiCommandHandler);
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
                        bool isAuthorized = await CommandAccessGuard.EnsureAuthorizedAsync("Tiling", "磁磚鋪設系統");
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
            UiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            try
            {
                // （已將原右側常駐頁面改為獨立彈出視窗模式）

                // 1. 建立 ExternalEvent（在 OnStartup 建立，才能在整個 session 存活）
                EventHandler = new TileSyncEventHandler();
                SyncEvent = ExternalEvent.Create(EventHandler);

                // AI 助手 ExternalEvent
                _aiSelectionHandler = new AISelectionHandler();
                _aiSelectionEvent = ExternalEvent.Create(_aiSelectionHandler);
                _aiCommandHandler = new AICommandHandler();
                _aiCommandEvent = ExternalEvent.Create(_aiCommandHandler);

                // 2. 建立 Ribbon：一個「RTS」頁籤，三個分類下拉按鈕，工具都在下拉選單中（按鈕清單集中在 Core\RibbonCatalog.cs）
                //   出圖        ：開圖 ─ 圖紙 ─ 標註 ─ Word 文件
                //   建模        ：裝修磁磚 ─ 鋼筋結構 ─ 族群 ─ 3D 與視圖
                //   檢討與管理  ：法規檢討 ─ 專案標準 ─ AI 助手 ─ 系統 ─ 語言
                // 新增外掛時在 RibbonCatalog.Layout() 對應分類加一行即可；文字會隨語言切換更新。
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                Result bridgeResult = EmbeddedBridge.Start(application, Path.GetDirectoryName(assemblyPath));
                Log(bridgeResult == Result.Succeeded
                    ? "BEAI 模型查詢服務已整合啟動。"
                    : "BEAI 模型查詢服務啟動失敗，Room Tile 其餘功能仍可使用。");

                RibbonCatalog.Build(application, assemblyPath, TrackedPanels, msg => Log(msg));

                // 3. 註冊 DMU Updater
                _updater = new TileUpdater(application.ActiveAddInId);
                // isOptional = true：別人在沒有裝本外掛的電腦開啟模型時，Revit 不會跳出「缺少第三方 Updater」警告
                UpdaterRegistry.RegisterUpdater(_updater, true);

                ElementClassFilter classFilter = new ElementClassFilter(typeof(FamilyInstance));
                ElementCategoryFilter categoryFilter = new ElementCategoryFilter(BuiltInCategory.OST_GenericModel);
                LogicalAndFilter gmFilter = new LogicalAndFilter(classFilter, categoryFilter);

                UpdaterRegistry.AddTrigger(_updater.GetUpdaterId(), gmFilter, Element.GetChangeTypeAny());

                ElementClassFilter dsClassFilter = new ElementClassFilter(typeof(DirectShape));
                UpdaterRegistry.AddTrigger(_updater.GetUpdaterId(), dsClassFilter, Element.GetChangeTypeGeometry());

                // 訂閱開檔/同步中央/讀取最新變更事件，以控制 Updater 避開這些高載入狀態
                application.ControlledApplication.DocumentOpening += OnDocOpening;
                application.ControlledApplication.DocumentOpened += OnDocOpened;
                application.ControlledApplication.DocumentSynchronizingWithCentral += OnDocSyncing;
                application.ControlledApplication.DocumentSynchronizedWithCentral += OnDocSynced;
                application.ControlledApplication.DocumentReloadingLatest += OnDocReloading;
                application.ControlledApplication.DocumentReloadedLatest += OnDocReloaded;
                // --- 補回 Adaptive Region 空間區域初始化與註冊 ---
                AdaptiveRegionHandler = new DevelopmentTools.Modules.AdaptiveSectionBox.Interaction.AdaptiveRegionExternalEventHandler();
                
                // 在 Revit 啟動時全局註冊 3D 著色渲染服務，確保 GPU 管線初始化完畢
                DevelopmentTools.Modules.AdaptiveSectionBox.Rendering.RenderingService.Instance.Register();

                // 註冊 Adaptive Region Dockable Pane 停靠面板
                try
                {
                    var paneId = new DockablePaneId(DevelopmentTools.Commands.Cmd_AdaptiveRegion.PaneGuid);
                    RegionPane = new DevelopmentTools.Modules.AdaptiveSectionBox.Views.RegionManagerDockablePane();
                    application.RegisterDockablePane(paneId, "智慧空間區域 (Adaptive Region)", RegionPane);
                }
                catch (Exception ex)
                {
                    Log($"註冊 DockablePane 失敗: {ex.Message}");
                }

                // 訂閱視圖切換事件以同步文件
                application.ViewActivated += OnViewActivated;

                application.ControlledApplication.ApplicationInitialized += (s, e) =>
                {
                    try
                    {
                        DevelopmentTools.Commands.Cmd_ChangeLanguageBase.UpdateRibbonTexts();
                    }
                    catch { }

                    // BIM MCP 已併入 RTS 頁籤的下拉選單，隱藏它自己的頁籤（外掛照常運作）
                    RibbonCatalog.HideMergedTabs();

                    // 每次 Revit 啟動就檢查更新，不再等使用者點特定按鈕
                    TriggerSilentUpdateCheck();
                };

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log($"OnStartup 崩潰: {ex.Message}\n{ex.StackTrace}");
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
                    dllPath = System.IO.Path.Combine(DevelopmentTools.Core.InstallLocation.CurrentAppDir, "dummy.dll");
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
                        { "bcr-project.png",         ("⚖️", "#E67E22") },
                        { "bcr-export.png",          ("📥", "#27AE60") },
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
                EmbeddedBridge.Stop();
                SyncEvent?.Dispose();
                _aiSelectionEvent?.Dispose();
                _aiCommandEvent?.Dispose();
                if (_updater != null)
                    UpdaterRegistry.UnregisterUpdater(_updater.GetUpdaterId());

                // 安全關閉滑鼠 Hook 編輯模式與註銷 3D 著色
                DevelopmentTools.Modules.AdaptiveSectionBox.Interaction.InteractionService.Instance.Stop();
                DevelopmentTools.Modules.AdaptiveSectionBox.Rendering.RenderingService.Instance.Dispose();

                // 取消訂閱事件
                application.ControlledApplication.DocumentOpening -= OnDocOpening;
                application.ControlledApplication.DocumentOpened -= OnDocOpened;
                application.ControlledApplication.DocumentSynchronizingWithCentral -= OnDocSyncing;
                application.ControlledApplication.DocumentSynchronizedWithCentral -= OnDocSynced;
                application.ControlledApplication.DocumentReloadingLatest -= OnDocReloading;
                application.ControlledApplication.DocumentReloadedLatest -= OnDocReloaded;

                application.ViewActivated -= OnViewActivated;
            }
            catch { }
            return Result.Succeeded;
        }

        private void OnViewActivated(object sender, Autodesk.Revit.UI.Events.ViewActivatedEventArgs e)
        {
            try
            {
                if (e.Document != null)
                {
                    var uidoc = new UIDocument(e.Document);
                    
                    // 自動同步當前文件與 3D 預覽
                    DevelopmentTools.Modules.AdaptiveSectionBox.ViewModels.RegionManagerViewModel.Instance.SetDocument(uidoc);

                    // 安全防禦：若目前編輯中，但切換至非 3D 視圖或不同文件，則自動結束編輯
                    var interactionService = DevelopmentTools.Modules.AdaptiveSectionBox.Interaction.InteractionService.Instance;
                    if (interactionService.IsEditModeActive)
                    {
                        if (e.CurrentActiveView?.ViewType != ViewType.ThreeD || e.Document.PathName != e.PreviousActiveView?.Document?.PathName)
                        {
                            interactionService.Stop();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in OnViewActivated: {ex.Message}");
            }
        }
    }
}

