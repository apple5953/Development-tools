# 外掛全面 AI Agent 化遷移計畫

狀態：提案（等待主管審核）。本文件說明 DevelopmentTools 的每一個外掛功能，改由 AI agent 執行時要怎麼改、分幾階段，以及哪些功能建議合併或不再需要，原因列給人判斷。

## 1. 原則

1. **只有一個 Reality Bridge。** agent 一律透過既有 `BEAI.QueryHost`（`http://127.0.0.1:8765/api/v1/execute`）與 MCP Server 呼叫外掛，不另開服務、不另做 MCP。
2. **外掛功能變成「無介面工具」。** 每個外掛拆成兩層：
   - `XxxRunner`：純流程，輸入參數物件、輸出結果物件，不開視窗、不跳 TaskDialog、不 PickObject。
   - 人用的 `Cmd_Xxx` 視窗與 agent 用的 `XxxAgentTool` 共用同一個 Runner，兩邊結果一致，維護一份程式。
3. **工具在 Addin 啟動時註冊到 `RealityToolRegistry`。** HTTP 端不能新增或替換工具；工具名稱、模型影響（`none`／`temporary-rollback`）、逾時都寫死在程式中，MCP 端另有白名單，CI（`scripts/validate-beai-mcp.py`）檢查兩邊一致。
4. **Bridge 本身不開交易。** 需要暫存元素（暫時 3D 視圖、暫時連結）的分析工具，交易寫在模組 Runner 內且一律 `RollBack()`，並宣告 `temporary-rollback`。
5. **模組分析工具預設關閉。** 需由人把 `Config/feature-flags.json` 的 `analysis` 改為 `true` 才會執行。
6. **授權不繞過、不跳窗。** agent 工具沿用原本 ToolId（例如 `CodeReview`）與白名單；尚未登入時回傳 `BEAI_PERMISSION_DENIED`，由人在 Revit 登入一次。
7. **會改模型的工具一律走「受控寫入」流程（第 3 階段）**：agent 先產生 `plan`（要建立／修改哪些元素、預覽差異），人核准後才 `commit`，每次 commit 一個 Revit 交易、可復原、有稽核紀錄。V0.1 Bridge 維持唯讀，這一段需要另開 ADR 與主管核准。
8. **結果只是輔助。** 法規、結構類輸出一律附 `humanReviewRequired`：機器檢查不能取代建築師、技師的審查與簽證。

## 2. 分階段

| 階段 | 內容 | 狀態 |
|---|---|---|
| 1 | 工具註冊機制＋`code_check.run`（全條文法規檢討） | 本 PR |
| 2 | 其他唯讀／回復型分析：`rebar.check`、`project_standards.compare`、`design_review.summary` | 待做，同一模式，各一個 PR |
| 3 | 受控寫入合約（plan → 人核准 → commit → 驗證），ADR＋Bridge 版本升級 | 需主管核准設計 |
| 4 | 出圖、建模類寫入工具逐一搬上受控寫入合約 | 依第 3 階段 |
| 5 | 視窗降級為「檢視 agent 結果與核准」的介面；確定不需要的指令下架 | 依本文件第 4 節的人工決定 |

## 3. 功能逐項規劃

欄位說明：**類型**＝唯讀分析／寫入／純介面。**agent 工具**＝建議的工具名稱（Bridge 名稱 `area.action`，MCP 名稱 `snake_case`）。

### 3.1 檢討與管理

| Ribbon 功能 | 指令類別 | 類型 | agent 工具 | 改法 |
|---|---|---|---|---|
| 全條文法規檢討 | `Cmd_CodeCheck` | 唯讀分析（暫存後回復） | `code_check.run` / `run_code_check` | **本 PR 完成**：抽出 `CodeCheckRunner`，`CodeCheckAgentTool` 註冊；專案條件可用 `args.settings` 單次覆寫，不寫回設定檔 |
| 專案標準比對 | `Cmd_ProjectStandards`（`DevelopmentTools.Modules/ProjectStandards`） | 唯讀分析 | `project_standards.compare` | 把比對結果做成結構化清單（類型、族群、參數差異），視窗只顯示 |
| 參數比對複製 | `Cmd_ProjectCompareCopy` | 比對＝唯讀；複製＝寫入 | `parameters.compare`（階段 2）＋`parameters.copy`（階段 4） | 比對與複製拆開，複製走受控寫入 |
| AI 模型分析 | `Cmd_AIAssistant` | 介面＋外部 LLM | 不建立 | 見 4.5，建議下架 |
| Agent 中心 | `Cmd_HotAgentCenter` | 背景 worker 啟動 | 保留，改由 MCP 列出／觸發已核准 worker | worker 已有 SHA-256 白名單，不改授權 |
| Google 登入／問題與反饋／檢查更新 | `LoginCommand`、`FeedbackCommand`、`CheckUpdateCommand` | 純介面／系統 | 不建立 | 留給人；agent 只回報「需登入」 |
| 語言（中／英／日） | `Cmd_LanguageZH/EN/JA` | 純介面 | 不建立 | agent 輸出語言由 agent 端決定 |

