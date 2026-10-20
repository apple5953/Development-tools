# Development Tools 更新與 3.2.7 修復說明

## 3.2.7 無法更新的原因

使用 ZIP 內 `install.bat` 安裝 3.2.7 或更早版本時，安裝腳本會先把
Updater 的相依 DLL 複製到 `%LOCALAPPDATA%\DevelopmentTools\Updater`，接著又刪除：

- `System.Buffers.dll`
- `System.Memory.dll`
- `System.Numerics.Vectors.dll`
- `System.Runtime.CompilerServices.Unsafe.dll`

`DevelopmentTools.Updater.exe` 使用 `System.Text.Json` 讀取
`pending_update.json`，而 `System.Text.Json` 需要上述檔案。缺少檔案時，
Updater 會在套用更新前結束，典型錯誤為：

```text
無法載入檔案或組件 'System.Memory, Version=4.0.1.2'
```

因此使用者仍能看到 3.2.7 更新資訊，也能下載通過 SHA-256 驗證的 ZIP，
但關閉 Revit 後不會完成安裝。

## 受影響版本的一次性修復

已用舊 `install.bat` 安裝的人，必須先執行一次包含本修正的新版安裝器。
舊版 Updater 本身缺少啟動相依檔，因此無法用同一個故障的 Updater 修復自己。

1. 關閉所有 Revit 視窗。
2. 下載經主管核准的新版本 `DevelopmentTools_Setup.exe`，或下載完整 ZIP、解壓縮後執行其中的 `install.bat`。
3. 重新開啟 Revit 2024。
4. 執行「檢查更新」，確認本機版號不再重複提示。
5. 若仍失敗，提供以下檔案給維護者：
   `%LOCALAPPDATA%\DevelopmentTools\Update\Logs\update_log.txt`。

請勿只複製單一 `DevelopmentTools.Addin.dll`。新版安裝器會一起修復 Updater
及其私有相依檔。

## 修正後的更新流程

1. Revit 外掛讀取本機 `version.json` 與遠端 `update_manifest.json`。
2. 下載 Release ZIP，最多重試三次，單次最長五分鐘。
3. 用 manifest 的 SHA-256 驗證整個 ZIP。
4. 從已驗證 ZIP 的 `UpdaterPayload/` 解出獨立 Updater 到
   `%LOCALAPPDATA%\DevelopmentTools\Update\UpdaterStaging`。
5. 產生 `pending_update.json`，記錄本次 Revit process ID、App、Updater、Backup 路徑。
6. 啟動 staged Updater，使用者關閉發起更新的 Revit；最長等待十五分鐘。
7. Updater 再驗證 ZIP，備份目前 App 與 Updater。
8. 安裝 App，並用 `UpdaterPayload/` 更新正式 Updater。
9. 成功後刪除 ZIP 與 pending 檔；失敗時回復 App 與 Updater。

整個流程不呼叫 Revit API、不開啟 `Transaction`，也不新增第二套 Reality Bridge。

## 維護者驗證清單

- Release ZIP SHA-256 與 `update_manifest.json` 一致。
- ZIP 根目錄包含 `DevelopmentTools.Addin.dll`、`version.json`、`install.bat`。
- ZIP 的 `UpdaterPayload/` 包含 Updater EXE、config、`System.Text.Json.dll` 與其相依 DLL。
- ZIP 安裝後 `%LOCALAPPDATA%\DevelopmentTools\Updater` 仍保留上述四個相依 DLL。
- 更新成功後 App 與 Updater 的備份都存在於 `%LOCALAPPDATA%\DevelopmentTools\Update\Backup`。
- Revit 2024 實機驗證通過；Revit 2025/2026 仍未驗證且目前安裝器不註冊。
