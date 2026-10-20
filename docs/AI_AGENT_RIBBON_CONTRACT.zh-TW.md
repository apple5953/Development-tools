# AI Agent Ribbon 契約（給 Codex／Claude Code 直接執行）

## 目標版面

```text
RTS
└─ AI Agents
   ├─ Agent A ▼
   │  ├─ 工具 A1
   │  └─ 工具 A2
   └─ Agent B ▼
      ├─ 工具 B1
      └─ 工具 B2
```

一個 `DevelopmentTools.Modules/<id>/` 資料夾代表一個 AI Agent。一個 Agent 只能佔一顆 Ribbon 下拉大按鈕；其功能全部宣告在 `module.json` 的 `Ribbon.tools`。禁止模組自行實作 `IExternalApplication`、建立 Ribbon 頁籤或面板。

## AI 必須遵守的實作順序

1. 讀根目錄 `AGENTS.md`、`CLAUDE.md` 與本文件。
2. 從最新 `main` 建立自己的 feature 分支，不直接修改 `main`。
3. 複製 `module-platform/template/` 到 `DevelopmentTools.Modules/<id>/`。
4. 一個 Agent 設定一個全域唯一 `ToolId`。同一 Agent 的所有小工具共用此授權。
5. 每個 `Ribbon.tools[]` 建立一個公開、可無參數建構的 `IExternalCommand` 類別。
6. 不修改 `App.cs`、`RibbonCatalog.cs`、`DevelopmentTools.Addin.csproj`、`.sln` 或生成檔。
7. 外部 AI 需要 Revit API 時，只能擴充既有 Reality Bridge 的註冊工具並經 `ExternalEvent`；不得建立第二套 Bridge。Reality Bridge V0.1 不得開啟 Transaction。
8. 執行驗證、單元測試與合法 Revit 2024 環境建置，建立 PR，等待檢查及維護者核准。不得建立正式 Release。

## `module.json` 範本

```json
{
  "id": "my-agent",
  "version": "0.1.0",
  "hostVersion": "3.2.10",
  "RevitVersion": ["2024"],
  "assembly": "MyAgent.dll",
  "entryClass": "MyAgent.Commands.OpenCommand",
  "ToolId": "DT_MyAgent",
  "Ribbon": {
    "name": "MyAgent",
    "text": "我的 Agent",
    "category": "review",
    "icon": "AI",
    "color": "#2878A8",
    "tools": [
      {
        "name": "MyAgentAnalyze",
        "text": "執行分析",
        "entryClass": "MyAgent.Commands.AnalyzeCommand",
        "tooltip": "分析目前模型"
      },
      {
        "name": "MyAgentReport",
        "text": "開啟報告",
        "entryClass": "MyAgent.Commands.ReportCommand",
        "tooltip": "開啟最近一次分析報告"
      }
    ]
  },
  "permissions": ["ribbon-command"],
  "dependencies": [],
  "tests": [
    "Revit 2024：Agent 只有一顆下拉大按鈕，內含兩個小工具",
    "未授權 DT_MyAgent 時兩個小工具都被阻止",
    "核准 DT_MyAgent 後兩個小工具都可執行"
  ]
}
```

## 命名及授權規則

- `id`：小寫 kebab-case，等於資料夾名稱。
- `Ribbon.name`：Agent 大按鈕的內部名稱，全儲存庫唯一。
- `Ribbon.text`：使用者看到的 Agent 名稱。
- `Ribbon.tools[].name`：小工具內部名稱，全儲存庫唯一；建議使用 `<Agent><動作>`。
- `Ribbon.tools[].entryClass`：小工具的完整 C# 類別名稱。
- `ToolId`：Agent 級授權 ID。不得為每個小工具另建登入、憑證或授權流程。
- `category`：只控制 Agent 排序，依 `drawing`、`model`、`review`，同類再依 `id` 排序。

## 自動產生內容

`module-platform/generate.py` 會產生：

- 共用 `AI Agents` Ribbon 面板。
- 每個 Agent 的一顆 `PulldownButton`。
- 每個 `Ribbon.tools[]` 的小按鈕。
- 每個小工具的授權包裝命令；先呼叫 `CommandAccessGuard.EnsureAuthorized(Agent ToolId, Agent/工具名稱)`，通過後才呼叫模組命令。
- 主外掛的自動 ProjectReference、混淆與打包清單沿用既有流程。

同事不得手動提交 `DevelopmentTools.Addin/GeneratedModules/GeneratedModuleCommands.cs` 的修改。

## 驗證命令

```powershell
python module-platform/validate.py
python -m unittest discover -s module-platform -p "test_*.py" -v
dotnet build DevelopmentTools.sln -c Debug -clp:ErrorsOnly
```

Revit 2025／2026 沒有實際環境時必須寫「未驗證」。CI 通過不等於 Revit 人工驗收通過，也不等於正式 Release 已發布。

## 可直接交給開發 AI 的提示詞

```text
先讀 AGENTS.md、CLAUDE.md、docs/AI_AGENT_RIBBON_CONTRACT.zh-TW.md、
docs/AGENT_MODULE_DEVELOPER_GUIDE.zh-TW.md 與 module-platform/template/。
從最新 main 建立 feature 分支，只修改 DevelopmentTools.Modules/<agent-id>/。
這個資料夾代表一個 Agent；Ribbon 只能有一顆下拉大按鈕，所有小工具逐項宣告在
module.json 的 Ribbon.tools，每個小工具建立一個 IExternalCommand。整個 Agent
共用一個 ToolId，授權由生成的 CommandAccessGuard 包裝命令處理。
不得修改 App.cs、RibbonCatalog.cs、主 csproj、sln 或生成檔；不得建立第二套
Bridge、MCP、授權、Updater 或正式 Release。外部 AI 的 Revit API 呼叫只能走
既有 Reality Bridge、結構化工具與 ExternalEvent；V0.1 不得開 Transaction。
完成 validate、單元測試、合法 Revit 2024 建置與人工驗收紀錄後建立 PR。
```
