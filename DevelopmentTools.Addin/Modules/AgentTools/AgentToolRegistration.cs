using System;
using DevelopmentTools.CodeCheck;
using DevelopmentTools.FastRebar;

namespace DevelopmentTools.AgentTools
{
    /// <summary>
    /// 所有外掛 agent 工具集中在這裡註冊到既有 Reality Bridge。新增工具時加一行，並同步
    /// BEAI/MCP（contracts.ts、gateway.ts、server.ts）與 scripts/validate-beai-mcp.py。
    /// 單一工具註冊失敗只記 log，不影響其他工具與外掛。
    /// </summary>
    public static class AgentToolRegistration
    {
        public static void RegisterAll(Action<string> log)
        {
            Try(CodeCheckAgentTool.Register, CodeCheckAgentTool.ToolName, log);
            Try(RebarCheckAgentTool.Register, RebarCheckAgentTool.ToolName, log);
            Try(ProjectStandardsAgentTool.Register, ProjectStandardsAgentTool.ToolName, log);
            Try(ParameterCompareAgentTool.Register, ParameterCompareAgentTool.ToolName, log);
        }

        private static void Try(Action register, string name, Action<string> log)
        {
            try { register(); }
            catch (Exception ex) { log?.Invoke("AI agent 工具註冊失敗（" + name + "）：" + ex.Message); }
        }
    }
}
