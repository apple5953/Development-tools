# 透視圖 × 現場 QR Code 檢核 — 設計草案 v0.1

> 狀態：草案。託管方式已決定：**Google Apps Script + Google Drive**（2026-11-04）。
> 所屬模組：`DevelopmentTools.Modules/PerspectiveMinimap`（不新增 RealityBridge，不經 MCP）。

## 1. 目標

現場人員站在指定點位、用手機掃 QR Code，就看到「BIM 模型在這個位置、這個方向、這個高度應該長什麼樣子」，
直接對照現場有沒有做錯（開口位置、管線高程、設備位置、裝修收邊等）。

能不能「直接看出來」取決於三件事一致：**站點 XY、視線高度、視角方向/FOV**。
這也是為什麼先修相機高度：高度差 30 cm，梁下管線與開口的相對位置就會整個看錯。

## 2. 使用流程

```
[Revit] 透視圖索引面板
  1. 在小地圖放置／調整檢核站點（XY、視高、方向）
  2. 「📤 發佈現場檢核點」→ 對每個站點：
       - 輸出透視圖圖片（直式 3:4，FOV 對齊手機主鏡頭）
       - 輸出站點平面定位小圖（含站立點與朝向箭頭）
       - 產生 station.json（中繼資料）
       - 產生 QR Code（只含站點網址，不含圖片）
  3. 輸出 A6 QR 標籤 PDF（站點編號、樓層、朝向箭頭、視高）

[現場]
  4. 標籤貼在站點旁的柱／牆，地面噴漆或貼紙標示站立點
  5. 手機掃描 → 瀏覽器開啟站點頁面：
       - 模型透視圖（可放大）
       - 「舉起手機對準」模式：手機相機畫面 + 半透明模型疊圖（不透明度滑桿）
       - 平面定位圖 / 樓層 / 視高 / 模型版本日期
       - 「回報問題」：拍照 + 文字 → 進入既有議題追蹤
```

## 3. 站點資料（station.json）

```json
{
  "stationId": "HS-3F-012",
  "token": "隨機 128-bit，不可猜",
  "project": "華山司法園區新建工程",
  "level": { "name": "3F", "displayElevationMm": 8400 },
  "eye": { "xMm": 0, "yMm": 0, "heightAboveFloorMm": 1500 },
  "direction": { "yawDeg": 90.0, "pitchDeg": 0.0, "fovDeg": 65.0 },
  "coordinates": "Revit 內部座標 + 專案基準點換算說明",
  "modelRevision": { "file": "xxx.rvt", "exportedAt": "2026-11-04T10:00:00+08:00", "by": "BIM 工程師" },
  "images": { "perspective": "perspective.jpg", "locator": "locator.png" }
}
```

QR Code 只放 `https://<host>/s/<stationId>?t=<token>`：
- 模型更新時重新發佈同一 stationId，**現場標籤不用重印**。
- 頁面固定顯示「模型版本日期」，避免拿舊圖驗收。

## 4. 讓疊圖對得上的關鍵參數

| 項目 | 建議 | 理由 |
|---|---|---|
| 視高 | 預設 1500 mm（手機拿在胸前約 1.4–1.5 m） | 與「站姿 1.5m」預設一致；標籤上印出視高 |
| 高度基準 | 以樓層內部座標 Z（`Level.ProjectElevation`）計算 | 本次修正內容；避免專案基準點/測量點高程造成整體偏移 |
| 樓板面 vs 樓層線 | 後續可選「以樓板頂面為基準」（向下射線找樓板） | 有粉刷層或降板時，樓層線不等於人站的地面 |
| 畫面比例 | 直式 3:4 | 對應手機直拿照相 |
| FOV | 以手機主鏡頭約 65–70° 為預設，可設定 | FOV 不一致時疊圖會縮放錯位 |
| 方向 | 標籤印朝向箭頭 + 參考物（「面向 C 軸柱」） | 方向偏 5° 就難以對齊 |

進階（第 3 階段）：每個站點另外輸出 6 張 90° 立方體貼圖 → 網頁 360° 檢視。
這樣現場不需精準對方向，轉手機就能看四周，適合機電密集區。

## 5. 分階段

| 階段 | 內容 | 需要 |
|---|---|---|
| P0（本 PR） | 修正相機高度基準錯誤 | Revit 實機驗收 |
| P1 MVP | 發佈站點：輸出圖片 + json + QR + 標籤 PDF；靜態檢視頁 | 決定託管方式；QR 函式庫（如 QRCoder，MIT）需負責人核可 |
| P2 | 手機相機疊圖、模型版本歷史、站點批次重新發佈 | — |
| P3 | 360° 立方體貼圖、問題回報串接議題追蹤 | 議題追蹤儲存位置 |

## 6. 架構與規範對照

- Revit 端：圖片輸出（`Document.ExportImage`）與建立視圖都放在本模組，經既有 `RevitEventDispatcher`（ExternalEvent）在 Revit UI 執行緒執行；**不碰 RealityBridge**，不新增 MCP 端點。
- 網頁端：純靜態檔案（HTML + 圖片 + json），不需要後端即可運作；回報問題為 P3 才需要寫入端。
- 安全：圖說屬業主機密，網址需帶隨機 token；不可用公開 GitHub Pages。

## 7. 託管：Google Apps Script + Drive（已決定）

```
Drive/
  BIM現場檢核/<專案>/
    stations.json            ← 站點索引 (stationId → token、資料夾 id、版本)
    HS-3F-012/
      station.json
      perspective.jpg
      locator.png
      history/<日期>/...     ← 舊版保留，頁面可切換

Apps Script Web App (doGet)
  ?s=<stationId>&t=<token>
    1. 驗證 token（與 stations.json 比對）
    2. 讀 station.json，圖片以 base64 內嵌或 Drive 縮圖網址回傳
    3. HtmlService 輸出手機版頁面（透視圖 / 定位圖 / 疊圖 / 版本）
```

- **上傳**：P1 先由 Revit 外掛輸出到本機資料夾，再用 Google Drive 桌面版同步到上述資料夾（不在外掛內存 Google 憑證）；之後再評估外掛直接呼叫 Apps Script 上傳端點。
- **存取權**：Web App 部署為「以我身分執行」，存取對象選「組織內」或「任何人（持連結）」＋ token；若現場有協力廠商無公司帳號，需選後者。
- **限制**：Apps Script 單次執行 6 分鐘、回應大小有上限，圖片需壓到 1–2 MB 以內；HtmlService 頁面在 iframe 內，手機相機疊圖 (`getUserMedia`) 需實測，若被擋則 P2 改為頁面內「開啟相機拍照後疊圖比對」。

## 8. 其他待決策

1. 現場網路：若地下室無訊號，需改為「離線包」（手機預先下載整層站點）。
2. 站點由誰放、放多少：建議每層依「檢核項目」放 10–30 點，由 BIM 工程師在面板中標記為「現場檢核點」。
