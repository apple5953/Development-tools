# Codex / Claude 模組開發

1. 從 `module-platform/template` 複製到 `DevelopmentTools.Modules/<id>`；`id` 使用小寫英文字、數字與連字號。自己的 Agent 至少須包含一個 `.csproj`、`module.json` 及實作 `IExternalCommand` 的 C# 程式。
2. 修改 `module.json`、專案 `AssemblyName` 與命令類別。`Ribbon.name`、每個 `Ribbon.tools[].name`、`Ribbon.text` 與 Agent `ToolId` 都必須唯一。建置會在 `RTS › AI Agents` 共用面板建立一顆以 `Ribbon.text` 命名的下拉大按鈕，`Ribbon.tools` 是按鈕內的小工具；`Ribbon.category` 只控制 `drawing`、`model`、`review` 的排序。`permissions` 是審查宣告，不能直接取得 Reality Bridge 權限。完整契約見 `docs/AI_AGENT_RIBBON_CONTRACT.zh-TW.md`。
3. 建立自己的 feature 分支，**只提交 `DevelopmentTools.Modules/<id>/` 資料夾**並開 PR 到 `main`。可用 GitHub 網頁的「Add file → Upload files」上傳整個資料夾，再選「Create a new branch」和「Propose changes」；也可用 Codex／Claude Code 在本機開分支與 PR。不要直接提交到 `main`。
4. PR 會在一次性 GitHub runner 檢查描述檔、名稱衝突、依賴及產生 Ribbon 註冊碼；不執行模組 DLL。若失敗，修改自己資料夾並更新 PR。本機可預先執行 `python module-platform/validate.py`。
5. PR 審核須檢查命令是否經 `CommandAccessGuard`、是否使用 Revit UI 執行緒、是否企圖增加外部 AI 工具或未授權 Transaction。外部 AI 工具只能另行審查後加入現有 Reality Bridge 的明確註冊清單。
6. 在 Revit 2024 實機檢查按鈕、授權、命令、更新與回退。2025/2026 在具備環境前標記未驗證。

## 自動建置待決定

PR 中的模組程式碼不可直接在共享的自架 Revit runner 執行。現在的 PR workflow 只做描述檔與產生碼檢查。可部署工件的自動產生仍須隔離 Windows runner、最小權限與無正式憑證、受控 Revit API 授權安裝、工件輸出審查，以及 fork PR 的隔離策略。正式發布仍由既有流程控制，且不得在 PR 自動執行。
