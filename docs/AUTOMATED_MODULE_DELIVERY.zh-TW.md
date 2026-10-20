# BIM AI Agent 自動建置與更新

## 目的

同事只提交自己的模組資料夾。PR 合併後，受保護的 `main` 由合法 Revit 2024 Windows Runner 自動建立候選安裝包。候選包通過實機驗收並經 `production` 環境核准後，系統建立版本 PR；版本 PR 合併後才建立正式 GitHub Release 並啟用穩定更新。

## 安全邊界

- Self-hosted Runner 只接受 `push` 到受保護 `main` 的候選建置工作，不執行 `pull_request` 程式碼。
- Reality Bridge 仍是唯一 Revit 與外部 AI 邊界；流程不建立第二套 Bridge。
- 模組仍由既有 `CommandAccessGuard` 驗證 ToolId。
- 候選工件不會修改正式更新資訊。
- 正式發布必須先通過 GitHub `production` 環境核准與版本 PR。
- Revit 2025／2026 未經實機驗證，不得標記為已支援。

## 自動流程

### 一、模組 PR

`.github/workflows/module-manifest.yml` 在 GitHub-hosted Runner 驗證 manifest、ToolId、相依與獨立 Worker。公司 Self-hosted Runner 不接觸未合併分支。

### 二、合併後候選包

`.github/workflows/revit-candidate.yml` 在 `main` 的相關檔案變更後執行：

1. 確認 Revit 2024 API 開發檔案存在。
2. 由目前正式版自動增加 patch 版號。
3. 執行 `release/release.ps1 -NoPublish`。
4. 驗證主 DLL、Updater、每個模組 DLL、內嵌版號與 SHA-256。
5. 上傳保存 30 天的候選工件。

候選工件名稱包含版本及來源 commit，例如 `development-tools-candidate-v3.2.9-ea8d849`。

### 三、Revit 2024 驗收

測試人員由候選 workflow 的 Artifacts 下載安裝程式，驗證：

- Ribbon 按鈕位置與名稱。
- ToolId 未授權會被阻止，授權後可執行。
- Revit API 呼叫位於合法 UI／ExternalEvent 流程。
- 既有功能、更新與回退正常。

### 四、準備正式發布

在 Actions 執行 `Prepare production release`，輸入候選 Run ID、候選版本與發布說明。`production` 環境要求主管核准。核准後 workflow：

1. 下載指定候選工件。
2. 驗證版本與 SHA-256。
3. 更新 `update_manifest.json`。
4. 建立不可變的 `release/releases/vX.Y.Z.json` 稽核紀錄。
5. 建立版本 PR。

### 五、正式更新

版本 PR 合併後，`Publish approved production release` 重新下載同一候選工件、再次驗證 SHA-256，建立 GitHub Release。既有更新器隨後由 `update_manifest.json` 看到新版本。

## 回退

1. 回退 `update_manifest.json` 到前一版並以 PR 合併。
2. 將有問題的 GitHub Release 標記為非 Latest 或移除。
3. 使用前一正式版安裝程式重新安裝。
4. 保留 `release/releases/*.json` 作稽核紀錄，不改寫歷史資料。

## Runner 要求

- 專用 Windows x64 電腦。
- 合法 Revit 2024 安裝或合法 Revit 2024 API 開發環境。
- Runner 標籤：`self-hosted`、`Windows`、`X64`、`revit-2024`。
- Python 3.12、.NET SDK 8、Inno Setup 6；workflow 會配置 Python 與 .NET SDK。
- Runner 服務帳號必須可讀取 `C:\Program Files\Autodesk\Revit 2024` 與 Inno Setup。
- 不得讓公開 PR workflow 使用此 Runner。
