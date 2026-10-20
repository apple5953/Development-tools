# BEAI Revit 可熱替換 Agent 架構與同事 AI 操作手冊

版本：v1，目標環境：Revit 2024／.NET Framework 4.8。Revit 2025、2026 尚未驗證。

本文件同時是架構規格、開發契約及交付手冊。Codex、Claude Code 或其他開發 AI 在建立 Agent Worker 前，必須先讀根目錄 `AGENTS.md`、`CLAUDE.md` 和本文件。

## 1. 「熱替換」在本系統的精確定義

熱替換 Agent 是**獨立行程 Worker**。Revit 內只保留固定的 Agent Host、既有授權閘道和既有 Reality Bridge。每次使用者執行 Agent，Host 都從核准目錄讀取目前啟用版本，驗證所有檔案 SHA-256，然後啟動該版本的 `.exe`，透過標準輸入／輸出交換 JSON。

安裝新版時不覆蓋正在使用的版本，而是建立新的版本資料夾，再原子切換 `approved-agents.json`。已經執行中的舊 Worker 可以完成；下一次從「Agent 中心」執行時使用新版。按「重新載入」或重新開啟 Agent 中心即可重新讀取目錄，Revit 不需要關閉。

這不是將已載入 Revit AppDomain 的 DLL 卸載。以下內容仍需完整建置主外掛並重開 Revit：

- Agent Host、Reality Bridge、ExternalEvent Handler 或 Revit Adapter。
- `IExternalCommand`、WPF 視窗、Ribbon 版面或 Revit API DLL。
- 授權、Updater、Installer 或正式 Release 流程。

未來同事可熱替換的是純 .NET／AI／規則／資料分析 Worker。Worker **不能引用 RevitAPI、不能取得 Document／Element、不能直接開 Transaction**。

## 2. 唯一安全邊界

```text
Codex／Claude／其他 AI Client
            │
            ▼
    現有 Reality Bridge
 結構化工具、權限、ExternalEvent
            │
            ▼
        Revit UI Thread
```

可熱替換 Worker 的執行路徑是：

```text
RTS Ribbon「Agent 中心」
            │
            ├─ 讀取 approved-agents.json
            ├─ 驗證版本、Revit 版本、權限、路徑、所有 SHA-256
            ├─ CommandAccessGuard 驗證 ToolId
            ▼
    獨立 Agent Worker.exe
   stdin JSON → 分析 → stdout JSON
            │
            ▼
       僅顯示分析結果
```

Worker 目前只有 `document-context-read`：Host 可提供文件標題、是否為工作共享模型、最多 200 個已選元素的 UniqueId。它不會傳入模型檔路徑、Revit API 物件或授權憑證。Worker 回應目前只允許顯示結果，不能要求 Host 執行模型修改。

Host 啟動 Worker 時會清空繼承的環境變數，只保留執行所需的 `SystemRoot`、`TEMP`、`TMP` 與協議版本，避免把 Revit 程序可能持有的 API token 或其他祕密交給 Worker。

Host 在 Revit UI thread 擷取唯讀 DTO 後，Worker 會在背景執行；Revit 不必等待 AI 或網路分析。Worker 完成後，結果再排回 Revit UI Dispatcher 顯示。背景執行期間不得再次存取原本的 `Document`、`Element` 或其他 Revit API 物件。

若未來 Agent 需要修改模型，先新增一個明確註冊的 Reality Bridge 結構化工具和固定 Revit Adapter，由現有 ExternalEvent 在 UI thread 執行白名單操作。不能讓 Worker 回傳任意 C#、Python、反射呼叫或 Transaction 指令。

## 3. 儲存庫元件

| 路徑 | 用途 |
|---|---|
| `DevelopmentTools.Addin/Core/AgentHost/` | 固定 Host、核准目錄與 SHA-256 驗證、Worker 執行及稽核。 |
| `DevelopmentTools.Addin/Commands/Cmd_HotAgentCenter.cs` | Revit「Agent 中心」入口；列出、重新載入並執行已核准 Worker。 |
| `DevelopmentTools.AgentWorkers/<id>/` | 每位同事只負責自己的 Worker 原始碼與描述檔。 |
| `module-platform/agent-worker-template/` | 新 Worker 模板，不參與正式建置。 |
| `module-platform/agent-worker.schema.json` | `agent.worker.json` 格式契約。 |
| `module-platform/validate_agents.py` | 不執行 Worker，只檢查描述檔、專案與禁止的 RevitAPI 參考。 |
| `module-platform/prepare_agents.py` | 建置後建立版本化套件、計算 SHA-256 並產生核准目錄。 |
| `module-platform/install_agent_bundle.py` | 驗證受信任 catalog hash、原子安裝新版、保留舊版。 |
| `module-platform/activate_agent_version.py` | 驗證舊版雜湊後，原子切回指定版本。 |
| `.github/workflows/module-manifest.yml` | PR／main 的中繼資料測試、隔離 Worker 建置和候選 artifact。 |

