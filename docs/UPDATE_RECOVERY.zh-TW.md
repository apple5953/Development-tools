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

## 3.2.13 下載檔被公司防毒攔截（安裝位置移出 C 槽）

3.2.13 以前，更新包下載（`Update\Staging`）、Updater 解壓（`Update\UpdaterStaging`）、
套件解壓（`%TEMP%\RTS_Update_*`）與安裝目錄全部位於系統碟的
`%LOCALAPPDATA%\DevelopmentTools` 與 `%TEMP%`。公司防毒會攔截寫入 C: 使用者目錄的
下載檔與執行檔，導致更新失敗。

修正後的安裝根目錄解析順序（Add-in `InstallLocation`、Inno Setup、`install.bat` 一致）：

1. 環境變數 `DEVELOPMENTTOOLS_ROOT`（IT 可統一指定，例如 `D:\DevelopmentTools`）。
2. `Config\appsettings.json` 的 `"InstallRoot"`（僅 Add-in 線上更新讀取）。
3. 目前已安裝在非系統碟時沿用原位置。
4. 第一個可寫入、剩餘空間 > 500 MB 的本機固定非系統碟：`X:\DevelopmentTools`。
5. 都不可用時才退回 `%LOCALAPPDATA%\DevelopmentTools`。

線上更新時：

- ZIP 下載到 `<根目錄>\Update\Staging`，Updater 由 `<根目錄>\Update\UpdaterStaging` 啟動。
- 套件解壓到 `<根目錄>\Update\Extract`，不再使用 `%TEMP%`。
- 若舊安裝在 C:，Updater 會在 Revit 關閉後安裝到新根目錄，並改寫
  `%APPDATA%\Autodesk\Revit\Addins\2024\DevelopmentTools.addin` 的 `<Assembly>`。
  舊 C: 目錄不會被刪除或修改（`ai_settings.json`、`LanguageSetting.txt` 仍在原處讀取）；
  失敗時只還原 `.addin` 並移除未完成的新目錄。
- 更新記錄改寫到 `<根目錄>\Update\Logs\update_log.txt`。

**已安裝 3.2.13（或更早）的電腦**：其 Add-in 的舊下載程式仍會把 ZIP 存到 C:，
所以這次必須手動執行一次新版 `DevelopmentTools_Setup.exe`（預設會安裝到 D:），
之後的線上更新才會全程避開 C 槽。Revit 必須能寫入的 `.addin` 註冊檔仍位於
`%APPDATA%\Autodesk\Revit\Addins`，這是 Revit 規定的位置，只是一個 XML 文字檔。
