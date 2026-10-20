# rfi-deck-agent（釋疑簡報）

負責人：@seamus0622　ToolId：`DT_RfiDeckAgent`　Ribbon：RTS › 檢討與管理 › 釋疑簡報

在 Revit 擷取視圖並記錄釋疑問題，累積在收件匣，再一鍵產出釋疑簡報 PPT；可選擇同步到 Google 共用簡報。屬於「文書與協作 Agent」的 Revit 端。

## 類型判斷

本模組使用 Revit API（截取視圖、讀取樓層／柱線／選取元件），所以是 **Revit Ribbon 功能模組**（`module.json`），不是可熱替換 Agent Worker（Worker 不得引用 RevitAPI）。

## 使用方式

按「釋疑簡報」後選擇：

| 選項 | 功能 |
|---|---|
| 釋疑擷取 | 截下目前視圖（3D／平面／剖面／立面），帶入樓層、柱線、選取元件，填寫問題、建議與圖說編號後存入收件匣。可「保留草稿」換視圖再擷取，多張截圖累積在同一筆 |
| 產生簡報 | 勾選收件匣項目，呼叫 Python 產生釋疑簡報 PPT，存到 AI AGENT 資料夾的 `04_成果\簡報PPT`；可同時寫入釋疑問題庫（R### 追蹤編號） |
| 共用簡報 | 開啟 Google 共用簡報（需先在設定填 Apps Script 網址與密鑰） |
| 收件匣、成果資料夾與設定 | 開啟資料夾，或設定 AI AGENT 資料夾、Python、Google 連線 |

第一次使用會先要求指定 **AI AGENT 資料夾**（內含 `01_AI運行本體\程式` 的 Python 程式、範本與 `03_資料庫`）。

## 版本紀錄

| 版本 | 內容 |
|---|---|
| 0.2.0 | 對照圖說定位改以「柱編號」為錨點：讀取選取構件與周圍結構柱的「備註」欄圖說編號（柱 C56、梁 G4e、小梁 J7a）與模型座標，交給 `seek_lookup.py` 在 PDF 上找同編號的柱做「模型座標→圖面」換算；建築圖改用整張柱線換算。對照圖說只取結構＋建築圖，輸出整張圖說（含圖框）加紅框／紅圈／橘框，候選視窗可另加局部放大圖。柱線只在自己的長度範圍內才算（避免不同棟的同名柱線誤判）。接續草稿時，有選取元件就整組更新位置欄位並重找對照圖說；草稿超過 2 小時先詢問。擷取說明列出周圍結構柱與距最近柱線距離，並提示同位置的舊釋疑。**查圖規則 `py/seek_lookup.py` 內嵌在 DLL**，模組更新就同步更新規則；專案特有的設定移到 AI AGENT 資料夾的 `查圖設定.json`；第一次查圖自動 `pip install --user pymupdf pillow openpyxl` |
| 0.1.0 | 由獨立外掛 DT AI AGENT 移入模組平台 |

## 輸入、輸出與副作用

| 項目 | 內容 |
|---|---|
| 輸入 | 目前視圖、選取元件（含「備註」欄圖說編號、柱位置標記）、周圍結構柱（主模型與連結模型，唯讀）、使用者輸入的問題與建議 |
| 輸出 | 收件匣 `item.json` 與截圖；釋疑簡報 `.pptx` |
| 模型 | **不修改**。不開 Transaction，只讀取元素資訊與匯出視圖影像 |
| 本機檔案 | 使用者指定的 AI AGENT 資料夾；`%APPDATA%\DT_AIAgent`（設定）；`%LOCALAPPDATA%\DT_AIAgent`（草稿） |
| 外部程式 | 本機 Python 3。第一次產生簡報時以 `pip install --user` 安裝 `python-pptx`、`openpyxl`；第一次查對照圖說時安裝 `pymupdf`、`pillow`、`openpyxl` |
| 內嵌程式 | `py/seek_lookup.py`（查圖規則）編進 DLL，執行時寫到 `%LOCALAPPDATA%\DT_AIAgent\py\`（內容不同才覆寫），以 `--root <AI AGENT 資料夾>` 執行；找不到內嵌資源時退回 AI AGENT 資料夾 `01_AI運行本體\程式` 的同名檔 |
| 網路（選用） | HTTPS 呼叫使用者自設的 Google Apps Script 網址。密鑰存在 `%APPDATA%\DT_AIAgent\gslides.json`，**不在程式碼內** |
| Bridge／MCP | 不使用。沒有建立第二套 Bridge、Ribbon 啟動器、授權或 Updater |

## 結構

| 路徑 | 內容 |
|---|---|
| `module.json` | 模組描述檔 |
| `RfiDeckAgent.csproj` | SDK 格式 net48 類別庫，沿用 `../Directory.Build.props`，另加 WinForms 與 `System.Web.Extensions` |
| `OpenCommand.cs` | 唯一入口：選單式 TaskDialog，分派到功能本體 |
| `src/*.cs` | 功能本體（擷取、收件匣、簡報、Google 同步、設定），命名空間 `RfiDeckAgent.Core` |
| `py/seek_lookup.py` | 查圖規則（依樓層、棟別、柱線、柱編號在圖說索引找結構／建築圖並畫紅框紅圈），以 EmbeddedResource 編進 DLL |

## 來源與修改

來源：`簡報製作及文書處理 AI AGENT\01_AI運行本體\Revit外掛\src\Core`（原獨立外掛 DT AI AGENT）。

- 不帶入原外掛的 `IExternalApplication`、獨立「DT Tools」頁籤、熱重載 Loader、`build_install.ps1`；6 個按鈕改為一個入口加選單（平台每個模組一個 Ribbon 按鈕）。
- 拿掉寫死的 `D:\` 路徑：未設定資料夾時先引導到設定。
- 預設文字去掉特定專案名稱；命名空間由 `DT_AIAgent.Core` 改為 `RfiDeckAgent.Core`。
- 設定與草稿資料夾名稱仍為 `DT_AIAgent`，與舊版資料相容。

## 已知限制

- 棟別由樓層名稱前綴推得（`LB_` 實驗棟、`OB_` 行政棟），屬事務所命名慣例，寫在 `src/ModelContext.cs`；其他專案辨識不到棟別時欄位留空。後續可比照 DrawingFactory 改為命名規則檔。
- 柱線、樓層與圖說對照依台灣常用圖名用語；「對照圖說」與「同位置釋疑歷史」需要 AI AGENT 資料夾內的**圖說索引**（`03_資料庫\圖說\圖說查找索引.db`）與 PDF，這些是專案資料，不在 Repo。
- 專案設定 `03_資料庫\圖說\查圖設定.json`（棟別名稱、數字柱線對應棟別、柱線座標檔、釋疑問題庫路徑）放在各專案的 AI AGENT 資料夾；沒有這個檔時不推棟別，其他功能照常。
- 柱編號定位需要模型結構柱、梁的「備註」（或「圖說編號」「構件編號」）參數填入圖說上的編號；沒填的構件改用柱線換算。
- 掃描周圍結構柱會走訪主模型與所有已載入連結模型的結構柱（30m 內），大型模型擷取時會多花一點時間。

## 不放進 Repo

專案圖資、DWG、建議書、產出成果、含真實人名的範例（客戶資料）；安裝檔、wheels、打包 zip、編譯結果；第三方反向工程資料。查圖規則 `seek_lookup.py` 已放進本模組（`py/`）；其他 Python 簡報程式與範本（會議紀錄、簽到表、雙週會簡報、釋疑問題清單）目前仍在各使用者的 AI AGENT 資料夾，另案處理。

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
