# BIM AI Agent 自動建置與更新

## 目的

同事只提交自己的模組資料夾。PR 合併後，受保護的 `main` 由合法 Revit 2024 Windows Runner 自動建立候選安裝包。候選包通過實機驗收並經 `production` 環境核准後，系統建立版本 PR；版本 PR 合併後才建立正式 GitHub Release 並啟用穩定更新。

## 安全邊界

- Self-hosted Runner 只接受 `push` 到受保護 `main` 的候選建置工作，不執行 `pull_request` 程式碼。
- `Trusted maintainer gate` 對主管本人建立的 PR 直接放行；其他作者必須取得主管對目前最新 commit 的核准，核准後再 Push 會自動失效。
- Reality Bridge 仍是唯一 Revit 與外部 AI 邊界；流程不建立第二套 Bridge。
- 模組仍由既有 `CommandAccessGuard` 驗證 ToolId。
- 所有 Agent 集中於 `RTS › AI Agents` 面板；每個 Agent 只有一顆下拉大按鈕，內部小工具由 `Ribbon.tools` 產生並共用 Agent ToolId。
- 候選工件不會修改正式更新資訊。
- 正式發布必須先通過 GitHub `production` 環境核准與版本 PR。
- Revit 2025／2026 未經實機驗證，不得標記為已支援。

## 自動流程

### 一、模組 PR

`.github/workflows/module-manifest.yml` 在 GitHub-hosted Runner 驗證 manifest、ToolId、相依與獨立 Worker。公司 Self-hosted Runner 不接觸未合併分支。

### 二、合併後候選包

`.github/workflows/revit-candidate.yml` 在 `main` 的相關檔案變更後執行：

1. 確認 Revit 2024 API 開發檔案存在。
2. 比較目前正式版與 GitHub 尚未過期的候選工件，取最高版本後自動增加 patch 版號。即使上一個候選尚未升為正式版，也不會重複使用相同版本。
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
- 預先安裝 Python 3.10 以上、.NET SDK 8 以上及 Inno Setup 6。workflow 只驗證版本，不安裝或修改系統工具鏈。
- Runner 服務帳號必須可讀取 `C:\Program Files\Autodesk\Revit 2024` 與 Inno Setup。
- 不得讓公開 PR workflow 使用此 Runner。

目前登錄的 Runner 名稱為 `BEAI-Revit2024-DESKTOP-COOSTPI`，安裝於
`C:\Users\User\ActionsRunners\Development-tools`。未安裝成 Windows 服務時，必須在該目錄執行
`run.cmd` 並保持登入；關閉 Runner 或登出後，候選建置會安全地停留在 queued，不會改用不具 Revit SDK 的主機。

若要長期無人值守，應由電腦管理員將官方 Runner 安裝成 Windows 服務，使用可讀取 Revit 2024、
Inno Setup、Python 與 .NET SDK 的專用低權限帳號。不得以系統管理員互動帳號執行任意 PR 程式碼。

## GitHub 儲存庫設定

- `main` ruleset 必須要求 `Policy and static checks` 與 `Trusted maintainer gate`。
- `production` environment 的 required reviewer 必須是 `apple5953`，且只允許 protected branches。
- `Prepare production release` 在 workflow 內只要求 `actions: read`、`contents: write`、`pull-requests: write`。
- GitHub **Settings → Actions → General → Workflow permissions** 必須允許 Actions 建立 Pull Request，否則候選包仍可完成，但正式版本 PR 無法自動建立。
- Release workflow 不會自動核准自己的 PR；`Trusted maintainer gate` 仍要求 `apple5953` 對機器人版本 PR 的目前最新 commit 親自核准。

## Runner 管理

檢查 Runner 是否上線：

```powershell
gh api repos/apple5953/Development-tools/actions/runners `
  --jq '.runners[] | {name,status,busy,labels:[.labels[].name]}'
```

手動啟動目前使用者模式 Runner：

```powershell
Set-Location 'C:\Users\User\ActionsRunners\Development-tools'
.\run.cmd
```

停止 Runner 時，在執行視窗按 `Ctrl+C`。不要在 Revit 或候選建置進行中強制終止電腦。