### 3.2 出圖

| Ribbon 功能 | 指令類別 | 類型 | agent 工具 | 改法 |
|---|---|---|---|---|
| 快速開圖套樣板 | `Cmd_QuickViewCreator` | 寫入 | `views.create_from_template` | Runner 參數：樓層、視圖類型、樣板名稱；plan 列出將建立的視圖 |
| 快速從屬視圖 | `Cmd_FastDependentViews` | 寫入 | `views.create_dependents` | 參數：母視圖 ID、切分範圍或範圍框 |
| 量化開圖 | `Cmd_DrawingFactory`（Modules/DrawingFactory） | 寫入 | `drawing_factory.plan` / `.commit` | 已是批次邏輯，最適合第一個受控寫入範例 |
| 逐層複製圖紙 | `Cmd_SheetDuplicator` | 寫入 | `sheets.duplicate_by_level` | |
| 圖紙排版 | `Cmd_SheetViewPlacer` | 寫入 | `sheets.place_views` | 排版規則改成參數（邊界、對齊、比例） |
| 圖紙批次更名 | `Cmd_BatchSheetRenamer` | 寫入 | `sheets.rename` | plan 回傳舊名→新名對照表給人核准 |
| 跨專案圖紙轉移 | `Cmd_SheetTransfer` | 寫入（兩個文件） | `sheets.transfer` | 需指定來源／目標文件，風險高，排在最後 |
| 快速尺寸標註 | `Cmd_QuickDimension` | 寫入 | `annotations.dimension` | 目前依賴使用者選取；改為元素 ID＋視圖 ID 參數 |
| 自動邊界 | `Cmd_AutoBoundary` | 寫入 | `areas.auto_boundary` | 建照圖面積計算的前置，優先度高 |
| 記錄標註邏輯 | `Cmd_RecordViewAnnotations` | 寫入（存設定） | `annotations.record_logic` | 搭配 `annotations.replay` 讓 agent 套用到其他視圖 |
| 開口剖面圖 | `Cmd_OpeningDetail`（Modules/OpeningDetail） | 寫入 | `details.opening_section` | |
| Word 連結五項 | `DT_WordPdfLink.*` | 寫入／開外部程式 | `word_links.list`（唯讀）、`word_links.refresh`（寫入） | 「開啟原始 Word」「重新指定來源」是人的操作，不做 agent 工具 |

### 3.3 建模

| Ribbon 功能 | 指令類別 | 類型 | agent 工具 | 改法 |
|---|---|---|---|---|
| 磁磚鋪設系統 | `ShowControlPanelCommand` | 寫入（大量） | `tiles.plan` / `.commit` | 見 4.1 合併提案 |
| 裝修配置與粉刷 | `Cmd_RoomFinishConfigurator` | 寫入 | `room_finish.apply` | 見 4.2 |
| 樓板貼房間 | `Cmd_FloorSnapToRoom` | 寫入 | `floors.snap_to_room` | 參數改為樓板 ID＋房間 ID |
| 降梁降板增築 | `Cmd_FinishBuildUp` | 寫入 | `finish_build_up.apply` | |
| 展開圖生成 | `DT_TileElevationGeneratorCommand` | 寫入 | `tiles.elevations` | |
| 快速配筋 | `Cmd_FastRebar` | 寫入 | `rebar.place` | 結構類，結果需技師審查 |
| 鋼筋檢核 | `Cmd_RebarCheck` | 唯讀分析 | `rebar.check` | **階段 2 第一個**：`RebarModelChecker` 已有摘要，直接包成工具 |
| 門族群批量深化 | `Cmd_BatchDoorUpdater` | 寫入（族群） | `doors.batch_update` | 共用參數檔挑選（`DoorSharedParamFilePicker`）改為參數 |
| 智慧剖面框 | `Cmd_AdaptiveRegion` | 純介面（DockablePane） | 不建立 | 見 4.4 |
| 透視圖索引面板 | `Cmd_PerspectiveMinimap` | 純介面 | 不建立 | 見 4.4 |
| 一鍵 4 視圖 | `Cmd_TileFourViews` | 純介面（視窗排列） | 不建立 | 見 4.4 |

## 4. 建議合併或不再需要（請人判斷）

以下只是提案，本 PR 沒有刪除任何指令。每一項請主管勾選「同意／不同意／再討論」。

### 4.1 `Cmd_TilePlanner` 併入磁磚鋪設系統（`ShowControlPanelCommand`）
- 現況：`Cmd_TilePlanner` 不在 Ribbon 上（只能從舊入口或程式呼叫），以 `PickObject` 選面後切 Part；磁磚鋪設系統的控制面板也做磁磚分割與鋪設，另有 `ConvertToEditableCommand` 由控制面板呼叫。
- 原因：同一件事有兩個入口、兩組授權 ToolId（`TilePlannerParts` 與控制面板），agent 化時只需要一個 `tiles.plan/commit`。
- 建議：把 `TilePlannerEngine` 當成 `tiles` Runner 的一個模式；下架 `Cmd_TilePlanner` 指令類別。需先確認是否有使用者靠舊 `.addin` 直接呼叫它。