固定 Host 仍在 `DevelopmentTools.Addin.dll`，沒有建立第二套 Bridge。Worker 專案透過 `ReferenceOutputAssembly="false"` 只建立建置順序；Worker 組件不會因 ProjectReference 被載入 Revit AppDomain。

## 4. 安裝目錄與版本切換

正式安裝位置跟隨主外掛，例如：

```text
%LOCALAPPDATA%\DevelopmentTools\App\Agents\
  approved-agents.json
  room-check-agent\
    versions\
      0.1.0\
        agent.worker.json
        RoomCheckAgent.exe
        RoomCheckAgent.exe.config
      0.2.0\
        agent.worker.json
        RoomCheckAgent.exe
        RoomCheckAgent.exe.config
```

`approved-agents.json` 是 Host 唯一讀取的核准目錄。每個版本列出：Agent ID、版本、最低 Host 版本、Revit 版本、執行檔、ToolId、權限、套件相對路徑和每個檔案 SHA-256。Host 拒絕：

- 絕對路徑、`..` 路徑逃逸或子資料夾中的任意執行檔。
- 未列入 catalog 的額外檔案。
- 遺失檔案或 SHA-256 不符。
- 不支援的 catalog／Worker protocol 版本。
- 未核准權限、重複 ToolId、不相容 Host 或 Revit 版本。
- 逾時、超過 1 MiB 輸出、非零 exit code 或無效 JSON。

Host 對同一 Agent ID 只選取一個 `active` 版本。安裝器先保留舊版、把同 ID 舊項目設為不啟用，再切換新 catalog。回退只切 catalog，不刪除套件。

## 5. Worker JSON 協議

Host 寫入 Worker stdin 的 UTF-8 JSON：

```json
{
  "protocolVersion": 1,
  "requestId": "唯一要求識別",
  "agentId": "room-check-agent",
  "agentVersion": "0.2.0",
  "revitVersion": "2024",
  "documentTitle": "測試模型",
  "documentIsWorkshared": false,
  "selectedElementUniqueIds": ["element-unique-id"]
}
```

Worker 必須把一個 UTF-8 JSON 寫到 stdout，其他診斷寫 stderr：

```json
{
  "protocolVersion": 1,
  "status": "succeeded",
  "title": "房間檢查完成",
  "message": "共分析 12 個房間，2 個需要人工複核。"
}
```

規則：

- stdout 只能放最終協議 JSON，不要混入 log、進度或 Markdown code fence。
- 成功時 exit code 為 0 且 `status` 為 `succeeded`。
- 失敗時可把診斷寫 stderr，並回傳非零 exit code；不要輸出金鑰或完整敏感資料。
- 在 `timeoutSeconds` 內結束；範圍 1–120 秒。
- Worker 是每次執行啟動一次的短生命週期行程。長時間服務、串流或常駐模型需要另提設計。
- API 金鑰不得放進 Git、manifest、exe.config 或測試輸出。使用公司核准的本機祕密來源或服務端代理。

## 6. 每位同事／AI 建立自己的 Worker

### 6.1 建立分支

```powershell
git switch main
git pull --ff-only origin main
git switch -c feature/room-check-agent
```

禁止直接修改 `main`。一個 Agent 一個分支、一個資料夾、一個 PR。

### 6.2 複製模板

將 `module-platform/agent-worker-template/` 複製成：

```text
DevelopmentTools.AgentWorkers/room-check-agent/
  RoomCheckAgent.csproj
  Program.cs
  agent.worker.json
  README.md
```

不要把模板資料夾本身移入正式 Worker 根目錄；正式根目錄下每個含 `.csproj` 的子資料夾都會被建置與驗證。

### 6.3 填寫 manifest

