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

使用者已回報在 Revit 2024 測試數個功能正常，未提供逐項按鈕或授權結果。測試後已執行
`stop-test.ps1`，`.addin` 清單已還原為原本 D: 工作區的 Debug DLL，Revit 已關閉。

## 手動驗收步驟

1. 開啟 Revit 2024，使用**測試模型或模型副本**。確認沒有外掛載入失敗訊息，且 RTS 頁籤與原有分類仍在。
2. 在 `RTS` 頁籤右側尋找共用的 `AI Agents` 面板。確認「範例 Agent」與「釋疑簡報」各只有一顆下拉大按鈕；展開後的小工具與各自 `module.json` 的 `Ribbon.tools` 一致，且原有按鈕名稱、順序及 BIM MCP 入口仍在。
3. 展開「範例 Agent」，分別點「開啟範例工具」與「檢查範例狀態」。未授權時兩項都應先走原授權流程且不能執行；若授權後端已核准 `DT_ExampleAgent`，兩項應顯示各自的範例對話框。未核准此 ToolId 時，成功路徑**無法驗收**；不要改動正式憑證來略過授權。
4. 測試一個已知可用的原有命令及原有 BEAI 唯讀查詢，確認未受新模組影響。模型修改類命令只在測試模型執行。
5. 關閉 Revit。執行 `& .\module-platform\local-test\stop-test.ps1` 還原原載入清單。再次開啟 Revit，確認原版本正常載入。

若須重新開始測試，先關閉 Revit 並還原，再執行 `& .\module-platform\local-test\start-test.ps1`。

## 尚未通過或未執行

- 使用者回報數個 Revit 功能正常；具體按鈕、範例模組授權成功路徑及完整既有功能回歸未留有逐項證據。
- Release 解決方案建置成功（0 錯誤、294 警告）。`-NoPublish` 候選 ZIP 中六個自有 DLL 各出現一次，SHA256 均與混淆輸出一致；Inno 安裝程式編譯成功並包含 ExampleAgent.dll。
- 2026-10-20：已在隔離目錄重現 3.2.7 portable 安裝缺少 `System.Memory.dll` 時，Updater 於解析 pending JSON 前以 exit code 1 失敗。
- 修正後的 `3.2.8 -NoPublish` 候選包已完成實際檔案升級測試：ZIP SHA-256、App 安裝、Updater 自我更新、App/Updater 雙備份、版本更新及暫存清理全部通過。
- 已用有效 SHA-256 但故意損壞 `version.json` 的候選包觸發套用失敗；Updater 回傳 exit code 5，App 與 Updater 均成功回復，pending 檔保留供診斷。
- 上述 Updater 測試使用隔離檔案目錄，未覆寫正式 `%LOCALAPPDATA%\DevelopmentTools`；`-NoPublish` 未推送、標記或建立正式 Release，生成的 `update_manifest.json` 已還原。
- 2026-10-20：可熱替換 Agent Host 的 Debug 與 Release 建置均為 0 錯誤；10 項 Python 驗證／安裝／回退測試通過。UTF-8 stdin/stdout Worker 協議已獨立執行成功。
- `3.2.8 -NoPublish` 候選 ZIP 與 Inno Installer 建置成功；ZIP 含版本化 `Agents/`、核准 catalog 與範例 Worker，Worker SHA-256 與 catalog 相符。未建立 tag、推送 Release 或修改正式憑證。
- Agent 中心按鈕、ToolId 授權、Revit 開啟期間安裝新版、重新載入、雜湊拒絕、逾時隔離及實機回退仍待 Revit 2024 人工驗收。
- Revit 2025／2026 未驗證。
- 候選分支以遠端 `main` 為基礎；本機原工作區及已安裝 3.2.6 可能含未提交修改。此候選版的舊功能與已安裝版不能直接視為完全相同。
