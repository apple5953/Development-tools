using System;
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

        public static void ShowOrActivateWindow(ExternalCommandData commandData)
        {
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
                loginBtn.ToolTip = "登入 Google 帳號，管理或切換外掛授權狀態。";
                
                PushButtonData feedbackBtn = new PushButtonData(
                    "SystemFeedback", "問題與反饋",
                    assemblyPath, "DevelopmentTools.Commands.FeedbackCommand");
                feedbackBtn.ToolTip = "向作者提交功能優化反饋或錯誤回報。";

                PushButtonData updateBtn = new PushButtonData(
                    "CheckForUpdates", "檢查更新",
                    assemblyPath, "DevelopmentTools.Commands.CheckUpdateCommand");
                updateBtn.ToolTip = "檢查是否有新版本的 Revit 外掛並進行自動更新。";

                adminPanel.AddItem(loginBtn);
                adminPanel.AddItem(feedbackBtn);
                adminPanel.AddItem(updateBtn);

                // Panel 2: 空間排版
                RibbonPanel layoutPanel = application.CreateRibbonPanel(tabName, "空間排版");
                
                PushButtonData mainBtn = new PushButtonData(
                    "ShowControlPanel", "磁磚鋪設系統",
                    assemblyPath, "DevelopmentTools.Commands.ShowControlPanelCommand");
                mainBtn.ToolTip = "開啟空間磁磚鋪設主控台（選 Room → 自動偵測裝修面 → Paint .pat）";
                layoutPanel.AddItem(mainBtn);

                // Panel 3: 粉刷裝修
                RibbonPanel finishPanel = application.CreateRibbonPanel(tabName, "粉刷裝修");
                
                PushButtonData wallFinishBtn = new PushButtonData(
                    "WallFinishTool", "自動粉刷",
                    assemblyPath, "DevelopmentTools.Commands.WallFinishCommand");
                wallFinishBtn.ToolTip = "自動在選定空間的牆面上鋪設粉刷層實體（Mock 驗證版）。";
                finishPanel.AddItem(wallFinishBtn);

                // 3. 註冊 DMU Updater
                _updater = new TileUpdater(application.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_updater);

                ElementClassFilter gmFilter = new ElementClassFilter(typeof(FamilyInstance));
                UpdaterRegistry.AddTrigger(_updater.GetUpdaterId(), gmFilter, Element.GetChangeTypeAny());

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
            }
            catch { }
            return Result.Succeeded;
        }
    }
}
