using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DevelopmentTools.FastRebar
{
    /// <summary>給視窗用的專案資訊（型號、彎鉤、選取摘要、代表構件尺寸）。</summary>
    public class FastRebarWindowInput
    {
        public List<FastRebarPreset> Presets;
        public string LastUsed;
        public Dictionary<string, double> BarDiametersMm = new Dictionary<string, double>();
        public List<string> Hook135Names = new List<string>();
        public List<string> Hook90TieNames = new List<string>();
        public List<string> Hook90MainNames = new List<string>();
        public string SelectionSummary;
        public int BeamCount, ColumnCount;
        /// <summary>第一支梁的寬、深（mm），0 = 沒選到梁。</summary>
        public double SampleBeamWidthMm, SampleBeamDepthMm;
        /// <summary>第一支柱的斷面兩向與高度（mm）。</summary>
        public double SampleColumnAMm, SampleColumnBMm, SampleColumnHeightMm;
    }

    /// <summary>快速配筋設定視窗（純程式碼建立，不用 XAML）。</summary>
    public class FastRebarWindow : Window
    {
        private const string Auto = "（自動尋找）";

        private readonly FastRebarWindowInput _in;
        private FastRebarPreset _current;
        private bool _loading;

        private ComboBox _presetBox;
        private TextBlock _noteText, _hintText;

        // 梁
        private ComboBox _bMain, _bStir, _bSide, _bEnd, _bForm, _bCross;
        private TextBox _bTop, _bBot, _bLayerClear, _bSideN, _bExtend, _bFirst, _bZoneFactor, _bSEnd, _bSMid;
        private CheckBox _bDouble, _bZonesOnly;
        // 柱
        private ComboBox _cMain, _cTie;
        private TextBox _cLong, _cShort, _cExtend, _cZoneMin, _cSConf, _cSMid, _cSJoint, _cFirst;
        private CheckBox _cTieEvery;
        // 一般
        private TextBox _cover;
        private ComboBox _h135, _h90t, _h90m, _existing;

        public FastRebarPreset Preset { get; private set; }
        public ExistingRebarPolicy Policy { get; private set; }
        public bool Confirmed { get; private set; }
        public List<FastRebarPreset> Presets => _in.Presets;

        public FastRebarWindow(FastRebarWindowInput input)
        {
            _in = input;
            Title = "快速配筋";
            Width = 640;
            Height = 760;
            MinWidth = 560;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Microsoft JhengHei UI");
            FontSize = 13;
            Background = Brushes.White;
            Content = BuildLayout();

            var start = _in.Presets.FirstOrDefault(p => p.Name == _in.LastUsed) ?? _in.Presets[0];
            _presetBox.SelectedItem = start.Name;
        }

        // ───────────────────────── 版面 ─────────────────────────

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(14) };

            // 上：樣板與選取摘要
            var top = new StackPanel();
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock { Text = "樣板", Width = 44, VerticalAlignment = VerticalAlignment.Center });
            var btnSaveAs = new Button { Content = "另存樣板…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
            var btnDelete = new Button { Content = "刪除", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(btnDelete, Dock.Right);
            DockPanel.SetDock(btnSaveAs, Dock.Right);
            row.Children.Add(btnDelete);
            row.Children.Add(btnSaveAs);
            _presetBox = new ComboBox { ItemsSource = _in.Presets.Select(p => p.Name).ToList() };
            row.Children.Add(_presetBox);
            top.Children.Add(row);
            _noteText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 6) };
            top.Children.Add(_noteText);
            top.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFA)),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new TextBlock { Text = _in.SelectionSummary, TextWrapping = TextWrapping.Wrap }
            });
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            // 下：提示與按鈕
            var bottom = new StackPanel();
            _hintText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x5B, 0x00)) };
            bottom.Children.Add(_hintText);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "開始配筋", Padding = new Thickness(18, 5, 18, 5), IsDefault = true };
            var cancel = new Button { Content = "取消", Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            // 中：分頁
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = $"梁（{_in.BeamCount}）", Content = Scroll(BuildBeamTab()) });
            tabs.Items.Add(new TabItem { Header = $"柱（{_in.ColumnCount}）", Content = Scroll(BuildColumnTab()) });
            tabs.Items.Add(new TabItem { Header = "保護層與彎鉤", Content = Scroll(BuildGeneralTab()) });
            if (_in.BeamCount == 0 && _in.ColumnCount > 0) tabs.SelectedIndex = 1;
            root.Children.Add(tabs);

            _presetBox.SelectionChanged += (s, e) => LoadPreset(_presetBox.SelectedItem as string);
            btnSaveAs.Click += (s, e) => SaveAs();
            btnDelete.Click += (s, e) => DeletePreset();
            ok.Click += (s, e) => Confirm();
            return root;
        }

        private static ScrollViewer Scroll(UIElement content)
        {
            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content, Padding = new Thickness(6) };
        }

        private Grid NewForm()
        {
            var g = new Grid { Margin = new Thickness(4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        private T AddRow<T>(Grid g, string label, T control, string tip = null) where T : FrameworkElement
        {
            int r = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4), TextWrapping = TextWrapping.Wrap };
            if (tip != null) { lbl.ToolTip = tip; control.ToolTip = tip; }
            Grid.SetRow(lbl, r);
            Grid.SetColumn(lbl, 0);
            control.Margin = new Thickness(0, 3, 0, 3);
            Grid.SetRow(control, r);
            Grid.SetColumn(control, 1);
            g.Children.Add(lbl);
            g.Children.Add(control);
            if (control is TextBox tb) tb.TextChanged += (s, e) => UpdateHints();
            if (control is ComboBox cb) cb.SelectionChanged += (s, e) => UpdateHints();
            if (control is CheckBox ch) { ch.Checked += (s, e) => UpdateHints(); ch.Unchecked += (s, e) => UpdateHints(); }
            return control;
        }

        private void AddHeader(Grid g, string text)
        {
            int r = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, r == 0 ? 0 : 12, 0, 4) };
            Grid.SetRow(t, r);
            Grid.SetColumnSpan(t, 2);
            g.Children.Add(t);
        }

        private ComboBox BarCombo() => new ComboBox { ItemsSource = _in.BarDiametersMm.Keys.ToList(), IsEditable = true };
        private static TextBox Num() => new TextBox { Padding = new Thickness(3, 2, 3, 2) };
        private static ComboBox Choice(params string[] items) => new ComboBox { ItemsSource = items };

        private UIElement BuildBeamTab()
        {
            var g = NewForm();
            AddHeader(g, "主筋");
            _bMain = AddRow(g, "主筋號數", BarCombo());
            _bTop = AddRow(g, "上層各層支數", Num(), "用 + 分隔各層，例如 7+7 = 第一層 7 支、第二層 7 支");
            _bBot = AddRow(g, "下層各層支數", Num(), "例如 7+5");
            _bLayerClear = AddRow(g, "層間淨距 (mm)", Num(), "第一層與第二層主筋之間的淨距。參考模型約 85 mm（讓正交梁主筋穿過）");
            _bEnd = AddRow(g, "柱處端部", Choice("穿過柱延伸到下一跨", "錨定在柱內（90° 彎鉤）", "停在柱面"));
            _bExtend = AddRow(g, "穿柱後延伸長度 (mm)", Num(), "從柱遠側面量起；0 = 停在柱遠側面扣保護層");
            _bCross = AddRow(g, "兩向梁主筋錯層", Choice("不錯層", "X 向梁讓位（退一個 db）", "Y 向梁讓位（退一個 db）"));
            _bSideN = AddRow(g, "腰筋每側支數", Num(), "0 = 不配腰筋");
            _bSide = AddRow(g, "腰筋號數", BarCombo());

            AddHeader(g, "箍筋");
            _bStir = AddRow(g, "箍筋號數", BarCombo());
            _bForm = AddRow(g, "箍筋型式", Choice("U 形箍 135° + 頂部繫筋 135°/90° 交替", "閉合箍 135°"));
            _bDouble = AddRow(g, "雙箍", new CheckBox { Content = "每個位置兩組（相距一個箍筋直徑）" });
            _bFirst = AddRow(g, "第一支距柱面 (mm)", Num());
            _bZoneFactor = AddRow(g, "加密區長度 = ? × 梁深", Num(), "耐震梁常用 2 倍梁深");
            _bSEnd = AddRow(g, "加密區間距 (mm)", Num());
            _bSMid = AddRow(g, "中段間距 (mm)", Num());
            _bZonesOnly = AddRow(g, "範圍", new CheckBox { Content = "只配兩端加密區（中段留空）" });
            return g;
        }

        private UIElement BuildColumnTab()
        {
            var g = NewForm();
            AddHeader(g, "主筋");
            _cMain = AddRow(g, "主筋號數", BarCombo());
            _cLong = AddRow(g, "長邊每面支數（含角筋）", Num());
            _cShort = AddRow(g, "短邊每面支數（含角筋）", Num());
            _cExtend = AddRow(g, "頂端往上延伸 (mm)", Num(), "搭接到上層柱的長度；0 = 停在柱頂扣保護層");

            AddHeader(g, "箍筋與繫筋");
            _cTie = AddRow(g, "箍筋號數", BarCombo());
            _cFirst = AddRow(g, "第一支距版面 (mm)", Num());
            _cZoneMin = AddRow(g, "圍束區長度下限 (mm)", Num(), "實際長度取 max(斷面長邊, 淨高/6, 此值)");
            _cSConf = AddRow(g, "圍束區間距 (mm)", Num());
            _cSMid = AddRow(g, "中段間距 (mm)", Num());
            _cSJoint = AddRow(g, "梁柱接頭區間距 (mm)", Num());
            _cTieEvery = AddRow(g, "繫筋", new CheckBox { Content = "每支中間主筋都加繫筋（不勾 = 隔支加）" });
            return g;
        }

        private UIElement BuildGeneralTab()
        {
            var g = NewForm();
            AddHeader(g, "保護層");
            _cover = AddRow(g, "保護層 (mm)", Num(), "0 = 讀各構件的保護層設定（上、下、其他面）");
            AddHeader(g, "彎鉤類型");
            _h135 = AddRow(g, "135° 耐震彎鉤", new ComboBox { ItemsSource = new[] { Auto }.Concat(_in.Hook135Names).ToList() });
            _h90t = AddRow(g, "90° 箍筋彎鉤", new ComboBox { ItemsSource = new[] { Auto }.Concat(_in.Hook90TieNames).ToList() });
            _h90m = AddRow(g, "90° 主筋彎鉤", new ComboBox { ItemsSource = new[] { Auto }.Concat(_in.Hook90MainNames).ToList() });
            AddHeader(g, "已有鋼筋的構件");
            _existing = AddRow(g, "處理方式", Choice("略過（不動已配好的構件）", "刪除原有鋼筋後重配", "直接加上去"));
            _existing.SelectedIndex = 0;
            return g;
        }

        // ───────────────────────── 樣板 ↔ 欄位 ─────────────────────────

        private void LoadPreset(string name)
        {
            var p = _in.Presets.FirstOrDefault(x => x.Name == name);
            if (p == null) return;
            _loading = true;
            _current = p;
            _noteText.Text = p.Note ?? "";
            var b = p.Beam;
            SetBar(_bMain, b.MainBarType);
            _bTop.Text = FastRebarPresetStore.FormatLayers(b.TopLayers);
            _bBot.Text = FastRebarPresetStore.FormatLayers(b.BottomLayers);
            _bLayerClear.Text = F(b.LayerClearMm);
            _bEnd.SelectedIndex = (int)b.EndMode;
            _bExtend.Text = F(b.ExtendPastSupportMm);
            _bCross.SelectedIndex = (int)b.CrossingYield;
            _bSideN.Text = b.SideBarsPerFace.ToString();
            SetBar(_bSide, b.SideBarType);
            SetBar(_bStir, b.StirrupBarType);
            _bForm.SelectedIndex = (int)b.StirrupForm;
            _bDouble.IsChecked = b.DoubleStirrups;
            _bFirst.Text = F(b.FirstStirrupOffsetMm);
            _bZoneFactor.Text = F(b.EndZoneDepthFactor);
            _bSEnd.Text = F(b.EndZoneSpacingMm);
            _bSMid.Text = F(b.MidSpacingMm);
            _bZonesOnly.IsChecked = b.EndZonesOnly;

            var c = p.Column;
            SetBar(_cMain, c.MainBarType);
            _cLong.Text = c.BarsLongFace.ToString();
            _cShort.Text = c.BarsShortFace.ToString();
            _cExtend.Text = F(c.ExtendAboveTopMm);
            SetBar(_cTie, c.TieBarType);
            _cFirst.Text = F(c.FirstTieOffsetMm);
            _cZoneMin.Text = F(c.ConfinedZoneMinMm);
            _cSConf.Text = F(c.ConfinedSpacingMm);
            _cSMid.Text = F(c.MidSpacingMm);
            _cSJoint.Text = F(c.JointSpacingMm);
            _cTieEvery.IsChecked = c.TieEveryIntermediateBar;

            _cover.Text = F(p.CoverOverrideMm);
            SetHook(_h135, p.Hook135Name);
            SetHook(_h90t, p.Hook90TieName);
            SetHook(_h90m, p.Hook90MainName);
            _loading = false;
            UpdateHints();
        }

        private void SetBar(ComboBox box, string name)
        {
            box.Text = name ?? "";
            if (name != null && _in.BarDiametersMm.ContainsKey(name)) box.SelectedItem = name;
        }

        private static void SetHook(ComboBox box, string name)
        {
            var items = (box.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
            box.SelectedItem = !string.IsNullOrEmpty(name) && items.Contains(name) ? name : Auto;
        }

        private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static double D(TextBox t, double fallback)
        {
            return double.TryParse((t.Text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        private static int I(TextBox t, int fallback)
        {
            return int.TryParse((t.Text ?? "").Trim(), out int v) ? v : fallback;
        }

        private static string HookName(ComboBox box)
        {
            string s = box.SelectedItem as string;
            return s == null || s == Auto ? null : s;
        }

        /// <summary>把欄位讀回成一份新的樣板（不修改原樣板，除非另存）。</summary>
        private FastRebarPreset ReadForm(string name)
        {
            var p = _current.Clone();
            p.Name = name;
            var b = p.Beam;
            b.MainBarType = _bMain.Text?.Trim();
            var top = FastRebarPresetStore.ParseLayers(_bTop.Text);
            var bot = FastRebarPresetStore.ParseLayers(_bBot.Text);
            if (top.Count > 0) b.TopLayers = top;
            if (bot.Count > 0) b.BottomLayers = bot;
            b.LayerClearMm = D(_bLayerClear, b.LayerClearMm);
            b.EndMode = (BeamBarEndMode)Math.Max(0, _bEnd.SelectedIndex);
            b.ExtendPastSupportMm = D(_bExtend, b.ExtendPastSupportMm);
            b.CrossingYield = (CrossingYield)Math.Max(0, _bCross.SelectedIndex);
            b.SideBarsPerFace = Math.Max(0, I(_bSideN, b.SideBarsPerFace));
            b.SideBarType = _bSide.Text?.Trim();
            b.StirrupBarType = _bStir.Text?.Trim();
            b.StirrupForm = (BeamStirrupForm)Math.Max(0, _bForm.SelectedIndex);
            b.DoubleStirrups = _bDouble.IsChecked == true;
            b.FirstStirrupOffsetMm = D(_bFirst, b.FirstStirrupOffsetMm);
            b.EndZoneDepthFactor = D(_bZoneFactor, b.EndZoneDepthFactor);
            b.EndZoneSpacingMm = D(_bSEnd, b.EndZoneSpacingMm);
            b.MidSpacingMm = D(_bSMid, b.MidSpacingMm);
            b.EndZonesOnly = _bZonesOnly.IsChecked == true;

            var c = p.Column;
            c.MainBarType = _cMain.Text?.Trim();
            c.BarsLongFace = I(_cLong, c.BarsLongFace);
            c.BarsShortFace = I(_cShort, c.BarsShortFace);
            c.ExtendAboveTopMm = D(_cExtend, c.ExtendAboveTopMm);
            c.TieBarType = _cTie.Text?.Trim();
            c.FirstTieOffsetMm = D(_cFirst, c.FirstTieOffsetMm);
            c.ConfinedZoneMinMm = D(_cZoneMin, c.ConfinedZoneMinMm);
            c.ConfinedSpacingMm = D(_cSConf, c.ConfinedSpacingMm);
            c.MidSpacingMm = D(_cSMid, c.MidSpacingMm);
            c.JointSpacingMm = D(_cSJoint, c.JointSpacingMm);
            c.TieEveryIntermediateBar = _cTieEvery.IsChecked == true;

            p.CoverOverrideMm = Math.Max(0, D(_cover, p.CoverOverrideMm));
            p.Hook135Name = HookName(_h135);
            p.Hook90TieName = HookName(_h90t);
            p.Hook90MainName = HookName(_h90m);
            p.Normalize();
            return p;
        }

        // ───────────────────────── 即時提示 ─────────────────────────

        private double Db(string barName)
        {
            if (barName != null && _in.BarDiametersMm.TryGetValue(barName.Trim(), out double d)) return d;
            string digits = new string((barName ?? "").Where(char.IsDigit).ToArray());
            return double.TryParse(digits, out double mm) ? mm : 0;
        }

        /// <summary>
        /// 依代表構件尺寸算建議上限（ACI 318-19 第 18 章／土木 401 耐震構架常用值，僅供參考），
        /// 以及主筋淨距與找不到型號的提醒。
        /// </summary>
        private void UpdateHints()
        {
            if (_loading || _current == null || _hintText == null) return;
            var notes = new List<string>();
            try
            {
                var p = ReadForm(_current.Name);
                var b = p.Beam;
                double dbB = Db(b.MainBarType), dsB = Db(b.StirrupBarType);
                if (_in.BeamCount > 0)
                {
                    if (dbB <= 0) notes.Add($"專案找不到梁主筋型號「{b.MainBarType}」。");
                    if (dsB <= 0) notes.Add($"專案找不到梁箍筋型號「{b.StirrupBarType}」。");
                }
                if (_in.BeamCount > 0 && _in.SampleBeamWidthMm > 0 && dbB > 0 && dsB > 0)
                {
                    double cover = p.CoverOverrideMm > 0 ? p.CoverOverrideMm : 40;
                    int n = b.TopLayers.Concat(b.BottomLayers).Max();
                    double width = _in.SampleBeamWidthMm - 2 * (cover + dsB) - dbB;
                    if (n > 1)
                    {
                        double clear = width / (n - 1) - dbB;
                        if (clear < Math.Max(dbB, 25)) notes.Add($"梁（寬 {_in.SampleBeamWidthMm:0}）一層 {n} 支 {b.MainBarType} 的淨距約 {clear:0} mm，小於 max(db, 25)。");
                    }
                    double d = _in.SampleBeamDepthMm - cover - dsB - dbB / 2;
                    double sEndMax = Math.Min(Math.Min(d / 4, 6 * dbB), 150);
                    if (b.EndZoneSpacingMm > sEndMax + 0.5) notes.Add($"梁加密區間距 {b.EndZoneSpacingMm:0} 大於常用上限 {sEndMax:0} mm（d/4、6db、150 取小）。");
                    if (b.MidSpacingMm > d / 2 + 0.5) notes.Add($"梁中段間距 {b.MidSpacingMm:0} 大於 d/2 ≈ {d / 2:0} mm。");
                }

                var c = p.Column;
                double dbC = Db(c.MainBarType), dtC = Db(c.TieBarType);
                if (_in.ColumnCount > 0)
                {
                    if (dbC <= 0) notes.Add($"專案找不到柱主筋型號「{c.MainBarType}」。");
                    if (dtC <= 0) notes.Add($"專案找不到柱箍筋型號「{c.TieBarType}」。");
                }
                if (_in.ColumnCount > 0 && _in.SampleColumnAMm > 0 && dbC > 0)
                {
                    double bMin = Math.Min(_in.SampleColumnAMm, _in.SampleColumnBMm);
                    double sMax = Math.Min(Math.Min(bMin / 4, 6 * dbC), 150);
                    if (c.ConfinedSpacingMm > sMax + 0.5) notes.Add($"柱圍束區間距 {c.ConfinedSpacingMm:0} 大於常用上限 {sMax:0} mm（短邊/4、6db、150 取小）。");
                    double lo = Math.Max(Math.Max(Math.Max(_in.SampleColumnAMm, _in.SampleColumnBMm), _in.SampleColumnHeightMm / 6), c.ConfinedZoneMinMm);
                    notes.Add($"代表柱圍束區長度將取 {lo:0} mm。");
                }
            }
            catch (Exception ex)
            {
                notes.Add("欄位格式有誤：" + ex.Message);
            }

            _hintText.Text = notes.Count == 0
                ? "設定檢查通過。建議值依 ACI 318-19／土木 401 耐震構架常用上限，正式配筋以結構圖與結構技師為準。"
                : string.Join("\n", notes.Select(n => "・" + n)) + "\n（建議值僅供參考，正式配筋以結構圖與結構技師為準）";
        }

        // ───────────────────────── 按鈕 ─────────────────────────

        private void SaveAs()
        {
            string name = Prompt("樣板名稱", _current.Name + "（修改）");
            if (string.IsNullOrWhiteSpace(name)) return;
            var p = ReadForm(name.Trim());
            int idx = _in.Presets.FindIndex(x => x.Name == p.Name);
            if (idx >= 0) _in.Presets[idx] = p; else _in.Presets.Add(p);
            FastRebarPresetStore.Save(_in.Presets, p.Name);
            _presetBox.ItemsSource = _in.Presets.Select(x => x.Name).ToList();
            _presetBox.SelectedItem = p.Name;
        }

        private void DeletePreset()
        {
            if (_in.Presets.Count <= 1)
            {
                MessageBox.Show(this, "至少要保留一個樣板。", "快速配筋");
                return;
            }
            if (MessageBox.Show(this, $"刪除樣板「{_current.Name}」？", "快速配筋", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _in.Presets.RemoveAll(x => x.Name == _current.Name);
            FastRebarPresetStore.Save(_in.Presets, _in.Presets[0].Name);
            _presetBox.ItemsSource = _in.Presets.Select(x => x.Name).ToList();
            _presetBox.SelectedItem = _in.Presets[0].Name;
        }

        private void Confirm()
        {
            Preset = ReadForm(_current.Name);
            Policy = (ExistingRebarPolicy)Math.Max(0, _existing.SelectedIndex);
            FastRebarPresetStore.Save(_in.Presets, _current.Name);
            Confirmed = true;
            DialogResult = true;
        }

        private string Prompt(string title, string initial)
        {
            var w = new Window
            {
                Title = title,
                Width = 360,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = FontFamily,
                FontSize = FontSize
            };
            var panel = new StackPanel { Margin = new Thickness(12) };
            var box = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 10) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "確定", Width = 72, IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 72, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            ok.Click += (s, e) => w.DialogResult = true;
            row.Children.Add(ok);
            row.Children.Add(cancel);
            panel.Children.Add(box);
            panel.Children.Add(row);
            w.Content = panel;
            box.SelectAll();
            box.Focus();
            return w.ShowDialog() == true ? box.Text : null;
        }
    }
}