```json
{
  "schemaVersion": 1,
  "id": "room-check-agent",
  "version": "0.1.0",
  "hostVersion": "3.2.4",
  "revitVersions": ["2024"],
  "executable": "RoomCheckAgent.exe",
  "toolId": "DT_RoomCheckHotAgent",
  "displayName": "房間檢查 Agent",
  "description": "分析 Host 提供的安全文件摘要並回傳人工檢查結果。",
  "permissions": ["document-context-read"],
  "timeoutSeconds": 30,
  "tests": ["stdin/stdout protocol", "Revit 2024 Agent Center authorization and hot update rollback"]
}
```

| 欄位 | 規則 |
|---|---|
| `id` | 小寫、數字、連字號；必須等於資料夾名稱。 |
| `version` | 三段 SemVer 數字；每次發布內容改變都要提高版本。禁止用同版本覆蓋。 |
| `hostVersion` | 最低相容主外掛版本。 |
| `revitVersions` | 只列實際支援版本；目前正式目標為 `2024`。 |
| `executable` | 必須等於 `.csproj` 的 `AssemblyName` 加 `.exe`。 |
| `toolId` | 唯一授權 ID；需要主管在既有授權系統核准。 |
| `permissions` | v1 只能是 `document-context-read`。 |
| `timeoutSeconds` | 1–120 秒。 |

`.csproj` 必須是 net48 Exe，不能加入 `RevitAPI` 或 `RevitAPIUI` Reference。需要第三方 NuGet 時，在 README 說明用途、版本、授權與部署檔；建置後所有必要 DLL 會進套件並被雜湊。

### 6.4 本機測試

```powershell
python module-platform/validate_agents.py
python -m unittest discover -s module-platform -p "test_*.py" -v
dotnet build DevelopmentTools.AgentWorkers/room-check-agent/RoomCheckAgent.csproj -c Release -clp:ErrorsOnly
python module-platform/prepare_agents.py --configuration Release --output artifacts/Agents
```

用 stdin 測協議；stdout 必須是單一 JSON：

```powershell
'{"protocolVersion":1,"documentTitle":"Worker 測試"}' |
  & .\DevelopmentTools.AgentWorkers\room-check-agent\bin\Release\RoomCheckAgent.exe
```

若輸入含中文，測試工具必須使用 UTF-8；正式 Host 已固定使用 UTF-8。

### 6.5 提交 PR

```powershell
git status --short
git diff --check
git add DevelopmentTools.AgentWorkers/room-check-agent
git commit -m "feat: add room check hot agent"
git push -u origin feature/room-check-agent
```

PR 必須附：負責人、Agent ID、版本、ToolId、用途、輸入與輸出、外部服務、第三方相依、資料敏感性、實際測試結果、Revit 2024 驗收、逾時行為和回退版本。禁止提交 `bin/`、`obj/`、token、私有模型或 Autodesk DLL。

GitHub-hosted Windows Runner 會在無正式憑證環境建置 Worker、驗證描述檔並產生 `agent-bundle-<commit SHA>` artifact。PR artifact 是候選物，**不能因 CI 綠色就直接裝入正式 Revit**。PR 合併後，以 `main` 對應 SHA 的 bundle 或正式 Release 包進行維護者驗收。

## 7. 安裝新版與熱切換

正式 Updater 尚未自動下載單 Agent artifact；目前由維護者取得已合併 `main` 或正式 Release 的可信 bundle。不得使用分支 build、來路不明 DLL 或只由開發者自行聲稱的 hash。

先從可信發布資料取得 `approved-agents.json` SHA-256，再執行：

```powershell
python module-platform/install_agent_bundle.py `
  --bundle D:\ApprovedAgentBundle\Agents `
  --install-root "$env:LOCALAPPDATA\DevelopmentTools\App\Agents" `
  --expected-catalog-sha256 <由可信 main artifact 或 Release manifest 提供的 SHA-256>
