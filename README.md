# Revit 整合型外掛系統框架 - Development tools (v3)

本系統是一套專為 Revit 設計的**整合式外掛平台框架 (Modular Add-in Platform Framework)**，命名為 **「Development tools」**。
透過單一的 DLL 核心與統一的 Google Sheets 雲端資料庫，可實現精準的使用者授權、時長統計、即時反饋，並支援動態插拔多種子外掛（例如：磁磚鋪設系統、自動粉刷工具等）。

---

## 🛠️ Revit 介面架構與按鈕詳解

安裝後，Revit 上方會出現名為 **「Development tools」** 的 Ribbon 頁籤，底下劃分為三個面板：

### ▍ 面板一：【系統管理】
管理使用者登入狀態與意見反饋管道。

| 按鈕名稱 | 執行指令 | 詳細功能說明 |
| :--- | :--- | :--- |
| **🔑 Google 登入** | `LoginCommand` | **【登入/帳號管理】** 點選後開啟瀏覽器進行 Google 驗證。若已登入，會顯示目前帳號，並提供「切換帳號/登出」選項。 |
| **💬 問題與反饋** | `FeedbackCommand` | **【反饋提交】** 開啟 Glassmorphic 意見回報視窗，填寫後點選送出即可直接將 Bug 或優化建議同步至雲端 Excel。 |

### ▍ 面板二：【空間排版】
核心工具：空間自動化幾何裁剪磁磚鋪設系統。

| 按鈕名稱 | 執行指令 | 新手使用指南（磁磚鋪設五部曲） |
| :--- | :--- | :--- |
| **📌 1. 生成鋪設網格** | `PickFaceCommand` | **【排版第一步】** 點選任意牆面或地板，讀取該材質內設定的磁磚規格與縫隙，在選定面上 Paint 暫時的網格格線供預覽排版，此步驟不破壞模型。 |
| **✔️ 2. 確認材質改寫** | `ConfirmMaterialPatCommand` | **【排版第二步】** 點選已 Paint 暫時網格的面，程式會複製並改寫該面材質的前景填充線（`.pat` 格式），正式將磁磚格線固定在模型上。 |
| **🧱 3-1. 建立牆磚實體** | `Generate3DTilesCommand` | **【排版第三步（牆）】** 點選已改寫前景填充線的牆面，程式會讀取裝修層材質厚度，自動生成獨立的 3D 原生磁磚牆元件。 |
| **🟫 3-2. 建立地磚實體** | `Generate3DFloorTilesCommand` | **【排版第三步（地）】** 點選已改寫前景地坪，自動在上方生成 3D 原生地磚元件。高程自動對齊完成面，並與標高（Level）完美綁定。 |
| **🎨 局部變更材質** | `ChangeLocalTileMaterialCommand` | 在生成 3D 磁磚後，若部分區域（如腰線、花磚）需要更換花色，可多選磁磚實體批次替換局部材質。 |
| **📐 轉換為可編輯** | `ConvertToEditableCommand` | 將 3D 磁磚實體就地轉換為 Revit 原生 Wall/Floor。轉換後即可雙擊或點選「編輯輪廓」來裁剪不規則邊角（避開管道間）。 |
| **📊 平面幾何統計** | `CalculatePlaneQuantityCommand` | 基於排版裁剪引擎，多選已確認鋪貼的面，直接估算所需磁磚片數。 |
| **🔍 3D 實體統計** | `Calculate3DQuantityCommand` | 多選房間，統計區域內所有已生成的 3D 磁磚實體，列出整磚數、裁切損耗與總面積。 |
| **📄 建立明細表** | `CreateScheduleCommand` | 一鍵在 Revit 項目瀏覽器中產生原生磁磚工程量統計明細表。 |
| **📥 匯出 Excel** | `ExportExcelCommand` | 蒐集專案磁磚數據，匯出為 Excel/CSV 明細報表。 |
| **🧹 移除預覽線** | `DeleteTilesCommand` | 清除第一步 Paint 在面上的暫時填充預覽線，還原乾淨視圖，不影響已生成的 3D 實體。 |

### ▍ 面板三：【粉刷裝修】
展示模組化動態授權驗證之 Mock 子工具。

