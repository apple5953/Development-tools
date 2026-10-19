# BEAI Revit Agent 模組開發與交付手冊（給同事及其 Codex／Claude Code）

本文件以 `main` 現有實作為準。先讀根目錄 `AGENTS.md`、`CLAUDE.md`，再讀本文件。每位 BIM 工程師在自己的分支新增一個 `DevelopmentTools.Modules/<id>/` 資料夾，提交 PR。主外掛在**建置時**驗證描述檔、自動引用專案並產生 Ribbon 包裝命令。維護者驗收後使用既有流程部署整包外掛。這不是把單一 DLL 丟到資料夾就能在執行中的 Revit 熱載入的系統。

## 0. 先決定你的 Agent 屬於哪一種

1. **Revit Ribbon 功能模組**：在 Revit 點按鈕，執行 C# `IExternalCommand`。使用本文件的 `module.json` 模板。可把規則分析、幾何計算等寫在模組，但不得繞過主外掛授權。
2. **從 Codex／Claude 等外部 AI 呼叫 Revit**：必須基於現有 Reality Bridge，使用已明確註冊的結構化工具、既有 `ExternalEvent` UI 執行流程。Bridge V0.1 為唯讀；新增 Bridge 工具或模型修改能力屬核心設計變更，先提計畫與審查。`module.json` 填 `bridge-read` 目前會被驗證器拒絕，不能藉此自行開通。
3. **要修改模型的 Ribbon 功能**：先說明輸入、預覽、授權、Transaction 範圍與回退，使用測試模型實測。Ribbon 命令不等於授權 LLM 直接開啟 Transaction。任何模型視窗以外 UI 或外部請求的 Revit API 操作，都須遵循既有 `ExternalEvent` 安全路徑。

**禁止建立第二套 Reality Bridge、MCP/Revit 入口、登入系統或 Updater。** ChatGPT、Claude、Codex 是 Client；Reality Bridge 是外部 AI 與 Revit 的唯一安全邊界。

## 1. 每位同事第一次設定

- 請主管邀請你的 GitHub 帳號加入 `apple5953/Development-tools`，接受邀請。
- Windows 安裝 Git、Python 3、可建置 .NET Framework 4.8 專案的 .NET SDK／Build Tools。需要本機編譯時，安裝並合法使用 Revit 2024 API 開發環境；目前共用 props 參考 `C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll` 與 `RevitAPIUI.dll`。不要把 Autodesk DLL 複製進 Git。
- 每人使用獨立 clone 或 Git worktree；不要讓兩個 AI 工具同時改同一個工作目錄。

```powershell
git clone https://github.com/apple5953/Development-tools.git
cd Development-tools
git switch main
git pull --ff-only origin main
git switch -c feature/room-check-agent
git status --short --branch
```

在 Codex／Claude Code 開啟**儲存庫根目錄**，而非只開啟舊 Agent 資料夾。開始前閱讀 `AGENTS.md`、`CLAUDE.md`、`module-platform/module.schema.json`、`module-platform/template/`、`DevelopmentTools.Modules/example-agent/`。不要直接提交到 `main`，不要將憑證、私有 BIM 模型、`bin/`、`obj/`、安裝檔放進 PR。

## 2. 建立新模組資料夾

以下以 `room-check-agent` 為例。將 `module-platform/template/` 的三個檔案複製到 `DevelopmentTools.Modules/room-check-agent/`，然後改名及改內容。建議另寫 `README.md`，記錄輸入／輸出、權限、資料需求、人工測試與回退。僅提交原始碼和描述檔。

```text
DevelopmentTools.Modules/room-check-agent/
  module.json
  RoomCheckAgent.csproj
  OpenCommand.cs
  README.md
```

`RoomCheckAgent.csproj` 至少包含：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>RoomCheckAgent</AssemblyName>
  </PropertyGroup>
