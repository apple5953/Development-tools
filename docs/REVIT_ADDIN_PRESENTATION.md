# 🏢 Revit Development Tools (v3) 模組化外掛架構全景簡報

本簡報針對此專案所有模組之技術實作、架構分層、業務流程與 Revit API 深度整合進行完整梳理。

---

## 📑 簡報章節導覽
1. **第一單元：法規檢討與空間分析階段 (Design Review & Spatial Clearance)**
2. **第二單元：空間裝修與磁磚排版階段 (Interior Finishing & Room Tile System)**
3. **第三單元：結構幾何與鋼筋配置階段 (Structural Modeling & Auto Rebar)**
4. **第四單元：施工出圖與圖紙批次排版階段 (Documentation, Sheets & Detailing)**
5. **第五單元：AI 模型稽核與外部橋接階段 (AI QC, ISO 19650 & BEAI Reality Bridge)**
6. **第六單元：外掛底層架構、權限與防護體系 (Core Architecture, License & Updater)**
7. **架構總結與核心實作類別矩陣速查表**

---

## 第一單元：法規檢討與空間分析階段 (Design Review & Spatial Clearance)

### 【階段常見狀況】
- **請照與方案初審人工測量**：專案進入建照申請或細部審查時，工程師需對建築物中數百處走廊淨寬、樓梯級高與級深、避難出入口、無障礙廁所迴轉空間及車位尺寸進行逐項核對。
- **異形多邊形空間視角受限**：面對非正交、斜向或 L 型複雜空間，Revit 原生剖面框（Section Box）僅支援平行於坐標軸的長方體，無法乾淨剖切隔離目標空間。

### 【痛點】
- **人工核算漏檢率高**：法規標準繁複且受建築類型影響（如集合住宅 vs 供公眾使用/學校），人工拉尺檢測極易遺漏。
- **3D 觀察視角遮蔽**：長方體 Section Box 會把相鄰無關空間與外牆切入，導致內部裝修與施工盲區被遮擋，缺乏任意角度自適應剖切工具。

### 【解法】
- **參數化法規引擎 (`DesignReview` 模組)**：
  - 內建台灣《建築技術規則 (BCR)》法規資料庫，支援本地設定檔與雲端 GitHub 動態同步 ([`LocalRegulationDatabase.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/DesignReview/Services/LocalRegulationDatabase.cs))。
  - 導入「幾何長寬比狹長推定演算法」，自動辨識無名通道是否屬於走廊，並區分單側居室與雙側居室標準。
  - 自動生成專屬法規檢核報表圖紙（Sheet G-101）與 Drafting View ([`ReportSheetService.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/DesignReview/Services/ReportSheetService.cs))。
