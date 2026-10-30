# rfi-deck-agent（釋疑簡報）

負責人：@seamus0622　ToolId：`DT_RfiDeckAgent`　Ribbon：RTS › AI Agents › 釋疑簡報 ▼

在 Revit 擷取視圖並記錄釋疑問題，累積在收件匣，再一鍵產出釋疑簡報 PPT；可選擇同步到 Google 共用簡報。屬於「文書與協作 Agent」的 Revit 端。

## 類型判斷

本模組使用 Revit API（截取視圖、讀取樓層／柱線／選取元件），所以是 **Revit Ribbon 功能模組**（`module.json`），不是可熱替換 Agent Worker（Worker 不得引用 RevitAPI）。

## 使用方式

按 `RTS › AI Agents › 釋疑簡報 ▼`，直接選擇下拉小工具：

| 選項 | 功能 |
|---|---|
| 釋疑擷取 | 截下目前視圖（3D／平面／剖面／立面），帶入樓層、柱線、選取元件，填寫問題、建議與圖說編號後存入收件匣。可「保留草稿」換視圖再擷取，多張截圖累積在同一筆 |
| 產生簡報 | 勾選收件匣項目，呼叫 Python 產生釋疑簡報 PPT，存到工作資料夾的 `04_成果\簡報PPT`；可同時寫入釋疑問題庫（R### 追蹤編號，沒有問題庫會自動建立） |
| 共用簡報 | 開啟 Google 共用簡報（需先在設定填 Apps Script 網址與密鑰） |
| 收件匣、成果資料夾與設定 | 開啟資料夾，或設定工作資料夾、圖說包、Python、Google 連線 |

### 新同事第一次使用（0.4.0 起）

只要拿到專案的 **圖說包資料夾**，其他都不用另外給：

