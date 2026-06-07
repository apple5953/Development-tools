using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Core
{
    public class AuthConfig
    {
        public bool Enabled { get; set; } = true;
        public string ClientId { get; set; } = "";
        public string GoogleSheetApiUrl { get; set; } = "";
        public string RedirectUri { get; set; } = "http://localhost:5000/";
    }

    public static class GoogleAuthManager
    {
        private static AuthConfig _config;

        static GoogleAuthManager()
        {
            LoadConfig();
        }

        private static void LoadConfig()
        {
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(assemblyPath);
                string configPath = Path.Combine(dir, "platform_config.json");

                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    _config = JsonSerializer.Deserialize<AuthConfig>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"無法載入驗證設定檔: {ex.Message}");
            }

            if (_config == null)
            {
                _config = new AuthConfig { Enabled = true };
            }
        }

        public static bool IsAuthEnabled()
        {
            return true;
        }

        private static string _currentUserAccessToken = "";
        public static string CurrentUserAccessToken
        {
            get => _currentUserAccessToken;
            set => _currentUserAccessToken = value;
        }

        private static string _currentUserEmail = "";
        public static string CurrentUserEmail
        {
            get => _currentUserEmail;
            set => _currentUserEmail = value;
        }

        public static bool VerifyAccess(string toolId, string toolName)
        {
            if (!IsAuthEnabled()) return true;

            string token = CurrentUserAccessToken;

            if (string.IsNullOrEmpty(token))
            {
                TaskDialogResult res = TaskDialog.Show(
                    "授權驗證", 
                    $"您目前尚未登入。即將開啟網頁瀏覽器進行 Google 登入驗證，以確認您對「{toolName}」的使用權限。", 
                    TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                );
                if (res != TaskDialogResult.Ok) return false;

                var authResult = Task.Run(async () => await LoginAndGetEmailAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
                if (authResult == null || !authResult.IsSuccess)
                {
                    string errMsg = authResult?.ErrorMessage ?? "Google 登入失敗或已取消，拒絕使用工具。";
                    TaskDialog.Show("授權失敗", errMsg);
                    return false;
                }
                token = CurrentUserAccessToken;
            }

            var checkResult = Task.Run(async () => await CheckEmailInWhiteListAsync(token, toolId).ConfigureAwait(false)).GetAwaiter().GetResult();
            if (!checkResult.IsAllowed)
            {
                TaskDialog td = new TaskDialog("未授權使用");
                td.MainInstruction = "您尚未獲得此工具的使用授權";
                td.MainContent = $"帳號: {CurrentUserEmail}\n\n說明: {checkResult.ErrorMessage}";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "點擊此處加入作者 Line 聯絡人 (開啟超連結)");
                
                TaskDialogResult res = td.Show();
                if (res == TaskDialogResult.CommandLink1)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
                    { 
                        FileName = "https://line.me/ti/p/ov08MDxYA1", 
                        UseShellExecute = true 
                    });
                }

                if (checkResult.ErrorMessage.Contains("停用") || checkResult.ErrorMessage.Contains("封鎖") || checkResult.ErrorMessage.Contains("過期"))
                {
                    CurrentUserAccessToken = ""; // 清除無效/過期的 Token
                    CurrentUserEmail = "";
                }
                return false;
            }

            return true;
        }

        public static async Task<bool> VerifyAccessAsync(string toolId, string toolName)
        {
            if (!IsAuthEnabled()) return true;

            var uiDispatcher = System.Windows.Application.Current?.Dispatcher;
            string token = CurrentUserAccessToken;

            if (string.IsNullOrEmpty(token))
            {
                bool proceed = false;
                if (uiDispatcher != null)
                {
                    uiDispatcher.Invoke(() =>
                    {
                        TaskDialogResult res = TaskDialog.Show(
                            "授權驗證", 
                            $"您目前尚未登入。即將開啟網頁瀏覽器進行 Google 登入驗證，以確認您對「{toolName}」的使用權限。", 
                            TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                        );
                        if (res == TaskDialogResult.Ok)
                        {
                            proceed = true;
                        }
                    });
                }
                else
                {
                    TaskDialogResult res = TaskDialog.Show(
                        "授權驗證", 
                        $"您目前尚未登入。即將開啟網頁瀏覽器進行 Google 登入驗證，以確認您對「{toolName}」的使用權限。", 
                        TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                    );
                    if (res == TaskDialogResult.Ok)
                    {
                        proceed = true;
                    }
                }

                if (!proceed) return false;

                var authResult = await LoginAndGetEmailAsync().ConfigureAwait(false);
                if (authResult == null || !authResult.IsSuccess)
                {
                    string errMsg = authResult?.ErrorMessage ?? "Google 登入失敗或已取消，拒絕使用工具。";
                    if (uiDispatcher != null)
                    {
                        uiDispatcher.Invoke(() =>
                        {
                            TaskDialog.Show("授權失敗", errMsg);
                        });
                    }
                    else
                    {
                        TaskDialog.Show("授權失敗", errMsg);
                    }
                    return false;
                }
                token = CurrentUserAccessToken;
            }

            // 實時向 Google Sheets API 校對針對特定 toolId 的授權狀態
            var checkResult = await CheckEmailInWhiteListAsync(token, toolId).ConfigureAwait(false);
            if (!checkResult.IsAllowed)
            {
                if (uiDispatcher != null)
                {
                    uiDispatcher.Invoke(() =>
                    {
                        ShowUnauthorisedDialog(checkResult);
                    });
                }
                else
                {
                    ShowUnauthorisedDialog(checkResult);
                }

                if (checkResult.ErrorMessage.Contains("停用") || checkResult.ErrorMessage.Contains("封鎖") || checkResult.ErrorMessage.Contains("過期"))
                {
                    CurrentUserAccessToken = ""; // 清除無效/過期的 Token
                    CurrentUserEmail = "";
                }
                return false;
            }

            return true;
        }

        private static void ShowUnauthorisedDialog(WhiteListCheckResult checkResult)
        {
            TaskDialog td = new TaskDialog("未授權使用");
            td.MainInstruction = "您尚未獲得此工具的使用授權";
            td.MainContent = $"帳號: {CurrentUserEmail}\n\n說明: {checkResult.ErrorMessage}";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "點擊此處加入作者 Line 聯絡人 (開啟超連結)");
            
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

        public static async Task<AuthResult> LoginAndGetEmailAsync()
        {
            if (_config == null || string.IsNullOrEmpty(_config.ClientId) || string.IsNullOrEmpty(_config.GoogleSheetApiUrl))
            {
                throw new InvalidOperationException("驗證設定未初始化，請檢查 platform_config.json。");
            }

            using (HttpListener listener = new HttpListener())
            {
                listener.Prefixes.Add(_config.RedirectUri);
                listener.Start();

                string state = Guid.NewGuid().ToString("N");
                string authUrl = $"https://accounts.google.com/o/oauth2/v2/auth" +
                                 $"?client_id={_config.ClientId}" +
                                 $"&redirect_uri={Uri.EscapeDataString(_config.RedirectUri)}" +
                                 $"&response_type=code" +
                                 $"&scope=email%20profile" +
                                 $"&state={state}";

                // 用預設瀏覽器打開登入頁面
                Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });

                AuthResult result = new AuthResult { IsSuccess = false, ContactInfo = "請透過 Line 聯絡作者：https://line.me/ti/p/ov08MDxYA1" };

                HttpListenerContext context = null;
                try
                {
                    // 異步等待瀏覽器回傳，增加 120 秒超時機制防止永久卡死
                    var contextTask = listener.GetContextAsync();
                    var timeoutTask = Task.Delay(120000); // 2 分鐘超時
                    var completedTask = await Task.WhenAny(contextTask, timeoutTask).ConfigureAwait(false);

                    if (completedTask == timeoutTask)
                    {
                        listener.Abort(); // 關閉 listener，這會導致 contextTask 拋出 Exception
                        result.ErrorMessage = "Google 登入驗證超時 (已逾時 2 分鐘)。";
                        return result;
                    }

                    context = await contextTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = $"等待瀏覽器回傳失敗: {ex.Message}";
                    return result;
                }

                HttpListenerRequest request = context.Request;
                string code = request.QueryString["code"];
                string returnedState = request.QueryString["state"];

                if (string.IsNullOrEmpty(code))
                {
                    result.ErrorMessage = "未取得 Google 授權代碼。";
                }
                else if (returnedState != state)
                {
                    result.ErrorMessage = "CSRF 驗證失敗 (State 不符合)。";
                }
                else
                {
                    string accessToken = await ExchangeCodeForTokenAsync(code).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        result.ErrorMessage = "無法取得 Google 存取權杖 (Access Token)。";
                    }
                    else
                    {
                        string email = await GetUserEmailAsync(accessToken).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(email))
                        {
                            result.ErrorMessage = "無法取得 Google 帳號 Email。";
                        }
                        else
                        {
                            result.Email = email;
                            var checkResult = await CheckEmailInWhiteListAsync(accessToken, "Platform", email).ConfigureAwait(false);
                            result.ContactInfo = checkResult.ContactInfo;
                            
                            if (checkResult.IsAllowed)
                            {
                                result.IsSuccess = true;
                                _currentUserAccessToken = accessToken;
                                _currentUserEmail = email; // 記錄目前的登入 Email，以便意見反饋時自動填入
                            }
                            else
                            {
                                result.ErrorMessage = checkResult.ErrorMessage ?? $"您的帳號 ({email}) 未在授權白名單中或已被封鎖。";
                            }
                        }
                    }
                }

                // 回傳對應網頁給瀏覽器
                HttpListenerResponse response = context.Response;
                string responseString = GetHtmlResponse(result.IsSuccess, result.Email, result.ErrorMessage, result.ContactInfo);
                
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentEncoding = Encoding.UTF8;
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
                listener.Stop();

                return result;
            }
        }

        private static string GetHtmlResponse(bool isSuccess, string email, string errorMessage, string contactInfo)
        {
            string title = isSuccess ? "RTS 授權驗證成功" : "RTS 授權驗證失敗";
            string icon = isSuccess ? "✔️" : "❌";
            string iconColor = isSuccess ? "#A6E3A1" : "#F38BA8"; // 貓咪粉綠 vs 貓咪粉紅
            string h1Text = isSuccess ? "Google 授權驗證成功" : "Google 授權驗證失敗";
            string h1Color = isSuccess ? "#CBA6F7" : "#F38BA8"; // 薰衣草紫 vs 貓咪粉紅
            
            string pContent;
            string subText;
            string subTextColor = isSuccess ? "#A6E3A1" : "#FAB387"; // 粉綠 vs 蜜桃橘

            if (isSuccess)
            {
                pContent = $"您的身份已成功通過 Development tools 外掛系統驗證。<br>帳號：{email}<br>請返回 Revit 視窗繼續使用外掛。";
                subText = "您可以安全地關閉此瀏覽器視窗。";
            }
            else
            {
                pContent = string.IsNullOrEmpty(errorMessage) 
                    ? "您的身份未通過 Development tools 外掛系統驗證。" 
                    : $"驗證未通過：<br>{errorMessage}";
                subText = $"聯絡管道：<a href=\"https://line.me/ti/p/ov08MDxYA1\" target=\"_blank\" style=\"color: #89B4FA; text-decoration: underline;\">點擊此處開啟 Line 聯絡人</a>";
            }

            return $@"<!DOCTYPE html>
<html lang=""zh-Hant"">
<head>
    <meta charset=""UTF-8"">
    <title>{title}</title>
    <style>
        body {{
            background-color: #1E1E2E;
            color: #CDD6F4;
            font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, Helvetica, Arial, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            overflow: hidden;
        }}
        .card {{
            background: rgba(30, 30, 46, 0.6);
            border: 1px solid rgba(255, 255, 255, 0.1);
            backdrop-filter: blur(10px);
            padding: 40px;
            border-radius: 16px;
            text-align: center;
            box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.37);
            max-width: 400px;
            animation: fadeIn 0.8s ease;
        }}
        @keyframes fadeIn {{
            from {{ opacity: 0; transform: translateY(20px); }}
            to {{ opacity: 1; transform: translateY(0); }}
        }}
        .icon {{
            font-size: 64px;
            color: {iconColor};
            margin-bottom: 20px;
            animation: pulse 2s infinite;
        }}
        @keyframes pulse {{
            0% {{ transform: scale(1); }}
            50% {{ transform: scale(1.05); }}
            100% {{ transform: scale(1); }}
        }}
        h1 {{
            font-size: 24px;
            margin: 0 0 10px 0;
            color: {h1Color};
        }}
        p {{
            font-size: 14px;
            color: #BAC2E0;
            line-height: 1.6;
            margin: 0 0 20px 0;
        }}
        .footer {{
            font-size: 11px;
            color: #6C7086;
            margin-top: 30px;
            border-top: 1px solid rgba(255, 255, 255, 0.05);
            padding-top: 15px;
        }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""icon"">{icon}</div>
        <h1>{h1Text}</h1>
        <p>{pContent}</p>
        <p style=""color: {subTextColor}; font-weight: bold; font-size: 12px;"">{subText}</p>
        <div class=""footer"">
            Development tools v3 &bull; Author: MAYOUCHR
        </div>
    </div>
</body>
</html>";
        }

        private static async Task<string> ExchangeCodeForTokenAsync(string code)
        {
            if (string.IsNullOrEmpty(_config.GoogleSheetApiUrl))
            {
                return null;
            }

            // 1. 優先嘗試：安全後端中轉交換
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    var payload = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "action", "exchange_code" },
                        { "code", code },
                        { "redirect_uri", _config.RedirectUri }
                    };
                    string json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(_config.GoogleSheetApiUrl, content).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using (JsonDocument doc = JsonDocument.Parse(responseBody))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("status", out JsonElement statusElem) && statusElem.GetString() == "success")
                            {
                                if (root.TryGetProperty("access_token", out JsonElement tokenElem))
                                {
                                    return tokenElem.GetString();
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"後端中轉交換 Token 失敗，將嘗試 Fallback 模式: {ex.Message}");
                }
            }

            // 2. Fallback 模式：如果使用者尚未更新後端 Apps Script，則在前端使用預設 ClientSecret 直接交換
            string defaultSecret = "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";
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    var values = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "code", code },
                        { "client_id", _config.ClientId },
                        { "client_secret", defaultSecret },
                        { "redirect_uri", _config.RedirectUri },
                        { "grant_type", "authorization_code" }
                    };

                    var content = new FormUrlEncodedContent(values);
                    var response = await client.PostAsync("https://oauth2.googleapis.com/token", content).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using (JsonDocument doc = JsonDocument.Parse(json))
                        {
                            if (doc.RootElement.TryGetProperty("access_token", out JsonElement tokenElem))
                            {
                                return tokenElem.GetString();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fallback 前端交換 Token 亦失敗: {ex.Message}");
                }
            }

            return null;
        }

        private static async Task<string> GetUserEmailAsync(string accessToken)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var response = await client.GetAsync("https://www.googleapis.com/oauth2/v2/userinfo").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("email", out JsonElement emailElem))
                    {
                        return emailElem.GetString();
                    }
                }
            }
            return null;
        }

        public static async Task<WhiteListCheckResult> CheckEmailInWhiteListAsync(string token, string tool = "Platform", string email = "")
        {
            var checkResult = new WhiteListCheckResult { IsAllowed = false, ContactInfo = "請透過 Line 聯絡作者取得授權：https://line.me/ti/p/ov08MDxYA1", ErrorMessage = "授權驗證失敗。" };
            if (string.IsNullOrEmpty(_config.GoogleSheetApiUrl))
            {
                checkResult.ContactInfo = "設定檔缺少 API 網址。";
                return checkResult;
            }

            using (HttpClient client = new HttpClient())
            {
                try
                {
                    string targetEmail = string.IsNullOrEmpty(email) ? CurrentUserEmail : email;
                    var payload = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "action", "check_auth" },
                        { "token", token },
                        { "email", targetEmail }, // 相容舊版後端
                        { "tool", tool }
                    };
                    string json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(_config.GoogleSheetApiUrl, content).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using (JsonDocument doc = JsonDocument.Parse(responseBody))
                        {
                            var root = doc.RootElement;
                            string status = "";
                            if (root.TryGetProperty("status", out JsonElement statusElem))
                            {
                                status = statusElem.GetString();
                            }

                            if (status == "success")
                            {
                                checkResult.IsAllowed = true;
                            }

                            if (root.TryGetProperty("message", out JsonElement msgElem))
                            {
                                checkResult.ErrorMessage = msgElem.GetString();
                            }
                            else
                            {
                                checkResult.ErrorMessage = checkResult.IsAllowed ? "" : "您的帳號已被封鎖或未授權使用此工具。";
                            }
                            
                            if (root.TryGetProperty("contact", out JsonElement contactElem))
                            {
                                checkResult.ContactInfo = contactElem.GetString();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"驗證白名單發生錯誤: {ex.Message}");
                    checkResult.ContactInfo = $"連線驗證伺服器失敗 ({ex.Message})。";
                    checkResult.ErrorMessage = $"伺服器連線失敗：{ex.Message}";
                }
            }
            return checkResult;
        }

        public static async Task<bool> SubmitFeedbackAsync(string title, string description)
        {
            if (string.IsNullOrEmpty(_config.GoogleSheetApiUrl) || string.IsNullOrEmpty(_currentUserAccessToken)) return false;

            using (HttpClient client = new HttpClient())
            {
                try
                {
                    var payload = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "action", "submit_feedback" },
                        { "token", _currentUserAccessToken },
                        { "email", _currentUserEmail }, // 相容舊版後端
                        { "title", title },
                        { "description", description }
                    };
                    string json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(_config.GoogleSheetApiUrl, content).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return result.Contains("\"success\"") || result.ToLower().Contains("success");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"送出反饋錯誤: {ex.Message}");
                }
            }
            return false;
        }

        public static async Task<bool> LogUsageDurationAsync(int durationSeconds)
        {
            if (string.IsNullOrEmpty(_config.GoogleSheetApiUrl) || string.IsNullOrEmpty(_currentUserAccessToken)) 
                return false;

            using (HttpClient client = new HttpClient())
            {
                try
                {
                    var payload = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "action", "log_duration" },
                        { "token", _currentUserAccessToken },
                        { "email", _currentUserEmail }, // 相容舊版後端
                        { "durationSeconds", durationSeconds.ToString() }
                    };
                    string json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(_config.GoogleSheetApiUrl, content).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return result.Contains("\"success\"") || result.ToLower().Contains("success");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"發送使用時長發生錯誤: {ex.Message}");
                }
            }
            return false;
        }
    }

    public class AuthResult
    {
        public bool IsSuccess { get; set; }
        public string Email { get; set; }
        public string ErrorMessage { get; set; }
        public string ContactInfo { get; set; }
    }

    public class WhiteListCheckResult
    {
        public bool IsAllowed { get; set; }
        public string ContactInfo { get; set; }
        public string ErrorMessage { get; set; }
    }
}
