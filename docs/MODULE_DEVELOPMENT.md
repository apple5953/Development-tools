# Codex / Claude 模組開發

1. 從 `module-platform/template` 複製到 `DevelopmentTools.Modules/<id>`；`id` 使用小寫英文字、數字與連字號。僅提交自己的模組資料夾。
2. 修改 `module.json`、專案 `AssemblyName` 與實作 `IExternalCommand` 的 `entryClass`。`Ribbon.name` 與 `ToolId` 必須是新的識別碼。`permissions` 是審查宣告，不能直接取得 Reality Bridge 權限。
3. 本機執行 `python module-platform/validate.py` 與 `python module-platform/generate.py`。產生碼僅供本機檢查；合併前需由受控 Windows 環境重新產生並建置。
4. PR 審核須檢查命令是否經 `CommandAccessGuard`、是否使用 Revit UI 執行緒、是否企圖增加外部 AI 工具或未授權 Transaction。外部 AI 工具只能另行審查後加入現有 Reality Bridge 的明確註冊清單。
5. 在 Revit 2024 實機檢查按鈕、授權、命令、更新與回退。2025/2026 在具備環境前標記未驗證。

## 自動建置待決定

PR 中的模組程式碼不可直接在共享的自架 Revit runner 執行。先決條件是一次性隔離 Windows runner、最小權限與無正式憑證、受控 Revit API 授權安裝、工件輸出審查，以及 fork PR 的隔離策略。完成這些控制後才加入 PR workflow。正式發布仍由既有流程控制，且不得在 PR 自動執行。
