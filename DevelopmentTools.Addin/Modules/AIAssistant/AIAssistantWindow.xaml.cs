using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Modules.AIAssistant
{
    public partial class AIAssistantWindow : Window
    {
        private readonly AIAssistantViewModel _vm;
        private ExternalEvent _selectionEvent;
        private AISelectionHandler _selectionHandler;
        private ExternalEvent _commandEvent;
        private AICommandHandler _commandHandler;

        public AIAssistantWindow(ExternalEvent selectionEvent, AISelectionHandler handler, ExternalEvent commandEvent, AICommandHandler commandHandler)
        {
            InitializeComponent();
            _selectionEvent = selectionEvent;
            _selectionHandler = handler;
            _commandEvent = commandEvent;
            _commandHandler = commandHandler;

            _vm = new AIAssistantViewModel();
            _vm.InjectRevitCommand(commandEvent, commandHandler);
            DataContext = _vm;

            // 每次有新訊息就自動滾到底部
            _vm.Messages.CollectionChanged += (s, e) =>
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    ChatScrollViewer.ScrollToBottom();
                }));
            };

            _selectionHandler.OnDataReady = (data, count, issues) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _vm.ElementContext = data;
                    _vm.SelectedCount = count;
                    _vm.QCIssues.Clear();
                    if (issues != null)
                    {
                        foreach (var issue in issues)
                        {
                            _vm.QCIssues.Add(issue);
                        }
                    }
                });
            };
        }

        private void BtnGetSelection_Click(object sender, RoutedEventArgs e)
        {
            _selectionEvent.Raise();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (_vm.SendMessageCommand.CanExecute(null))
                    _vm.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    // ── ExternalEvent Handler：讀取 Revit 選取元件 ─────────────────────
    public class AISelectionHandler : IExternalEventHandler
    {
        public Action<string, int, System.Collections.Generic.List<QCIssue>> OnDataReady { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                if (uiDoc == null) return;

                var doc = uiDoc.Document;
                var ids = uiDoc.Selection.GetElementIds();

                if (ids.Count == 0)
                {
                    // 執行全專案 ISO 19650 與裝修 QC 掃描
                    var levels = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .ToList();

                    var rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .Cast<Room>()
                        .ToList();

                    var walls = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Walls)
                        .WhereElementIsNotElementType()
                        .Cast<Wall>()
                        .ToList();

                    var floors = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Floors)
                        .WhereElementIsNotElementType()
                        .Cast<Floor>()
                        .ToList();

                    var sbStats = new StringBuilder();
                    sbStats.AppendLine("【全專案 ISO 19650 資訊完備度與裝修 QC 報告】");
                    sbStats.AppendLine($"分析時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sbStats.AppendLine($"專案指標：樓層數 {levels.Count} | 總房間數 {rooms.Count} | 牆體數 {walls.Count} | 地板數 {floors.Count}");
                    sbStats.AppendLine();

                    var issues = new System.Collections.Generic.List<QCIssue>();

                    // 1. ISO 19650 核心分類參數統計 (Assembly Code & OmniClass)
                    int wallWithCode = 0;
                    int floorWithCode = 0;
                    var missingCodeWalls = new System.Collections.Generic.List<string>();
                    var missingCodeFloors = new System.Collections.Generic.List<string>();

                    foreach (var w in walls)
                    {
                        var type = doc.GetElement(w.GetTypeId()) as ElementType;
                        var code = type?.get_Parameter(BuiltInParameter.UNIFORMAT_CODE)?.AsString() 
                                   ?? type?.LookupParameter("Assembly Code")?.AsString();
                        
                        var lvlName = doc.GetElement(w.LevelId)?.Name ?? "未知樓層";
#if REVIT2024 || REVIT2025 || REVIT2026
                        long idVal = w.Id.Value;
#else
                        long idVal = w.Id.IntegerValue;
#endif

                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            wallWithCode++;
                        }
                        else
                        {
                            issues.Add(new QCIssue
                            {
                                LevelName = lvlName,
                                ElementType = "牆 (Wall)",
                                ElementName = w.Name,
                                IssueType = "缺失分類編碼",
                                Description = "未填寫 Assembly Code 類型參數，不符 ISO 19650 標準。",
                                ElementId = idVal
                            });
                            missingCodeWalls.Add($"{w.Name} (ID: {idVal})");
                        }
                    }

                    foreach (var f in floors)
                    {
                        var type = doc.GetElement(f.GetTypeId()) as ElementType;
                        var code = type?.get_Parameter(BuiltInParameter.UNIFORMAT_CODE)?.AsString() 
                                   ?? type?.LookupParameter("Assembly Code")?.AsString();
                        
                        var lvlName = doc.GetElement(f.LevelId)?.Name ?? "未知樓層";
#if REVIT2024 || REVIT2025 || REVIT2026
                        long idVal = f.Id.Value;
#else
                        long idVal = f.Id.IntegerValue;
#endif

                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            floorWithCode++;
                        }
                        else
                        {
                            issues.Add(new QCIssue
                            {
                                LevelName = lvlName,
                                ElementType = "地板 (Floor)",
                                ElementName = f.Name,
                                IssueType = "缺失分類編碼",
                                Description = "未填寫 Assembly Code 類型參數，不符 ISO 19650 標準。",
                                ElementId = idVal
                            });
                            missingCodeFloors.Add($"{f.Name} (ID: {idVal})");
                        }
                    }

                    sbStats.AppendLine("■ ISO 19650 資訊編碼統計 (Assembly Code / Uniclass)：");
                    sbStats.AppendLine($"  ├─ 牆體分類填寫率：{wallWithCode} / {walls.Count}");
                    if (walls.Count > wallWithCode)
                    {
                        sbStats.AppendLine($"  │  └─ 缺失分類牆體 (前5個)：{string.Join(", ", missingCodeWalls.Take(5))}");
                    }
                    sbStats.AppendLine($"  └─ 地板分類填寫率：{floorWithCode} / {floors.Count}");
                    if (floors.Count > floorWithCode)
                    {
                        sbStats.AppendLine($"     └─ 缺失分類地板 (前5個)：{string.Join(", ", missingCodeFloors.Take(5))}");
                    }
                    sbStats.AppendLine();

                    // 2. 各樓層房間裝修與命名檢驗
                    sbStats.AppendLine("■ 各樓層建置進度與空間 QC：");
                    foreach (var lvl in levels)
                    {
                        var lvlRooms = rooms.Where(r => r.LevelId == lvl.Id).ToList();
                        var lvlWalls = walls.Where(w => w.LevelId == lvl.Id).ToList();
                        var lvlFloors = floors.Where(f => f.LevelId == lvl.Id).ToList();

                        if (lvlRooms.Count == 0 && lvlWalls.Count == 0 && lvlFloors.Count == 0)
                            continue;

                        int totalRooms = lvlRooms.Count;
                        int roomsWithFinish = 0;
                        var unfinishRooms = new System.Collections.Generic.List<string>();

                        foreach (var r in lvlRooms)
                        {
                            var baseFinish = r.LookupParameter("Base Finish")?.AsString() ?? r.LookupParameter("地板裝修")?.AsString();
                            var wallFinish = r.LookupParameter("Wall Finish")?.AsString() ?? r.LookupParameter("牆面裝修")?.AsString();
                            bool hasFinish = !string.IsNullOrWhiteSpace(baseFinish) || !string.IsNullOrWhiteSpace(wallFinish);
                            
#if REVIT2024 || REVIT2025 || REVIT2026
                            long idVal = r.Id.Value;
#else
                            long idVal = r.Id.IntegerValue;
#endif

                            if (hasFinish)
                            {
                                roomsWithFinish++;
                            }
                            else
                            {
                                issues.Add(new QCIssue
                                {
                                    LevelName = lvl.Name,
                                    ElementType = "房間 (Room)",
                                    ElementName = $"{r.Name}(房號:{r.Number})",
                                    IssueType = "漏設裝修材質",
                                    Description = "地板或牆面裝修參數為空，模型資訊不完整。",
                                    ElementId = idVal
                                });
                                unfinishRooms.Add($"{r.Name}(房號:{r.Number})");
                            }
                        }

                        sbStats.AppendLine($"  ● 樓層：{lvl.Name}");
                        sbStats.AppendLine($"    ├─ 元件數：牆 {lvlWalls.Count} | 地板 {lvlFloors.Count}");
                        sbStats.AppendLine($"    ├─ 房間裝修填寫率：{roomsWithFinish} / {totalRooms}");
                        if (unfinishRooms.Count > 0)
                        {
                            sbStats.AppendLine($"    └─ 未填裝修空間 (前5)：{string.Join(", ", unfinishRooms.Take(5))}");
                        }
                        else if (totalRooms > 0)
                        {
                            sbStats.AppendLine($"    └─ 空間裝修：此樓層空間資訊已完整建置。");
                        }
                        else
                        {
                            sbStats.AppendLine($"    └─ 空間裝修：無定義房間物件。");
                        }
                    }

                    OnDataReady?.Invoke(sbStats.ToString(), walls.Count + floors.Count + rooms.Count, issues);
                    return;
                }

                var sb = new StringBuilder();
                int count = 0;
                var selectIssues = new System.Collections.Generic.List<QCIssue>();

                foreach (var id in ids)
                {
                    var elem = doc.GetElement(id);
                    if (elem == null) continue;

                    count++;
                    
#if REVIT2024 || REVIT2025 || REVIT2026
                    long curIdVal = elem.Id.Value;
#else
                    long curIdVal = elem.Id.IntegerValue;
#endif

                    // 手動選取時的個別元件 QC 檢查
                    if (elem is Wall w)
                    {
                        var type = doc.GetElement(w.GetTypeId()) as ElementType;
                        var code = type?.get_Parameter(BuiltInParameter.UNIFORMAT_CODE)?.AsString() 
                                   ?? type?.LookupParameter("Assembly Code")?.AsString();
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            var lvlName = doc.GetElement(w.LevelId)?.Name ?? "未知樓層";
                            selectIssues.Add(new QCIssue
                            {
                                LevelName = lvlName,
                                ElementType = "選中牆 (Wall)",
                                ElementName = w.Name,
                                IssueType = "缺失分類編碼",
                                Description = "未填寫 Assembly Code 類型參數，不符 ISO 19650 標準。",
                                ElementId = curIdVal
                            });
                        }
                    }
                    else if (elem is Floor f)
                    {
                        var type = doc.GetElement(f.GetTypeId()) as ElementType;
                        var code = type?.get_Parameter(BuiltInParameter.UNIFORMAT_CODE)?.AsString() 
                                   ?? type?.LookupParameter("Assembly Code")?.AsString();
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            var lvlName = doc.GetElement(f.LevelId)?.Name ?? "未知樓層";
                            selectIssues.Add(new QCIssue
                            {
                                LevelName = lvlName,
                                ElementType = "選中地板 (Floor)",
                                ElementName = f.Name,
                                IssueType = "缺失分類編碼",
                                Description = "未填寫 Assembly Code 類型參數，不符 ISO 19650 標準。",
                                ElementId = curIdVal
                            });
                        }
                    }
                    else if (elem is Room r)
                    {
                        var baseFinish = r.LookupParameter("Base Finish")?.AsString() ?? r.LookupParameter("地板裝修")?.AsString();
                        var wallFinish = r.LookupParameter("Wall Finish")?.AsString() ?? r.LookupParameter("牆面裝修")?.AsString();
                        bool hasFinish = !string.IsNullOrWhiteSpace(baseFinish) || !string.IsNullOrWhiteSpace(wallFinish);
                        if (!hasFinish)
                        {
                            var lvlName = r.Level?.Name ?? "未知樓層";
                            selectIssues.Add(new QCIssue
                            {
                                LevelName = lvlName,
                                ElementType = "選中房間 (Room)",
                                ElementName = $"{r.Name}(房號:{r.Number})",
                                IssueType = "漏設裝修材質",
                                Description = "空間地板或牆面裝修為空，模型資訊不完整。",
                                ElementId = curIdVal
                            });
                        }
                    }

                    sb.AppendLine($"── 元件 #{count} ──────────────");
                    sb.AppendLine($"類型：{elem.GetType().Name}");
                    sb.AppendLine($"Id  ：{curIdVal}");

                    if (elem.Category != null)
                        sb.AppendLine($"類別：{elem.Category.Name}");

                    // Element Name
                    string name = elem.Name;
                    if (!string.IsNullOrEmpty(name))
                        sb.AppendLine($"名稱：{name}");

                    // 讀取常見參數
                    TryAppendParam(sb, elem, "Area",        "面積");
                    TryAppendParam(sb, elem, "Volume",      "體積");
                    TryAppendParam(sb, elem, "Length",      "長度");
                    TryAppendParam(sb, elem, "Level",       "樓層");
                    TryAppendParam(sb, elem, "Base Finish", "地板裝修");
                    TryAppendParam(sb, elem, "Wall Finish", "牆面裝修");
                    TryAppendParam(sb, elem, "Ceiling Finish", "天花板裝修");
                    TryAppendParam(sb, elem, "Tile_Joint_Width", "磁磚縫寬");
                    TryAppendParam(sb, elem, "Number",      "編號");
                    TryAppendParam(sb, elem, "Comments",    "備註");

                    sb.AppendLine();

                    if (count >= 30)
                    {
                        sb.AppendLine($"... 以及其他 {ids.Count - count} 個元件（已截斷顯示）");
                        break;
                    }
                }

                OnDataReady?.Invoke(sb.ToString(), ids.Count, selectIssues);
            }
            catch (Exception ex)
            {
                OnDataReady?.Invoke($"讀取元件時發生錯誤：{ex.Message}", 0, null);
            }
        }

        private void TryAppendParam(StringBuilder sb, Autodesk.Revit.DB.Element elem, string paramName, string label)
        {
            try
            {
                var param = elem.LookupParameter(paramName);
                if (param == null) return;
                string val = param.AsValueString() ?? param.AsString() ?? "";
                if (!string.IsNullOrWhiteSpace(val))
                    sb.AppendLine($"{label}：{val}");
            }
            catch { }
        }

        public string GetName() => "AIAssistantSelectionHandler";
    }

    // ── BoolToVisibility Converter ─────────────────────────────────────
    public class BoolToVisibility : IValueConverter
    {
        public static readonly BoolToVisibility Instance = new BoolToVisibility(false);
        public static readonly BoolToVisibility InvertedInstance = new BoolToVisibility(true);

        private readonly bool _invert;
        private BoolToVisibility(bool invert) { _invert = invert; }

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool b = value is bool b2 && b2;
            if (_invert) b = !b;
            return b ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }
}