</Project>
```

父層 `DevelopmentTools.Modules/Directory.Build.props` 提供 `net48`、C# 7.3、WPF 與 Revit 2024 API 參考。不要在新模組中加入另一套 Bridge、Ribbon 啟動器或授權系統。若需要 NuGet 或其他模組相依，先記錄來源、版本及部署檔案；不要假設只改 `dependencies` 就會自動安裝第三方 DLL。

`module.json` 範例：

```json
{
  "id": "room-check-agent",
  "version": "0.1.0",
  "hostVersion": "3.0.0",
  "RevitVersion": ["2024"],
  "assembly": "RoomCheckAgent.dll",
  "entryClass": "RoomCheckAgent.Commands.OpenCommand",
  "ToolId": "DT_RoomCheckAgent",
  "Ribbon": {
    "name": "RoomCheckAgent",
    "text": "房間檢查",
    "category": "review"
  },
  "permissions": ["ribbon-command"],
  "dependencies": [],
  "tests": ["Revit 2024 按鈕、授權、命令與回退驗收"]
}
```

| 欄位 | 實際規則／用途 |
|---|---|
| `id` | 小寫英文字母開頭，後接小寫、數字或 `-`；3–64 字元；必須等於模組資料夾名稱。 |
| `version` | 三段數字，例如 `0.1.0`。 |
| `hostVersion` | 三段數字；不得高於目前 `DevelopmentTools.Addin/version.json` 的版本。填最低相容版本，不要憑空填未測的版本。 |
| `RevitVersion` | 填實際驗證版本；目前 Revit 2024 為已使用的建置環境，2025／2026 未測就不要宣稱通過。 |
| `assembly` | 必須等於 `.csproj` 的 `AssemblyName` 加 `.dll`。不可與現有模組 DLL 撞名。 |
| `entryClass` | 完整命名空間與類別名稱，須為可建立的 `IExternalCommand`。C# 編譯與 Revit 實測還須驗證其可用。 |
| `ToolId` | 唯一授權識別，格式 `DT_` 加英文名稱；不要改用既有 ToolId。新的 ID 還需由授權系統核准。 |
| `Ribbon` | `name` 為唯一內部按鈕名稱；`text` 是使用者看到的名稱；`category` 僅可選 `drawing`、`model`、`review`。 |
| `permissions` | 普通按鈕使用 `ribbon-command`。`bridge-read` 尚不能由新模組自行宣告使用。 |
| `dependencies` | 已存在模組的 `id` 清單；不能自我相依或形成循環。它是描述與驗證，不代替完整部署相依審查。 |
| `tests` | 至少一個具體的人工或自動驗收項目。 |

所有欄位以 `module-platform/module.schema.json` 和 `module-platform/validate.py` 的實際檢查為準；描述檔不接受未定義欄位。

## 3. 撰寫命令與授權

一個最小、唯讀的 `OpenCommand.cs`：

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RoomCheckAgent.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class OpenCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            TaskDialog.Show("房間檢查", "模組已載入");
            return Result.Succeeded;
        }
    }
}
```

`entryClass` 必須是 `RoomCheckAgent.Commands.OpenCommand`。產生器會在主外掛建立一層 `Generated_<Ribbon.name>` 命令：先呼叫既有 `CommandAccessGuard.EnsureAuthorized(ToolId, Ribbon.text)`，通過才呼叫模組命令。授權後端若尚未核准新 ToolId，按鈕可能可見但命令不能成功執行；不得透過修改正式憑證或繞過閘道來「修好」測試。PR 要明列新的 ToolId 供主管安排授權。

目前 `module-platform/generate.py` 產生的包裝命令標記為 `TransactionMode.Manual`；這**不代表**允許 AI 或 Bridge 任意修改模型。實際模組應依功能正確設定自身 Transaction 行為；任何修改模型的設計都要單獨審查並在測試模型驗證。外部 AI 呼叫不能直接執行未註冊工具或自行開啟 Revit Transaction。

## 4. 本機自動檢查

在儲存庫根目錄執行並把實際結果貼到 PR：

