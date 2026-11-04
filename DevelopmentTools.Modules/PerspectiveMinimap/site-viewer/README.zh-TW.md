# 現場檢核點手機檢視器（Apps Script MVP v0.1）

設計說明見 `../docs/QR_SITE_VIEWER_DESIGN.zh-TW.md`。

## 檔案
- `apps-script/Code.gs`：`doGet` 讀 Drive 站點資料夾、驗證 token、輸出頁面。
- `apps-script/Index.html`：手機頁面（模型透視 / 站立位置 / 疊圖比對）。
- `sample/HS-3F-012/station.json`：站點資料範例（token 請換成隨機值）。

## 部署
1. 在 Google Drive 建立 `BIM現場檢核/<專案>/`，每個站點一個資料夾（名稱＝stationId），放 `station.json`、`perspective.jpg`、`locator.png`。
2. 新建 Apps Script 專案，貼上 `Code.gs` 與 `Index.html`（檔名需為 `Index`）。
3. 專案設定 > 指令碼屬性：`ROOT_FOLDER_ID` = 上述專案資料夾 id。
4. 部署 > 新增部署 > 網頁應用程式：執行身分「我」；存取權依現場人員帳號狀況選擇。
5. QR Code 內容：`<網頁應用程式網址>?s=<stationId>&t=<token>`。

## 已知限制
- Apps Script 頁面跑在 iframe，部分手機瀏覽器會擋 `getUserMedia`；頁面已提供「拍照比對」替代。
- 圖片以 base64 內嵌，單張上限 4 MB，建議輸出 1–2 MB 的 JPG。
- Revit 端「發佈檢核點」（輸出圖片、station.json、QR 標籤）尚未實作。