| 按鈕名稱 | 執行指令 | 詳細功能說明 |
| :--- | :--- | :--- |
| **🧱 自動粉刷** | `WallFinishCommand` | **【子外掛細粒度授權驗證】** 點擊時外掛會向伺服器請求 `WallFinish` 授權。若雲端白名單中未開通此功能，將彈出「未授權」視窗與超連結按鈕。 |

---

## 🚀 新手快速安裝指南 (3 分鐘上手)

為了讓拿到外掛發布包的人能夠無痛安裝，我們將步驟簡化至極致：

### 📌 步驟 1：一鍵解壓與安裝
1.  下載並解壓縮外掛壓縮包 **`RTS_Plugin_Release.zip`**。
2.  **【📢 重要防呆】**：請務必先將壓縮包**完整解壓縮**，不要在壓縮檔內部直接執行安裝程式！
3.  進入解壓縮後的資料夾，滑鼠雙擊執行 **`install.bat`**。
4.  當視窗跳出 `🎉 安裝成功！` 後，按任意鍵關閉視窗，安裝即完成。

### 📌 步驟 2：首次啟動 Revit 載入
1.  打開您的 **Revit 2024**。
2.  因為是首次安裝，Revit 會彈出安全提示：`安全警告 - 未簽署的外掛`。
3.  請務必點選 **`永遠載入 (Always Load)`**，以確保每次啟動外掛皆正常運作。

### 📌 步驟 3：Google 登入驗證
1.  點選 Revit 上方 `Development tools` 頁籤，點選任意按鈕。
2.  外掛會彈出提示並自動打開網頁瀏覽器，引導您進行 Google 帳號安全性驗證。
3.  驗證成功後，系統將實時比對權限。如果是全新帳號，雲端會**自動註冊**並預設為您開通 `Tiling` (磁磚鋪設) 工具權限！

---

## 🔑 雲端管理與細粒度授權設定 (Google Sheets + Apps Script)

本系統支援為不同用戶個別配置子工具的存取權限。

### 1. 試算表 `Users` 欄位設計
您的 Google 試算表中，`Users` 工作表應包含以下標題（欄位順序可自由調整，系統會自動定位）：

| 欄位名稱 | 說明 | 範例值 |
| :--- | :--- | :--- |
| **Email** | 使用者的 Google 帳號 | `user@gmail.com` |
| **Status** | 總開關狀態（Allowed = 啟用 / Blocked = 封鎖） | `Allowed` |
| **AllowedTools** | 授權該用戶使用的子工具 ID (半形逗號分隔) | `Tiling,WallFinish` |
| **RegisterTime** | 首次自動註冊時間 | `2026-05-31...` |
| **LastActiveTime** | 最後活動時間 | `2026-05-31...` |
| **UsageCount** | 累計點擊使用次數 | `15` |
| **TotalDurationMinutes** | 累計在 Revit 中的開啟使用時長 (分鐘) | `45.5` |

### 2. 雲端 Apps Script 部署步驟
1.  將本機 [google_apps_script.js](file:///d:/Room%20Tile%20Local%203%20System/DevelopmentTools.Addin/google_apps_script.js) 的完整程式碼複製。
2.  貼入試算表中的 **「擴充功能 > Apps Script」**，儲存。
3.  點擊右上角 **「部署 > 新增部署」**，類型選擇「網頁應用程式」，將存取權限設為 **「所有人」** (Anyone)。
4.  複製產生的部署網頁 URL，填入外掛設定檔 `platform_config.json` 的 `GoogleSheetApiUrl` 中。

---

## 💻 本地測試與模擬伺服器
如果您在沒有網路連線或不想影響雲端資料庫時進行測試，我們在根目錄提供了一個本地輕量模擬伺服器 [mock_auth_server.py](file:///d:/Room%20Tile%20Local%203%20System/mock_auth_server.py)：
1.  將 `platform_config.json` 的 `GoogleSheetApiUrl` 改為 `"http://localhost:8080/"`。
2.  在終端機執行 `python mock_auth_server.py`。
3.  開啟 Revit，即可透過以下預設帳號模擬各種授權情境：
    - `dimadima5953@gmail.com`：只能使用磁磚系統，點自動粉刷會被拒絕。
    - `admin@example.com`：擁有磁磚與粉刷所有工具權限。
    - `blocked_user@example.com`：全域停用帳號。
