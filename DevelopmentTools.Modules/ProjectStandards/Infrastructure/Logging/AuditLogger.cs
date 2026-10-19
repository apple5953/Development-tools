using System;
using System.Text;

namespace DevelopmentTools.ProjectStandards.Infrastructure.Logging
{
    public class AuditLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string User { get; set; }
        public string MachineName { get; set; }
        public string PluginVersion { get; set; }
        public string RevitVersion { get; set; }
        public string MasterModel { get; set; }
        public string MasterVersion { get; set; }
        public string TargetModel { get; set; }
        public string TargetVersionBefore { get; set; }
        public string TargetVersionAfter { get; set; }
        public int TotalOperations { get; set; }
        public int Warnings { get; set; }
        public int Errors { get; set; }
        public string Result { get; set; }

        public AuditLogEntry()
        {
            Timestamp = DateTime.UtcNow;
            User = Environment.UserName;
            MachineName = Environment.MachineName;
            PluginVersion = "1.0.0";
            RevitVersion = "2024";
        }
    }

    public static class AuditLogger
    {
        public static void LogScan(string model, string source, string version)
        {
            Console.WriteLine(string.Format("[AUDIT SCAN] model={0} source={1} version={2}", model, source, version));
        }

        public static void LogDiff(string category, int equal, int modified, int missing)
        {
            Console.WriteLine(string.Format("[AUDIT DIFF] category={0} equal={1} modified={2} missing={3}", category, equal, modified, missing));
        }

        public static void LogSync(string operation, string name, string result)
        {
            Console.WriteLine(string.Format("[AUDIT SYNC] operation={0} name={1} result={2}", operation, name, result));
        }

        public static void LogCloudVersionCheck(string expectedVersion, string actualVersion)
        {
            Console.WriteLine(string.Format("[AUDIT CLOUD] expectedVersion={0} actualVersion={1}", expectedVersion, actualVersion));
        }
    }
}