1. 直接按「釋疑擷取」或「產生簡報」即可。工作資料夾（收件匣、問題庫、成果）自動建在 `<RTS 資料位置>\DT_AIAgent\工作區`（RTS 設定 `DEVELOPMENTTOOLS_DATA` 時在非系統碟；沒有時為 `%LOCALAPPDATA%\DT_AIAgent\工作區`）。
2. 第一次擷取或按「找對照圖說」時，請他選 **圖說包資料夾**，選一次就記住。圖說包附 `共用簡報連線.json` 時，Google 共用簡報連線自動帶入。
3. 電腦沒有 Python 時跳出 **安裝教學**：使用圖說包 `Python安裝檔\` 裡的安裝檔（或從 python.org 下載），只裝給目前帳號、預設裝到 C 槽以外（例如 `D:\Python312`），裝好後自動安裝套件（圖說包有 `Python安裝檔\wheels\` 時離線安裝）。也附手動安裝步驟。

圖說包資料夾內容（由專案負責人準備，是專案資料，不放 Repo）：

| 檔案 | 用途 |
|---|---|
| `圖說查找索引.db` | 查圖索引（必要；選資料夾時以此辨識） |
| `查圖設定.json` | 棟別名稱、數字柱線對應棟別、PDF 子資料夾、柱線座標檔 |
| `<專案>柱線_Revit.json` | Revit 柱線座標（建築圖紅圈定位） |
| `<PDF 子資料夾>\*.pdf` | 合約圖說 |
| `共用簡報連線.json`（選用） | `{"Url": "…/exec", "Secret": "…", "DeckUrl": "…"}` |
| `Python安裝檔\`（選用） | `python-3.12.x-amd64.exe` 與 `wheels\*.whl` |

舊版已指定 AI AGENT 資料夾的使用者不受影響：設定裡的「工作資料夾」沿用原本的 AI AGENT 資料夾，圖說包預設就是其中的 `03_資料庫\圖說`。

## 版本紀錄

| 版本 | 內容 |
|---|---|
| 0.4.0 | **新同事只需圖說包**：產生簡報、問題庫、查圖的 Python 程式全部內嵌在 DLL（`py/*.py`），不再需要 AI AGENT 資料夾；工作資料夾自動建立；新增「圖說包資料夾」設定（第一次使用時選一次，附 Google 連線時自動帶入）；沒有 Python 時跳出安裝教學並裝到 C 槽以外；套件安裝先用圖說包離線套件、先裝進 Python 本身資料夾再退回 `--user`；新電腦自動建立空白釋疑問題庫 |
| 0.3.1 | 設定、草稿、Python 腳本與暫存檔改存非系統碟資料目錄（`DEVELOPMENTTOOLS_DATA`），避開公司防毒對 C 槽的攔截；首次使用自動複製舊設定 |
| 0.3.0 | 採用 Agent 多工具 Ribbon 契約：單一「釋疑簡報」下拉大按鈕內含六個小工具，全部共用 `DT_RfiDeckAgent` 授權 |
| 0.2.0 | 對照圖說定位改以「柱編號」為錨點：讀取選取構件與周圍結構柱的「備註」欄圖說編號（柱 C56、梁 G4e、小梁 J7a）與模型座標，交給 `seek_lookup.py` 在 PDF 上找同編號的柱做「模型座標→圖面」換算；建築圖改用整張柱線換算。對照圖說只取結構＋建築圖，輸出整張圖說（含圖框）加紅框／紅圈／橘框，候選視窗可另加局部放大圖。柱線只在自己的長度範圍內才算（避免不同棟的同名柱線誤判）。接續草稿時，有選取元件就整組更新位置欄位並重找對照圖說；草稿超過 2 小時先詢問。擷取說明列出周圍結構柱與距最近柱線距離，並提示同位置的舊釋疑。**查圖規則 `py/seek_lookup.py` 內嵌在 DLL**，模組更新就同步更新規則；專案特有的設定移到 AI AGENT 資料夾的 `查圖設定.json`；第一次查圖自動 `pip install --user pymupdf pillow openpyxl` |
| 0.1.0 | 由獨立外掛 DT AI AGENT 移入模組平台 |

## 輸入、輸出與副作用

| 項目 | 內容 |
|---|---|
| 輸入 | 目前視圖、選取元件（含「備註」欄圖說編號、柱位置標記）、周圍結構柱（主模型與連結模型，唯讀）、使用者輸入的問題與建議 |
| 輸出 | 收件匣 `item.json` 與截圖；釋疑簡報 `.pptx` |
| 模型 | **不修改**。不開 Transaction，只讀取元素資訊與匯出視圖影像 |
| 本機檔案 | 工作資料夾（預設 `<RTS 資料位置>\DT_AIAgent\工作區`，或使用者指定的舊版 AI AGENT 資料夾）；圖說包資料夾（唯讀）；`<RTS 資料位置 或 %APPDATA%>\DT_AIAgent`（設定 root.txt、drawings.txt、python.txt、gslides.json）；草稿 |
| 外部程式 | 本機 Python 3。沒有時由使用者在安裝教學中確認後執行官方安裝檔（`/passive InstallAllUsers=0 TargetDir=<非 C 槽>`，不需管理員）；套件 `python-pptx openpyxl pymupdf pillow` 以 pip 安裝（離線 wheels 優先） |
| 內嵌程式 | `py/*.py`（seek_lookup、revit_deck、gen_rfi_deck、pptx_tools、rfi_db）編進 DLL，執行時全部寫到 `<RTS 資料位置>\DT_AIAgent\py\`（內容不同才覆寫），以 `--root <工作資料夾>`、`--dwg <圖說包>` 執行；找不到內嵌資源時退回舊版 AI AGENT 資料夾 `01_AI運行本體\程式` |
| 網路（選用） | HTTPS 呼叫使用者自設的 Google Apps Script 網址；使用者在安裝教學按「從 python.org 下載」時下載官方安裝檔；pip 沒有離線套件時連 PyPI。密鑰存在使用者設定檔或圖說包，**不在程式碼內** |
| Bridge／MCP | 不使用。沒有建立第二套 Bridge、Ribbon 啟動器、授權或 Updater |

## 結構

| 路徑 | 內容 |
|---|---|
| `module.json` | 模組描述檔 |
| `RfiDeckAgent.csproj` | SDK 格式 net48 類別庫，沿用 `../Directory.Build.props`，另加 WinForms 與 `System.Web.Extensions` |
| `OpenCommand.cs` | 舊版相容入口；新版 Ribbon 不再依賴第二層 TaskDialog 選單 |
| `ToolCommands.cs` | 六個 Ribbon 小工具命令，分派到既有功能本體 |
| `src/*.cs` | 功能本體（擷取、收件匣、簡報、Google 同步、設定），命名空間 `RfiDeckAgent.Core` |
| `py/seek_lookup.py` | 查圖規則（依樓層、棟別、柱線、柱編號在圖說索引找結構／建築圖並畫紅框紅圈） |
| `py/revit_deck.py`、`gen_rfi_deck.py`、`pptx_tools.py`、`rfi_db.py` | 產生釋疑簡報 PPT、釋疑問題庫（匯入、追蹤編號；沒有問題庫時自動建立） |
| `src/PythonSetupForm.cs` | 沒有 Python 時的安裝教學（裝到非 C 槽、自動裝套件、手動步驟） |

## 來源與修改

來源：`簡報製作及文書處理 AI AGENT\01_AI運行本體\Revit外掛\src\Core`（原獨立外掛 DT AI AGENT）。

- 不帶入原外掛的 `IExternalApplication`、獨立「DT Tools」頁籤、熱重載 Loader、`build_install.ps1`；六個功能放在平台產生的單一「釋疑簡報」下拉大按鈕內。
- 拿掉寫死的 `D:\` 路徑：未設定資料夾時先引導到設定。
- 預設文字去掉特定專案名稱；命名空間由 `DT_AIAgent.Core` 改為 `RfiDeckAgent.Core`。
- 設定與草稿資料夾名稱仍為 `DT_AIAgent`，與舊版資料相容。

## 已知限制

- 棟別由樓層名稱前綴推得（`LB_` 實驗棟、`OB_` 行政棟），屬事務所命名慣例，寫在 `src/ModelContext.cs`；其他專案辨識不到棟別時欄位留空。後續可比照 DrawingFactory 改為命名規則檔。
- 柱線、樓層與圖說對照依台灣常用圖名用語；「對照圖說」需要圖說包（索引＋PDF），是專案資料，不在 Repo。
- 專案設定 `查圖設定.json` 放在圖說包；沒有這個檔時不推棟別，其他功能照常。
- 自動安裝 Python 可能被公司防毒或群組原則擋下；此時依安裝教學的手動步驟安裝，或請 IT 協助。
- 柱編號定位需要模型結構柱、梁的「備註」（或「圖說編號」「構件編號」）參數填入圖說上的編號；沒填的構件改用柱線換算。
- 掃描周圍結構柱會走訪主模型與所有已載入連結模型的結構柱（30m 內），大型模型擷取時會多花一點時間。

## 不放進 Repo

專案圖資、DWG、建議書、產出成果、含真實人名的範例（客戶資料）；安裝檔、wheels、打包 zip、編譯結果；第三方反向工程資料。釋疑擷取與簡報用到的 Python 程式已全部放進本模組（`py/`）；其他文書工具（會議紀錄、簽到表、雙週會簡報）與其範本仍在 AI AGENT 資料夾，另案處理。

## 測試

```powershell
python module-platform/validate.py
python -m unittest discover -s module-platform -p "test_*.py" -v
dotnet build DevelopmentTools.sln -c Debug -clp:ErrorsOnly
```

Revit 2024 人工驗收項目見 `module.json` 的 `tests`。新 ToolId `DT_RfiDeckAgent` 需由主管在授權系統核准；未核准前按鈕可見但無法執行。

## 已安裝舊版獨立外掛的使用者

請刪除 `%APPDATA%\Autodesk\Revit\Addins\2024\DT_AIAgent.addin` 與 `DT_AIAgent\` 資料夾，否則會同時看到舊的「DT Tools」頁籤。

## 回退

刪除 `DevelopmentTools.Modules/rfi-deck-agent/` 後重新建置。不影響既有功能、授權或存檔格式。