### 4.2 `WallFinishCommand` 併入 `Cmd_RoomFinishConfigurator`
- 現況：兩個指令都只是開同一個 `RoomFinishConfiguratorWindow`；`WallFinishCommand` 不在 Ribbon，只出現在 `obfuscar.xml`。
- 原因：重複入口、沒有獨立功能。
- 建議：刪除 `WallFinishCommand`（並同步 `obfuscar.xml`）。

### 4.3 三套法規檢討收斂為一套
- 現況：
  - `Cmd_CodeCheck`（全條文，Ribbon 上，本 PR 已 agent 化）。
  - `Cmd_DesignReview`／`Cmd_ShowCodeReviewPane`／`Cmd_CreateCodeReviewProject`／`Cmd_ExportCodeReviewReport`（`Modules/DesignReview`，集合住宅 `Residential_TW` 規則庫、IssueTracker、DockablePane），**都不在 Ribbon 上**；`Cmd_CreateCodeReviewProject` 還會開交易寫入模型。
- 原因：兩套規則庫、兩種報告格式，agent 與人看到的結果可能不同；不在 Ribbon 的那套使用者也找不到。
- 建議：以 `code_check.run` 為唯一的法規檢討工具；`DesignReview` 中有價值的部分（問題追蹤、檢討案紀錄）改成讀 `code_check.run` 結果的「追蹤層」，其餘四個指令下架。需請熟悉 `Residential_TW` 規則的同事確認有沒有條文是 `CodeCheck` 沒涵蓋的。

### 4.4 視圖導覽介面不做 agent 工具（`Cmd_AdaptiveRegion`、`Cmd_PerspectiveMinimap`、`Cmd_TileFourViews`）
- 原因：功能是幫「人」在畫面上看模型（剖面框、透視索引、視窗排列），agent 讀模型不需要畫面。
- 建議：保留給人工檢視 agent 結果時使用，不投入 agent 化工時。若 agent 需要截圖給人看，另做 `views.capture`。

### 4.5 `Cmd_AIAssistant`（AI 模型分析）建議下架
- 現況：在 Revit 內開一個聊天視窗，自己呼叫外部 LLM（`AIApiSettings`），再透過 `AISelectionHandler`／`AICommandHandler` 操作模型。
- 原因：這等於第二條 AI→Revit 通道，與「只有一個 Reality Bridge／MCP」的規則衝突；API 金鑰存在使用者端；agent 改由 MCP 接上後，這個視窗的功能完全被取代。
- 建議：下一版從 Ribbon 移除，保留程式一個版本以便回退，之後刪除。`App.cs` 中 `_aiSelectionEvent`／`_aiCommandEvent` 一併移除。**涉及 App.cs，需主管核准。**

### 4.6 語言切換、登入、反饋、檢查更新
- 不做 agent 工具（人用的系統功能）。其中「檢查更新」屬 updater，依規則不由 agent 變更。

### 4.7 `Cmd_AutoRebar` 與 `Cmd_FastRebar`
- 現況：`Cmd_AutoRebar`（516 行，`RebarConfigWindow`）不在 Ribbon；`Cmd_FastRebar` 在 Ribbon，功能是「選梁柱套樣板配筋＋檢核」。
- 原因：疑似舊版與新版並存。
- 建議：確認 `Cmd_AutoRebar` 是否還有 `FastRebar` 沒有的配筋規則；沒有就下架，agent 只做 `rebar.place`（FastRebar）。

## 5. agent 呼叫方式（目前已可用的部分）

1. Revit 2024 啟動並開啟模型，Addin 啟動時註冊 `code_check.run`。
2. 人在 `Config/feature-flags.json` 把 `analysis` 改為 `true`（**正式環境設定，需主管核准**）。
3. MCP Client（Claude Code 等）呼叫 `run_code_check`：

```json
{
  "maxItems": 200,
  "writeReport": true,
  "settings": {
    "useGroups": ["H-2"],
    "zone": "住宅區",
    "siteAreaM2": 300,
    "maxCoverageRatio": 0.5,
    "maxFarRatio": 2.25
  }
}
```

4. 回傳：`summary`（各狀態條文數、規則結果數）、`articles`（不符合／需確認／資料不足的條文）、`items`（逐條規則結果：規則 ID、條文、元素 ID、量測值、限值、說明、所屬模型）、`ruleErrors`、`notes`、`report`（HTML／CSV 路徑，選用）、`bridgeWarnings`（含「暫存元素已回復」）。
5. agent 依 `items` 產生修改建議；修改模型要等第 3 階段的受控寫入合約。

## 6. 驗收要求

- 每個 agent 工具都要附 Revit 2024 實機測試（測試模型、步驟、結果截圖或 JSON），不得以編譯通過代替。
- 人用視窗與 agent 工具對同一模型的結果數量需一致。
- 回復型工具執行前後，模型的 `GetChangedElements`／檔案雜湊不變（存檔前後比對）。
