using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace DevelopmentTools.PluginHostPoc
{
    // Contract only. This project is deliberately not referenced by the production add-in.
    public interface IPlugin
    {
        string Id { get; }
        Version Version { get; }
        Type RibbonCommandType { get; }
    }

    public interface IAuthorizationGateway
    {
        bool EnsureAuthorized(string toolId, string toolName);
    }

    public sealed class ApprovedModule
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string EntryClass { get; set; }
        public string Sha256 { get; set; }
        public Version MinimumHostVersion { get; set; }
    }

    public sealed class ApprovedPluginHost
    {
        private readonly string _modulesDirectory;
        private readonly IAuthorizationGateway _authorization;

        public ApprovedPluginHost(string modulesDirectory, IAuthorizationGateway authorization)
        {
            if (authorization == null) throw new ArgumentNullException(nameof(authorization));
            _modulesDirectory = Path.GetFullPath(modulesDirectory);
            _authorization = authorization;
        }

        public IPlugin LoadApproved(ApprovedModule approved, Version hostVersion, string toolId, string toolName)
        {
            if (approved == null || hostVersion == null) throw new ArgumentNullException();
            if (!_authorization.EnsureAuthorized(toolId, toolName)) throw new UnauthorizedAccessException("Module authorization denied.");
            if (approved.MinimumHostVersion == null || approved.MinimumHostVersion.CompareTo(hostVersion) > 0)
                throw new InvalidOperationException("Incompatible host version.");
            if (string.IsNullOrWhiteSpace(approved.FileName) || Path.GetFileName(approved.FileName) != approved.FileName ||
                !approved.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Module file name must be a DLL basename.");
            if (string.IsNullOrWhiteSpace(approved.Sha256) || approved.Sha256.Length != 64)
                throw new InvalidDataException("Approved SHA-256 is required.");
            var path = Path.GetFullPath(Path.Combine(_modulesDirectory, approved.FileName));
            if (!path.StartsWith(_modulesDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Module path escapes approved directory.");
            if (!File.Exists(path)) throw new FileNotFoundException("Approved module missing.", path);
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                if (!actual.Equals(approved.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Module hash does not match approved catalog.");
            }
            // The catalog must come from a separately trusted, reviewed release artifact.
            var assembly = Assembly.LoadFrom(path);
            var entry = assembly.GetType(approved.EntryClass, true, false);
            if (!typeof(IPlugin).IsAssignableFrom(entry) || entry.IsAbstract)
                throw new InvalidDataException("Entry class does not implement IPlugin.");
            var plugin = (IPlugin)Activator.CreateInstance(entry);
            if (plugin.Id != approved.Id || plugin.Version == null || plugin.RibbonCommandType == null)
                throw new InvalidDataException("Plugin identity or command contract mismatch.");
            return plugin;
        }
    }
}
