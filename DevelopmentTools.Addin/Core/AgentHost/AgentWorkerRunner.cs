using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Core.AgentHost
{
    internal sealed class AgentWorkerResponse
    {
        [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
    }

    internal sealed class AgentWorkerRequest
    {
        [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; set; }
        [JsonPropertyName("requestId")] public string RequestId { get; set; }
        [JsonPropertyName("agentId")] public string AgentId { get; set; }
        [JsonPropertyName("agentVersion")] public string AgentVersion { get; set; }
        [JsonPropertyName("revitVersion")] public string RevitVersion { get; set; }
        [JsonPropertyName("documentTitle")] public string DocumentTitle { get; set; }
        [JsonPropertyName("documentIsWorkshared")] public bool DocumentIsWorkshared { get; set; }
        [JsonPropertyName("selectedElementUniqueIds")] public List<string> SelectedElementUniqueIds { get; set; }
    }

    internal static class AgentWorkerRunner
    {
        private const int MaxOutputCharacters = 1024 * 1024;

        public static AgentWorkerRequest CaptureRequest(ApprovedAgent agent, ExternalCommandData commandData)
        {
            if (agent == null || commandData == null) throw new ArgumentNullException();
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document document = uidoc == null ? null : uidoc.Document;
            var selected = new List<string>();
            if (uidoc != null && document != null)
            {
                foreach (ElementId id in uidoc.Selection.GetElementIds())
                {
                    Element element = document.GetElement(id);
                    if (element != null && selected.Count < 200) selected.Add(element.UniqueId);
                }
            }
            return new AgentWorkerRequest
            {
                ProtocolVersion = 1,
                RequestId = Guid.NewGuid().ToString("N"),
                AgentId = agent.Id,
                AgentVersion = agent.Version.ToString(),
                RevitVersion = uiapp.Application.VersionNumber,
                DocumentTitle = document == null ? null : document.Title,
                DocumentIsWorkshared = document != null && document.IsWorkshared,
                SelectedElementUniqueIds = selected
            };
        }

        public static AgentWorkerResponse Run(ApprovedAgent agent, AgentWorkerRequest request)
        {
            if (agent == null || request == null) throw new ArgumentNullException();
            var start = new ProcessStartInfo
            {
                FileName = agent.ExecutablePath,
                WorkingDirectory = agent.PackageDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            // Do not leak Revit/user process secrets to a Worker. Approved Workers receive only a minimal OS environment.
            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            string temporary = Path.GetTempPath();
            start.EnvironmentVariables.Clear();
            if (!string.IsNullOrWhiteSpace(systemRoot)) start.EnvironmentVariables["SystemRoot"] = systemRoot;
            start.EnvironmentVariables["TEMP"] = temporary;
            start.EnvironmentVariables["TMP"] = temporary;
            start.EnvironmentVariables["BEAI_AGENT_PROTOCOL_VERSION"] = "1";
            start.StandardOutputEncoding = new UTF8Encoding(false);
            start.StandardErrorEncoding = new UTF8Encoding(false);
            using (var process = new Process { StartInfo = start })
            {
                if (!process.Start()) throw new InvalidOperationException("無法啟動 Agent Worker。");
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                process.StandardInput.Write(JsonSerializer.Serialize(request));
                process.StandardInput.Close();
                if (!process.WaitForExit(agent.TimeoutSeconds * 1000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("Agent Worker 執行逾時。");
                }
                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();
                if (output.Length > MaxOutputCharacters || error.Length > MaxOutputCharacters)
                    throw new InvalidDataException("Agent Worker 輸出超過限制。");
                if (process.ExitCode != 0) throw new InvalidOperationException("Agent Worker 失敗：" + error);
                var response = JsonSerializer.Deserialize<AgentWorkerResponse>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (response == null || response.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(response.Status))
                    throw new InvalidDataException("Agent Worker 回應格式無效。");
                if (!string.Equals(response.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(response.Message ?? "Agent Worker 未成功完成。");
                return response;
            }
        }
    }
}
