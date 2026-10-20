using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DevelopmentTools.Core.AgentHost
{
    internal static class AgentAuditLog
    {
        private static readonly object Gate = new object();

        public static void Write(ApprovedAgent agent, string status, long elapsedMilliseconds, string detail)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevelopmentTools", "Logs");
                Directory.CreateDirectory(directory);
                string line = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    { "timestampUtc", DateTime.UtcNow.ToString("o") },
                    { "agentId", agent == null ? null : agent.Id },
                    { "agentVersion", agent == null ? null : agent.Version.ToString() },
                    { "toolId", agent == null ? null : agent.ToolId },
                    { "status", status },
                    { "elapsedMilliseconds", elapsedMilliseconds },
                    { "detail", detail }
                });
                lock (Gate) File.AppendAllText(Path.Combine(directory, "agent-host.jsonl"), line + Environment.NewLine);
            }
            catch { }
        }
    }
}
