# DevelopmentTools.Modules

主外掛（DevelopmentTools.Addin / Room Tile System v3）內建的功能模組。每個模組是一個獨立的 net48 類別庫，由主外掛以 `ProjectReference` 引用，建置時 DLL 會自動複製到主外掛輸出資料夾，`release.ps1` 與 Inno Setup 已用 `*.dll` 打包，不需另外設定。

| 資料夾 | 組件 | Ribbon 位置 | 授權 ToolId | 來源 |
|---|---|---|---|---|
| `PerspectiveMinimap` | RevitPerspectiveMinimap.Core | RTS｜施工圖 › 視圖導覽 › 透視圖索引面板、一鍵 4 視圖 | `DT_PerspectiveMinimap` | `office-plugins-source\ai\Revit 透視圖索引面板\src\RevitPerspectiveMinimap.Core` |
| `OpeningDetail` | OpeningDetailCore | RTS｜施工圖 › 標註 › 開口剖面圖 | `DT_OpeningDetail` | `office-plugins-source\ai\開口元件開剖圖` |
| `DrawingFactory` | BIMDrawingFactory.UI | RTS｜施工圖 › 出圖量產 › 量化開圖 | `DT_DrawingFactory` | `office-plugins-source\ai\圖紙視圖量化開圖\BIMDrawingFactory.UI` |
| `ProjectStandards` | DevelopmentTools.ProjectStandards | RTS｜裝修與管理 › 文件與標準 › 專案標準比對 | `DT_ProjectStandards` | `office-plugins-source\ai\專案圖說轉移\DevelopmentTools.ProjectStandards` |
| （主外掛內）`Commands/Cmd_BatchDoorUpdater.cs` | DevelopmentTools.Addin | RTS｜裝修與管理 › 族群 › 門族群批量深化 | `DT_BatchDoorUpdater` | `office-plugins-source\ai\門元件建立\BatchDoorUpdater.cs` |

## 運作方式

- Ribbon 按鈕都指向主外掛裡的包裝命令（`DevelopmentTools.Addin/Commands/Cmd_IntegratedModules.cs`），先跑 `CommandAccessGuard.EnsureAuthorized`，通過後才呼叫模組原本的命令。
- 共用設定（net48、WPF、Revit 2024 API 參考）在 `Directory.Build.props`。
- Release 建置時主外掛與 4 個模組 DLL 都經 Obfuscar 混淆（`DevelopmentTools.Addin/obfuscar.xml`）。模組保留型別、屬性、事件與列舉名稱（WPF 繫結與存檔依名稱運作），私有方法與欄位改名、字串加密。`release.ps1` 與安裝程式只打包 `Obfuscated\` 裡的版本。

## 從原始版本改了什麼

- 專案檔改為 SDK 格式，才能用 `dotnet build DevelopmentTools.sln` 一起建置；組件名稱與命名空間不變，WPF 資源路徑不受影響。
- 沒有帶進來：各模組自己的 `IExternalApplication` Ribbon（開口的 `Core/App.cs`）、熱載入 Loader / `HotReload`、量化開圖的獨立執行 `App.xaml`、ProjectStandards 測試專案。
- 量化開圖：原本寫死的事務所慣例（圖號 `FY-SB*`、棟別「行政棟／實驗棟」、FY_ 尺寸類型與標籤族群）全部移到命名規則檔，程式本身不再含任何專案名稱。
- Word 連結：主外掛 `Modules/WordPdfLink` 改用辦公室版（可選 Word 頁碼、預覽、依圖紙記錄連結），存檔格式與舊版相容。
- 門族群批量深化：共用參數檔路徑不再寫死，執行時讓使用者選檔（記在 `%LOCALAPPDATA%\DevelopmentTools\door-shared-param-path.txt`）。

## 量化開圖的命名規則（conventions.json）

位置：`%APPDATA%\DevelopmentTools\DrawingFactory\conventions.json`。第一次開啟量化開圖時會自動寫出預設檔；修改後重開 Revit 生效。

| 欄位 | 用途 | 預設 |
|---|---|---|
| `Buildings` | 棟別：`Key`（樓層名稱前綴，如 `A`）、`DisplayName`（圖名中的棟別文字）、`Aliases`、`HasBasementVariant` | 空：從樓層名稱前綴自動辨識（`A_1FL`、`B_1FL`…，至少兩種前綴才算多棟） |
| `SharedBuildingKey` / `SharedAliases` | 全區共用樓層 | `ALL` / `全區`、`ALL` |
| `SeriesOptionalPrefix` | 圖號系列前可有可無的前綴 | 空 |
| `SeriesCorePattern` | 圖號中代表系列的正規式 | 空：取圖號前段（`S5-01` → `S5`） |
| `DefaultSeriesCode` / `DefaultTargetSeries` / `PreferredSourceSeries` | 預設系列 | `S1` / `S2` / 空 |
| `DimensionRules` | 圖名含關鍵字時自動加的尺寸標註（柱心、梁邊、板開口、牆開口、牆邊）與尺寸類型名稱 | 空：不自動標註 |
| `TaggingRules` | 圖名含關鍵字時自動加的標籤族群／類型（Floors、Walls、Doors） | 空：不自動加標籤 |

都辨識不到棟別時只顯示一張「全棟」卡片，圖名不加棟別文字。

事務所自己的設定（行政棟／實驗棟、`FY-SB` 圖號、FY_ 尺寸類型與標籤）存在 `office-plugins-source\量化開圖_辦公室命名規則.json`，**不隨產品發佈**；辦公室電腦把它複製成上面的 `conventions.json` 即可恢復原本行為。

## 已知限制

- 量化開圖的圖號換算假設「系列-序號」格式（例如 `S5-01` → `S6-01`）；樓層寫在圖號中段的格式（例如 `A-101` → `A-201`）尚未支援。
- 樓層代碼解析與部分判斷依台灣常用圖名用語（`1FL`、`B1F`、`RF`、「見上軀體圖」「索引圖」「柱牆圖」等）。
- 讀取 Revit 失敗時，量化開圖部分畫面會退回範例資料（已改為中性名稱），應視為錯誤狀態。
- 尚未在 Revit 2024 實際建置與測試。
