using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Core
{
    /// <summary>
    /// Ribbon 目錄：所有按鈕集中在這裡定義「放在哪個頁籤、哪個面板、第幾個」。
    ///
    /// 新增外掛時只要在 <see cref="Layout"/> 對應面板加一行 Btn(...)，不必再改 App.cs。
    /// 規則：
    ///   1. 按鈕 Name（第一個參數）是 Revit 內部識別碼，語言切換、授權與使用者的快速存取列都靠它，已上線的按鈕不要改名。
    ///   2. 文字與提示優先用 LanguageManager 的 Ribbon_Btn_{Name} / Ribbon_TT_{Name}（沒有就用這裡寫的中文）。
    ///   3. 同一功能只放一個入口；新舊版並存時，舊版命令保留在程式中但不上 Ribbon。
    ///   4. 每個按鈕獨立建立，一個失敗不影響其他按鈕。
    /// </summary>
    internal static class RibbonCatalog
    {
        // ───── 頁籤（只有一個；Id 即顯示名稱，切換語言時以 Key 更新標題） ─────
        public const string TabMain = "RTS";

        public static readonly Dictionary<string, string> TabKeys = new Dictionary<string, string>
        {
            { TabMain, "Ribbon_Tab_Main" },
        };

        // ───── 定義型別 ─────
        internal class ButtonDef
        {
            public string Name, ClassName, Text, Tip, Emoji, Bg, Fg = "#FFFFFF", IconFile;
            public Func<string> AssemblyOverride;           // 不在主外掛 DLL 的命令（例如 BEAI 查詢服務）
            public List<ButtonDef> Children;                // 有子項目＝下拉選單
            public bool IsPulldown => Children != null;
            public bool IsSeparator;                        // 下拉選單中的分隔線（區分小分類）
        }

        internal class ItemDef
        {
            public List<ButtonDef> Buttons = new List<ButtonDef>();   // 1 個＝大按鈕；2～3 個＝上下堆疊小按鈕
        }

        internal class PanelDef
        {
            public string Tab, TitleKey, Title;
            public List<ItemDef> Items = new List<ItemDef>();
        }

        private static ButtonDef Btn(string name, string className, string text, string emoji, string bg, string tip = null)
            => new ButtonDef { Name = name, ClassName = className, Text = text, Emoji = emoji, Bg = bg, Tip = tip };

        private static ButtonDef Menu(string name, string text, string emoji, string bg, string tip, params ButtonDef[] children)
            => new ButtonDef { Name = name, Text = text, Emoji = emoji, Bg = bg, Tip = tip, Children = children.ToList() };

        /// <summary>下拉選單中的分隔線，前後是不同的小分類。</summary>
        private static ButtonDef Sep() => new ButtonDef { IsSeparator = true, Name = "" };

        private static ItemDef One(ButtonDef b) => new ItemDef { Buttons = { b } };
        private static ItemDef Stack(params ButtonDef[] b) => new ItemDef { Buttons = b.ToList() };

        private static PanelDef Panel(string tab, string titleKey, string title, params ItemDef[] items)
            => new PanelDef { Tab = tab, TitleKey = titleKey, Title = title, Items = items.ToList() };

        private const string C = "DevelopmentTools.Commands.";

        /// <summary>
        /// 整個 Ribbon 的版面：一個「RTS」頁籤，裡面三個分類下拉按鈕（出圖／建模／檢討與管理），
        /// 各工具都放在下拉選單裡，以分隔線分成小分類。新增外掛就在對應分類裡加一行。
        /// </summary>
        public static List<PanelDef> Layout()
        {
            return new List<PanelDef>
            {
                Panel(TabMain, "Ribbon_Panel_Main", "RTS 工具",
                    // ═════════════ 出圖：開圖 ─ 圖紙 ─ 標註 ─ Word 文件 ═════════════
                    One(Menu("CatDrawing", "出圖", "🖨", "#C0392B", "開圖、圖紙、標註與 Word 文件",
                        Btn("QuickViewCreator", C + "Cmd_QuickViewCreator", "快速開圖套樣板", "🖼", "#C0392B"),
                        Btn("FastDependentViews", C + "Cmd_FastDependentViews", "快速從屬視圖", "🧩", "#0078D4"),
                        Btn("DrawingFactory", C + "Cmd_DrawingFactory", "量化開圖", "🏭", "#AF601A"),
                        Btn("SheetDuplicator", C + "Cmd_SheetDuplicator", "逐層複製圖紙", "📑", "#1F618D"),
                        Sep(),
                        Btn("SheetViewPlacer", C + "Cmd_SheetViewPlacer", "圖紙排版", "📋", "#1A5276"),
                        Btn("BatchSheetRename", C + "Cmd_BatchSheetRenamer", "圖紙批次更名", "✏", "#2C3E50"),
                        Btn("SheetTransfer", C + "Cmd_SheetTransfer", "跨專案圖紙轉移", "📦", "#0052CC"),
                        Sep(),
                        Btn("QuickDimension", C + "Cmd_QuickDimension", "快速尺寸標註", "📏", "#6C3483"),
                        Btn("AutoBoundary", C + "Cmd_AutoBoundary", "自動邊界", "🏷", "#111318"),
                        Btn("RecordAnnotationLogic", C + "Cmd_RecordViewAnnotations", "記錄標註邏輯", "🔖", "#2E7D32", RecordAnnotationTip),
                        Btn("OpeningDetail", C + "Cmd_OpeningDetail", "開口剖面圖", "🪟", "#117A65"),
                        Sep(),
                        Btn("LinkWordCommand", "DT_WordPdfLink.Commands.LinkWordCommand", "連結 Word", "📄", "#2B579A", "連結 Word 文件 (.docx) 到目前的圖紙"),
                        Btn("RefreshSelectedCommand", "DT_WordPdfLink.Commands.RefreshSelectedCommand", "更新選取 Word 連結", "🔄", "#3498DB", "更新所選的 Word 連結內容"),
                        Btn("RefreshAllCommand", "DT_WordPdfLink.Commands.RefreshAllCommand", "更新全部 Word 連結", "🔃", "#2ECC71", "檢查並更新專案中所有 Word 連結"),
                        Btn("OpenWordCommand", "DT_WordPdfLink.Commands.OpenWordCommand", "開啟原始 Word", "📖", "#F1C40F", "開啟所選連結的 Word 原始文件"),
                        Btn("RelinkWordCommand", "DT_WordPdfLink.Commands.RelinkWordCommand", "重新指定 Word 來源", "🔗", "#E74C3C", "為所選的連結重新指定 Word 來源檔案"))),

                    // ═════════════ 建模：裝修磁磚 ─ 鋼筋結構 ─ 族群 ─ 3D 與視圖 ═════════════
                    One(Menu("CatModel", "建模", "🏗", "#16A085", "裝修磁磚、鋼筋結構、族群、3D 與視圖",
                        Btn("ShowControlPanel", C + "ShowControlPanelCommand", "磁磚鋪設系統", "🟦", "#2980B9"),
                        Btn("RoomFinishConfigurator", C + "Cmd_RoomFinishConfigurator", "裝修配置與粉刷", "🏠", "#16A085"),
                        Btn("FloorSnapToRoom", C + "Cmd_FloorSnapToRoom", "樓板貼房間", "⬇", "#D35400"),
                        Btn("FinishBuildUp", C + "Cmd_FinishBuildUp", "降梁降板增築", "▤", "#7D6608",
                            "依裝修表比對每間房的面層底與結構頂：建立板上／梁上增築，或找出需要降板、降梁的結構。"),
                        Btn("TileElevationGenerator", C + "DT_TileElevationGeneratorCommand", "展開圖生成", "📐", "#8E44AD"),
                        Sep(),
                        Btn("FastRebar", C + "Cmd_FastRebar", "快速配筋", "⛓", "#E67E22"),
                        Btn("RebarCheck", C + "Cmd_RebarCheck", "鋼筋檢核", "✔", "#27AE60"),
                        Sep(),
                        Btn("BatchDoorUpdater", C + "Cmd_BatchDoorUpdater", "門族群批量深化", "🚪", "#7B241C"),
                        Sep(),
                        Btn("AdaptiveRegion", C + "Cmd_AdaptiveRegion", "智慧剖面框", "🎯", "#E74C3C"),
                        Btn("PerspectiveMinimap", C + "Cmd_PerspectiveMinimap", "透視圖索引面板", "🧭", "#2471A3"),
                        Btn("TileFourViews", C + "Cmd_TileFourViews", "一鍵 4 視圖", "⊞", "#5D6D7E"))),

                    // ═════════════ 檢討與管理：法規 ─ 專案標準 ─ AI ─ 系統 ─ 語言 ═════════════
                    One(Menu("CatReview", "檢討與管理", "§", "#1F4E79", "法規檢討、專案標準、AI 助手、帳號與更新",
                        Btn("CodeCheckAll", C + "Cmd_CodeCheck", "全條文法規檢討", "§", "#1F4E79"),
                        Sep(),
                        Btn("ProjectStandards", C + "Cmd_ProjectStandards", "專案標準比對", "⚖", "#6C3483"),
                        Btn("ProjectCompareCopy", C + "Cmd_ProjectCompareCopy", "參數比對複製", "⇄", "#1F618D"),
                        Sep(),
                        Btn("AIAssistant", C + "Cmd_AIAssistant", "AI 模型分析", "🤖", "#6E40C9"),
                        new ButtonDef
                        {
                            Name = "BEAIQueryStatus", ClassName = typeof(BEAI.QueryHost.Commands.ShowStatusCommand).FullName,
                            AssemblyOverride = () => typeof(BEAI.QueryHost.Commands.ShowStatusCommand).Assembly.Location,
                            Text = "AI 查詢狀態", Tip = "確認唯讀模型查詢服務與目前模型是否可供 ChatGPT 使用", Emoji = "✓", Bg = "#18864B"
                        },
                        Sep(),
                        Btn("GoogleLogin", C + "LoginCommand", "Google 登入", "🔑", "#4285F4"),
                        Btn("SystemFeedback", C + "FeedbackCommand", "問題與反饋", "💬", "#34A853"),
                        Btn("CheckForUpdates", C + "CheckUpdateCommand", "檢查更新", "🔄", "#EA4335"),
                        Sep(),
                        Btn("LanguageZH", C + "Cmd_LanguageZH", "繁體中文", "中", "#3498DB"),
                        Btn("LanguageEN", C + "Cmd_LanguageEN", "English", "EN", "#3498DB"),
                        Btn("LanguageJA", C + "Cmd_LanguageJA", "日本語", "日", "#3498DB"))),

                    // ═════════════ BIM MCP：AI 助理連接 Revit（另一個外掛 Horizun.Revit.dll 的命令；沒安裝就不顯示） ═════════════
                    One(Menu("CatBimMcp", "BIM MCP", "🔌", "#18864B", "AI 助理（Claude、Codex 等）操作 Revit 的連接狀態、權限與紀錄",
                        Mcp("McpBridgeStatus", "BridgeStatusCommand", "連線狀態", "✓", "#18864B", "AI 助理能否操作這個 Revit、BIM MCP 版本與紀錄檔位置。遇到問題先看這裡。"),
                        Mcp("McpOpenHub", "OpenHubCommand", "開啟 BIM MCP 網站", "🌐", "#2471A3", "在瀏覽器開啟 BIM MCP：模型稽核、族群標準化、交付品質管控等現成工作流程。"),
                        Sep(),
                        Mcp("McpPause", "PauseMcpCommand", "暫停／恢復助理", "⏸", "#C0392B", "暫停後助理不能操作 Revit；再按一次恢復。"),
                        Mcp("McpBimMode", "BimModeCommand", "助理可以做什麼", "🛡", "#6C3483", "設定助理的權限：只能查看、可修改開啟中的模型，或可匯出與開關文件。"),
                        Mcp("McpCentralProtection", "CentralProtectionCommand", "保護共用模型", "🔒", "#7B241C", "啟用後，工作共用（中央檔）模型只能被助理查看。"),
                        Mcp("McpSyncCentral", "SyncCentralPermissionCommand", "允許與中央檔同步", "⇅", "#1F618D", "是否允許助理執行「與中央檔同步」。"),
                        Mcp("McpPython", "PythonPermissionCommand", "Python 權限", "🐍", "#2C3E50", "是否允許助理在 Revit 中執行 Python。"),
                        Sep(),
                        Mcp("McpAuditHistory", "AuditHistoryCommand", "助理做了什麼", "📜", "#AF601A", "查看助理的變更紀錄，並可復原 BIM MCP 的變更。"),
                        Mcp("McpOperationsPane", "Ui.ShowOperationsPaneCommand", "操作窗格", "📋", "#1A5276", "開啟操作窗格：佇列、已套用的動作與變更內容。"),
                        Mcp("McpAdvanced", "AdvancedOptionsCommand", "進階選項", "⚙", "#7F8C8D", "BIM MCP 進階選項。")))),
            };
        }

        // ───── 外部外掛：BIM MCP ─────
        public const string BimMcpTabTitle = "BIM MCP";
        public static bool BimMcpMerged { get; private set; }

        /// <summary>BIM MCP 外掛主程式位置；沒安裝時回傳 null（選單就不建立）。</summary>
        public static string BimMcpAssembly()
        {
            try
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Horizun.Revit");
                if (loaded != null && !string.IsNullOrEmpty(loaded.Location) && File.Exists(loaded.Location)) return loaded.Location;
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk", "Revit", "Addins", "2024", "BIM-MCP", "Horizun.Revit.dll");
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        private static ButtonDef Mcp(string name, string cls, string text, string emoji, string bg, string tip)
            => new ButtonDef { Name = name, ClassName = "Horizun.Revit." + cls, AssemblyOverride = BimMcpAssembly, Text = text, Emoji = emoji, Bg = bg, Tip = tip };

        /// <summary>
        /// BIM MCP 已併入 RTS 頁籤時，把它自己的「BIM MCP」頁籤隱藏（只是不顯示，外掛照常運作）。
        /// 在所有外掛載入完成後（ApplicationInitialized）呼叫。
        /// </summary>
        public static void HideMergedTabs()
        {
            if (!BimMcpMerged) return;
            try
            {
                foreach (var tab in Autodesk.Windows.ComponentManager.Ribbon.Tabs)
                {
                    if (tab == null || TabKeys.ContainsKey(tab.Id ?? "")) continue;
                    if (string.Equals((tab.Title ?? "").Trim(), BimMcpTabTitle, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals((tab.Id ?? "").Trim(), BimMcpTabTitle, StringComparison.OrdinalIgnoreCase))
                        tab.IsVisible = false;
                }
            }
            catch { }
        }

        private static ButtonDef WithIcon(this ButtonDef b, string file) { b.IconFile = file; return b; }

        private static readonly string RecordAnnotationTip = "【記錄標註邏輯 - 快速指南】\n\n" + "1. 切換至目標平面或天花板視圖。\n" + "2. 點擊此按鈕，設定該視圖預期使用的自動標註邏輯（如柱中心標註、偏移量等）。\n" + "3. 點擊「儲存」，設定會加密儲存於視圖中。\n" + "4. 後續執行跨專案圖紙移轉時，系統會自動在目標視圖重做相同的標註。";

        /// <summary>按鈕的詳細說明（滑鼠停久一點顯示），沿用原本的內容。</summary>
        private static readonly Dictionary<string, string> LongDescriptions = new Dictionary<string, string>
        {
            { "GoogleLogin", "【Google 登入 - 安全與權限詳細說明】\n\n" + "■ 帳號白名單：登入後，系統會至後台雲端比對管理員設定的允許名單。\n" + "■ 功能細粒度控制：不同的 Google 帳號可分別授權開通不同模組（如磁磚、粉刷、樓板等）。若有功能點擊後顯示「未授權」，請向系統管理員提出開通申請。\n" + "■ 安全保障：外掛採用 Google 官方 OAuth 2.0 驗證協議，絕不會收集或儲存您的密碼，僅確認您的使用授權狀態。" },
            { "SystemFeedback", "【問題與反饋 - 常見反饋引導】\n\n" + "■ 錯誤回報提示：若外掛產生錯誤或閃退，建議提供完整的錯誤說明。您也可以點擊查看日誌，將 RevitAddinLog.txt 提供給開發小組排查。\n" + "■ 功能提案：若有任何希望新增的指令、優化操作流程的想法，都歡迎直接寫在反饋中，我們將於後續版本評估加入。" },
            { "CheckForUpdates", "【檢查更新 - 升級與防毒說明】\n\n" + "■ 自動比對：程式會檢查本機版號與雲端最新配置，確保您隨時可以使用最穩定、修正了 Bug 的新外掛。\n" + "■ 靜默檢測：每次點開排版主視窗時，後台執行緒都會自動做一次無感的更新檢測。\n" + "■ 防毒信任：更新時需要透過獨立的 Updater 覆蓋安裝，若被防毒軟體攔截，請選擇「信任並允許執行」。" },
            { "ShowControlPanel", "【磁磚鋪設系統 - 進階使用提示】\n\n" + "■ 排磚原理：程式會取得 Room 牆體的最外側裝修面，並直接在表面 Paint .pat 填充線，不需手動繪製。\n" + "■ 設計微調：可在面板中微調起磚原點偏移量 X/Y，或調整整體排磚旋轉角度，以避開碎磚或對齊特定縫隙。\n" + "■ 動態關聯：外掛在專案中註冊了 DMU (Dynamic Model Update)，當房間的牆壁被手動拖動時，面域內的磁磚會自動重新計算，無須人工手動重做。" },
            { "TileElevationGenerator", "【展開圖生成 - 拼接與對齊細節】\n\n" + "■ 無縫對接技術：排版至圖紙時，系統會自動在背景隱藏 Section 視圖中的標高、軸線、剖面線等干擾符號，以便抓取純淨的 CropBox 邊界尺寸進行完美貼合排版。\n" + "■ 高低差自動補償：讀取各牆面底部的 Level 標高高程，在圖紙 Y 軸對齊時自動進行偏移量扣除，確保所有立面圖的樓面線 (Floor Line) 處於同一條水平線上。\n" + "■ 剖面方向：生成剖面線視角預設是從樓板中心點朝牆體表面向外看。" },
            { "RoomFinishConfigurator", "【房間裝修配置 - 數據關聯說明】\n\n" + "■ 參數對應：直接讀寫 Revit Room 元件內建的地板裝修 (Base Finish)、牆面裝修 (Wall Finish) 與天花板裝修 (Ceiling Finish) 屬性。\n" + "■ 自動化工作流：透過本工具在房間中預先填入好裝修編號或材質（如: T01 地磚、P01 乳膠漆），後續其他裝修生成工具在分析房間時，即可自動識別並生成相應厚度與材質的實體牆/板/填充線，實現 BIM 智慧化裝修自動化。" },
            { "FloorSnapToRoom", "【樓板貼房間 - 進階吸附與幾何控制】\n\n" + "■ 幾何對齊原理：分析樓板所在的 Room 房間完成面（Finish Boundary）。針對草圖中與牆面平行、距離在 MaxSnapDistance (預設 300 mm) 內且有一定重疊長度的直線段進行平移對齊。\n" + "■ 端點重建：移動後，程式會針對所有相鄰的直線在 2D XY 平面上重新求無限延伸線的交點，重設其端點。對於圓弧 (Arc)，則會保持其曲率半徑，僅微調其相鄰端點，確保草圖 100% 閉合。\n" + "■ 獨立防崩潰控制：框選多個樓板時，每塊樓板都會使用獨立的事務群組 (TransactionGroup) 處理。若有部分樓板因為草圖本身太亂或自交而對齊失敗，程式會單獨 Rollback 該樓板並提供報告，其餘成功的樓板依然會照常套用。" },
            { "BatchSheetRename", "【圖紙批次更名 - 衝突防護與批次功能說明】\n\n" + "■ 衝突防護機制：Revit 原生要求圖紙編號必須唯一。如果您想對調兩張圖紙的編號（如 A:001->002，B:002->001），本工具會自動偵測衝突並採用「兩步暫存修改法」（先暫時加上隨機或 Temp 後綴，再統一修改為目標編號），完美避開 Revit 限制。\n" + "■ 批次修改與預覽：支援即時編輯，提供直觀的介面，防止因手動一個個修改圖紙屬性而造成的繁瑣操作與編號衝突。" },
            { "QuickViewCreator", "【快速開圖套樣板 - 複製與圖紙自動化說明】\n\n" + "■ 複製機制：支援「僅複製結構 (Duplicate)」、「複製詳圖 (With Detailing)」與「建立相依 (As Dependent)」三種模式，完美應對不同的出圖需求。\n" + "■ 命名防碰撞：新視圖名稱為 [來源視圖名稱]-[樣板名稱]。若名稱已存在，系統會自動遞增後綴 (如 _1, _2) 避免 Revit 衝突崩潰。\n" + "■ 一鍵自動出圖：若勾選「同步建立圖紙」，系統會自動採用專案圖框為新視圖建立對齊圖紙，並自動將視圖放置到圖紙正中央，實現開圖到排版的一鍵完成。" },
            { "FastDependentViews", "【快速建立從屬視圖】\n\n" + "■ 依目前作用中視圖批次建立從屬視圖。\n" + "■ 可依房間、空間或樓板邊界自動套用裁剪範圍。\n" + "■ 支援命名樣板與批次勾選，適合大量分區出圖。" },
            { "SheetViewPlacer", "【圖紙視圖排版 - 拖放移轉與防重疊說明】\n\n" + "■ 智能跨圖紙搬移：由於 Revit 限制普通視圖只能放置在單一圖紙上，當您跨圖紙拖曳時，系統會自動在後台刪除舊圖紙的 Viewport 並於新圖紙重建，避免 Revit 異常。\n" + "■ 位置自動偏移：置入新視圖時，若圖紙已有視窗，系統會自動套用微幅偏移以防完全重疊遮擋，並會自動套用下拉選定的視埠樣式。\n" + "■ 支援明細表與圖例：除了一般平面/剖立面/3D 視圖外，亦支援將明細表 (Schedule) 等可多重置入的視圖進行拖放排版。" },
            { "QuickDimension", "【快速尺寸標註 - 標註原理與幾何提取說明】\n\n" + "■ 柱中心對齊：自動讀取結構柱/建築柱內部的 Center 幾何面參照，依柱子排列方向（水平或垂直）建立橫向或縱向的尺寸標註。\n" + "■ 牆中心對齊：藉由牆體定位中心線直接提取 Reference，自動在平行牆體中線建立連續的對齊標註。\n" + "■ 開口邊到邊：讀取門、窗、洞口元件內部的 Left 與 Right 強參照面，實現開口淨寬與間距的快速一鍵尺寸標註。" },
            { "AutoBoundary", "【自動邊界與標籤】\n\n" + "■ 依目前平面視圖牆體邊界建立房間、空間或區域分隔線。\n" + "■ 可優化既有邊界線端點，協助閉合分區。\n" + "■ 完成後可接續使用 Revit 內建標註工具放置標籤。" },
            { "DrawingFactory", "【量化開圖】\n\n■ 以已完成的標準圖說為範本，批次建立多棟、多樓層的整套圖紙、視圖與標註。\n■ 只需把一組圖紙複製到其他樓層時，用「逐層複製圖紙」較快。" },
            { "SheetDuplicator", "【逐層複製圖紙】\n\n■ 把一組已完成的圖紙複製到其他樓層（簡單、快速）。\n■ 要以標準圖說量產多棟多樓層整套圖紙時，請用「量化開圖」。" },
        };

        // ───────────────────────── 建立 Ribbon ─────────────────────────

        /// <summary>依 Layout 建立頁籤、面板與按鈕；回傳 (面板, 標題字串鍵) 供語言切換更新。</summary>
        public static void Build(UIControlledApplication app, string assemblyPath, List<KeyValuePair<RibbonPanel, string>> tracked, Action<string> log)
        {
            var lm = LanguageManager.Instance;
            foreach (string tab in TabKeys.Keys)
            {
                try { app.CreateRibbonTab(tab); } catch (Exception ex) { log?.Invoke($"建立頁籤 {tab} 失敗：{ex.Message}"); }
            }

            foreach (PanelDef p in Layout())
            {
                RibbonPanel panel;
                try
                {
                    panel = app.CreateRibbonPanel(p.Tab, Text(lm, p.TitleKey, p.Title));
                    tracked.Add(new KeyValuePair<RibbonPanel, string>(panel, p.TitleKey));
                }
                catch (Exception ex)
                {
                    log?.Invoke($"建立面板 {p.Title} 失敗：{ex.Message}");
                    continue;
                }

                foreach (ItemDef item in p.Items)
                {
                    try
                    {
                        if (item.Buttons.Count == 1)
                        {
                            ButtonDef b = item.Buttons[0];
                            if (b.IsPulldown)
                            {
                                // 外部外掛的命令：外掛沒安裝就略過；整個選單都沒有可用項目就不建立
                                if (!b.Children.Any(c => !c.IsSeparator && (c.AssemblyOverride == null || c.AssemblyOverride() != null)))
                                {
                                    log?.Invoke($"略過選單 {b.Name}：相關外掛未安裝。");
                                    continue;
                                }
                                if (b.Name == "CatBimMcp") BimMcpMerged = true;
                                var data = new PulldownButtonData(b.Name, Label(lm, b));
                                Decorate(data, b, lm, large: true);
                                var pd = panel.AddItem(data) as PulldownButton;
                                foreach (ButtonDef c in b.Children)
                                {
                                    try
                                    {
                                        if (c.IsSeparator) { pd?.AddSeparator(); continue; }
                                        if (c.AssemblyOverride != null && c.AssemblyOverride() == null) continue;
                                        var cd = Push(c, assemblyPath, lm, large: false);
                                        pd?.AddPushButton(cd);
                                    }
                                    catch (Exception ex) { log?.Invoke($"建立按鈕 {c.Name} 失敗：{ex.Message}"); }
                                }
                            }
                            else
                            {
                                panel.AddItem(Push(b, assemblyPath, lm, large: true));
                            }
                        }
                        else
                        {
                            var datas = item.Buttons.Select(b => (RibbonItemData)Push(b, assemblyPath, lm, large: false)).ToList();
                            if (datas.Count == 2) panel.AddStackedItems(datas[0], datas[1]);
                            else panel.AddStackedItems(datas[0], datas[1], datas[2]);
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"建立按鈕 {string.Join("/", item.Buttons.Select(x => x.Name))} 失敗：{ex.Message}");
                    }
                }
            }
        }

        private static string Text(LanguageManager lm, string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            string v = lm[key];
            return string.IsNullOrEmpty(v) || v == key ? fallback : v;
        }

        private static string Label(LanguageManager lm, ButtonDef b)
        {
            if (b.Name == "LanguageZH" || b.Name == "LanguageEN" || b.Name == "LanguageJA") return b.Text;
            if (b.Name == "LanguageDropdown") return Text(lm, "Ribbon_Btn_LangDropdown", b.Text);
            return Text(lm, "Ribbon_Btn_" + LangKey(b.Name), b.Text);
        }

        private static string Tip(LanguageManager lm, ButtonDef b)
        {
            if (b.Name == "LanguageDropdown") return Text(lm, "Ribbon_TT_LangDropdown", b.Tip);
            return Text(lm, "Ribbon_TT_" + LangKey(b.Name), b.Tip);
        }

        /// <summary>把大按鈕用的兩行文字併成一行（中日文直接相連，英文以空格相接）。</summary>
        public static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\n') < 0) return s;
            var parts = s.Split('\n');
            var sb = new System.Text.StringBuilder(parts[0].Trim());
            for (int i = 1; i < parts.Length; i++)
            {
                string next = parts[i].Trim();
                if (next.Length == 0) continue;
                char a = sb.Length > 0 ? sb[sb.Length - 1] : ' ', b = next[0];
                bool latin = a < 0x2E80 && b < 0x2E80;
                sb.Append(latin ? " " : "").Append(next);
            }
            return sb.ToString();
        }

        /// <summary>按鈕內部名稱與語系字串鍵不同的對照（歷史命名）。</summary>
        public static string LangKey(string name)
        {
            switch (name)
            {
                case "GoogleLogin": return "Login";
                case "SystemFeedback": return "Feedback";
                case "CheckForUpdates": return "Update";
                case "ShowControlPanel": return "TileSystem";
                case "TileElevationGenerator": return "Elevation";
                case "RoomFinishConfigurator": return "RoomConfig";
                case "FloorSnapToRoom": return "FloorSnap";
                case "BatchSheetRename": return "SheetRename";
                case "QuickViewCreator": return "QuickView";
                case "SheetViewPlacer": return "SheetPlacer";
                case "QuickDimension": return "QuickDim";
                case "AIAssistant": return "AI";
                default: return name;
            }
        }

        private static PushButtonData Push(ButtonDef b, string assemblyPath, LanguageManager lm, bool large)
        {
            string asm = b.AssemblyOverride != null ? b.AssemblyOverride() : assemblyPath;
            string label = Label(lm, b);
            if (!large) label = OneLine(label);   // 下拉選單裡的項目用單行文字
            var d = new PushButtonData(b.Name, label, asm, b.ClassName);
            Decorate(d, b, lm, large);
            return d;
        }

        private static void Decorate(ButtonData d, ButtonDef b, LanguageManager lm, bool large)
        {
            string tip = Tip(lm, b);
            if (!string.IsNullOrEmpty(tip)) d.ToolTip = tip;
            if (LongDescriptions.TryGetValue(b.Name, out string longDesc)) d.LongDescription = longDesc;
            System.Windows.Media.ImageSource img = null;
            if (!string.IsNullOrEmpty(b.IconFile)) img = LoadIcon(b.IconFile);
            if (img == null && !string.IsNullOrEmpty(b.Emoji)) img = Icon(b.Emoji, b.Bg, b.Fg, 32);
            if (img != null)
            {
                d.LargeImage = img;
                d.Image = string.IsNullOrEmpty(b.Emoji) ? img : Icon(b.Emoji, b.Bg, b.Fg, 16);
            }
        }

        private static System.Windows.Media.ImageSource LoadIcon(string file)
        {
            try
            {
                string dll = Assembly.GetExecutingAssembly().Location;
                string path = Path.Combine(Path.GetDirectoryName(dll) ?? "", "Resources", "RibbonIcons", file);
                if (!File.Exists(path))
                {
                    dll = new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath;
                    path = Path.Combine(Path.GetDirectoryName(dll) ?? "", "Resources", "RibbonIcons", file);
                }
                if (!File.Exists(path)) return null;
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>以 emoji 或文字畫出方形色塊圖示（32 px 大圖示、16 px 小圖示）。</summary>
        public static System.Windows.Media.Imaging.BitmapSource Icon(string text, string bgHex, string fgHex, int size)
        {
            try
            {
                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var conv = new System.Windows.Media.BrushConverter();
                    var bg = (System.Windows.Media.Brush)conv.ConvertFrom(bgHex ?? "#555555");
                    var fg = (System.Windows.Media.Brush)conv.ConvertFrom(fgHex ?? "#FFFFFF");
                    double r = size / 6.0;
                    dc.DrawRoundedRectangle(bg, null, new System.Windows.Rect(0, 0, size, size), r, r);
                    var ft = new System.Windows.Media.FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                        System.Windows.FlowDirection.LeftToRight, new System.Windows.Media.Typeface("Segoe UI Emoji"),
                        size * 0.56, fg, System.Windows.Media.VisualTreeHelper.GetDpi(dv).PixelsPerDip);
                    dc.DrawText(ft, new System.Windows.Point((size - ft.Width) / 2, (size - ft.Height) / 2));
                }
                var rt = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rt.Render(dv);
                rt.Freeze();
                return rt;
            }
            catch { return null; }
        }
    }
}