```powershell
python module-platform/validate.py
python -m unittest discover -s module-platform -p 'test_*.py' -v
dotnet build DevelopmentTools.sln -c Debug -clp:ErrorsOnly
```

`validate.py` 檢查模組資料夾、專案、ToolId、Ribbon 名稱、版本、權限、依賴與循環。`generate.py` 在主外掛建置時執行，產生 `DevelopmentTools.Addin/GeneratedModules/GeneratedModuleCommands.cs`；此檔為產物，**不得手動修改或提交為模組變更**。主外掛專案用 `DevelopmentTools.Modules/*/*.csproj` 自動收集模組，無需同事改 `App.cs`、`RibbonCatalog.cs`、主 `.csproj` 或 `.sln`。

缺少合法 Revit API 的電腦可以執行前兩項 Python 檢查，但不能宣稱整個 .NET 建置或 Revit 功能已通過。GitHub PR 的模組 workflow 目前也只跑 Python 描述檔、單元測試及註冊產生器；它**不會**在 Windows 上編譯模組 DLL 或測試 Revit。含第三方套件、WPF、族群資源或動態檔案的模組，還要核對 Release 候選包是否包含必要檔案。

## 5. Revit 2024 人工驗收

在測試機、測試模型或模型副本進行。先關閉 Revit，保留原本已安裝外掛與 `.addin` 清單備份。可以參照 `docs/LOCAL_REVIT_2024_TEST.md` 的 `module-platform/local-test/start-test.ps1`／`stop-test.ps1` 流程，但該 `start-test.ps1` 現在硬性檢查試點 `ExampleAgent.dll`，並假設既有 `DevelopmentTools.addin` 已存在；它是本機試點工具，不是每位同事通用的安裝器。使用前檢查路徑，不能覆蓋他人的環境。

至少記錄以下結果：

1. Revit 2024 正常啟動，沒有外掛載入錯誤；原有 RTS、MCP、BEAI 入口仍在。
2. 新按鈕出現在 `Ribbon.category` 指定分類；文字、圖示、命令路徑與原有按鈕均正常。
3. 未授權 ToolId 被阻止；已核准 ToolId 可執行。若尚未核准，將成功路徑標記「未驗證」，不要跳過授權。
4. 使用指定測試模型驗證結果、例外處理、取消操作及需要的模型保存／復原。
5. 同時測至少一個已知可用的原有功能，確認沒有明顯回歸。
6. 關閉 Revit、還原原 `.addin` 清單及上個正式包，重開確認舊版本正常。

逐項附測試日期、Revit 版本、外掛提交 SHA、模型類型、預期／實際結果、截圖或日誌。Revit 2025／2026 沒有環境時標記「未驗證」，不可用 2024 結果推定通過。

## 6. 提交 PR，而不是直接上傳 DLL

```powershell
git status --short
git diff --check
git add DevelopmentTools.Modules/room-check-agent
git diff --cached --stat
git commit -m "feat: add room check agent"
git push -u origin feature/room-check-agent
```

在 GitHub 建立對 `main` 的 PR。只暫存自己已檢查的檔案；不要直接 `git add -A`，以免加入模型、憑證、`bin/obj`。PR 至少列出：

- 任務、負責人、分支、模組路徑與用途。
- `id`、版本、ToolId、Ribbon 位置、需核准的授權與相依。
- 輸入、輸出、模型副作用、外部 AI 或 Revit API 入口。
- 執行過的命令及原始測試結果；沒有跑的項目寫「未執行」與原因。
- Revit 2024 人工驗收證據、既有功能回歸、回退步驟。
- 若碰到核心檔案，明列原因與回退；需按 `.github` 政策做「核心變更已揭露」，由維護者審查。

CI 通過、PR 合併、部署，是三個不同關卡。**合併 PR 不代表同事的 Revit 立刻取得新版 DLL。** 維護者在合法 Revit API Windows 環境建置 Release、核對混淆 DLL 與安裝包，再依既有 Updater／Release 流程發布。一般同事不要執行正式 `release/release.ps1` 或建立正式 GitHub Release。