```

注意：如果只在下載後自己計算同一份未知 bundle 的 hash，它只能證明傳輸後沒有再改變，不能證明來源可信。正式部署的 hash 必須來自受保護的 `main` 工作流程或正式 Release manifest。

安裝完成後：

1. 目前正在執行的舊 Worker 不受影響。
2. 回到 Revit「RTS → 檢討與管理 → Agent 中心」。
3. 按「重新載入」，或關閉 Agent 中心再開一次。
4. 清單顯示新版本；下一次執行啟動新版本 exe。
5. Revit 主程序不重新啟動。

若這是第一次部署 Agent Host 本身，仍需先安裝新版主外掛並重開一次 Revit。後續純 Worker 更新才可熱切換。

## 8. 回退

舊版本不會被安裝器刪除。若新版失敗：

```powershell
python module-platform/activate_agent_version.py `
  --install-root "$env:LOCALAPPDATA\DevelopmentTools\App\Agents" `
  --id room-check-agent `
  --version 0.1.0
```

回到 Agent 中心按「重新載入」。工具會先驗證舊版檔案仍符合 catalog SHA-256，再切換 active 版本。若雜湊不符，停止回退並重新取得可信套件。

Host 稽核檔位於 `%LOCALAPPDATA%\DevelopmentTools\Logs\agent-host.jsonl`，記錄 UTC 時間、Agent ID、版本、ToolId、成功／失敗、耗時與錯誤摘要，不記錄完整模型內容。

## 9. Revit 2024 驗收清單

維護者需在測試模型或副本逐項記錄：

1. 安裝含 Agent Host 的完整候選外掛並重開 Revit。
2. 原有 RTS、Reality Bridge、MCP、授權、Updater 與既有命令正常。
3. Agent 中心只顯示 catalog 中 active、版本相容且雜湊正確的 Worker。
4. 未授權 ToolId 被既有授權閘道阻止。
5. 已授權 Agent 收到預期文件摘要並正確顯示結果。
6. Revit 開著時安裝高版本，重新載入後下一次執行使用高版本。
7. 修改任一 exe 位元組，Host 必須拒絕執行。
8. 加入 catalog 未列出的 DLL，Host 必須拒絕執行。
9. Worker 逾時、崩潰、輸出非法 JSON時，Revit 不崩潰並留下失敗稽核。
10. 切回舊版後，不重開 Revit即可再次執行舊版。

尚未完成以上 Revit 實機驗收時，PR 或文件只能寫「建置／協議測試通過」，不能宣稱正式熱替換已驗收。

## 10. 可直接交給同事 AI 的提示詞

```text
你是 BEAI Revit 可熱替換 Agent Worker 工程師。
先讀 AGENTS.md、CLAUDE.md、docs/HOT_SWAPPABLE_AGENT_ARCHITECTURE.zh-TW.md、
module-platform/agent-worker.schema.json 和 module-platform/agent-worker-template/。

任務：[填寫]
Agent ID：[小寫連字號]
版本：[例如 0.1.0]
ToolId：[DT_開頭的唯一名稱]
分析輸入與輸出：[填寫]
外部 AI／API：[填寫，沒有則寫無]
驗收條件：[填寫]

確認目前位於自己的 feature 分支。只新增或修改
DevelopmentTools.AgentWorkers/<Agent ID>/。建立 net48 Exe、Program.cs、
agent.worker.json 和 README.md。Worker 只能讀 stdin JSON、進行純 .NET／AI 分析，
並把一個 protocolVersion=1 的結果 JSON 寫到 stdout。

不得引用 RevitAPI/RevitAPIUI，不得建立第二套 Reality Bridge、MCP、授權或 Updater，
不得取得 Document/Element，不得直接執行 Transaction，不得輸出或提交金鑰。
若需求需要模型修改，停止 Worker 實作並提出既有 Reality Bridge 結構化工具、
ExternalEvent、白名單操作、人工核准與回退設計，等待核心審查。

執行 validate_agents.py、全部 module-platform 單元測試、Release Worker 建置與
stdin/stdout 協議測試。逐項記錄真實結果。只提交自己的 Worker 原始碼與文件，
建立指向 main 的 PR；不要直接安裝 PR artifact、push main 或建立正式 Release。
```

## 11. 目前未完成項目

- 本架構需完成 Revit 2024 Agent 中心、熱更新、雜湊拒絕、逾時與回退實機驗收。
- 單 Agent 自動下載尚未整合正式 Updater；目前使用可信 bundle＋原子安裝工具，完整 Release 也會包含 `Agents/`。
- catalog 使用 SHA-256 與受保護 GitHub／Release 來源；獨立數位簽章與公司憑證鏈尚未實作。
- Worker v1 只回傳顯示文字，不執行模型修改，也不是長時間常駐服務。
- Revit 2025、2026 尚未驗證。
