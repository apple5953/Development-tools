using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DT_WordPdfLink.Utils
{
    public static class PathUtils
    {
        public static string ResolveLocalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Check for "Autodesk Docs" virtual folder path
            string accDocsToken = "\\autodesk docs\\";
            int index = path.ToLowerInvariant().IndexOf(accDocsToken);
            if (index != -1)
            {
                string relativePart = path.Substring(index + accDocsToken.Length);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(userProfile, "Autodesk Docs", relativePart);
            }

            // Check for legacy "ACCDocs" virtual folder path
            string legacyToken = "\\accdocs\\";
            index = path.ToLowerInvariant().IndexOf(legacyToken);
            if (index != -1)
            {
                string relativePart = path.Substring(index + legacyToken.Length);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(userProfile, "ACCDocs", relativePart);
            }

            return path;
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string resolved = ResolveLocalPath(path);
            try
            {
                // Check if it is a cloud path (e.g. CLD:\\ or contains ://)
                if (resolved.StartsWith("CLD:\\", StringComparison.OrdinalIgnoreCase) || resolved.Contains("://"))
                {
                    return resolved.Trim().ToLowerInvariant();
                }
                return Path.GetFullPath(resolved).Trim().ToLowerInvariant();
            }
            catch (Exception)
            {
                return resolved.Trim().ToLowerInvariant();
            }
        }

        public static string GetPathHash(string path)
        {
            if (string.IsNullOrEmpty(path)) return "empty";
            string normalized = NormalizePath(path);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                StringBuilder sb = new StringBuilder();
                // Take first 8 bytes for a clean short hash
                for (int i = 0; i < Math.Min(bytes.Length, 8); i++)
                {
                    sb.Append(bytes[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }
    }
}