## 7. 舊獨立 Agent 移入時

先唯讀盤點舊專案的 `.csproj`、Revit 版本、資源、外部 DLL、Ribbon、授權、Bridge、MCP、Updater、模型輸入及測試步驟。只搬入所需原始碼；不搬 `.git`、`bin/obj`、舊登入金鑰、私有模型或 Autodesk DLL。若舊 Agent 自帶 Revit 橋接器、載入器或更新器，先列出如何改用本儲存庫既有邊界，不能把那一套一併搬入。若單靠新模組資料夾無法整合，明列額外核心變更與理由，經 PR 審查後才做。

## 8. 目前能力與限制：不要誤導使用者

| 能力 | 現況 |
|---|---|
| 同事只新增自己的模組資料夾，產生專案引用與 Ribbon 包裝命令 | 已在 `main`。 |
| 新模組走既有 `CommandAccessGuard` | 已在產生的包裝命令。新 ToolId 仍需授權後端核准。 |
| 新模組進入既有 Release ZIP／安裝程式 | 已完成打包候選驗證；正式部署需依維護者流程。 |
| 在 Revit 執行中只上傳 DLL 即熱替換 | **未支援**。不要要求同事上傳未知 DLL 到使用者電腦。 |
| 主外掛不重新編譯、單模組更新並於重開 Revit 後載入 | `module-platform/host-poc/` 只有未接線的概念驗證；版本、相依隔離、錯誤隔離、更新及回退尚需實作與 Revit 2024 實測。 |
| Updater 對新模組的失敗回退 | 尚未完成實際升級／失敗回退驗收。 |

目前 Host POC 使用經核准目錄與 SHA-256 檢查後呼叫 `Assembly.LoadFrom`，**正式 Addin 尚未引用它**。在 .NET Framework 4.8，已載入的單一 assembly 不能從目前 AppDomain 個別卸載。因此未來若做單模組更新，先以「下載核准模組包、關閉並重開 Revit、啟動時驗證載入、失敗回退」為目標，不承諾執行中熱替換。

## 9. 可直接貼給同事 AI 的任務文字

```text
你是 BEAI Revit 模組工程師。請先讀 AGENTS.md、CLAUDE.md、
docs/AGENT_MODULE_DEVELOPER_GUIDE.zh-TW.md、module-platform/module.schema.json、
module-platform/template/ 和 DevelopmentTools.Modules/example-agent/。

任務名稱：[填寫]
功能與預期輸出：[填寫]
指定模組資料夾：DevelopmentTools.Modules/[小寫 id]/
預計 ToolId：DT_[唯一英文名稱]
Ribbon 分類：[drawing/model/review]
模型是否會被修改：[否／若是，先提出審核設計]
測試模型與驗收條件：[填寫]

先確認 origin、目前分支與工作樹狀態；若在 main，建立自己的 feature 分支。
只修改指定模組資料夾，使用 template 建立 module.json、SDK net48 csproj、
IExternalCommand 與 README。不要建立第二套 Bridge、MCP 入口、授權或 Updater；
不要手動改 App.cs、RibbonCatalog.cs、主專案引用或生成檔。
外部 AI 的 Revit API 呼叫只走現有 Reality Bridge 的註冊工具與 ExternalEvent；
Reality Bridge V0.1 不可開 Transaction。

執行 python module-platform/validate.py、驗證器單元測試，以及在合法
Revit 2024 API 環境下的 Debug 建置。逐項記錄命令、實際結果與未執行項目。
用測試模型完成 Ribbon、授權、功能、原有功能回歸和回退驗收；若你不能操作
Revit，標記待人工驗收，不可聲稱通過。檢查提交檔案後推送自己的分支，
建立指向 main 的 PR，附上 ToolId 授權需求和回退方案。不要發布正式 Release。
```
