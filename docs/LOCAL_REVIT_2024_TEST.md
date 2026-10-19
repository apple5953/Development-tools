# Revit 2024 本機測試紀錄與操作

測試日期：2026-10-19。測試分支：`codex/module-platform`。這是 Debug 候選版，不是正式 Release。

## 已完成的自動檢查

- 已使用本機 Revit 2024 API 與官方 net48 參考組件建置整個 `DevelopmentTools.sln`：0 錯誤、293 警告。
- 警告包含既有 `System.Text.Json` 8.0.3 的 NuGet 安全性警示；本次尚未升級該相依套件。
- 已建置獨立 `host-poc`：0 錯誤、0 警告。
- `module-platform` 的 5 項驗證器測試通過；範例模組描述檔驗證通過。
- Debug 輸出含 `DevelopmentTools.Addin.dll`、`ExampleAgent.dll`、原有四個模組 DLL、`BEAI.QueryHost.dll`、Resources 與 Runtime Worker。
- 已測試 Revit `.addin` 清單切換及還原，再切回測試版；正式 `%LOCALAPPDATA%\DevelopmentTools\App` 未覆寫。

## 目前本機載入狀態

`%APPDATA%\Autodesk\Revit\Addins\2024\DevelopmentTools.addin` 目前指向本工作樹的
`DevelopmentTools.Addin\bin\Debug\net48\DevelopmentTools.Addin.dll`。原清單備份在
`dist\local-test\DevelopmentTools.addin.original`。保持工作樹位置不變直到測試完成。

## 手動驗收步驟

1. 開啟 Revit 2024，使用**測試模型或模型副本**。確認沒有外掛載入失敗訊息，且 RTS 頁籤與原有分類仍在。
2. 在「檢討與管理」尋找「範例模組」。確認原有按鈕名稱、順序及 BIM MCP 入口仍在。
3. 點「範例模組」。未授權時應先走原授權流程，且不能執行命令。若授權後端已核准 `DT_ExampleAgent`，應出現 `Template command is installed.` 對話框。未核准此 ToolId 時，命令成功路徑**無法驗收**；不要改動正式憑證來略過授權。
4. 測試一個已知可用的原有命令及原有 BEAI 唯讀查詢，確認未受新模組影響。模型修改類命令只在測試模型執行。
5. 關閉 Revit。執行 `& .\module-platform\local-test\stop-test.ps1` 還原原載入清單。再次開啟 Revit，確認原版本正常載入。

若須重新開始測試，先關閉 Revit 並還原，再執行 `& .\module-platform\local-test\start-test.ps1`。

## 尚未通過或未執行

- Revit UI 的實際按鈕、授權成功路徑、命令執行與既有功能回歸，須由使用者在 Revit 2024 完成上述步驟。
- Release 混淆包、Installer、Updater 升級及整包回退尚未完成本次候選版驗收。
- Revit 2025／2026 未驗證。
- 候選分支以遠端 `main` 為基礎；本機原工作區及已安裝 3.2.6 可能含未提交修改。此候選版的舊功能與已安裝版不能直接視為完全相同。
