using System;
using System.Threading.Tasks;
using DevelopmentTools.Core;

namespace DevelopmentTools.AgentTools
{
    /// <summary>
    /// agent 工具的授權檢查：沿用人用指令的 ToolId 與白名單，但不跳出任何對話框。
    /// 尚未登入或未在白名單時回傳原因，由人到 Revit 中登入一次。
    /// </summary>
    public static class AgentToolAccess
    {
        /// <returns>null when allowed; otherwise a message for BEAI_PERMISSION_DENIED.</returns>
        public static string Check(string toolId, string toolName)
        {
            if (!GoogleAuthManager.IsAuthEnabled()) return null;
            string token = GoogleAuthManager.CurrentUserAccessToken;
            if (string.IsNullOrEmpty(token))
                return "Not signed in. Sign in once from the RTS ribbon in Revit (Google 登入), then retry.";
            try
            {
                WhiteListCheckResult check = Task.Run(async () => await GoogleAuthManager.CheckEmailInWhiteListAsync(token, toolId).ConfigureAwait(false)).GetAwaiter().GetResult();
                return check != null && check.IsAllowed ? null : "This account is not authorized for " + toolName + ": " + (check?.ErrorMessage ?? "unknown");
            }
            catch (Exception ex)
            {
                return "Authorization check failed: " + ex.Message;
            }
        }
    }
}
