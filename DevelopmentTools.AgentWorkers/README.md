# DevelopmentTools.AgentWorkers

此目錄只放可熱替換的獨立 AI／規則／資料分析 Worker。開始前必須完整閱讀 [`docs/HOT_SWAPPABLE_AGENT_ARCHITECTURE.zh-TW.md`](../docs/HOT_SWAPPABLE_AGENT_ARCHITECTURE.zh-TW.md)。

每位同事只新增自己的 `DevelopmentTools.AgentWorkers/<id>/`，內容至少包含一個 net48 Exe `.csproj`、`Program.cs`、`agent.worker.json` 與 README。Worker 不得引用 RevitAPI／RevitAPIUI，不得建立 Bridge、授權、Updater 或 Transaction。Revit 模型操作仍須走現有 Reality Bridge、已註冊結構化工具與 ExternalEvent。

模板位於 `module-platform/agent-worker-template/`；可執行範例位於 `example-hot-agent/`。