- **任意多邊形空間區域剖切 (`AdaptiveSectionBox` 模組)**：
  - 採用多邊形稜柱體幾何演算法 ([`PolygonPrism.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/AdaptiveSectionBox/Models/PolygonPrism.cs))。
  - 實作 Revit [`IDirectContext3DServer`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/AdaptiveSectionBox/Rendering/DirectContext3DServer.cs)，透過 Direct3D/OpenGL 在 3D 視圖中進行毫秒級硬體加速線框渲染。
  - 掛載 Win32 [`RevitWndProcHook.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/AdaptiveSectionBox/Interaction/RevitWndProcHook.cs) 攔截視窗滑鼠事件，提供可直接拖曳的 3D 互動 Gizmo，並非同步建立 `DirectShape` 代理量體輔助剖切。

### 【功能】
- [`Cmd_CreateCodeReviewProject.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_CreateCodeReviewProject.cs) / [`Cmd_ShowCodeReviewPane.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_ShowCodeReviewPane.cs)：啟動 BCR 檢討側邊欄，自動稽核：
  - **樓梯檢核 (`StairChecker`)**：級高（$\le 160$ 或 $180\text{ mm}$）、級深（$\ge 260$ 或 $240\text{ mm}$）、淨寬（$\ge 1200$ 或 $1400\text{ mm}$）。
  - **走廊檢核 (`CorridorChecker`)**：單側居室（$1200\text{ mm}$）、雙側居室（$1600\text{ mm}$ / 供公眾 $2400\text{ mm}$）。
  - **門扇與無障礙 (`DoorChecker`)**：安全梯避難門淨寬（$\ge 900\text{ mm}$）、避難層大門（$\ge 1200\text{ mm}$）、無障礙門檻高差（$\le 20\text{ mm}$）、防火門時效（$\ge 1.0\text{ hr}$）。
  - **其他空間法規**：坡道斜率（$\le 1:12$）、居室淨高（$\ge 2100\text{ mm}$）、車位尺寸（一般 $2500\times 5500\text{ mm}$ / 無障礙 $3500\times 5500\text{ mm}$）。
- [`Cmd_ExportCodeReviewReport.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_ExportCodeReviewReport.cs)：自動於 Revit 中建立 Drafting 視圖並排版至圖紙 `G-101`。
- [`Cmd_AdaptiveRegion.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_AdaptiveRegion.cs)：啟動 Dockable Pane 智慧空間區域管理器，支援多邊形拉伸、即時 3D 線框預覽與 Gizmo 控制。

---

## 第二單元：空間裝修與磁磚排版階段 (Interior Finishing & Room Tile System)

### 【階段常見狀況】
- **起磚排版與碎磚調整**：衛浴、客廳、走道貼磚需對齊起磚點或特定門口基準，避免邊緣出現小於三分之一磚寬的零碎磚。
- **平面隔間動態修訂**：建築牆體修改移動時，原有手繪磁磚線或粉刷層脫節報廢。
- **樓板輪廓脫節**：隔間牆平移後，樓板邊界與房間完成面不吻合。

### 【痛點】
- **算磚失準與耗損過大**：手動估算無法精確統計起磚損耗與塊數。
- **樓板草圖自交報錯**：手動調整樓板草圖時，微小角度偏差易造成 Revit 丟出「線條自交或無法閉合」錯誤並強迫還原。
- **裝修層門窗開口扣減繁瑣**：長實體粉刷牆時，若未精確扣除門窗，導致明細表工程量嚴重虛增。

### 【解法】
- **面驅動幾何分析與動態關聯 (`TilePlannerEngine` + `TileUpdater`)**：
  - 註冊 Revit **DMU (Dynamic Model Update)** ([`TileUpdater.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Core/TileUpdater.cs))，監聽牆體與樓板幾何變更，牆動磁磚自動連動更新。
  - **8 方向射線容錯投票法 ([`RoomGeometryAnalyzer.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Algorithms/RoomGeometryAnalyzer.cs))**：解決控制器因放置於牆角導致 `anchor.Room` 回傳 null 的問題，以 8 個方向多重距離向外探測空間進行投票決策。
- **草圖拓撲原位編輯演算法 ([`FloorSketchEditor.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/FloorTools/FloorSnapToRoom/FloorSketchEditor.cs))**：
  - 不刪除樓板實體（保留 ElementId、參數與託管物件），直接透過 `sketch.GetAllElements()` 提取 `CurveElement`。
  - 投影至 2D 平面，以 $5^\circ$ 平行度與 $300\text{ mm}$ 吸附門檻計算平移向量。
  - 聯立求解直線交點，呼叫 `ceToModify.SetGeometryCurve(newC, true)` 更新幾何，確保草圖 100% 封閉。
  - 獨立 `TransactionGroup` 封裝，單塊樓板失敗自動 Rollback，不影響其餘成功物件。
- **實體粉刷自動長出與精準洞口剪裁 ([`WallFinishCommand.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/WallFinishCommand.cs))**：
  - 沿房間 Finish 邊界長出薄牆，自動提取門窗族群實體洞口扣減重疊區域。

### 【功能】
- [`ShowControlPanelCommand.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/ShowControlPanelCommand.cs)（磁磚排版主面板）：自訂磁磚規格（如 $30\times 60$, $60\times 60\text{ cm}$）、起磚錨點偏移、排磚旋轉角度，並一鍵產生 3D 實體磚或材質填充線。
- [`Cmd_FloorSnapToRoom.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_FloorSnapToRoom.cs)（樓板貼齊房間）：一鍵將選取樓板草圖邊界吸附至所屬房間完成面。
- [`WallFinishCommand.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/WallFinishCommand.cs)（自動粉刷）：自動生成粉刷牆並扣減開口。
- [`Cmd_RoomFinishConfigurator.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_RoomFinishConfigurator.cs)：DataGrid 批量配置全專案房間的地坪、牆面、天花板與踢腳板材質編號。

---

## 第三單元：結構幾何與鋼筋配置階段 (Structural Modeling & Auto Rebar)

### 【階段常見狀況】
- **結構樑柱鋼筋手動建模繁複**：Revit 原生鋼筋工具需手動在剖面中逐根繪製與指定參照面。
- **耐震法規配置複雜**：箍筋需依台灣 CNS 560 與耐震規範區分「端部圍束加密區」與「中央區」。

### 【痛點】
- **局部坐標變換困難**：斜樑或旋轉柱的局部坐標系（Local Coordinate System）各異，手動排筋易產生軸向偏斜。
- **搭接與續接避讓困難**：交接處鋼筋碰撞難以直觀排查，續接器缺乏自動化避讓配置。

### 【解法】
- **幾何坐標框架提取 (`PlacementFrame.FromElement`)**：
  - 自動讀取混凝土構件（樑或柱）之定位軸線與邊界幾何，建構出帶有 $X, Y, Z$ 軸向量之局部空間框架。
- **CNS 規範連動配筋演算法 ([`Cmd_AutoRebar.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_AutoRebar.cs))**：
  - 自動換算英制內部單位與 CNS 標準號數（#3, #4, #8, #10 等）。
  - 自動識別端部加密區（如長度取 $2H$ 或柱端 $L/6$），依不同間距（如 $100\text{ mm}$ 與 $150/200\text{ mm}$）配置閉合箍筋與繫筋。
  - 支援續接器模式（分段避讓標記）與搭接長度自適應。

### 【功能】
- [`Cmd_AutoRebar.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_AutoRebar.cs)（可調式鋼筋配置）：點選混凝土樑或柱，彈出規格視窗，支援主筋號數、排數、保護層厚度與耐震加密區參數調整，一鍵長出 3D 鋼筋籠。

---

## 第四單元：施工出圖與圖紙批次排版階段 (Documentation, Sheets & Detailing)

### 【階段常見狀況】
- **室內立面展開圖排版極耗時**：衛浴與大廳四面牆需依序切剖面、調整視圖樣板、拉到圖紙上左右對齊。
- **圖紙編號衝突互換**：客戶要求調換圖號時（如 A01 改 A02，A02 改 A01），Revit 因唯一性約束直接拋錯中斷。
- **外部規範說明書圖紙整合脫節**：Word (.docx) 施工說明書需手動截圖或貼字進圖紙，改版時失去連動。

### 【痛點】
- **視口純幾何邊界難以提取**：Viewport 外框包含標高、軸網與剖面符號，手動排版無法達成「等高無縫拼接」。
- **跨圖紙視口搬移限制**：Revit 規定單一視圖僅能放於一張圖紙，跨圖紙搬移需手動刪除再置入，遺失原有對齊。

### 【解法】
- **無縫展開圖自動化排版演算法 ([`DT_TileElevationGeneratorViewModel.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/TileElevationGenerator/DT_TileElevationGeneratorViewModel.cs))**：
  - **註解暫時隱藏法**：置入圖紙瞬間，在背景設定 `view.AreAnnotationCategoriesHidden = true`，排除標註干擾，取得純模型邊界寬度 $W$ 與高度 $H$。
  - **樓面高程補償演算法**：讀取各牆底實際 Level 高程與 CropBox 中心，計算 Y 軸偏移量：
    $$\Delta Y = \frac{\text{LevelElevation} - Z_{\text{center}}}{\text{Scale}}$$
    將所有展開圖的室內地坪完成面強制對齊在同一條水平線上。
  - **連續等高推展**：以 $X_{\text{current}} = X_{\text{current}} + W$ 實現視口零縫隙拼接。
- **兩階段暫存法圖紙更名 ([`Cmd_BatchSheetRenamer.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_BatchSheetRenamer.cs))**：
  - 遇到編號互換衝突時，第一階段加上隨機 Temp 前綴跳脫約束，第二階段再批次修正至目標圖號。
- **Word 轉向量 PDF 熱連結管線 (`WordPdfLink` 模組)**：
  - 背景自動將 Word (.docx) 轉為高解析度 PDF，以向量方式連結進 Revit Sheet。
  - 於 Extensible Storage 記錄檔案關聯與 Hash，提供「一鍵更新選取/全部連結」。
- **跨專案標準與視圖轉移 ([`SheetTransferService.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/SheetTransfer/Services/SheetTransferService.cs))**：
  - 突破官方「傳送專案標準」無法轉移放有視圖之圖紙的限制。依序同步：專案參數 $\rightarrow$ 視圖樣板 $\rightarrow$ 複製繪圖視圖/明細表 $\rightarrow$ 重建圖紙與 Viewport。

### 【功能】
- [`DT_TileElevationGeneratorCommand.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/DT_TileElevationGeneratorCommand.cs)（磁磚展開圖生成）：自動依房間剖切各面牆，一鍵生成無縫拼接且地坪對齊之圖紙。
- [`Cmd_BatchSheetRenamer.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_BatchSheetRenamer.cs)（圖紙批次更名）：表格化批次編輯圖號與名稱，防衝突置換。
- [`Cmd_QuickViewCreator.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_QuickViewCreator.cs)（快速開圖）：批次複製平面圖（結構/詳圖/從屬），自動套用樣板並可選同步建立對應圖紙。
- [`Cmd_SheetViewPlacer.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_SheetViewPlacer.cs)（圖紙拖曳排版）：支援跨圖紙拖曳移動視埠，自動在舊圖紙刪除並於新圖紙偏移重建。
- [`Cmd_QuickDimension.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_QuickDimension.cs)（快速尺寸標註）：一鍵標註柱中心、平行牆中心線或門窗洞口強參照。
- `LinkWordCommand` & `WordManageDropdown`：Word 說明書向量關聯至圖紙並支援熱更新。
- [`Cmd_SheetTransfer.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/SheetTransfer/Commands/Cmd_SheetTransfer.cs)：跨專案完整轉移圖紙、視圖樣板與參數。

---

## 第五單元：AI 模型稽核與外部橋接階段 (AI QC, ISO 19650 & BEAI Reality Bridge)

### 【階段常見狀況】
- **ISO 19650 BIM 交付審核**：需快速確認全專案所有元件的 `Assembly Code`（Uniclass / Uniformat）與 `OmniClass` 分類編碼是否漏填。
- **外部 AI 整合需求**：設計團隊期望利用 ChatGPT / Claude 審查模型數據，但外部請求無法直接與 Revit API 安全通訊。

### 【痛點】
- **跨執行緒呼叫崩潰**：Revit API 要求必須在 UI 主執行緒執行，外部 HTTP 請求若直接訪問 Revit DB 會導致 Revit 閃退。
- **AI 誤改模型風險**：若授予 AI 寫入權限，容易因理解誤差或英制公制單位混淆而破壞模型幾何。

### 【解法】
- **BEAI Reality Bridge 安全橋接架構**：
  - **唯一安全邊界**：禁止建立第二套橋接；本地服務僅綁定 Loopback（`http://127.0.0.1:8765/`）。
  - **嚴格唯讀 (V0.1 Read-Only)**：底層 [`GenericRevitReader`](file:///d:/Room%20Tile%20Local%203%20System/BEAI/RealityBridge/BEAI.QueryHost/Core/GenericRevitReader.cs) 僅執行模型數據萃取，嚴禁開啟任何 Revit `Transaction`。
  - **ExternalEvent 非同步排程**：HTTP 請求進入時封裝為 `BeaiQueryRequest` 排入佇列，透過 `ExternalEvent.Raise()` 排定於主執行緒安全讀取，每次回呼限制處理 10 筆，避免卡死 Revit。
  - **MCP 標準協定支援 (`BEAI/MCP`)**：採用 Model Context Protocol (TypeScript SDK)，提供 `system_health`、`get_active_document`、`list_levels`、`query_elements` 等標準工具介面。
- **AI 雙重安全確認防改機制 (`AIAssistant` 模組)**：
  - AI 提出修改建議時，聊天視窗跳出「⚡ 立即在 Revit 中執行 AI 修改」卡片。
  - 點擊後跳出 Revit 原生 `TaskDialog`，清楚展示：【圖元 ID】、【參數名稱】、【舊值 $\rightarrow$ 新值】，並自動將英呎內部值轉換為毫米（mm）。使用者點選「Yes」才啟動 Transaction，點選「No」直接 Rollback。

### 【功能】
- [`Cmd_AIAssistant.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_AIAssistant.cs)（AI 模型分析與 ISO 19650 審查助手）：
  - 支援 OpenAI 相容 API（GPT-4o、本地 Ollama 等）。
  - **選取模式**：讀取選中元件之幾何、材質與尺寸做局部審查。
  - **全專案掃描模式**：統計 ISO 19650 分類代碼填寫率，列出漏填代碼與未定義裝修之房間清單。
- [`ShowStatusCommand`](file:///d:/Room%20Tile%20Local%203%20System/BEAI/RealityBridge/BEAI.QueryHost/Commands/ShowStatusCommand.cs)（BEAI 查詢狀態）：診斷本機 8765 通道、Revit 模型狀態與 ExternalEvent 連線健康度。
- `BEAI MCP Server`：提供大語言模型標準化的唯讀模型探測能力。

---

## 第六單元：外掛底層架構、權限與防護體系 (Core Architecture, License & Updater)

### 【階段常見狀況】
- **多成員權限分級管理**：企業內需依據成員職責動態開通特定模組權限。
- **外掛版本熱更新與部署**：Revit 外掛 DLL 一旦載入便被系統鎖定，傳統覆蓋更新極其痛苦。

### 【痛點】
- **同步驗證導致 Revit 假死**：若啟動時進行同步網路驗證，網路微小延遲便會造成 Revit 卡死數十秒。
- **更新失敗外掛損毀**：更新若被防毒軟體中斷，易造成 DLL 損毀且無法還原。

### 【解法】
- **Google OAuth 2.0 + Apps Script 雲端白名單 ([`GoogleAuthManager.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Core/GoogleAuthManager.cs))**：
  - 瀏覽器授權登入，雲端 Google Sheet 後台控制 `AllowedTools` 欄位（以逗號分隔開通功能，如 `Tiling,WallFinish`）。
  - 全程採 `Task.Run` 非同步執行，完全不阻礙 Revit 主執行緒。
- **獨立無感更新器 ([`UpdateManager.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Core/UpdateManager.cs) + `DevelopmentTools.Updater`)**：
  - 背景比對 `update_manifest.json` 與 SHA256 雜湊值。
  - 下載更新檔後，在 Revit 關閉時由外部獨立的 `Updater.exe` 覆蓋檔案，具備完整備份與自動 Rollback 機制。
  - 發行流程整合 `Obfuscar` 程式碼混淆，兼顧智慧財產權防護。

### 【功能】
- [`LoginCommand.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/LoginCommand.cs)：Google 帳號快速登入驗證與模組開通。
- [`FeedbackCommand.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/FeedbackCommand.cs)：內建問題回報，自動附帶 `RevitAddinLog.txt` 診斷日誌直傳後台。
- [`CheckUpdateCommand.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/CheckUpdateCommand.cs)：手動/自動檢查新版本並一鍵升級。
- [`Cmd_ChangeLanguageBase.cs`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_ChangeLanguage.cs)（繁中 / 英文 / 日文）：動態多國語系切換，即時更新 Ribbon 介面文字與 Tooltip。

---

## 總結：核心架構與實作類別速查矩陣

| 業務模組 | 核心技術 / C# 實作類別 | 解決之 Revit 原生痛點 | 主要業務與產出價值 |
| :--- | :--- | :--- | :--- |
| **磁磚排版系統** | [`TilePlannerEngine`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Core/TilePlannerEngine.cs)<br>[`RoomGeometryAnalyzer`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Algorithms/RoomGeometryAnalyzer.cs)<br>[`TileUpdater`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Core/TileUpdater.cs) (DMU) | 牆體移動排磚失效、控制器牆角偵測失敗 | 牆動磚自動更新、8方向射線容錯、出量精確至單塊磚 |
| **樓板貼齊房間** | [`FloorSketchEditor`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/FloorTools/FloorSnapToRoom/FloorSketchEditor.cs)<br>[`FloorSnapGeometryUtils`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/FloorTools/FloorSnapToRoom/FloorSnapGeometryUtils.cs) | 樓板重畫遺失參數、草圖微小誤差自交報錯 | 原位修改 ModelCurve，保留 ElementId，自動交點閉合 |
| **展開圖生成** | [`DT_TileElevationGeneratorViewModel`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/TileElevationGenerator/DT_TileElevationGeneratorViewModel.cs)<br>[`WallElevationViewCreator`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/TileElevationGenerator/WallElevationViewCreator.cs) | 視口帶標註無法拼接、地坪高低不齊 | 暫時隱藏註解量測淨幾何、高程差 Y 軸自動對齊無縫排版 |
| **圖紙與視圖** | [`Cmd_BatchSheetRenamer`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_BatchSheetRenamer.cs)<br>[`Cmd_SheetViewPlacer`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_SheetViewPlacer.cs)<br>[`Cmd_QuickViewCreator`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_QuickViewCreator.cs) | 圖號對調唯一性衝突、視圖不能跨圖紙拖放 | 兩階段暫存法改名、拖曳時後台重置 Viewport、一鍵套板開圖 |
| **法規自動檢核** | [`CodeReviewCheckers`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/DesignReview/Services/CodeReviewCheckers.cs)<br>[`LocalRegulationDatabase`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/DesignReview/Services/LocalRegulationDatabase.cs)<br>[`ReportSheetService`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/DesignReview/Services/ReportSheetService.cs) | 人工拉尺漏檢、規範標準變動不易維護 | 走廊/樓梯/避難門/無障礙自動檢核，一鍵出 Sheet G-101 |
| **任意剖切區域** | [`DirectContext3DServer`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/AdaptiveSectionBox/Rendering/DirectContext3DServer.cs)<br>[`RevitWndProcHook`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/AdaptiveSectionBox/Interaction/RevitWndProcHook.cs)<br>[`PolygonPrism`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/AdaptiveSectionBox/Models/PolygonPrism.cs) | 官方 Section Box 只能切正長方體 | 任意多邊形柱體剖切，GPU 視口即時渲染，滑鼠 Gizmo 控制 |
| **Word 連結管理** | [`WordPdfExporter`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/WordPdfLink/Services/WordPdfExporter.cs)<br>[`RevitPdfLinkService`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Modules/WordPdfLink/Services/RevitPdfLinkService.cs) | Revit 無法直接插入並連動 Word 說明書 | Word 轉向量 PDF 連結圖紙，支援 Hash 感知一鍵熱重載 |
| **鋼筋自動配置** | [`Cmd_AutoRebar`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Commands/Cmd_AutoRebar.cs) | 斜向樑柱坐標難對齊、手動排箍筋費時 | 自動提取局部坐標系，依 CNS 560 自動建立端部加密箍筋 |
| **BEAI 橋接 & AI** | [`GenericRevitReader`](file:///d:/Room%20Tile%20Local%203%20System/BEAI/RealityBridge/BEAI.QueryHost/Core/GenericRevitReader.cs)<br>[`BeaiQueryExternalEventHandler`](file:///d:/Room%20Tile%20Local%203%20System/BEAI/RealityBridge/BEAI.QueryHost/Core/BeaiQueryExternalEventHandler.cs) | AI 誤改模型崩潰、外部呼叫跨執行緒死鎖 | 嚴格唯讀安全邊界、ExternalEvent 佇列、TaskDialog 二次確認 |
| **權限與熱更新** | [`GoogleAuthManager`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Core/GoogleAuthManager.cs)<br>[`UpdateManager`](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/Core/UpdateManager.cs) | 驗證卡死主執行緒、DLL 被 Revit 鎖死無法更新 | 非同步 OAuth 驗證、細粒度模組授權、關閉時自動置換與回滾 |
