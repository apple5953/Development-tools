# BEAI 模型查詢整合

Room Tile System 已內建 BEAI 唯讀模型查詢橋接層。Revit 載入 `DevelopmentTools.Addin` 時會一併啟動本機服務：

```text
http://127.0.0.1:8765/
```

## 使用方式

1. 開啟 Revit 與模型。
2. 前往「Development tools」頁籤。
3. 在「AI 助手」面板按「查詢狀態」。
4. 確認外掛、查詢服務及模型均可用。

不再需要安裝獨立的 `BEAI.QueryHost.addin`。Room Tile 安裝程式會自動移除舊的獨立註冊，避免兩個服務同時占用連接埠 8765。

## 更新 BEAI 模組

在 Room Tile 專案根目錄執行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sync-beai-bridge.ps1
```

同步後重新建置 Room Tile。發行腳本會自動包含 `BEAI.QueryHost.dll`、背景 Worker 與查詢規則。

## 熱更新範圍

- `Runtime/Rules/*.json` 可在 Revit 執行期間自動重載。
- 背景 Worker 可獨立重新啟動。
- 核心 DLL 與 Ribbon 仍需重新啟動 Revit 才能切換版本，這是 Revit 2024 .NET Framework 的載入限制。
