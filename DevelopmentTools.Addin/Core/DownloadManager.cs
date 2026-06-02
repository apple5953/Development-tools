using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DevelopmentTools.Core
{
    public static class DownloadManager
    {
        private static readonly HttpClient HttpClientInstance = new HttpClient();

        static DownloadManager()
        {
            // 強制啟用 TLS 1.2 與 TLS 1.3 (3072)，解決部分 Windows Client 未啟用 TLS 連線 GitHub 握手失敗的經典問題
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= 
                    System.Net.SecurityProtocolType.Tls12 | 
                    (System.Net.SecurityProtocolType)3072;
            }
            catch { }

            HttpClientInstance.Timeout = TimeSpan.FromSeconds(30);
            HttpClientInstance.DefaultRequestHeaders.UserAgent.ParseAdd("RevitUpdater/1.0");
        }

        public static async Task<string> DownloadJsonAsync(string url)
        {
            try
            {
                return await HttpClientInstance.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                UpdateLogger.Log($"下載 JSON 失敗 URL: {url}", ex);
                return null;
            }
        }

        public static async Task<bool> DownloadFileAsync(string url, string destinationPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(destinationPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var response = await HttpClientInstance.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var streamToReadFrom = await response.Content.ReadAsStreamAsync())
                    {
                        using (var streamToWriteTo = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await streamToReadFrom.CopyToAsync(streamToWriteTo);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                UpdateLogger.Log($"下載檔案失敗 URL: {url} 到 {destinationPath}", ex);
                return false;
            }
        }

        public static bool VerifySha256(string filePath, string expectedHash)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                using (var sha256 = SHA256.Create())
                {
                    using (var fileStream = File.OpenRead(filePath))
                    {
                        byte[] hashBytes = sha256.ComputeHash(fileStream);
                        string computedHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                        return computedHash.Equals(expectedHash.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateLogger.Log($"驗證雜湊值異常，檔案: {filePath}", ex);
                return false;
            }
        }
    }
}
