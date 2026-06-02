using System;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoginCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            string email = GoogleAuthManager.CurrentUserEmail;

            if (!GoogleAuthManager.IsAuthEnabled())
            {
                TaskDialog.Show("系統管理", "本外掛系統目前未啟用 Google 授權驗證功能。");
                return Result.Succeeded;
            }

            if (!string.IsNullOrEmpty(email))
            {
                TaskDialogResult res = TaskDialog.Show(
                    "Google 帳號管理",
                    $"目前已登入帳號：\n{email}\n\n是否要登出並重新進行 Google 登入？",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                );

                if (res == TaskDialogResult.No)
                {
                    return Result.Succeeded;
                }

                // 清除登入狀態
                GoogleAuthManager.CurrentUserEmail = "";
            }

            Task.Run(async () =>
            {
                try
                {
                    uiDispatcher.Invoke(() =>
                    {
                        TaskDialog.Show("Google 登入", "即將開啟網頁瀏覽器進行 Google 帳號授權登入...");
                    });

                    var authResult = await GoogleAuthManager.LoginAndGetEmailAsync();
                    
                    uiDispatcher.Invoke(() =>
                    {
                        if (authResult != null && authResult.IsSuccess)
                        {
                            TaskDialog.Show("登入成功", $"登入成功！\n已驗證帳號：{authResult.Email}");
                        }
                        else
                        {
                            string errMsg = authResult?.ErrorMessage ?? "登入失敗或已被取消。";
                            TaskDialog td = new TaskDialog("Google 登入");
                            td.MainInstruction = "登入失敗";
                            td.MainContent = errMsg;
                            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "點擊此處加入作者 Line 聯絡人");
                            
                            TaskDialogResult res = td.Show();
                            if (res == TaskDialogResult.CommandLink1)
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
                                { 
                                    FileName = "https://line.me/ti/p/ov08MDxYA1", 
                                    UseShellExecute = true 
                                });
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    uiDispatcher.Invoke(() =>
                    {
                        TaskDialog.Show("登入錯誤", $"登入過程發生異常：{ex.Message}");
                    });
                }
            });

            return Result.Succeeded;
        }
    }
}
