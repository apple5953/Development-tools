using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevelopmentTools.Core.AgentHost
{
    internal sealed class AgentCatalogDocument
    {
        [JsonPropertyName("catalogVersion")] public int CatalogVersion { get; set; }
        [JsonPropertyName("agents")] public List<ApprovedAgentRecord> Agents { get; set; }
    }

    internal sealed class ApprovedAgentRecord
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("version")] public string VersionText { get; set; }
        [JsonPropertyName("hostVersion")] public string HostVersionText { get; set; }
        [JsonPropertyName("revitVersions")] public List<string> RevitVersions { get; set; }
        [JsonPropertyName("executable")] public string Executable { get; set; }
        [JsonPropertyName("toolId")] public string ToolId { get; set; }
        [JsonPropertyName("displayName")] public string DisplayName { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("permissions")] public List<string> Permissions { get; set; }
        [JsonPropertyName("timeoutSeconds")] public int TimeoutSeconds { get; set; }
        [JsonPropertyName("packagePath")] public string PackagePath { get; set; }
        [JsonPropertyName("files")] public List<ApprovedAgentFile> Files { get; set; }
        [JsonPropertyName("active")] public bool Active { get; set; }
    }

    internal sealed class ApprovedAgentFile
    {
        [JsonPropertyName("path")] public string Path { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; }
    }

    internal sealed class ApprovedAgent
    {
        public string Id { get; set; }
        public Version Version { get; set; }
        public string ToolId { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string PackageDirectory { get; set; }
        public string ExecutablePath { get; set; }
        public int TimeoutSeconds { get; set; }
        public override string ToString() => DisplayName + "  v" + Version;
    }

    internal static class ApprovedAgentCatalog
    {
        public static IReadOnlyList<ApprovedAgent> Load(string root, string revitVersion, Version hostVersion)
        {
            string canonicalRoot = CanonicalDirectory(root);
            string catalogPath = Path.Combine(canonicalRoot, "approved-agents.json");
            if (!File.Exists(catalogPath)) return new ApprovedAgent[0];
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var catalog = JsonSerializer.Deserialize<AgentCatalogDocument>(File.ReadAllText(catalogPath), options);
            if (catalog == null || catalog.CatalogVersion != 1 || catalog.Agents == null)
                throw new InvalidDataException("不支援的 Agent 核准目錄格式。");

            var result = new List<ApprovedAgent>();
            var activeTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in catalog.Agents.Where(a => a != null && a.Active))
            {
                Version version, minimumHost;
                if (!Version.TryParse(record.VersionText, out version) || !Version.TryParse(record.HostVersionText, out minimumHost))
                    throw new InvalidDataException("Agent 版本格式錯誤：" + record.Id);
                if (minimumHost > hostVersion || record.RevitVersions == null || !record.RevitVersions.Contains(revitVersion)) continue;
                if (string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.ToolId) ||
                    string.IsNullOrWhiteSpace(record.Executable) || Path.GetFileName(record.Executable) != record.Executable ||
                    !record.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Agent 識別或執行檔格式錯誤。");
                if (record.Permissions == null || record.Permissions.Count != 1 || record.Permissions[0] != "document-context-read")
                    throw new InvalidDataException("Agent 要求未核准權限：" + record.Id);
                if (!activeTools.Add(record.ToolId)) throw new InvalidDataException("Agent ToolId 重複：" + record.ToolId);
                string package = CanonicalChild(canonicalRoot, record.PackagePath);
                VerifyFiles(package, record.Files);
                string executable = CanonicalChild(package, record.Executable);
                if (!File.Exists(executable)) throw new FileNotFoundException("Agent 執行檔不存在。", executable);
                result.Add(new ApprovedAgent
                {
                    Id = record.Id, Version = version, ToolId = record.ToolId,
                    DisplayName = record.DisplayName, Description = record.Description,
                    PackageDirectory = package, ExecutablePath = executable,
                    TimeoutSeconds = Math.Max(1, Math.Min(120, record.TimeoutSeconds))
                });
            }
            return result.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(a => a.Version).First())
                .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void VerifyFiles(string package, IList<ApprovedAgentFile> files)
        {
            if (files == null || files.Count == 0) throw new InvalidDataException("Agent 缺少核准雜湊清單。");
            var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.Path) || Path.GetFileName(file.Path) != file.Path ||
                    string.IsNullOrWhiteSpace(file.Sha256) || file.Sha256.Length != 64 || !approved.Add(file.Path))
                    throw new InvalidDataException("Agent 核准檔案清單無效。");
                string path = CanonicalChild(package, file.Path);
                if (!File.Exists(path) || !Hash(path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Agent 檔案雜湊不符：" + file.Path);
            }
            foreach (string actual in Directory.GetFiles(package, "*", SearchOption.TopDirectoryOnly))
                if (!approved.Contains(Path.GetFileName(actual))) throw new InvalidDataException("Agent 套件含未核准檔案：" + Path.GetFileName(actual));
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static string CanonicalDirectory(string path)
        {
            string full = Path.GetFullPath(path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(full)) return full;
            return full;
        }

        private static string CanonicalChild(string parent, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Agent 路徑無效。");
            string full = Path.GetFullPath(Path.Combine(parent, relative.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Agent 路徑超出核准目錄。");
            return full;
        }
    }
}
