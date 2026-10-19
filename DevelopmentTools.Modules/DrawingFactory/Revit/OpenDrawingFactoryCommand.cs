using System;
using System.Diagnostics;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIMDrawingFactory.UI.Services;
using BIMDrawingFactory.UI.ViewModels;
using BIMDrawingFactory.UI.Views;

namespace BIMDrawingFactory.UI.Revit
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenDrawingFactoryCommand : IExternalCommand
    {
        private static MainShellView _activeWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 即使目前 Loader 還是舊版，也要清除先前熱載入組件留下的同名視窗。
                // 不以 CLR 型別相等判斷，因為每次 Assembly.Load(byte[]) 都是不同型別身分。
                if (System.Windows.Application.Current != null)
                {
                    var staleWindows = new System.Collections.Generic.List<System.Windows.Window>();
                    foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
                    {
                        if (window != null && window.GetType().FullName == "BIMDrawingFactory.UI.Views.MainShellView")
                            staleWindows.Add(window);
                    }
                    foreach (var window in staleWindows)
                    {
                        try { window.Close(); } catch { }
                    }
                }

                if (_activeWindow != null)
                {
                    try
                    {
                        _activeWindow.Close();
                    }
                    catch
                    {
                    }
                    _activeWindow = null;
                }

                // 0. 確保 WPF 主題資源字典已與 Revit Application 合併
                ResourceHelper.EnsureResourcesLoaded();

                // 1. 初始化服務層
                if (RevitApp.ExternalEventHandler == null)
                {
                    RevitApp.ExternalEventHandler = new RevitExternalEventHandler();
                }

                try
                {
                    if (RevitApp.ExternalEvent == null)
                    {
                        RevitApp.ExternalEvent = ExternalEvent.Create(RevitApp.ExternalEventHandler);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("[ExternalEvent.Create Warning] {0}", ex.Message));
                }

                UIApplication uiApp = commandData != null ? commandData.Application : null;
                INavigationService navigationService = new NavigationService();
                INotificationService notificationService = new NotificationService();
                IDialogService dialogService = new DialogService();
                IRevitDataService revitDataService = new RevitDataService(uiApp, RevitApp.ExternalEventHandler);

                var mainShellViewModel = new MainShellViewModel(
                    navigationService,
                    notificationService,
                    revitDataService,
                    dialogService);

                _activeWindow = new MainShellView(mainShellViewModel);

                IntPtr revitWindowHandle = IntPtr.Zero;
                try
                {
                    if (commandData != null && commandData.Application != null)
                    {
                        revitWindowHandle = commandData.Application.MainWindowHandle;
                    }
                }
                catch
                {
                }

                if (revitWindowHandle == IntPtr.Zero)
                {
                    try
                    {
                        revitWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
                    }
                    catch
                    {
                    }
                }

                if (revitWindowHandle != IntPtr.Zero)
                {
                    try
                    {
                        var interopHelper = new WindowInteropHelper(_activeWindow);
                        interopHelper.Owner = revitWindowHandle;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(string.Format("[WindowInteropHelper Warning] {0}", ex.Message));
                    }
                }

                _activeWindow.Closed += (s, e) => _activeWindow = null;
                _activeWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                var sb = new System.Text.StringBuilder();
                var cur = ex;
                while (cur != null)
                {
                    sb.AppendLine(cur.Message);
                    cur = cur.InnerException;
                }
                sb.AppendLine();
                sb.AppendLine(ex.StackTrace);
                message = sb.ToString();
                return Result.Failed;
            }
        }

        public static void CloseWindowIfOpen()
        {
            if (_activeWindow != null && _activeWindow.IsLoaded)
            {
                _activeWindow.Close();
                _activeWindow = null;
            }
        }

        public static void CleanupForReload()
        {
            CloseWindowIfOpen();
            if (RevitApp.ExternalEvent != null)
            {
                try { RevitApp.ExternalEvent.Dispose(); } catch { }
                RevitApp.ExternalEvent = null;
            }
            RevitApp.ExternalEventHandler = null;
        }
    }
}
