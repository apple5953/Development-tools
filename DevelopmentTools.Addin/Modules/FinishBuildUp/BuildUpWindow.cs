using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Visibility = System.Windows.Visibility;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace DevelopmentTools.FinishBuildUp
{
    /// <summary>
    /// 降梁降板增築：①整理裝修表 ②分析房間（面層底 vs 結構頂）③建立增築／套用降板降梁。
    /// 視窗為強制回應，按鈕直接在 Revit API 環境中執行並各自開交易。
    /// </summary>
    public class BuildUpWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly BuildUpSettings _s;
        private readonly string _key;
        private BuildUpEngine _engine;

        private DataGrid _finishGrid, _resultGrid;
        private TextBlock _status;
        private ComboBox _scope, _baseType, _filter;
        private TextBox _minGap, _reviewGap, _depth, _round, _typePattern, _matPattern, _infoParam, _wsKey, _exclude;
        private CheckBox _links, _createFinish, _keepExisting;
        private TabControl _tabs;
        private List<Row> _rows = new List<Row>();

        private static readonly Brush Mute = new SolidColorBrush(Color.FromRgb(0x71, 0x80, 0x96));

        public class Row
        {
            public bool Selected { get; set; }
            public string Level { get; set; }
            public string Room { get; set; }
            public string Code { get; set; }
            public string Finish { get; set; }
            public string Structure { get; set; }
            public string StructTop { get; set; }
            public string Gap { get; set; }
            public string Action { get; set; }
            public string Area { get; set; }
            public string Note { get; set; }
            public BuildUpPiece Piece;
            public RoomBuildUp RoomData;
            public bool Actionable;
        }

        public BuildUpWindow(UIDocument uidoc, BuildUpSettings s, string key)
        {
            _uidoc = uidoc; _doc = uidoc.Document; _s = s; _key = key;
            Title = "降梁降板增築（依裝修表）";
            Width = 1320; Height = 820;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Microsoft JhengHei UI");
            FontSize = 13;
            Background = Brushes.White;
            Content = Build();
            RefreshFinishGrid();
        }

        // ───────────────────────── 介面 ─────────────────────────

        private static Button Btn(string t, RoutedEventHandler h, bool bold = false)
        {
            var b = new Button { Content = t, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
            if (bold) b.FontWeight = FontWeights.SemiBold;
            b.Click += h;
            return b;
        }

        private UIElement Build()
        {
            var root = new DockPanel { Margin = new Thickness(10) };

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            bar.Children.Add(new TextBlock { Text = "範圍：", VerticalAlignment = VerticalAlignment.Center });
            _scope = new ComboBox { Width = 170, Margin = new Thickness(0, 0, 8, 0), ItemsSource = new[] { "目前視圖的樓層", "選取的房間", "全部房間" }, SelectedIndex = 0 };
            bar.Children.Add(_scope);
            bar.Children.Add(Btn("① 分析", (o, e) => Analyze(), true));
            bar.Children.Add(Btn("② 建立增築", (o, e) => CreateBuildUps(), true));
            bar.Children.Add(Btn("③ 套用降板／降梁", (o, e) => ApplyDrops()));
            bar.Children.Add(Btn("選取元素", (o, e) => SelectInModel()));
            bar.Children.Add(Btn("匯出結果 CSV", (o, e) => ExportResults()));
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);

            _status = new TextBlock { Foreground = Mute, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(_status, Dock.Bottom);
            root.Children.Add(_status);

            _tabs = new TabControl();
            _tabs.Items.Add(new TabItem { Header = "裝修表", Content = BuildFinishTab() });
            _tabs.Items.Add(new TabItem { Header = "分析結果", Content = BuildResultTab() });
            _tabs.Items.Add(new TabItem { Header = "規則設定", Content = BuildSettingsTab() });
            root.Children.Add(_tabs);
            _status.Text = "流程：先在「裝修表」確認各地坪代碼的厚度與是否退縮 → ① 分析 → 檢查結果 → ② 建立增築；結構太高的地方可用 ③ 套用降板／降梁後再分析一次。";
            return root;
        }

        private UIElement BuildFinishTab()
        {
            var dp = new DockPanel { Margin = new Thickness(6) };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            bar.Children.Add(Btn("從模型地坪類型更新", (o, e) => { _s.Finishes = FinishTable.Merge(_s.Finishes, FinishTable.FromModel(_doc)); RefreshFinishGrid(); Save(); }));
            bar.Children.Add(Btn("匯入裝修表 CSV…", (o, e) => ImportCsv()));
            bar.Children.Add(Btn("匯出 CSV…", (o, e) => ExportCsv()));
            bar.Children.Add(Btn("新增一列", (o, e) => { CommitGrid(_finishGrid); _s.Finishes.Add(new FinishSpec { Code = "新代碼", Recessed = true, Source = "手動" }); RefreshFinishGrid(); }));
            bar.Children.Add(Btn("刪除選取列", (o, e) => { CommitGrid(_finishGrid); foreach (var f in _finishGrid.SelectedItems.OfType<FinishSpec>().ToList()) _s.Finishes.Remove(f); RefreshFinishGrid(); Save(); }));
            bar.Children.Add(Btn("儲存", (o, e) => { CommitGrid(_finishGrid); Save(); _status.Text = "裝修表已儲存。"; }));
            DockPanel.SetDock(bar, Dock.Top);
            dp.Children.Add(bar);

            var hint = new TextBlock
            {
                Foreground = Mute, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
                Text = "代碼＝房間「地板裝修」欄位的值（FI1-1 與類型名稱「地坪_室_F1-1」視為同一代碼）。退縮：面層埋在結構降板內、面層頂＝完成面；" +
                       "不退裝修：面層鋪在完成面上（例如環氧樹脂、自平水泥＋地毯），結構＋增築要做到完成面。完成面高差：該裝修的完成面相對樓層（例如浴廁 -20）。"
            };
            DockPanel.SetDock(hint, Dock.Top);
            dp.Children.Add(hint);

            _finishGrid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, SelectionMode = DataGridSelectionMode.Extended
            };
            _finishGrid.Columns.Add(new DataGridTextColumn { Header = "代碼", Binding = new Binding("Code"), Width = 90 });
            _finishGrid.Columns.Add(new DataGridTextColumn { Header = "地坪類型（建立面層用）", Binding = new Binding("FloorType"), Width = 240 });
            _finishGrid.Columns.Add(new DataGridTextColumn { Header = "厚度 mm", Binding = new Binding("ThicknessMm"), Width = 80 });
            _finishGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "退縮", Binding = new Binding("Recessed") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 50 });
            _finishGrid.Columns.Add(new DataGridTextColumn { Header = "完成面高差 mm", Binding = new Binding("FlOffsetMm"), Width = 110 });
            _finishGrid.Columns.Add(new DataGridTextColumn { Header = "說明", Binding = new Binding("Description"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _finishGrid.Columns.Add(new DataGridTextColumn { Header = "來源", Binding = new Binding("Source"), Width = 80, IsReadOnly = true });
            dp.Children.Add(_finishGrid);
            return dp;
        }

        private UIElement BuildResultTab()
        {
            var dp = new DockPanel { Margin = new Thickness(6) };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            bar.Children.Add(new TextBlock { Text = "顯示：", VerticalAlignment = VerticalAlignment.Center });
            _filter = new ComboBox { Width = 160, ItemsSource = new[] { "需處理（增築／降板降梁）", "增築", "降板／降梁", "有備註的房間", "全部" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 8, 0) };
            _filter.SelectionChanged += (o, e) => FillResults();
            bar.Children.Add(_filter);
            bar.Children.Add(Btn("全選", (o, e) => { foreach (var r in VisibleRows()) if (r.Actionable) r.Selected = true; _resultGrid.Items.Refresh(); }));
            bar.Children.Add(Btn("全不選", (o, e) => { foreach (var r in VisibleRows()) r.Selected = false; _resultGrid.Items.Refresh(); }));
            DockPanel.SetDock(bar, Dock.Top);
            dp.Children.Add(bar);

            _resultGrid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, SelectionMode = DataGridSelectionMode.Extended
            };
            _resultGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "建立", Binding = new Binding("Selected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 46 });
            _resultGrid.Columns.Add(new DataGridTextColumn { Header = "樓層", Binding = new Binding("Level"), Width = 110, IsReadOnly = true });
            _resultGrid.Columns.Add(new DataGridTextColumn { Header = "房間", Binding = new Binding("Room"), Width = 170, IsReadOnly = true });
            _resultGrid.Columns.Add(new DataGridTextColumn { Header = "代碼", Binding = new Binding("Code"), Width = 70, IsReadOnly = true });
            _resultGrid.Columns.Add(new DataGridTextColumn { Header = "面層", Binding = new Binding("Finish"), Width = 110, IsReadOnly = true });
            _resultGrid.Columns.Add(new DataGridTextColumn { Header = "結構", Binding = new Binding("Structure"), Width = 190, IsReadOnly = true });
            _resultGrid.Columns.Add(new DataGridTextColumn { Header = "結構頂(相對FL)", Binding = new Binding("StructTop"), Width = 100, IsReadOnly = true });
            _resultGrid.Columns.Add(new DataGridTextColumn { Header = "差距", Binding = new Binding("Gap"), Width = 70, IsReadOnly = true });
            _resultGrid.Columns.Add(new DataGridTextColumn { Header = "處理", Binding = new Binding("Action"), Width = 170, IsReadOnly = true });
            _resultGrid.Columns.Add(new DataGridTextColumn { Header = "面積", Binding = new Binding("Area"), Width = 70, IsReadOnly = true });
            _resultGrid.Columns.Add(new DataGridTextColumn { Header = "備註", Binding = new Binding("Note"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            _resultGrid.MouseDoubleClick += (o, e) => SelectInModel();
            dp.Children.Add(_resultGrid);
            return dp;
        }

        private UIElement BuildSettingsTab()
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var g = new Grid { Margin = new Thickness(10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int row = 0;
            void Add(string label, UIElement el, string hint = null)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
                Grid.SetRow(tb, row); g.Children.Add(tb);
                var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                sp.Children.Add(el);
                if (hint != null) sp.Children.Add(new TextBlock { Text = hint, Foreground = Mute, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                Grid.SetRow(sp, row); Grid.SetColumn(sp, 1); g.Children.Add(sp);
                row++;
            }
            string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            _minGap = new TextBox { Text = F(_s.MinGapMm), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            Add("最小增築厚度 mm", _minGap, "差距小於此值視為符合，不建立增築、也不降板。");
            _reviewGap = new TextBox { Text = F(_s.ReviewGapMm), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            Add("需確認門檻 mm", _reviewGap, "增築超過此厚度標示「需確認」（可能是回填或結構降板不正確）。");
            _depth = new TextBox { Text = F(_s.SearchDepthMm), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            Add("往下搜尋結構深度 mm", _depth);
            _round = new TextBox { Text = F(_s.RoundMm), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            Add("增築厚度取整 mm", _round, "1＝取到 mm；5＝取到 0.5 cm。");
            _typePattern = new TextBox { Text = _s.TypePattern };
            Add("增築類型命名", _typePattern, "{0} 會換成公分數，例：樓板_{0}cm 增築 → 樓板_6cm 增築。沒有該厚度的類型時自動複製建立。");
            var floorTypes = new FilteredElementCollector(_doc).OfClass(typeof(FloorType)).Cast<FloorType>().Select(t => t.Name).OrderBy(n => n).ToList();
            _baseType = new ComboBox { IsEditable = true, ItemsSource = floorTypes, Text = _s.BaseType ?? floorTypes.FirstOrDefault(n => n.Contains("增築")) ?? "" };
            Add("複製新類型的來源類型", _baseType, "建議選一個單層的增築類型。");
            _matPattern = new TextBox { Text = _s.MaterialPattern };
            Add("增築材料命名", _matPattern, "有同名材料就套用（例：增築_6cm），沒有則沿用來源類型的材料。");
            _infoParam = new TextBox { Text = _s.InfoParam };
            Add("寫入增築資訊的參數", _infoParam, "寫入「板上增築6cm」「梁上增築6cm」，供明細表分類。");
            _wsKey = new TextBox { Text = _s.WorksetKeyword };
            Add("增築工作集關鍵字", _wsKey, "工作共用模型中，新增的增築放到名稱含此字的工作集（例：13-增築）。");
            _exclude = new TextBox { Text = string.Join("、", _s.ExcludeKeywords) };
            Add("不算結構的樓板關鍵字", _exclude, "類型名稱含這些字的樓板不當作結構頂（面層、既有增築、防水、回填…）。以頓號分隔。");
            _links = new CheckBox { Content = "連結模型中的結構樓板與梁也納入（結構拆檔時）", IsChecked = _s.IncludeLinks };
            Add("連結模型", _links);
            _createFinish = new CheckBox { Content = "同時依房間建立地坪面層（使用裝修表的地坪類型）", IsChecked = _s.CreateFinishFloors };
            Add("地坪面層", _createFinish);
            _keepExisting = new CheckBox { Content = "房間內已有人工建立的增築時，仍預設勾選", IsChecked = _s.KeepRoomsWithExisting };
            Add("既有增築", _keepExisting, "預設不勾選，避免與既有模型重複。");
            var save = Btn("儲存設定", (o, e) => { if (ReadSettings()) { Save(); _status.Text = "規則設定已儲存。"; } });
            save.HorizontalAlignment = HorizontalAlignment.Left;
            Add("", save);
            sv.Content = g;
            return sv;
        }

        // ───────────────────────── 資料 ─────────────────────────

        private static void CommitGrid(DataGrid g)
        {
            try { g.CommitEdit(DataGridEditingUnit.Row, true); g.CommitEdit(); } catch { }
        }

        private void RefreshFinishGrid()
        {
            _finishGrid.ItemsSource = null;
            _finishGrid.ItemsSource = _s.Finishes;
        }

        private bool ReadSettings()
        {
            bool Num(TextBox t, out double v) => double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0;
            if (!Num(_minGap, out double a) || !Num(_reviewGap, out double b) || !Num(_depth, out double c) || !Num(_round, out double d))
            {
                MessageBox.Show(this, "數值設定需為大於 0 的數字。", Title);
                return false;
            }
            if (!_typePattern.Text.Contains("{0}") || !_matPattern.Text.Contains("{0}"))
            {
                MessageBox.Show(this, "類型與材料命名需包含 {0}（公分數）。", Title);
                return false;
            }
            _s.MinGapMm = a; _s.ReviewGapMm = b; _s.SearchDepthMm = c; _s.RoundMm = d;
            _s.TypePattern = _typePattern.Text.Trim(); _s.MaterialPattern = _matPattern.Text.Trim();
            _s.BaseType = _baseType.Text?.Trim(); _s.InfoParam = _infoParam.Text.Trim(); _s.WorksetKeyword = _wsKey.Text.Trim();
            _s.ExcludeKeywords = _exclude.Text.Split(new[] { '、', ',', '，', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            _s.IncludeLinks = _links.IsChecked == true;
            _s.CreateFinishFloors = _createFinish.IsChecked == true;
            _s.KeepRoomsWithExisting = _keepExisting.IsChecked == true;
            return true;
        }

        private void Save()
        {
            try { _s.Save(_key); } catch (Exception ex) { _status.Text = "儲存失敗：" + ex.Message; }
        }

        private void ImportCsv()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CSV (*.csv)|*.csv", Title = "匯入裝修表（代碼, 地坪類型, 厚度mm, 退縮, 完成面高差mm, 說明）" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var rows = FinishTable.ImportCsv(dlg.FileName);
                foreach (var r in rows)
                {
                    var old = _s.Finishes.FirstOrDefault(x => string.Equals(x.Code, r.Code, StringComparison.OrdinalIgnoreCase));
                    if (old != null) _s.Finishes.Remove(old);
                    _s.Finishes.Add(r);
                }
                _s.Finishes = _s.Finishes.OrderBy(x => x.Code, StringComparer.Ordinal).ToList();
                RefreshFinishGrid();
                Save();
                _status.Text = $"已匯入 {rows.Count} 筆裝修代碼。";
            }
            catch (Exception ex) { MessageBox.Show(this, "匯入失敗：" + ex.Message, Title); }
        }

        private void ExportCsv()
        {
            CommitGrid(_finishGrid);
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"裝修表_{_doc.Title}.csv" };
            if (dlg.ShowDialog(this) != true) return;
            FinishTable.ExportCsv(dlg.FileName, _s.Finishes);
            _status.Text = "已匯出裝修表：" + dlg.FileName;
        }

        private void Progress(string s)
        {
            _status.Text = s;
            Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }

        private IList<Room> ScopeRooms()
        {
            var all = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                .Cast<Room>().Where(r => r.Area > 1e-6 && r.Location != null).ToList();
            switch (_scope.SelectedIndex)
            {
                case 0:
                    var lv = _doc.ActiveView?.GenLevel;
                    if (lv == null) { MessageBox.Show(this, "目前視圖沒有對應樓層，請切到平面圖或改選其他範圍。", Title); return new List<Room>(); }
                    return all.Where(r => r.LevelId == lv.Id).ToList();
                case 1:
                    var sel = new HashSet<ElementId>(_uidoc.Selection.GetElementIds());
                    var rs = all.Where(r => sel.Contains(r.Id)).ToList();
                    if (rs.Count == 0) MessageBox.Show(this, "請先在模型中選取房間（關閉此視窗、選取後再開）。", Title);
                    return rs;
                default:
                    return all;
            }
        }

        // ───────────────────────── 動作 ─────────────────────────

        private void Analyze()
        {
            CommitGrid(_finishGrid);
            if (!ReadSettings()) return;
            Save();
            if (_s.Finishes.Count == 0) _s.Finishes = FinishTable.FromModel(_doc);
            var rooms = ScopeRooms();
            if (rooms.Count == 0) return;
            Cursor = Cursors.Wait;
            try
            {
                _engine = new BuildUpEngine(_doc, _s, Progress);
                _engine.Analyze(rooms);
                BuildRows();
                _tabs.SelectedIndex = 1;
                FillResults();
                var missing = _engine.Rooms.Where(r => r.Spec == null && !string.IsNullOrEmpty(r.Code)).Select(r => r.Code).Distinct().ToList();
                int add = _rows.Count(r => r.Piece != null && r.Piece.GapMm > _s.MinGapMm);
                int drop = _rows.Count(r => r.Piece != null && r.Piece.GapMm < -_s.MinGapMm);
                _status.Text = $"分析 {rooms.Count} 間房：需增築 {add} 處、結構太高（需降板／降梁）{drop} 處。" +
                               (missing.Count > 0 ? $"　裝修表缺少代碼：{string.Join("、", missing.Take(10))}{(missing.Count > 10 ? "…" : "")}（請到「裝修表」補上）。" : "") +
                               (_engine.Log.Count > 1 ? "　" + string.Join(" ", _engine.Log.Skip(1).Take(3)) : "");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), Title);
            }
            finally { Cursor = null; }
        }

        private void BuildRows()
        {
            _rows = new List<Row>();
            foreach (var rb in _engine.Rooms.OrderBy(r => r.LevelName).ThenBy(r => r.Label))
            {
                string finish = rb.Spec == null ? "" : $"{rb.Spec.ThicknessMm:0.#}mm {(rb.Spec.Recessed ? "退縮" : "不退")}";
                string notes = string.Join("；", rb.Notes);
                if (rb.Pieces.Count == 0)
                {
                    _rows.Add(new Row { Level = rb.LevelName, Room = rb.Label, Code = rb.Code, Finish = finish, Note = notes, RoomData = rb, Action = rb.Spec == null ? "—" : "無結構" });
                    continue;
                }
                foreach (var p in rb.Pieces)
                {
                    bool act = Math.Abs(p.GapMm) > _s.MinGapMm;
                    _rows.Add(new Row
                    {
                        Selected = p.Selected && act, Level = rb.LevelName, Room = rb.Label, Code = rb.Code, Finish = finish,
                        Structure = p.SourceLabel, StructTop = ((p.TopZFt - rb.FlZFt) * 304.8).ToString("0", CultureInfo.InvariantCulture),
                        Gap = p.GapMm.ToString("0.#", CultureInfo.InvariantCulture), Action = p.Action(_s), Area = p.AreaM2.ToString("0.00", CultureInfo.InvariantCulture),
                        Note = notes, Piece = p, RoomData = rb, Actionable = act
                    });
                    notes = "";   // 備註只在該房第一列顯示
                }
            }
        }

        private IEnumerable<Row> VisibleRows()
        {
            switch (_filter.SelectedIndex)
            {
                case 0: return _rows.Where(r => r.Actionable);
                case 1: return _rows.Where(r => r.Piece != null && r.Piece.GapMm > _s.MinGapMm);
                case 2: return _rows.Where(r => r.Piece != null && r.Piece.GapMm < -_s.MinGapMm);
                case 3: return _rows.Where(r => r.RoomData != null && r.RoomData.Notes.Count > 0);
                default: return _rows;
            }
        }

        private void FillResults()
        {
            if (_resultGrid == null) return;
            _resultGrid.ItemsSource = VisibleRows().ToList();
        }

        private void SyncSelection()
        {
            CommitGrid(_resultGrid);
            foreach (var r in _rows.Where(x => x.Piece != null)) r.Piece.Selected = r.Selected && r.Actionable;
        }

        private void CreateBuildUps()
        {
            if (_engine == null) { MessageBox.Show(this, "請先執行「① 分析」。", Title); return; }
            SyncSelection();
            var rooms = _engine.Rooms.Where(r => r.Pieces.Any(p => p.Selected && p.GapMm > _s.MinGapMm) || (_s.CreateFinishFloors && r.Spec != null)).ToList();
            int n = rooms.Sum(r => r.Pieces.Count(p => p.Selected && p.GapMm > _s.MinGapMm));
            if (n == 0 && !_s.CreateFinishFloors) { MessageBox.Show(this, "沒有勾選要建立的增築。", Title); return; }
            if (MessageBox.Show(this, $"將為 {rooms.Count} 間房建立 {n} 處增築{(_s.CreateFinishFloors ? "與地坪面層" : "")}。\n本工具先前在這些房間建立的增築會先刪除再重建（人工建立的不動）。\n要繼續嗎？",
                    Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            Cursor = Cursors.Wait;
            try
            {
                BuildUpEngine.ApplyResult res;
                using (var t = new Transaction(_doc, "降梁降板增築：建立增築"))
                {
                    t.Start();
                    res = _engine.CreateBuildUps(rooms);
                    t.Commit();
                }
                _status.Text = $"已建立增築 {res.Created} 塊{(res.FinishCreated > 0 ? $"、地坪面層 {res.FinishCreated} 塊" : "")}；新增類型 {res.TypesCreated} 個；刪除舊的 {res.Deleted} 塊；失敗 {res.Failed}。" +
                               (res.Messages.Count > 0 ? "　" + string.Join(" ", res.Messages.Take(4)) : "");
                if (res.NewIds.Count > 0) try { _uidoc.Selection.SetElementIds(res.NewIds); } catch { }
            }
            catch (Exception ex) { MessageBox.Show(this, "建立失敗：" + ex.Message, Title); }
            finally { Cursor = null; }
        }

        private void ApplyDrops()
        {
            if (_engine == null) { MessageBox.Show(this, "請先執行「① 分析」。", Title); return; }
            SyncSelection();
            var needs = _engine.DropNeeds();
            // 只處理有勾選的那幾塊
            var chosen = new HashSet<ElementId>(_rows.Where(r => r.Selected && r.Piece != null && r.Piece.GapMm < -_s.MinGapMm && r.Piece.SourceIsHost).Select(r => r.Piece.Source.Id));
            needs = needs.Where(n => chosen.Contains(n.Element.Id)).ToList();
            if (needs.Count == 0) { MessageBox.Show(this, "沒有勾選需要降板／降梁的結構（連結模型中的結構需到該模型修改）。", Title); return; }
            var sb = new StringBuilder();
            foreach (var n in needs.Take(15)) sb.AppendLine($"{n.Kind} {n.Element.Name}（ID {n.Element.Id}）：{n.DropsMm.Min():0}～{n.DropsMm.Max():0} mm");
            if (needs.Count > 15) sb.AppendLine($"…共 {needs.Count} 個");
            if (MessageBox.Show(this, "將修改結構樓板高度偏移／梁起訖點偏移：\n\n" + sb + "\n各房間要求不一致的元素會略過並列出（需拆板）。要繼續嗎？", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try
            {
                List<string> msg;
                using (var t = new Transaction(_doc, "降梁降板增築：降板降梁"))
                {
                    t.Start();
                    msg = _engine.ApplyDrops(needs);
                    t.Commit();
                }
                _status.Text = "降板／降梁完成，請重新「① 分析」以計算增築。　" + string.Join(" ", msg.Take(6)) + (msg.Count > 6 ? "…" : "");
                MessageBox.Show(this, string.Join("\n", msg), Title);
            }
            catch (Exception ex) { MessageBox.Show(this, "修改失敗：" + ex.Message, Title); }
        }

        private void SelectInModel()
        {
            var rows = _resultGrid.SelectedItems.OfType<Row>().ToList();
            var ids = new List<ElementId>();
            foreach (var r in rows)
            {
                if (r.Piece != null && r.Piece.SourceIsHost) ids.Add(r.Piece.Source.Id);
                if (r.RoomData != null) ids.Add(r.RoomData.Room.Id);
            }
            ids = ids.Distinct().ToList();
            if (ids.Count == 0) { _status.Text = "沒有可選取的元素。"; return; }
            try { _uidoc.Selection.SetElementIds(ids); _uidoc.ShowElements(ids); } catch { }
            _status.Text = $"已選取 {ids.Count} 個元素（房間與結構），關閉視窗後可在 Revit 中查看。";
        }

        private void ExportResults()
        {
            if (_rows.Count == 0) { _status.Text = "沒有結果可匯出。"; return; }
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"降梁降板增築_{_doc.Title}_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
            if (dlg.ShowDialog(this) != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("樓層,房間,代碼,面層,結構,結構頂相對FL(mm),差距(mm),處理,面積(m2),備註");
            string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            foreach (var r in _rows)
                sb.AppendLine(string.Join(",", Q(r.Level), Q(r.Room), Q(r.Code), Q(r.Finish), Q(r.Structure), Q(r.StructTop), Q(r.Gap), Q(r.Action), Q(r.Area), Q(r.Note)));
            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            _status.Text = "已匯出：" + dlg.FileName;
        }
    }
}
