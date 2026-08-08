using Autodesk.Revit.UI;
using System.Threading.Tasks;

namespace DevelopmentTools.Core
{
    public static class CommandAccessGuard
    {
        public static bool EnsureAuthorized(string toolId, string toolName)
        {
            if (!GoogleAuthManager.IsAuthEnabled())
            {
                return true;
            }

            return GoogleAuthManager.VerifyAccess(toolId, toolName);
        }

        public static Task<bool> EnsureAuthorizedAsync(string toolId, string toolName)
        {
            if (!GoogleAuthManager.IsAuthEnabled())
            {
                return Task.FromResult(true);
            }

            return GoogleAuthManager.VerifyAccessAsync(toolId, toolName);
        }

        public static Result CancelIfUnauthorized(string toolId, string toolName)
        {
            return EnsureAuthorized(toolId, toolName)
                ? Result.Succeeded
                : Result.Cancelled;
        }
    }
}
