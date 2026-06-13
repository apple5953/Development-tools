using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Data;

namespace DevelopmentTools.Core
{
    public class LanguageManager : INotifyPropertyChanged
    {
        private static LanguageManager _instance;
        public static LanguageManager Instance => _instance ?? (_instance = new LanguageManager());

        public event PropertyChangedEventHandler PropertyChanged;

        private string _currentLanguage = "zh-TW"; // Default
        private Dictionary<string, Dictionary<string, string>> _translations;

        private readonly string _settingsPath;

        private LanguageManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _settingsPath = Path.Combine(appData, "DevelopmentTools", "App", "LanguageSetting.txt");

            LoadSettings();
            InitializeTranslations();
        }

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    SaveSettings();
                    // 觸發重新綁定，所有 XAML 的 {Binding [Key]} 都會更新
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
                    LanguageChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler LanguageChanged;

        public string this[string key]
        {
            get
            {
                if (_translations.TryGetValue(_currentLanguage, out var langDict))
                {
                    if (langDict.TryGetValue(key, out var val))
                    {
                        return val;
                    }
                }
                
                // Fallback to zh-TW if not found
                if (_currentLanguage != "zh-TW" && _translations.TryGetValue("zh-TW", out var fallbackDict))
                {
                    if (fallbackDict.TryGetValue(key, out var fallbackVal))
                    {
                        return fallbackVal;
                    }
                }

                return key; // return the key itself if missing
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string lang = File.ReadAllText(_settingsPath).Trim();
                    if (lang == "zh-TW" || lang == "en-US" || lang == "ja-JP")
                    {
                        _currentLanguage = lang;
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                File.WriteAllText(_settingsPath, _currentLanguage);
            }
            catch { }
        }

        private void InitializeTranslations()
        {
            _translations = new Dictionary<string, Dictionary<string, string>>
            {
                { "zh-TW", new Dictionary<string, string>() },
                { "en-US", new Dictionary<string, string>() },
                { "ja-JP", new Dictionary<string, string>() }
            };

            // ----------------------------------------------------------------
            // Ribbon 相關 (App.cs)
            // ----------------------------------------------------------------
            
            AddTranslation("Ribbon_Panel_Admin", "管理面板", "Admin", "管理パネル");
            AddTranslation("Ribbon_Panel_Layout", "空間排版", "Layout", "空間レイアウト");
            AddTranslation("Ribbon_Panel_Finish", "粉刷裝修", "Finishes", "塗装仕上げ");
            AddTranslation("Ribbon_Panel_Floor", "樓板工具", "Floors", "床ツール");
            AddTranslation("Ribbon_Panel_Sheet", "圖紙工具", "Sheets", "シートツール");
            AddTranslation("Ribbon_Panel_Language", "語言切換", "Language", "言語設定");
            AddTranslation("Ribbon_Panel_AI", "AI 助手", "AI Assistant", "AIアシスト");
            AddTranslation("Ribbon_Panel_DocTools", "文件工具", "Document Tools", "ドキュメントツール");

            // AI Assistant
            AddTranslation("Ribbon_Btn_AI", "AI 模型分析", "AI Analysis", "AIモデル分析");
            AddTranslation("Ribbon_TT_AI", "使用大語言模型分析 Revit 模型元件，提供優化建議（API 串接建置中）。", "Analyze Revit model elements with LLM and get optimization suggestions (API integration in progress).", "大規模言語モデルでRevitモデルを分析し、最適化提案を提供します（API連携構築中）。");

            // Sheet Transfer
            AddTranslation("Ribbon_Btn_SheetTransfer", "跨專案圖紙轉移", "Sheet Transfer", "シート転送");
            AddTranslation("Ribbon_TT_SheetTransfer", "將其他 Revit 專案中的圖紙、詳圖、圖例、明細表快速轉移至目前專案。", "Transfer sheets, drafting views, legends, and schedules from another Revit project to the current project.", "他のRevitプロジェクトからシート、ドラフトビュー、凡例、集計表を現在のプロジェクトに転送します。");

            // Google Login
            AddTranslation("TileSys_InstallWallPattern", "安裝[牆粉刷線]至此專案", "Install Wall Pattern", "壁パターンをインストール");
            AddTranslation("Ribbon_Btn_Login", "Google 登入", "Google Login", "Google ログイン");
            AddTranslation("Ribbon_TT_Login", "點擊以登入您的 Google 帳號進行權限認證。", "Click to login with Google for authorization.", "クリックしてGoogleアカウントにログインし、認証を行います。");

            // Feedback
            AddTranslation("Ribbon_Btn_Feedback", "問題與反饋", "Feedback", "フィードバック");
            AddTranslation("Ribbon_TT_Feedback", "提交錯誤報告或功能建議。", "Submit a bug report or feature suggestion.", "バグ報告や機能提案を送信します。");

            // Check Update
            AddTranslation("Ribbon_Btn_Update", "檢查更新", "Check Update", "更新チェック");
            AddTranslation("Ribbon_TT_Update", "線上檢查是否有最新版本發佈。", "Check online for the latest version.", "オンラインで最新バージョンを確認します。");

            // Tile System
            AddTranslation("Ribbon_Btn_TileSystem", "磁磚鋪設系統", "Tile System", "タイル敷設システム");
            AddTranslation("Ribbon_TT_TileSystem", "開啟磁磚設計面板，自動產生面域排磚。", "Open the tile design panel to automatically generate tile layouts.", "タイル設計パネルを開き、自動でタイル配置を生成します。");

            // Elevation
            AddTranslation("Ribbon_Btn_Elevation", "展開圖生成", "Elevation Gen", "展開図生成");
            AddTranslation("Ribbon_TT_Elevation", "快速生成牆面展開立面圖並自動排版圖紙。", "Quickly generate wall elevations and layout on sheets.", "壁の展開図を迅速に生成し、シートに自動配置します。");

            // Wall Finish
            AddTranslation("Ribbon_Btn_WallFinish", "自動粉刷", "Auto Finish", "自動塗装");
            AddTranslation("Ribbon_TT_WallFinish", "依據房間自動生成實體牆面粉刷層，並扣除門窗洞口。", "Auto-generate wall finish layers based on rooms and deduct openings.", "部屋に基づいて壁の塗装レイヤーを自動生成し、開口部を控除します。");

            // Room Finish Config
            AddTranslation("Ribbon_Btn_RoomConfig", "房間裝修配置", "Room Config", "部屋仕上げ設定");
            AddTranslation("Ribbon_TT_RoomConfig", "批量設定各房間的地板、牆面與天花板裝修材料參數。", "Batch configure floor, wall, and ceiling finishes for rooms.", "各部屋の床、壁、天井の仕上げ材を一括設定します。");

            // Floor Snap
            AddTranslation("Ribbon_Btn_FloorSnap", "樓板貼房間", "Snap Floor", "床スナップ");
            AddTranslation("Ribbon_TT_FloorSnap", "自動將樓板草圖貼齊同樓層房間的牆面邊界。", "Auto-snap floor sketches to room boundaries.", "床のスケッチを部屋の壁境界に自動スナップします。");

            // Batch Sheet Rename
            AddTranslation("Ribbon_Btn_SheetRename", "圖紙批次更名", "Batch Rename", "シート一括リネーム");
            AddTranslation("Ribbon_TT_SheetRename", "一次性修改多張圖紙的編號與名稱，並防止編號衝突。", "Batch rename sheets and prevent number conflicts.", "複数シートの番号と名前を一括変更し、番号の重複を防ぎます。");

            // Quick View Creator
            AddTranslation("Ribbon_Btn_QuickView", "快速開圖套樣板", "Quick Views", "クイックビュー");
            AddTranslation("Ribbon_TT_QuickView", "一鍵批次複製平面圖、套用樣板並建立圖紙。", "Batch duplicate views, apply templates and create sheets.", "ビューの複製、テンプレート適用、シート作成を一括実行します。");
            
            // Languages
            AddTranslation("Ribbon_Lang_ZH", "繁體中文", "繁體中文", "繁體中文");
            AddTranslation("Ribbon_Lang_EN", "English", "English", "English");
            AddTranslation("Ribbon_Lang_JA", "日本語", "日本語", "日本語");

            // ----------------------------------------------------------------
            // 共用字串
            // ----------------------------------------------------------------
            AddTranslation("Common_Save", "確定更新", "Save Changes", "変更を保存");
                        AddTranslation("BatchRename_Title", "圖紙批次更名", "Batch Sheet Renamer", "シート一括名前変更");
            AddTranslation("BatchRename_SubTitle", "批次修改圖紙名稱或編號", "Batch rename sheet names or numbers", "シート名または番号を一括変更");
            AddTranslation("BatchRename_KeywordFilter", "關鍵字篩選", "Keyword Filter", "キーワードフィルター");
            AddTranslation("BatchRename_KeywordTooltip", "輸入關鍵字篩選清單", "Enter keyword to filter the list", "キーワードを入力してリストをフィルタリング");
            AddTranslation("BatchRename_FindReplaceTitle", "尋找與取代", "Find & Replace", "検索と置換");
            AddTranslation("BatchRename_TargetField", "目標欄位", "Target Field", "対象フィールド");
            AddTranslation("BatchRename_TargetNumber", "圖紙編號", "Sheet Number", "シート番号");
            AddTranslation("BatchRename_TargetName", "圖紙名稱", "Sheet Name", "シート名");
            AddTranslation("BatchRename_FindText", "尋找目標", "Find What", "検索する文字列");
            AddTranslation("BatchRename_ReplaceText", "取代為", "Replace With", "置換後の文字列");
            AddTranslation("BatchRename_ApplyReplaceBtn", "套用取代", "Apply Replace", "置換を適用");
            AddTranslation("BatchRename_ApplyReplaceTooltip", "套用尋找與取代", "Apply find and replace", "検索と置換を適用");
            AddTranslation("BatchRename_PrefixSuffixTitle", "加入前綴 / 後綴", "Add Prefix / Suffix", "接頭辞 / 接尾辞を追加");
            AddTranslation("BatchRename_PrefixChar", "前綴字元", "Prefix", "接頭辞");
            AddTranslation("BatchRename_SuffixChar", "後綴字元", "Suffix", "接尾辞");
            AddTranslation("BatchRename_ApplyPrefixSuffixBtn", "套用前/後綴", "Apply", "適用");
            AddTranslation("BatchRename_ApplyPrefixSuffixTooltip", "套用前綴與後綴", "Apply prefix and suffix", "接頭辞と接尾辞を適用");
            AddTranslation("BatchRename_AutoIndexTitle", "自動流水號", "Auto Numbering", "自動連番");
            AddTranslation("BatchRename_StartNumber", "起始號碼", "Start Number", "開始番号");
            AddTranslation("BatchRename_StartNumberTooltip", "輸入起始號碼，例如 1 或 01", "Enter start number, e.g., 1 or 01", "開始番号を入力（例：1 または 01）");
            AddTranslation("BatchRename_Increment", "增量", "Increment", "増分");
            AddTranslation("BatchRename_IncrementTooltip", "輸入每次增加的數值", "Enter increment value", "増分値を入力");
            AddTranslation("BatchRename_ApplyAutoIndexBtn", "套用流水號", "Apply Numbering", "連番を適用");
            AddTranslation("BatchRename_ApplyAutoIndexTooltip", "套用自動流水號", "Apply auto numbering", "自動連番を適用");
            AddTranslation("BatchRename_InvertSelection", "反轉選取", "Invert Selection", "選択を反転");
            AddTranslation("BatchRename_EditHint", "提示：可直接在表格中點擊修改「新圖紙名稱」或「新圖紙編號」。", "Hint: You can directly edit New Name or Number in the table.", "ヒント：表内で新しい名前や番号を直接編集できます。");
            AddTranslation("BatchRename_ColOriginalNum", "原編號", "Original Num", "元の番号");
            AddTranslation("BatchRename_ColNewNum", "新編號", "New Num", "新しい番号");
            AddTranslation("BatchRename_ColOriginalName", "原名稱", "Original Name", "元の名前");
            AddTranslation("BatchRename_ColNewName", "新名稱", "New Name", "新しい名前");
            AddTranslation("BatchRename_ColModified", "已修改", "Modified", "変更済み");
            AddTranslation("BatchRename_SaveBtn", "確認修改", "Apply Changes", "変更を適用");
            AddTranslation("BatchRename_CancelBtn", "取消", "Cancel", "キャンセル");
            AddTranslation("QuickDim_Title", "快速標註", "Quick Dimension", "クイック寸法");
            AddTranslation("QuickDim_SubTitle", "快速建立尺寸標註", "Quickly create dimensions", "すばやく寸法を作成");
            AddTranslation("QuickDim_ModeTitle", "標註模式", "Dimension Mode", "寸法モード");
            AddTranslation("QuickDim_ModeColCol", "柱 - 柱", "Column to Column", "柱 - 柱");
            AddTranslation("QuickDim_ModeWallWall", "牆 - 牆", "Wall to Wall", "壁 - 壁");
            AddTranslation("QuickDim_ModeWallEdge", "牆 - 邊界", "Wall to Edge", "壁 - 境界");
            AddTranslation("QuickDim_ModeOpening", "開口標註", "Opening", "開口部");
            AddTranslation("QuickDim_ParamTitle", "標註參數", "Parameters", "パラメーター");
            AddTranslation("QuickDim_DimStyle", "標註型式", "Dim Style", "寸法スタイル");
            AddTranslation("QuickDim_OffsetDistance", "偏移距離", "Offset Dist", "オフセット距離");
            AddTranslation("QuickDim_StartBtn", "開始標註", "Start Dimension", "寸法作成開始");
            AddTranslation("QuickView_Title", "快速建置視圖", "Quick View Creator", "クイックビュー作成");
            AddTranslation("QuickView_SubTitle", "透過此工具可快速為專案中的元件建立獨立視圖", "Quickly create isolated views for components", "コンポーネントの独立したビューをすばやく作成します");
            AddTranslation("QuickView_Step1", "1. 選擇要建立視圖的元件", "1. Select Components", "1. コンポーネントを選択");
            AddTranslation("QuickView_ToggleSelectAll", "全選 / 取消全選", "Select / Deselect All", "すべて選択 / 選択解除");
            AddTranslation("QuickView_LoadingPreview", "正在載入預覽...", "Loading Preview...", "プレビューを読み込み中...");
            AddTranslation("QuickView_Step2", "2. 選擇視圖樣板與數量", "2. Select Template & Quantity", "2. ビューテンプレートと数量を選択");
            AddTranslation("QuickView_SelectTemplate", "選擇視圖樣板", "Select View Template", "ビューテンプレートを選択");
            AddTranslation("QuickView_Count", "建立數量", "Creation Count", "作成数");
            AddTranslation("QuickView_AddToList", "加入清單", "Add to List", "リストに追加");
            AddTranslation("QuickView_Step3", "3. 確認清單並建立", "3. Confirm List & Create", "3. リストを確認して作成");
            AddTranslation("QuickView_RemoveSelected", "移除選取項目", "Remove Selected", "選択項目を削除");
            AddTranslation("QuickView_ClearAll", "清空清單", "Clear All", "すべてクリア");
            AddTranslation("QuickView_ColCreateSheet", "建立圖紙", "Create Sheet", "シート作成");
            AddTranslation("QuickView_ColSource", "來源元件", "Source Component", "ソースコンポーネント");
            AddTranslation("QuickView_ColTemplate", "視圖樣板", "View Template", "ビューテンプレート");
            AddTranslation("QuickView_ColTargetName", "目標視圖名稱", "Target View Name", "ターゲットビュー名");
            AddTranslation("QuickView_ColSheetNum", "圖紙編號", "Sheet Number", "シート番号");
            AddTranslation("QuickView_ExecBtn", "開始建立", "Start Creation", "作成開始");
            AddTranslation("SheetPlacer_Title", "圖紙視圖排版", "Sheet View Placer", "シートビュー配置");
            AddTranslation("SheetPlacer_SubTitle", "快速將視圖排列至圖紙", "Quickly arrange views on sheets", "シートにビューをすばやく配置");
            AddTranslation("SheetPlacer_TabVisual", "視覺排版", "Visual Layout", "ビジュアル配置");
            AddTranslation("SheetPlacer_TabNumeric", "數值排版", "Numeric Layout", "数値配置");
            AddTranslation("SheetPlacer_SettingsTitle", "排版設定", "Layout Settings", "配置設定");
            AddTranslation("SheetPlacer_DefaultTitleBlock", "預設圖框", "Default TitleBlock", "デフォルトタイトルブロック");
            AddTranslation("SheetPlacer_DefaultViewport", "預設視埠類型", "Default Viewport", "デフォルトビューポート");
            AddTranslation("SheetPlacer_InstructTitle", "操作說明", "Instructions", "操作説明");
            AddTranslation("SheetPlacer_Instruct1Title", "步驟 1：", "Step 1:", "ステップ 1：");
            AddTranslation("SheetPlacer_Instruct1Text", "從左側清單選擇要放置的視圖。", "Select views to place from the left list.", "左側のリストから配置するビューを選択します。");
            AddTranslation("SheetPlacer_Instruct2Title", "步驟 2：", "Step 2:", "ステップ 2：");
            AddTranslation("SheetPlacer_Instruct2Text", "在右側圖紙預覽區域點擊以放置視圖。", "Click in the preview area to place the view.", "右側のプレビュー領域をクリックしてビューを配置します。");
            AddTranslation("SheetPlacer_Instruct3Title", "步驟 3：", "Step 3:", "ステップ 3：");
            AddTranslation("SheetPlacer_Instruct3Text", "拖曳已放置的視圖來調整位置。", "Drag placed views to adjust positions.", "配置されたビューをドラッグして位置を調整します。");
            AddTranslation("SheetPlacer_TweakTitle", "微調視圖", "Tweak Views", "ビューの微調整");
            AddTranslation("SheetPlacer_TweakStepTitle", "對齊工具：", "Alignment Tools:", "整列ツール：");
            AddTranslation("SheetPlacer_TweakStepLabel", "選取多個視圖後可進行對齊：", "Select multiple views to align:", "複数のビューを選択して整列：");
            AddTranslation("SheetPlacer_AlignLeft", "靠左對齊", "Align Left", "左揃え");
            AddTranslation("SheetPlacer_AlignHCenter", "水平置中", "Align H-Center", "水平中央揃え");
            AddTranslation("SheetPlacer_AlignRight", "靠右對齊", "Align Right", "右揃え");
            AddTranslation("SheetPlacer_AlignTop", "靠上對齊", "Align Top", "上揃え");
            AddTranslation("SheetPlacer_AlignVCenter", "垂直置中", "Align V-Center", "垂直中央揃え");
            AddTranslation("SheetPlacer_AlignBottom", "靠下對齊", "Align Bottom", "下揃え");
            AddTranslation("SheetPlacer_AlignTitle", "對齊標題", "Align Titles", "タイトルを揃える");
            AddTranslation("SheetPlacer_GridTitle", "網格排版：", "Grid Layout:", "グリッド配置：");
            AddTranslation("SheetPlacer_GridRows", "列數：", "Rows:", "行：");
            AddTranslation("SheetPlacer_GridCols", "欄數：", "Cols:", "列：");
            AddTranslation("SheetPlacer_GridGapX", "水平間距：", "Gap X:", "水平間隔：");
            AddTranslation("SheetPlacer_GridGapY", "垂直間距：", "Gap Y:", "垂直間隔：");
            AddTranslation("SheetPlacer_GridStartX", "起始 X：", "Start X:", "開始 X：");
            AddTranslation("SheetPlacer_GridStartY", "起始 Y：", "Start Y:", "開始 Y：");
            AddTranslation("SheetPlacer_GridApplyBtn", "套用網格", "Apply Grid", "グリッドを適用");
            AddTranslation("SheetPlacer_NewSheetBtn", "新增圖紙", "New Sheet", "新規シート");
            AddTranslation("SheetPlacer_RefreshBtn", "重新整理", "Refresh", "更新");
            AddTranslation("SheetPlacer_RemoveViewBtn", "移除視圖", "Remove View", "ビューを削除");
            AddTranslation("SheetPlacer_CloseBtn", "關閉", "Close", "閉じる");
            AddTranslation("SheetPlacer_SearchPrompt", "搜尋視圖...", "Search views...", "ビューを検索...");
            AddTranslation("SheetPlacer_StatusReady", "準備就緒", "Ready", "準備完了");
            AddTranslation("SheetPlacer_LoadingPreview", "載入中...", "Loading...", "読み込み中...");
            AddTranslation("TileElev_Title", "磁磚立面產生器", "Tile Elevation Generator", "タイル立面ジェネレーター");
            AddTranslation("TileElev_SubTitle", "為房間或空間自動產生磁磚立面圖", "Auto-generate tile elevations for rooms", "部屋のタイル立面図を自動生成します");
            AddTranslation("TileElev_SourceModeTitle", "來源模式", "Source Mode", "ソースモード");
            AddTranslation("TileElev_ModeFloor", "以樓板為界", "By Floor", "床を基準");
            AddTranslation("TileElev_ModeWall", "以牆為界", "By Wall", "壁を基準");
            AddTranslation("TileElev_SettingTitle", "立面設定", "Elevation Settings", "立面設定");
            AddTranslation("TileElev_ViewPrefix", "視圖前綴", "View Prefix", "ビュー接頭辞");
            AddTranslation("TileElev_ViewTemplate", "視圖樣板", "View Template", "ビューテンプレート");
            AddTranslation("TileElev_GenerateBtn", "產生立面", "Generate Elevation", "立面を作成");
            AddTranslation("TileElev_SelectBtn", "選擇房間", "Select Room", "部屋を選択");
            AddTranslation("TileElev_Tip", "提示：選擇一個封閉空間即可自動產生四向立面。", "Tip: Select a closed space to auto-generate 4-way elevations.", "ヒント：閉じた空間を選択すると、4方向の立面図が自動生成されます。");
            AddTranslation("TileElev_StepTitle", "產生步驟", "Generation Steps", "生成ステップ");
            AddTranslation("TileElev_Step1", "1. 選擇空間", "1. Select Space", "1. 空間を選択");
            AddTranslation("TileElev_Step2", "2. 偵測邊界", "2. Detect Boundaries", "2. 境界を検出");
            AddTranslation("TileElev_Step3", "3. 建立標註", "3. Create Dimensions", "3. 寸法を作成");
            AddTranslation("TileElev_Step4", "4. 建立視圖", "4. Create Views", "4. ビューを作成");
            AddTranslation("TileElev_Step5", "5. 放置圖紙", "5. Place on Sheets", "5. シートに配置");
            AddTranslation("TileElev_HelpBtn", "說明", "Help", "ヘルプ");
            AddTranslation("TileElev_HelpTooltip", "點擊查看操作說明", "Click for instructions", "クリックして操作説明を表示");
            AddTranslation("Common_Close", "關閉", "Close", "閉じる");
                        AddTranslation("TileSys_Title", "磁磚排版系統 v3", "Tile Layout System v3", "タイルレイアウトシステム v3");
            AddTranslation("TileSys_GenLayout", "自動產生排版", "Generate Layout", "レイアウト自動作成");
            AddTranslation("TileSys_AdjustOrigin", "調整磁磚起點", "Adjust Origin", "タイル起点調整");
            AddTranslation("TileSys_DeleteLayout", "刪除現有排版", "Delete Layout", "レイアウトを削除");
            AddTranslation("TileSys_ResetOrigin", "重設磁磚起點", "Reset Origin", "タイル起点をリセット");
            AddTranslation("RoomFinish_Title", "房間裝修配置工具", "Room Finish Configurator", "部屋仕上げ設定ツール");
            AddTranslation("RoomFinish_AddPatternBtn", "新增粉刷線", "Add Pattern", "パターンを追加");
            AddTranslation("RoomFinish_AutoGenerateBtn", "自動依房間產生", "Auto Generate by Room", "部屋から自動生成");
            AddTranslation("RoomFinish_RoomFilter", "房間篩選", "Room Filter", "部屋フィルター");
            AddTranslation("RoomFinish_FilterByLevel", "依樓層篩選", "Filter by Level", "レベルで絞り込み");
            AddTranslation("RoomFinish_FilterByName", "名稱包含", "Name Contains", "名前に含まれる");
            AddTranslation("RoomFinish_FilterByNumber", "編號包含", "Number Contains", "番号に含まれる");
            AddTranslation("RoomFinish_SelectAll", "全選", "Select All", "すべて選択");
            AddTranslation("RoomFinish_DeselectAll", "取消全選", "Deselect All", "選択解除");
            AddTranslation("RoomFinish_ConfigPanel", "配置設定", "Configuration", "設定");
            AddTranslation("RoomFinish_StatusReady", "準備就緒", "Ready", "準備完了");
            
            AddTranslation("SheetDuplicator_Title", "📑 圖紙逐層量化開圖", "📑 Sheet Duplicator", "📑 シート複製ツール");
            AddTranslation("SheetDuplicator_Prefix", "圖紙編號前綴：", "Sheet Prefix:", "シート接頭辞：");
            AddTranslation("SheetDuplicator_Analyze", "分析", "Analyze", "分析");
            AddTranslation("SheetDuplicator_Structure", "圖紙與視圖結構", "Sheet & View Structure", "シートとビューの構造");
            AddTranslation("SheetDuplicator_TargetLevels", "選擇目標樓層", "Select Target Levels", "対象レベルを選択");
            AddTranslation("SheetDuplicator_Generate", "執行量化開圖", "Generate Sheets", "シート作成実行");
            AddTranslation("SheetDuplicator_PreviewTitle", "版面配置示意預覽", "Layout Preview", "レイアウトプレビュー");
            AddTranslation("SheetDuplicator_PreviewPrompt", "請於左側樹狀圖選擇一張圖紙或視圖", "Please select a sheet or view from the tree on the left", "左側のツリーからシートまたはビューを選択してください");
            AddTranslation("Ribbon_Btn_SheetPlacer", "圖紙排版", "Sheet Placer", "シート配置");
            AddTranslation("Ribbon_TT_SheetPlacer", "快速將視圖排列至圖紙", "Quickly arrange views on sheets", "シートにビューをすばやく配置");
            AddTranslation("Ribbon_Btn_SheetDuplicator", "圖紙逐層量化", "Sheet Duplicator", "シート複製ツール");
            AddTranslation("Ribbon_TT_SheetDuplicator", "自動分析標準圖紙的內容並複製到多個目標樓層。", "Automatically analyze standard sheet content and duplicate to multiple target levels.", "標準シートの内容を自動分析し、複数の対象レベルに複製します。");
            AddTranslation("Ribbon_Btn_QuickDim", "快速標註", "Quick Dimension", "クイック寸法");
            AddTranslation("Ribbon_TT_QuickDim", "快速建立尺寸標註", "Quickly create dimensions", "すばやく寸法を作成");
            AddTranslation("Ribbon_Btn_LangDropdown", "語言切換", "Language", "言語切替");
            AddTranslation("Ribbon_TT_LangDropdown", "切換介面語言", "Switch Interface Language", "インターフェース言語を切り替え");

                        AddTranslation("Ribbon_Panel_Tag", "標註工具", "Tags", "注釈ツール");
                        AddTranslation("TileSys_Desc_GenLayout", "點選任意牆面或地板面，自動讀取描述欄位內的磁磚尺寸與縫隙，疊加填充線（不動原有材質）", "Select any wall or floor face to auto-generate tile layout patterns based on description parameters (non-destructive).", "壁または床面を選択して、タイルのレイアウトパターンを自動生成します（既存の材質に影響しません）。");
            AddTranslation("TileSys_ConfirmPaint", "✔ 1. 確認鋪貼並改寫原材質圖案", "✔ 1. Confirm and Override Material Pattern", "✔ 1. 割り当てを確定し、マテリアルパターンを上書き");
            AddTranslation("TileSys_Desc_ConfirmPaint", "選取已 Paint 暫時填充線的面，將其確認為正式貼面並修改原有裝修層材質的前景 .pat 填充線", "Select painted faces to confirm as formal tiles and override the foreground .pat of the original finish material.", "ペイントされた面を選択し、正式なタイルとして確定し、元の仕上げ材のフォアグラウンドパターンを上書きします。");
            AddTranslation("TileSys_Gen3DWall", "🧱 2-1. 建立 3D 牆面磁磚實體", "🧱 2-1. Generate 3D Wall Tiles", "🧱 2-1. 3D壁タイルを生成");
            AddTranslation("TileSys_Desc_Gen3DWall", "點選已改寫前景填充線的牆面，依結構厚度生成 3D 牆面磁磚實體", "Select overridden wall faces to generate actual 3D tile elements based on structural thickness.", "上書きされた壁面を選択し、構造の厚みに基づいて実際の3Dタイル要素を生成します。");
            AddTranslation("TileSys_Gen3DFloor", "🟨 2-2. 建立 3D 地坪磁磚實體", "🟨 2-2. Generate 3D Floor Tiles", "🟨 2-2. 3D床タイルを生成");
            AddTranslation("TileSys_Desc_Gen3DFloor", "點選已改寫前景填充線的地坪面，依結構厚度生成 3D 地坪磁磚實體", "Select overridden floor faces to generate actual 3D tile elements based on structural thickness.", "上書きされた床面を選択し、構造の厚みに基づいて実際の3Dタイル要素を生成します。");
            AddTranslation("TileSys_ChangeTileMaterial", "🎨 局部變更磁磚材質", "🎨 Change Local Tile Material", "🎨 局所的なタイルマテリアルを変更");
            AddTranslation("TileSys_Desc_ChangeTileMaterial", "選取多塊已生成的 3D 磁磚，批次變更其局部材質與樣式", "Select multiple 3D tiles to batch change their individual material and style.", "複数の3Dタイルを選択し、個々のマテリアルとスタイルを一括で変更します。");
            AddTranslation("TileSys_ConvertToEditable", "📐 轉換為可編輯磁磚", "📐 Convert to Editable Tiles", "📐 編集可能なタイルに変換");
            AddTranslation("TileSys_Desc_ConvertToEditable", "點選不可調整的 3D 磁磚，原地轉換為原生 Walls / Floors 元件，即可雙擊 Edit Profile 進行修改", "Select non-adjustable 3D tiles to convert them into native Walls/Floors for profile editing.", "調整不可能な3Dタイルを選択し、プロファイル編集用のネイティブな壁/床に変換します。");
            AddTranslation("TileSys_Stats2D", "📊 3. 平面幾何統計", "📊 3. 2D Geometry Stats", "📊 3. 2D ジオメトリ統計");
            AddTranslation("TileSys_Desc_Stats2D", "多選已改寫前景填充線的正式面，藉由幾何排版引擎估算數量", "Select overridden formal faces to estimate quantities using the geometric layout engine.", "上書きされた正式な面を選択し、幾何学レイアウトエンジンを使用して数量を推定します。");
            AddTranslation("TileSys_Stats3D", "🔍 4. 3D 實體統計", "🔍 4. 3D Element Stats", "🔍 4. 3D 要素統計");
            AddTranslation("TileSys_Desc_Stats3D", "多選房間元件，統計選定空間內所有已生成的 3D 磁磚實體與損耗率", "Select rooms to calculate generated 3D tiles and waste rates within the space.", "部屋を選択し、空間内の生成された3Dタイルとロス率を計算します。");
            AddTranslation("TileSys_CreateSchedule", "📄 建立 Revit 明細表", "📄 Create Revit Schedule", "📄 Revit 集計表を作成");
            AddTranslation("TileSys_Desc_CreateSchedule", "在專案中建立常規模型磁磚明細表", "Create a Generic Model Tile Schedule in the project.", "プロジェクト内に一般モデルのタイル集計表を作成します。");
            AddTranslation("TileSys_ExportExcel", "📥 匯出 Excel 統計表", "📥 Export Excel Stats", "📥 Excel 統計をエクスポート");
            AddTranslation("TileSys_Desc_ExportExcel", "收集專案中所有磁磚資料，匯出為 Excel / CSV 統計報表", "Collect all tile data in the project and export as Excel / CSV reports.", "プロジェクト内のすべてのタイルデータを収集し、Excel / CSV レポートとしてエクスポートします。");
            AddTranslation("TileSys_AddJointParam", "▍ 加入縫隙寬度參數（首次使用時點擊）", "▍ Add Joint Width Parameter (Click on first use)", "▍ 目地幅パラメーターを追加（初回のみクリック）");
            AddTranslation("TileSys_WallJointParam", "🧱 裝修牆縫隙參數", "🧱 Wall Finish Joint Parameter", "🧱 壁仕上げ目地パラメーター");
            AddTranslation("TileSys_Desc_WallJointParam", "在所有裝修牆的屬性面板新增「Tile_Joint_Width」欄位（mm），預設 3mm，可個別修改後再執行鋪貼", "Adds a Tile_Joint_Width (mm) parameter to all finish walls, default 3mm. Can be customized before layout.", "すべての仕上げ壁に Tile_Joint_Width (mm) パラメーターを追加します（デフォルト 3mm）。レイアウト前にカスタマイズ可能です。");
            AddTranslation("TileSys_FloorJointParam", "🟨 裝修地板縫隙參數", "🟨 Floor Finish Joint Parameter", "🟨 床仕上げ目地パラメーター");
            AddTranslation("TileSys_Desc_FloorJointParam", "在所有裝修地板的屬性面板新增「Tile_Joint_Width」欄位（mm），預設 3mm，可個別修改後再執行鋪貼", "Adds a Tile_Joint_Width (mm) parameter to all finish floors, default 3mm. Can be customized before layout.", "すべての仕上げ床に Tile_Joint_Width (mm) パラメーターを追加します（デフォルト 3mm）。レイアウト前にカスタマイズ可能です。");
            AddTranslation("TileSys_Desc_DeleteLayout", "移除已疊加的填充線（不影響原有材質）", "Remove overlaid patterns (does not affect original materials)", "オーバーレイされたパターンを削除（元のマテリアルには影響しません）");
            AddTranslation("TileSys_Desc_ResetOrigin", "只移除本外掛疊加的磁磚填充線 Paint，不會修改或刪除 any 原有材質設定", "Only removes the tile paint applied by this plugin. Does not alter original material settings.", "このプラグインで適用されたタイルペイントのみを削除します。元のマテリアル設定は変更しません。");
            AddTranslation("TileSys_Feedback", "💬 問題與意見反饋", "💬 Feedback & Support", "💬 フィードバックとサポート");
            AddTranslation("TileSys_Desc_Feedback", "向作者提交功能優化反饋或錯誤回報", "Submit feature requests or bug reports to the author.", "作成者に機能リクエストやバグ報告を送信します。");
                        AddTranslation("Feedback_Title", "問題與意見反饋", "Feedback & Support", "フィードバックとサポート");
            AddTranslation("Feedback_SubmitTitle", "💬 提交意見與問題反饋", "💬 Submit Feedback & Issues", "💬 フィードバックと問題の送信");
            AddTranslation("Feedback_Account", "目前帳號：", "Current Account:", "現在のアカウント：");
            AddTranslation("Feedback_Topic", "反饋標題：", "Title:", "タイトル：");
            AddTranslation("Feedback_Desc", "詳細描述：", "Description:", "詳細な説明：");
            AddTranslation("Feedback_Template", "【問題回報格式範例】\\n1. 發生的工具：...\\n...", "[Issue Report Template]\\n1. Tool:...", "[問題報告テンプレート]\\n1. ツール：...");
            AddTranslation("RoomFinish_Desc1", "集中列出專案房間，批次編輯天地牆裝修材質，為自動化排磚與粉刷提供數據基礎", "Centrally list project rooms, batch edit finishes for floors, walls, and ceilings to provide a data foundation for automated tiling and finishing.", "プロジェクトの部屋を集中管理し、床、壁、天井の仕上げを一括編集して、自動タイル配置と仕上げのデータ基盤を提供します。");
            AddTranslation("RoomFinish_SearchHint", "可搜尋房間編號、名稱、地/牆/天裝修材質名稱", "Search by room number, name, or finish material name.", "部屋番号、名前、または仕上げ材名で検索できます。");
            AddTranslation("RoomFinish_HelpText", "這能幫助你快速為多個房間填入相同的裝修規格名稱（如：地磚 60x60、乳膠漆、矽酸鈣板）。完成後可在右側即時檢查並點擊確定寫入房間參數。", "This helps you quickly fill in the same finish specifications for multiple rooms (e.g., floor tiles 60x60, latex paint, calcium silicate board). Once complete, you can instantly verify on the right and click to write to room parameters.", "これにより、複数の部屋に同じ仕上げ仕様をすばやく入力できます（例：床タイル 60x60、ラテックス塗料、ケイ酸カルシウム板）。完了したら、右側で即座に確認し、クリックして部屋パラメーターに書き込みます。");
            AddTranslation("RoomFinish_BaseFinish", "地板裝修 (Base Finish)", "Base Finish", "床仕上げ");
            AddTranslation("RoomFinish_WallFinish", "牆面裝修 (Wall Finish)", "Wall Finish", "壁仕上げ");
            AddTranslation("RoomFinish_CeilingFinish", "天花板裝修 (Ceiling Finish)", "Ceiling Finish", "天井仕上げ");
            AddTranslation("RoomFinish_InputHint", "填入要批次寫入的材質代號或名稱，例如: T-01", "Enter the material code or name to batch write, e.g., T-01", "一括で書き込むマテリアルコードまたは名前を入力します（例：T-01）");
            AddTranslation("TileSys_ConvertToEditable2", "📐 轉換為可編輯磁磚", "📐 Convert to Editable Tiles", "📐 編集可能なタイルに変換");
            AddTranslation("TileSys_ConvertToEditable_Desc", "點選不可調整的 3D 磁磚，原地轉換為原生 Walls / Floors 元件，即可雙擊 Edit Profile 進行修改", "Select non-adjustable 3D tiles and convert them to native Walls/Floors for profile editing.", "調整不可能な3Dタイルを選択し、プロファイル編集用にネイティブな壁/床に変換します。");
            AddTranslation("TileSys_Stats2D2", "📊 3. 平面幾何統計", "📊 3. 2D Geometry Stats", "📊 3. 2D ジオメトリ統計");
            AddTranslation("TileSys_Stats2D_Desc", "多選已改寫前景填充線的正式面，藉由幾何排版引擎估算數量", "Select overridden formal faces to estimate quantities using the layout engine.", "上書きされた正式な面を選択し、レイアウトエンジンを使用して数量を推定します。");
            AddTranslation("TileSys_Stats3D2", "🔍 4. 3D 實體統計", "🔍 4. 3D Element Stats", "🔍 4. 3D 要素統計");
            AddTranslation("TileSys_Stats3D_Desc", "多選房間元件，統計選定空間內所有已生成的 3D 磁磚實體與損耗率", "Select rooms to calculate generated 3D tiles and waste rates.", "部屋を選択し、生成された3Dタイルとロス率を計算します。");
            AddTranslation("TileSys_CreateSchedule2", "📄 建立 Revit 明細表", "📄 Create Revit Schedule", "📄 Revit 集計表を作成");
            AddTranslation("TileSys_CreateSchedule_Desc", "在專案中建立常規模型磁磚明細表", "Create a Generic Model Tile Schedule in the project.", "プロジェクト内に一般モデルのタイル集計表を作成します。");
            AddTranslation("TileSys_ExportExcel2", "📥 匯出 Excel 統計表", "📥 Export Excel Stats", "📥 Excel 統計をエクスポート");
            AddTranslation("TileSys_ExportExcel_Desc", "收集專案中所有磁磚資料，匯出為 Excel / CSV 統計報表", "Collect all tile data in the project and export as Excel/CSV.", "プロジェクト内のすべてのタイルデータを収集し、Excel/CSVとしてエクスポートします。");
            AddTranslation("TileSys_AddJointParam2", "▍ 加入縫隙寬度參數（首次使用時點擊）", "▍ Add Joint Width Parameter", "▍ 目地幅パラメーターを追加");
            AddTranslation("TileSys_WallJointParam2", "🧱 裝修牆縫隙參數", "🧱 Wall Finish Joint Parameter", "🧱 壁仕上げ目地パラメーター");
            AddTranslation("TileSys_WallJointParam_Desc", "在所有裝修牆的屬性面板新增「Tile_Joint_Width」欄位（mm），預設 3mm，可個別修改後再執行鋪貼", "Adds a Tile_Joint_Width (mm) parameter to all finish walls, default 3mm.", "すべての仕上げ壁に Tile_Joint_Width (mm) パラメーターを追加します（デフォルト 3mm）。");
            AddTranslation("TileSys_FloorJointParam2", "🟨 裝修地板縫隙參數", "🟨 Floor Finish Joint Parameter", "🟨 床仕上げ目地パラメーター");
            AddTranslation("TileSys_FloorJointParam_Desc", "在所有裝修地板的屬性面板新增「Tile_Joint_Width」欄位（mm），預設 3mm，可個別修改後再執行鋪貼", "Adds a Tile_Joint_Width (mm) parameter to all finish floors, default 3mm.", "すべての仕上げ床に Tile_Joint_Width (mm) パラメーターを追加します（デフォルト 3mm）。");
            AddTranslation("TileSys_DeleteLayout_Desc", "移除已疊加的填充線（不影響原有材質）", "Remove overlaid patterns (does not affect original materials)", "オーバーレイされたパターンを削除（元のマテリアルには影響しません）");
            AddTranslation("TileSys_ResetOrigin_Desc", "只移除本外掛疊加的磁磚填充線 Paint，不會修改或刪除 any 原有材質設定", "Only removes the tile paint applied by this plugin. Does not alter original materials.", "このプラグインで適用されたタイルペイントのみを削除します。元のマテリアルは変更しません。");
            AddTranslation("TileSys_Feedback2", "💬 問題與意見反饋", "💬 Feedback & Support", "💬 フィードバックとサポート");
            AddTranslation("TileSys_Feedback_Desc", "向作者提交功能優化反饋或錯誤回報", "Submit feature requests or bug reports to the author.", "作成者に機能リクエストやバグ報告を送信します。");
            AddTranslation("RoomFinish_Title_Full", "🏠 房間裝修材質配置工具", "🏠 Room Finish Configurator", "🏠 部屋仕上げ設定ツール");
            AddTranslation("TileSys_Mode1_Btn_Alt", "📌  手動點選面 → 生成磁磚填充線", "📌  Manually select faces → Generate Tile Patterns", "📌  面を手動で選択 → タイルパターンを生成");
            AddTranslation("TileSys_Mode1_Confirm_Alt", "✔️  1. 確認鋪貼並改寫原材質圖案", "✔️  1. Confirm and Override Pattern", "✔️  1. 割り当てを確定し、パターンを上書き");
            AddTranslation("TileSys_Gen3DWall2_Alt", "🧱  2-1. 建立 3D 牆面磁磚實體", "🧱  2-1. Generate 3D Wall Tiles", "🧱  2-1. 3D壁タイルを生成");
            AddTranslation("TileSys_Gen3DFloor2_Alt", "🟨  2-2. 建立 3D 地坪磁磚實體", "🟨  2-2. Generate 3D Floor Tiles", "🟨  2-2. 3D床タイルを生成");
            AddTranslation("TileSys_ChangeTileMaterial2_Alt", "🎨  局部變更磁磚材質", "🎨  Change Local Tile Material", "🎨  局所的なタイルマテリアルを変更");
            AddTranslation("TileSys_ConvertToEditable2_Alt", "📐  轉換為可編輯磁磚", "📐  Convert to Editable Tiles", "📐  編集可能なタイルに変換");
            AddTranslation("TileSys_FloorJointParam2_Alt", "🟨 裝修地坪縫隙參數", "🟨 Floor Finish Joint Parameter", "🟨 床仕上げ目地パラメーター");
            AddTranslation("RoomFinish_SearchLabel", "🔍 房間搜尋：", "🔍 Room Search:", "🔍 部屋検索：");
            AddTranslation("RoomFinish_BatchFillTitle", "⚡ 批量填入裝修值", "⚡ Batch Fill Finishes", "⚡ 仕上げ値を一括入力");
            AddTranslation("RoomFinish_TargetField", "目標欄位", "Target Field", "対象フィールド");
            AddTranslation("RoomFinish_MaterialSpec", "材質規格", "Material Spec", "マテリアル仕様");
            AddTranslation("RoomFinish_ApplyBtn", "套用至選中/當前房間", "Apply to Selected/Current", "選択/現在の部屋に適用");
            AddTranslation("RoomFinish_InvertSelectBtn", "☑️ 反選所有房間", "☑️ Invert Selection", "☑️ 選択を反転");
            AddTranslation("RoomFinish_EditHint", "* 提示：按兩下材質欄位即可直接修改單一房間屬性", "* Hint: Double-click a material cell to edit directly", "* ヒント：マテリアルセルをダブルクリックして直接編集");
            AddTranslation("RoomFinish_ColModified", "異動", "Mod", "変更");
            AddTranslation("RoomFinish_ColNumber", "編號", "Number", "番号");
            AddTranslation("RoomFinish_ColName", "房間名稱", "Room Name", "部屋名");
            AddTranslation("RoomFinish_ColFloor", "地板裝修 (Base Finish)", "Base Finish", "床仕上げ");
            AddTranslation("RoomFinish_ColWall", "牆面裝修 (Wall Finish)", "Wall Finish", "壁仕上げ");
            AddTranslation("RoomFinish_ColCeiling", "天花板裝修 (Ceiling Finish)", "Ceiling Finish", "天井仕上げ");
            AddTranslation("RoomFinish_BaseboardFinish", "踢腳板裝修 (Baseboard Finish)", "Baseboard Finish", "幅木仕上げ");
            AddTranslation("RoomFinish_CloudSyncTitle", "🌐 雲端材質同步", "🌐 Cloud Material Sync", "🌐 クラウド同期");
            AddTranslation("RoomFinish_CloudUrlLabel", "雲端連結", "Cloud URL", "クラウドURL");
            AddTranslation("RoomFinish_SyncBtn", "同步雲端設定", "Sync Cloud Config", "クラウドと同期");
            AddTranslation("RoomFinish_DownloadTemplateBtn", "📥 下載 Excel 範本", "📥 Download Excel Template", "📥 テンプレートをダウンロード");
            AddTranslation("RoomFinish_GenerateTitle", "🏗️ 實體裝修模型生成", "🏗️ Generate Finish Geometry", "🏗️ 仕上げジオメトリ生成");
            AddTranslation("RoomFinish_JointRelationLabel", "接合關係", "Joint Relation", "接合関係");
            AddTranslation("RoomFinish_JointFloorFirst", "地坪接牆 (牆作到底)", "Floor First (Wall to Bottom)", "床優先");
            AddTranslation("RoomFinish_JointWallFirst", "牆接地坪 (地坪作到底)", "Wall First (Floor to Bottom)", "壁優先");
            AddTranslation("RoomFinish_GenerateBtn", "生成選中房間的裝修", "Generate Room Finishes", "仕上げモデルを生成");
            AddTranslation("Feedback_SubmitBtn", "送出反饋", "Submit Feedback", "フィードバックを送信");
            AddTranslation("TileSys_ExportSection", "▍ 統計與明細表匯出", "▍ Stats & Schedule Export", "▍ 統計と集計表のエクスポート");
            AddTranslation("TileSys_OtherSection", "▍ 其他", "▍ Others", "▍ その他");
                        AddTranslation("Elevation_StatusReady", "準備就緒，請選擇樓板或牆面。", "Ready. Please select Floor or Walls.", "準備完了。床または壁を選択してください。");
            AddTranslation("Elevation_NoElement", "尚未選取任何元素", "No element selected.", "要素が選択されていません。");
            AddTranslation("Elevation_FloorPrefix", "樓板: ", "Floor: ", "床: ");
            AddTranslation("Elevation_NoFloor", "尚未選取樓板", "No Floor selected.", "床が選択されていません。");
            AddTranslation("Elevation_WallsSelectedSuffix", " 面牆已選取", " Walls selected.", " つの壁が選択されました。");
            AddTranslation("Elevation_NoWalls", "尚未選取牆面", "No Walls selected.", "壁が選択されていません。");
            AddTranslation("Elevation_PromptSelectFloor", "請在 Revit 中點選一個樓板...", "Please click to select a Floor in Revit...", "Revit で床をクリックして選択してください...");
            AddTranslation("Elevation_FloorSuccess", "樓板選取成功。", "Floor selected successfully.", "床の選択に成功しました。");
            AddTranslation("Elevation_PromptSelectWalls", "請在 Revit 中框選多個牆面...", "Please select multiple Walls in Revit...", "Revit で複数の壁を選択してください...");
            AddTranslation("Elevation_WallsSuccess", "牆面選取成功。", "Walls selected successfully.", "壁の選択に成功しました。");
            AddTranslation("Elevation_SelectionCancelled", "使用者取消選取。", "Selection cancelled by user.", "ユーザーにより選択がキャンセルされました。");
            AddTranslation("Elevation_SelectionError", "選取錯誤: ", "Selection error: ", "選択エラー: ");
            AddTranslation("Common_Confirm", "確認", "Confirm", "確認");
            AddTranslation("Common_Cancel", "取消", "Cancel", "キャンセル");
            AddTranslation("Common_Close", "關閉", "Close", "閉じる");
            AddTranslation("Common_Success", "成功", "Success", "成功");
            AddTranslation("Common_Warning", "提示", "Warning", "警告");
            AddTranslation("Common_Error", "錯誤", "Error", "エラー");
            AddTranslation("Common_NoTemplate", "[不套用樣板]", "[No Template]", "[テンプレートなし]");

            // ----------------------------------------------------------------
            // 新手教學彙整多國語言
            // ----------------------------------------------------------------
            AddTranslation("TileSys_Tutorial", "新手說明", "Tutorial", "チュートリアル");
            AddTranslation("TileSys_TutorialDesc", "開啟此工具的新手使用指南與操作步驟說明", "Open user guide and step-by-step instructions for this tool", "このツールのユーザーガイドとステップバイステップの手順を表示します");

            // 1. 磁磚鋪設系統 (TileSys)
            AddTranslation("Tut_TileSys_Title", "磁磚鋪設系統 - 新手指南", "Tile Layout System - Guide", "タイルレイアウトシステム - ガイド");
            AddTranslation("Tut_TileSys_Content", "【快捷排磚四部曲】\n" +
                                     "1. [點選面預覽] - 點擊「手動點選面 → 生成磁磚填充線」，在視圖中點選要鋪磁磚的牆面或地板面。\n" +
                                     "2. [確認鋪貼] - 點擊「確認鋪貼並改寫原材質圖案」，點選該面將排版填充線正式寫入材質中。\n" +
                                     "3. [生成3D實體] - 點擊「建立 3D 牆面磁磚實體」或「建立 3D 地坪磁磚實體」，選取面即可生成 3D 磁磚實體。\n" +
                                     "4. [人工修整收邊] - 點選需要修剪的磁磚，點擊「轉換為可編輯磁磚」，即可雙擊或使用 Revit 的「編輯輪廓」手動裁剪不規則邊角。\n\n" +
                                     "【關鍵提示】\n" +
                                     "• [縫隙參數]：首次使用時，請點擊下方的「裝修牆/地板縫隙參數」，在專案中加入 Tile_Joint_Width 縫寬屬性。\n" +
                                     "• [局部變更]：使用「局部變更磁磚材質」可以多選 3D 磁磚進行局部更換材質球（如腰線）。\n" +
                                     "• [重設與刪除]：使用「重設磁磚起點」可重新定位排磚原點；使用「刪除現有排版」可清除外掛疊加的填充線。\n" +
                                     "• [明細與導出]：使用「建立 Revit 明細表」或「匯出 Excel 統計表」可輕鬆統計工程量與損耗率。",
                                     "【4-Step Quick Start】\n" +
                                     "1. [Select Face & Preview] - Click 'Manually select faces -> Generate Tile Patterns' and select target wall/floor faces in your view.\n" +
                                     "2. [Confirm Layout] - Click '1. Confirm and Override Pattern' and select the face to write the pattern to the material.\n" +
                                     "3. [Generate 3D Tiles] - Click '2-1. Generate 3D Wall Tiles' or '2-2. Generate 3D Floor Tiles' and click the face.\n" +
                                     "4. [Manual Edit] - Choose tiles, click 'Convert to Editable Tiles' to enable native profile editing.\n\n" +
                                     "【Key Tips】\n" +
                                     "• [Joint Parameter] - Run 'Wall Finish Joint Parameter' & 'Floor Finish Joint Parameter' on first use to inject joint width attributes.\n" +
                                     "• [Local Change] - Use 'Change Local Tile Material' to batch change material types for specific tiles (e.g. border tiles).\n" +
                                     "• [Reset & Delete] - Use 'Reset Origin' to reposition the tile start point; use 'Delete Layout' to remove overlaid patterns.\n" +
                                     "• [Export & Schedule] - Export to Excel or generate Revit schedule for quick estimates and waste calculations.",
                                     "【タイル配置の4つの手順】\n" +
                                     "1. [面を選択してプレビュー] - 「面を手動で選択 → タイルパターンを生成」をクリックし、ビュー内の壁面または床面を選択します。\n" +
                                     "2. [配置を確定] - 「1. 割り当てを確定し、パターンを上書き」をクリックし、該当する面を選択してパターンを適用します。\n" +
                                     "3. [3Dタイルの作成] - 「2-1. 3D壁タイルを生成」または「2-2. 3D床タイルを生成」をクリックして、該当面を選択します。\n" +
                                     "4. [手動微調整] - 「編集可能なタイルに変換」をクリックして、Revit標準の「プロファイルを編集」で形状をトリミングします。\n\n" +
                                     "【重要なヒント】\n" +
                                     "• [目地パラメータ] - 初回使用時は、最下部の「壁/床仕上げ目地パラメータ」をクリックし、プロジェクトに Tile_Joint_Width 属性を追加してください。\n" +
                                     "• [部分変更] - 「局所的なタイルマテリアルを変更」を使用して、一部 of タイルのマテリアルを変更できます。\n" +
                                     "• [リセットと削除] - 「タイル起点をリセット」で起点を再設定し、「レイアウトを削除」でオーバーレイされたパターンのみを削除できます。\n" +
                                     "• [集計表と出力] - 「Revit 集計表を作成」または「Excel 統計をエクスポート」を使用して数量とロス率を集計します。");

            // 2. 展開圖生成器 (TileElev)
            AddTranslation("Tut_TileElev_Title", "展開圖生成器 - 新手指南", "Tile Elevation Generator - Guide", "タイル立面ジェネレーター - ガイド");
            AddTranslation("Tut_TileElev_Content", "【快速入門三步驟】\n" +
                                     "1. [選擇來源模式與物件]：\n" +
                                     "   - 樓板模式 (Floor Mode)：選取一個樓板，系統會自動抓取相鄰牆面並以「順時針幾何方向」進行排序與編號（預設為 A, B, C, D）。\n" +
                                     "   - 牆體模式 (Wall Mode)：在 Revit 中多選多面牆體，系統亦會「自動依順時針幾何方向」進行排序與編號（預設為 A, B, C, D）。\n" +
                                     "2. [微調清單與參數]：\n" +
                                     "   - 載入剖面清單後，點選特定項目可修改 WallOffset（前移距離）、ViewDepth（剖面深度）、SideExtension（兩側延伸，預設 150mm 以呈現轉角裝修）以及上下偏移（預設頂部 -100mm、底部 100mm）。右側 Canvas 將即時繪製範圍示意圖。\n" +
                                     "   - [指定起點]：可點選任一剖面並點擊「將選中項目設為起始位置 (順時針排列)」，清單將自動循環移位重新指派 A, B, C, D 的順序，圖紙排版順序亦會同步更新。\n" +
                                     "   - [方向反轉]：若剖面方向相反，可勾選「相反方向 (反轉)」一鍵旋轉 180 度，右側示意圖會即時連動。\n" +
                                     "3. [一鍵建立與排版]：\n" +
                                     "   - 設定完畢後，點擊「一鍵產生磁磚展開圖」，工具會自動建立剖面視圖、套用樣板並置入圖紙中由左至右對齊排列，防止重疊。",
                                     "【Quick Start Guide】\n" +
                                     "1. [Choose Mode & Elements]:\n" +
                                     "   - Floor Mode: Select a floor. Adjacent walls are auto-detected and sorted \"clockwise\" geometrically (named A, B, C, D by default).\n" +
                                     "   - Wall Mode: Select multiple walls. They will also be sorted \"clockwise\" geometrically (named A, B, C, D by default).\n" +
                                     "2. [Refine Settings]:\n" +
                                     "   - Load profiles to tweak Wall Offset, View Depth, Side Extension (default 150mm for corners), and Top/Bottom Offset (default top: -100mm, bottom: 100mm). Preview changes instantly on the right Canvas.\n" +
                                     "   - [Set Start Point]: Select an item and click 'Set Selected as Start (Clockwise)' to rotate the sequence. Sheet layout order will sync automatically.\n" +
                                     "   - [Flip Direction]: Check 'Flip Direction' to rotate the section view 180 degrees. The right Canvas will update instantly.\n" +
                                     "3. [Generate & Layout]:\n" +
                                     "   - Click 'Generate Elevation' to automatically create section views, apply templates, and layout views onto a sheet aligned horizontally to prevent overlap.",
                                     "【操作の3ステップ】\n" +
                                     "1. [選択モードとオブジェクト]：\n" +
                                     "   - 床を基準 (Floor Mode)：床を選択すると、隣接する壁が「時計回り」に自動検出・ソートされます (A, B, C, D と命名)。\n" +
                                     "   - 壁を基準 (Wall Mode)：複数の壁を選択すると、同様に「時計回り」に自動検出・ソートされます (A, B, C, D と命名)。\n" +
                                     "2. [リストとパラメータの微調整]：\n" +
                                     "   - 壁オフセット、ビュー奥行き、左右延長（角の仕上げ用にデフォルト 150mm）、上下オフセット（デフォルト上: -100mm、下: 100mm）を微調整すると、右側のキャンバスにプレビューが即座に描画されます。\n" +
                                     "   - [起点を設定]：アイテムを選択し、「選択項目を起点に設定 (時計回り)」をクリックすると、ソート順がローテーションし、シート配置順も自動同期されます。\n" +
                                     "   - [方向反転]：断面方向が逆の場合は、「反対方向 (反転)」にチェックを入れて180度回転でき、プレビューも即座に連動します。\n" +
                                     "3. [自動生成と配置]：\n" +
                                     "   - 設定完了後、「立面を作成」をクリックすると、ビューの作成、テンプレートの適用、シートへの重なり防止水平配置が一括で実行されます。");

            // 3. 房間裝修配置工具 (RoomFinish)
            AddTranslation("Tut_RoomFinish_Title", "房間裝修配置工具 - 新手指南", "Room Finish Configurator - Guide", "部屋仕上げ設定ツール - ガイド");
            AddTranslation("Tut_RoomFinish_Content", "【快速入門說明】\n" +
                                     "1. [房間篩選與載入]：\n" +
                                     "   - 工具開啟後，左側會列出目前專案內所有房間。可依據樓層或房間名稱進行篩選過濾。\n" +
                                     "2. [批量填入與雲端同步]：\n" +
                                     "   - [批次填入]：在「批量填入裝修值」區塊，選擇目標欄位 (地板、牆面、天花板、踢腳板)，輸入材質規格，點擊「套用」即可寫入所有已勾選的房間。亦可於右側表格雙擊進行個別修改。\n" +
                                     "   - [雲端 Excel 同步]：您可以點擊「下載 Excel 範本」，或在 CSV URL 輸入框中輸入雲端 CSV 連結，點擊「同步雲端設定」直接批次載入材質配置。\n" +
                                     "3. [自動產生裝修實體模型]：\n" +
                                     "   - 設定好材質參數後，在最下方選擇接合優先順序 (地坪優先或牆優先)，點擊「生成選中房間的裝修」，外掛會依據房間邊界自動生成實體牆面、地坪與天花板的細部粉刷層，並自動扣除門窗洞口！",
                                     "【Introduction】\n" +
                                     "1. [Filter & Load Rooms]:\n" +
                                     "   - Lists all rooms in the project on the right table. Filter by Level or Name on the top.\n" +
                                     "2. [Batch Assign & Cloud Sync]:\n" +
                                     "   - [Batch Assign]: In 'Batch Fill Finishes', select a target field (Base, Wall, Ceiling, Baseboard), type a material spec, and click 'Apply'. You can also double-click cells to edit parameters individually.\n" +
                                     "   - [Cloud Sync]: Click 'Download Excel Template' or enter a cloud CSV URL in the input box, then click 'Sync Cloud Config' to import material settings directly.\n" +
                                     "3. [Auto-Generate Geometry]:\n" +
                                     "   - Tweak the 'Joint Relation' settings (Floor First or Wall First) and click 'Generate Room Finishes'. It will auto-create physical Revit walls/floors finish layers based on room bounds, cutting out doors/windows openings automatically!",
                                     "【クイックユーザーガイド】\n" +
                                     "1. [部屋のフィルタリングとロード]：\n" +
                                     "   - ツール起動後、左側にプロジェクト内のすべての部屋が表示されます。レベルや部屋名でフィルタリングできます。\n" +
                                     "2. [一括入力とクラウド同期]：\n" +
                                     "   - [一括入力]：対象フィールド (床、壁、天井、幅木) を選択し、仕上げ値（例：F-01）を入力して「適用」をクリックします。右側の表のセルをダブルクリックして個別編集も可能です。\n" +
                                     "   - [クラウド同期]：「Excelテンプレートのダウンロード」をクリックするか、CSVのURLを入力して「クラウド設定と同期」をクリックし、直接仕上げ仕様をインポートできます。\n" +
                                     "3. [仕上げモデルの自動生成]：\n" +
                                     "   - 接合優先順（床優先または壁優先）を選択し、「仕上げモデルを生成」をクリックすると、部屋の境界線に沿って自動的に壁・床・天井の仕上げモデルが生成され、開口部も自動で控除されます。");

            // 4. 圖紙批次更名 (BatchRename)
            AddTranslation("Tut_BatchRename_Title", "圖紙批次更名 - 新手指南", "Batch Sheet Renamer - Guide", "シート一括名前変更 - ガイド");
            AddTranslation("Tut_BatchRename_Content", "【快捷更名三法寶】\n" +
                                     "1. [尋找與取代]：\n" +
                                     "   - 選擇目標欄位 (編號或名稱)，輸入尋找文字與取代文字，點擊「套用取代」，即可批次完成部分文字替換。\n" +
                                     "2. [加入前綴與後綴]：\n" +
                                     "   - 可為選取的圖紙編號或名稱前方/後方批次加上特定字元 (例如在編號前方加上 A-)。\n" +
                                     "3. [自動流水號重排]：\n" +
                                     "   - 輸入起始號碼 (如 A-101) 與增量 (如 1 或 2)，點擊「套用流水號」，系統會為選取的圖紙自動依序編號。\n\n" +
                                     "★ [無衝突二階段寫入]：\n" +
                                     "   - 填寫完新編號後，點擊「確認修改」，工具底層會以兩階段臨時編號法更新 Revit 圖紙編號，徹底避免 Revit 內建的「圖紙編號重複」衝突警告，讓您輕鬆重排圖紙順序！",
                                     "【Quick Start Guide】\n" +
                                     "1. [Find & Replace]:\n" +
                                     "   - Select target field (Number or Name), input 'Find What' and 'Replace With', then click 'Apply Replace'.\n" +
                                     "2. [Add Prefix / Suffix]:\n" +
                                     "   - Add specific characters (e.g. 'A-') to the start or end of your sheets batch-wise.\n" +
                                     "3. [Auto Numbering]:\n" +
                                     "   - Set a start number (e.g. A-101) and an increment value. Click 'Apply Numbering' to re-sequence selected sheets.\n\n" +
                                     "★ [Non-conflict 2-Step Writing]:\n" +
                                     "   - When clicking 'Apply Changes', the backend uses a temporary naming strategy to safely swap sheet numbers, bypassing Revit's strict 'Sheet Number already in use' constraints.",
                                     "【便利な一括リネーム機能】\n" +
                                     "1. [検索と置換]：\n" +
                                     "   - 対象フィールド (番号または名前) を選択し、検索文字列と置換文字列を入力して「置換を適用」をクリックします。\n" +
                                     "2. [接頭辞 / 接尾辞の追加]：\n" +
                                     "   - 選択したシートの番号または名前の前後に、特定の文字列 (例: 「A-」など) を一括追加します。\n" +
                                     "3. [自動連番]：\n" +
                                     "   - 開始番号（例：A-101）と増分を入力し、「連番を適用」をクリックして自動的に再採番します。\n\n" +
                                     "★ [重複回避の2段階書き込み]：\n" +
                                     "   - 「変更を適用」をクリックすると、ツールは自動的に一時的な番号を経由して更新を行うため、Revitの「シート番号の重複」エラーを回避してスムーズに並べ替えることができます。");

            // 5. 快速尺寸標註 (QuickDim)
            AddTranslation("Tut_QuickDim_Title", "快速尺寸標註 - 新手指南", "Quick Dimension - Guide", "クイック寸法 - ガイド");
            AddTranslation("Tut_QuickDim_Content", "【快速標註操作法】\n" +
                                     "1. [選擇標註模式]：\n" +
                                     "   - 柱 - 柱 (Column to Column)：多選多個柱子，自動連貫標註各柱子中心至中心尺寸。\n" +
                                     "   - 牆 - 牆 (Wall to Wall)：選取多面平行牆體，自動連貫標註各牆體中心至中心尺寸。\n" +
                                     "   - 牆 - 邊界 (Wall to Edge)：選取多面平行牆體，自動標註牆面外側淨寬與牆厚度尺寸。\n" +
                                     "   - 開口標註 (Opening)：選取單一牆體，自動偵測並標註該牆面上所有門、窗或洞口的寬度與定位。\n" +
                                     "2. [設定參數與執行]：\n" +
                                     "   - 在下拉選單選擇標註樣式，並設定標註線的偏移距離 (Offset Distance)。\n" +
                                     "   - 點擊「開始標註」後，視窗會自動隱藏，請在 Revit 視圖中依據提示選擇對應的物件，即可秒級生成標註線！",
                                     "【Introduction】\n" +
                                     "1. [Choose Dimension Mode]:\n" +
                                     "   - Column to Column: Select multiple columns to auto-create aligned dimension lines through column centers.\n" +
                                     "   - Wall to Wall: Select parallel walls to dimension wall centerlines continuously.\n" +
                                     "   - Wall to Edge: Select parallel walls to dimension net widths between faces and wall thicknesses.\n" +
                                     "   - Opening: Select a single wall to automatically tag all windows, doors, and opening boundaries on that wall.\n" +
                                     "2. [Parameter Settings]:\n" +
                                     "   - Choose a Dimension Style, and adjust the 'Offset Distance' slider.\n" +
                                     "   - Click 'Start Dimension'. The window will temporarily hide. Go ahead and select the elements in Revit to automatically generate the dimension strings!",
                                     "【クイック寸法の操作方法】\n" +
                                     "1. [寸法モードの選択]：\n" +
                                     "   - 柱 - 柱：複数の柱を選択し、それぞれの中心から中心への連続寸法を自動作成します。\n" +
                                     "   - 壁 - 壁：複数の平行な壁を選択し、それぞれの中心線間の連続寸法を作成します。\n" +
                                     "   - 壁 - 境界：複数の平行な壁を選択し、壁の内法幅および壁厚寸法を自動作成します。\n" +
                                     "   - 開口部：1つの壁を選択すると、その壁に配置されているすべての窓、ドア、開口部の幅と位置寸法を自動抽出します。\n" +
                                     "2. [パラメータ設定と実行]：\n" +
                                     "   - 寸法スタイルを選択し、寸法線の「オフセット距離」を設定します。\n" +
                                     "   - 「寸法作成開始」をクリックするとウィンドウが非表示になるので、Revit上で要素を選択すると即座に寸法線が生成されます。");

            // 6. 快速開圖套樣板 (QuickView)
            AddTranslation("Tut_QuickView_Title", "快速建置視圖 - 新手指南", "Quick View Creator - Guide", "クイックビュー作成 - ガイド");
            AddTranslation("Tut_QuickView_Content", "【快速開圖說明】\n" +
                                     "1. [選擇來源視圖]：\n" +
                                     "   - 左側列表會樹狀展開目前專案內的所有平面視圖，可使用關鍵字搜尋。勾選您想要複製或作為範本來源的視圖。\n" +
                                     "2. [設定樣板與數量]：\n" +
                                     "   - 在中間控制台，選擇複製後要自動套用的「視圖樣板」。\n" +
                                     "   - 輸入要生成的數量 (例如為多個設計階段複製 3 份)，點擊「加入清單」。\n" +
                                     "3. [確認並批量建立]：\n" +
                                     "   - 右側清單會預覽即將產生的新視圖名稱與編號。可直接在表格中點擊修改目標名稱。\n" +
                                     "   - 若勾選「建立圖紙」，外掛會同步建立空白圖紙並自動將生成的新視圖放置上去。\n" +
                                     "   - 確認無誤後，點擊「開始建立」即可批量執行。",
                                     "【Introduction】\n" +
                                     "1. [Select Source Views]:\n" +
                                     "   - The left panel displays all project plan views in a tree structure. Search using keywords. Check the views you want to duplicate.\n" +
                                     "2. [Configure Templates & Count]:\n" +
                                     "   - In the middle, select the 'View Template' to be applied to the duplicated views.\n" +
                                     "   - Set the duplicate copy count (e.g. 3 copies), then click 'Add to List'.\n" +
                                     "3. [Verify & Batch Create]:\n" +
                                     "   - The right panel shows a preview of target view names and numbers. You can directly edit name cells.\n" +
                                     "   - Toggle 'Create Sheet' if you want a blank sheet created with the view pre-placed on it.\n" +
                                     "   - Click 'Start Creation' to run the batch process.",
                                     "【操作手順の案内】\n" +
                                     "1. [ソースビューの選択]：\n" +
                                     "   - 左側のリストにプロジェクト内のすべての平面ビューがツリー形式で表示されます。複製元にしたいビューにチェックを入れます。\n" +
                                     "2. [テンプレートと数量の設定]：\n" +
                                     "   - 中央のパネルで、複製後に適用する「ビューテンプレート」を選択します。\n" +
                                     "   - 作成するコピー数を入力し、「リストに追加」をクリックします。\n" +
                                     "3. [確認と一括作成]：\n" +
                                     "   - 右側のプレビューリストに作成される新しいビュー名が表示されます。セルを直接クリックして名前を修正できます。\n" +
                                     "   - 「シート作成」を有効にすると、新規シートを作成して自動的にビューを配置します。\n" +
                                     "   - 確認後、「作成開始」をクリックして実行します。");

            // 7. 圖紙視圖排版 (SheetPlacer)
            AddTranslation("Tut_SheetPlacer_Title", "圖紙視圖排版 - 新手指南", "Sheet View Placer - Guide", "シートビュー配置 - ガイド");
            AddTranslation("Tut_SheetPlacer_Content", "【圖紙排版雙模式指南】\n" +
                                     "1. [視覺排版模式] (拖曳放置)：\n" +
                                     "   - 步驟 1：左側清單會列出專案中「尚未放置於圖紙」的視圖與明細表。\n" +
                                     "   - 步驟 2：選中某個視圖後，在右側圖紙的灰色虛擬畫布區域點擊，即可預置視圖範圍框。\n" +
                                     "   - 步驟 3：在畫布上直接拖曳視圖框可以微調排版位置，完成後會同步寫回 Revit。\n" +
                                     "2. [數值排版模式] (精確對齊與網格)：\n" +
                                     "   - 切換至「數值排版」頁籤，選取多個視圖後，點擊對齊工具 (如: 靠左對齊、垂直置中、對齊標題) 進行精準排版。\n" +
                                     "   - 網格排版：設定網格列數、欄數、行間距(Gap X/Y)，點擊「套用網格」，系統會將視圖自動以矩陣網格方式整齊排列在圖紙中！\n\n" +
                                     "★ [提示]：上方可設定預設圖框與視埠類型，下方可點擊「新增圖紙」即時建立新圖框圖紙。",
                                     "【Layout Guide】\n" +
                                     "1. [Visual Layout Mode] (Click & Drag):\n" +
                                     "   - Step 1: Select an unplaced view or schedule from the left tree list.\n" +
                                     "   - Step 2: Click inside the grey canvas on the right (representing the sheet) to place the viewport outline box.\n" +
                                     "   - Step 3: Drag viewport outline boxes around the sheet canvas to layout views interactively.\n" +
                                     "2. [Numeric Layout Mode] (Alignments & Grid):\n" +
                                     "   - Switch to 'Numeric Layout' tab. Select multiple placed viewports, and click alignment buttons (e.g. Align Left, Align V-Center, Align Titles) to snap positions.\n" +
                                     "   - Grid Layout: Input rows, columns, and grid spacing (Gap X/Y). Click 'Apply Grid' to arrange selected views in an organized grid automatically!\n\n" +
                                     "★ [Tips]: Configure default title blocks or viewports at the top, and click 'New Sheet' to create sheets instantly.",
                                     "【シートレイアウト機能の使い方】\n" +
                                     "1. [ビジュアル配置モード] (ドラッグ＆ドロップ)：\n" +
                                     "   - ステップ 1：左側のリストから、シートに未配置のビューまたは集計表を選択します。\n" +
                                     "   - ステップ 2：右側のシートプレビュー領域（グレーのキャンバス）をクリックして、ビューを配置します。\n" +
                                     "   - ステップ 3：配置されたビュー枠をドラッグして位置を微調整します。\n" +
                                     "2. [数値配置モード] (整列とグリッド)：\n" +
                                     "   - 「数値配置」タブに切り替えます。複数のビューを選択し、整列ツール（左揃え、垂直中央揃え、タイトル整列など）で位置を揃えます。\n" +
                                     "   - グリッド配置：行数、列数、間隔を入力し、「グリッドを適用」をクリックすると、ビューがマトリクス状に整然と自動配置されます。\n\n" +
                                     "★ [ヒント]：上部で既定のタイトルブロックとビューポートタイプを設定でき、下部で「新規シート」を追加できます。");

            // 8. 圖紙逐層量化 (SheetDuplicator)
            AddTranslation("Tut_SheetDuplicator_Title", "圖紙逐層量化 - 新手指南", "Sheet Duplicator - Guide", "シート複製ツール - ガイド");
            AddTranslation("Tut_SheetDuplicator_Content", "【樓層量化開圖說明】\n" +
                                     "1. [輸入標準圖紙前綴並分析]：\n" +
                                     "   - 輸入標準範本圖紙的編號前綴 (例如以 1F 圖紙 A-10 開頭)，點擊「分析」。\n" +
                                     "   - 系統會解析範本圖紙，樹狀顯示該圖紙上放置了哪些「視圖」及其相對座標、比例與視圖樣板。\n" +
                                     "2. [選擇目標樓層]：\n" +
                                     "   - 在下方目標樓層列表中，勾選您要複製量化出來的目標樓層 (例如 2F, 3F, 4F 等)。\n" +
                                     "3. [執行量化複製]：\n" +
                                     "   - 點擊「執行量化開圖」，外掛會針對每個勾選的樓層自動做以下事情：\n" +
                                     "     a. 複製範本圖紙對應的新圖紙，並建立同類型圖框。\n" +
                                     "     b. 依據目標樓層自動複製新的平面圖/天花板圖說，套用原視圖樣板，並將視圖範圍切換至目標高程。\n" +
                                     "     c. 精確將新視圖放置於新圖紙的「同一個座標位置」，完美實現多樓層圖紙的逐層量化複製！",
                                     "【Introduction】\n" +
                                     "1. [Input Prefix & Analyze]:\n" +
                                     "   - Enter the sheet number prefix of your template sheets (e.g. 'A-10' for 1F sheets) and click 'Analyze'.\n" +
                                     "   - Displays a tree showing views placed on these sheets, along with their coordinates, scales and templates.\n" +
                                     "2. [Select Target Levels]:\n" +
                                     "   - Check target levels (e.g. 2F, 3F, 4F) in the bottom list to create duplicate sheets for.\n" +
                                     "3. [Run Quantified Duplication]:\n" +
                                     "   - Click 'Generate Sheets'. For each selected level, the tool will automatically:\n" +
                                     "     a. Create new sheets matching the template layout and title block type.\n" +
                                     "     b. Duplicate plan/ceiling views, assign original templates, and update view ranges to target levels.\n" +
                                     "     c. Place duplicated views onto the new sheets at the exact coordinates as the template sheets, ensuring standard alignment across levels!",
                                     "【フロア一括展開の使い方】\n" +
                                     "1. [基準シート接頭辞の入力と分析]：\n" +
                                     "   - 基準となるテンプレートシートの番号接頭辞 (例: 1F用の A-10 など) を入力し、「分析」をクリックします。\n" +
                                     "   - 該当シート上の配置ビュー、その相対座標、尺度、テンプレート情報がツリー表示されます。\n" +
                                     "2. [対象レベルの選択]：\n" +
                                     "   - 下部の対象レベルリストで、シートを展開したいフロア（例：2F, 3F, 4Fなど）にチェックを入れます。\n" +
                                     "3. [展開複製の実行]：\n" +
                                     "   - 「シート作成実行」をクリックします。チェックされたフロアごとに、ツールが以下を自動実行します：\n" +
                                     "     a. 同じタイトルブロックを使用して新しいシートを作成します。\n" +
                                     "     b. ソースとなる平面図や天井伏図を複製し、元のテンプレートを適用して、ビュー範囲を対象レベルに自動変更します。\n" +
                                     "     c. 新しいビューを新規シート上の「全く同じ相対座標」に配置し、複数フロアの図面レイアウトを完全一致で自動作成します。");

            // 9. 跨專案圖紙轉移 (SheetTransfer)
            AddTranslation("Tut_SheetTransfer_Title", "跨專案圖紙轉移 - 新手指南", "Sheet Transfer - Guide", "シート転送 - ガイド");
            AddTranslation("Tut_SheetTransfer_Content", "【跨檔案轉移指南】\n" +
                                     "1. [載入來源專案]：\n" +
                                     "   - 在下拉選單中選擇其他已在 Revit 中開啟的專案檔案作為來源專案。\n" +
                                     "2. [挑選轉移項目]：\n" +
                                     "   - 列表中會載入該檔案內的所有圖紙、詳圖視圖、圖例 (Legend) 以及明細表 (Schedule)。\n" +
                                     "   - 勾選您想要導入到當前專案的項目。\n" +
                                     "3. [執行複製與置入]：\n" +
                                     "   - 點擊「開始轉移」，工具將完美保留圖框尺寸與排版格式，將圖紙上的所有詳圖、明細表以及圖紙元件一同完整拷貝並置入當前專案中，免去手動匯出/匯入的繁瑣步驟！",
                                     "【Introduction】\n" +
                                     "1. [Select Source Project]:\n" +
                                     "   - Select another currently opened Revit project from the dropdown menu.\n" +
                                     "2. [Select Elements to Transfer]:\n" +
                                     "   - Choose the sheets, drafting views, legends, and schedules you want to copy into your current file.\n" +
                                     "3. [Execute Transfer]:\n" +
                                     "   - Click 'Start Transfer'. The tool imports all checked title blocks, details, and schedules directly, placing them in current workspace at identical coordinates without manual export/import!",
                                     "【別プロジェクトからのシート転送ガイド】\n" +
                                     "1. [コピー元プロジェクトの選択]：\n" +
                                     "   - プルダウンメニューから、現在Revitで開かれている別のプロジェクトファイルを選択します。\n" +
                                     "2. [転送項目の選択]：\n" +
                                     "   - 選択されたプロジェクト内のシート、製図ビュー、凡例、集計表がリストアップされます。現在のプロジェクトに読み込みたい項目にチェックを入れます。\n" +
                                     "3. [転送の実行]：\n" +
                                     "   - 「転送開始」をクリックすると、タイトルブロックのサイズやレイアウトを維持したまま、シート上の製図ビューや集計表などを現在のプロジェクトに一括コピーします。手動での書き出し・読み込みの手間を大幅に削減できます。");
        }

        private void AddTranslation(string key, string zh, string en, string ja)
        {
            _translations["zh-TW"][key] = zh;
            _translations["en-US"][key] = en;
            _translations["ja-JP"][key] = ja;
        }
    }
}
