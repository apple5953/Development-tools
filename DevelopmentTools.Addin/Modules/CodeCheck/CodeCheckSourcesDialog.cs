using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DevelopmentTools.CodeCheck
{
    /// <summary>
    /// 檢討範圍：選擇要合併檢討的模型（目前檔案、已連結模型、未連結的模型檔），
    /// 以及每個模型要讀哪些內容（避免拆檔時建築、機電模型重複計算同一類元素）。
    /// </summary>
    public class CodeCheckSourcesDialog : Window
    {
        private static readonly (SourceParts Part, string Text, string Tip)[] PartCols =
        {
            (SourceParts.Spaces, "房間樓層", "房間、樓層、樓地板面積、女兒牆"),
            (SourceParts.Openings, "門窗", "門、窗（防火門、出入口、採光通風）"),
            (SourceParts.Circulation, "樓梯坡道欄杆", "樓梯、坡道、欄杆扶手"),
            (SourceParts.Equipment, "設備", "消防設備、衛生設備、昇降機等"),
            (SourceParts.Parking, "車位", "停車位"),
            (SourceParts.Areas, "防火區劃", "名稱含「防火」的面積配置"),
        };

        private readonly ProjectSettings _s;
        private readonly List<SourceEntry> _entries;
        private readonly Grid _table = new Grid();
        private TextBox _tol;
        private CheckBox _keepDup;

        public List<SourceEntry> Entries => _entries;

        public CodeCheckSourcesDialog(ProjectSettings s, List<SourceEntry> entries)
        {
            _s = s;
            _entries = entries;
            Title = "檢討範圍（合併多個模型）";
            Width = 1040; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Microsoft JhengHei UI");
            FontSize = 13;
            Background = Brushes.White;

            var root = new DockPanel { Margin = new Thickness(12) };

            var intro = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                Text = "大型案拆檔時，勾選要一起檢討的模型。目前檔案中「已連結」的模型會自動列出；尚未連結的模型檔可用「加入模型檔…」，" +
                       "檢討時會暫時以共用座標連結（檢討結束即移除，不會修改任何檔案）。\n" +
                       "每個模型可指定要讀的內容：例如機電模型只讀「設備」、結構模型全部不勾（仍會參與淨高、梁下高度量測）。不同模型同位置的重複元素只計一次。"
            };
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

            var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "確定", Padding = new Thickness(18, 4, 18, 4), IsDefault = true };
            var cancel = new Button { Content = "取消", Padding = new Thickness(18, 4, 18, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            ok.Click += (o, e) => { if (Save()) DialogResult = true; };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Right);
            bottom.Children.Add(buttons);

            var opts = new StackPanel { Orientation = Orientation.Horizontal };
            var add = new Button { Content = "加入模型檔…", Padding = new Thickness(12, 4, 12, 4) };
            add.Click += (o, e) => AddFiles();
            opts.Children.Add(add);
            var allOn = new Button { Content = "全部納入", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
            allOn.Click += (o, e) => { foreach (var x in _entries.Where(x => x.Available)) x.Include = true; Fill(); };
            opts.Children.Add(allOn);
            var guess = new Button { Content = "依檔名重設讀取內容", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "檔名含 STR／結構 → 只參與淨高量測；含 MEP／機電／消防／給排水／空調／電氣 → 只讀設備；其餘視為建築模型全部讀取" };
            guess.Click += (o, e) => { foreach (var x in _entries.Where(x => x.Kind != "host")) x.PartFlags = ModelSources.DefaultParts(x.Name); Fill(); };
            opts.Children.Add(guess);
            opts.Children.Add(new TextBlock { Text = "樓層合併容許高程差", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 4, 0) });
            _tol = new TextBox { Width = 60, Text = s.LevelToleranceMm.ToString("0", CultureInfo.InvariantCulture), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "不同模型的樓層高程相差在此範圍內視為同一層（例如結構 SL 與建築 FL）" };
            opts.Children.Add(_tol);
            opts.Children.Add(new TextBlock { Text = "mm", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            _keepDup = new CheckBox { Content = "不排除重複元素", IsChecked = s.KeepDuplicates, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0),
                ToolTip = "預設會把不同模型中同位置的房間、門、樓梯、設備只計一次" };
            opts.Children.Add(_keepDup);
            bottom.Children.Add(opts);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _table };
            root.Children.Add(scroll);
            Content = root;
            Fill();
        }

        private void Fill()
        {
            _table.Children.Clear();
            _table.RowDefinitions.Clear();
            _table.ColumnDefinitions.Clear();
            var widths = new List<double> { 44, 260, 80, 190 };
            widths.AddRange(PartCols.Select(_ => 84.0));
            widths.Add(48);
            foreach (double w in widths) _table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

            int row = 0;
            void AddRow() { _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            void Put(UIElement el, int r, int c)
            {
                Grid.SetRow(el, r); Grid.SetColumn(el, c);
                if (el is FrameworkElement fe) fe.Margin = new Thickness(2, 3, 2, 3);
                _table.Children.Add(el);
            }

            AddRow();
            string[] heads = new[] { "納入", "模型", "類型", "狀態" }.Concat(PartCols.Select(p => p.Text)).Concat(new[] { "" }).ToArray();
            for (int c = 0; c < heads.Length; c++)
            {
                var tb = new TextBlock { Text = heads[c], FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
                if (c >= 4 && c < 4 + PartCols.Length) tb.ToolTip = PartCols[c - 4].Tip;
                Put(tb, 0, c);
            }
            row++;

            foreach (var e in _entries)
            {
                var entry = e;
                AddRow();
                var inc = new CheckBox { IsChecked = e.Include, IsEnabled = e.Available, HorizontalAlignment = HorizontalAlignment.Center };
                inc.Checked += (o, a) => entry.Include = true;
                inc.Unchecked += (o, a) => entry.Include = false;
                Put(inc, row, 0);
                Put(new TextBlock { Text = new string('　', Math.Max(0, e.Depth - 1)) + e.Name, ToolTip = e.Path, TextTrimming = TextTrimming.CharacterEllipsis,
                                    FontWeight = e.Kind == "host" ? FontWeights.SemiBold : FontWeights.Normal }, row, 1);
                Put(new TextBlock { Text = e.KindText, Foreground = Brushes.DimGray }, row, 2);
                Put(new TextBlock { Text = e.Status, Foreground = e.Available ? Brushes.DimGray : Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }, row, 3);
                for (int i = 0; i < PartCols.Length; i++)
                {
                    var part = PartCols[i].Part;
                    var cb = new CheckBox { IsChecked = (e.PartFlags & part) != 0, IsEnabled = e.Available, HorizontalAlignment = HorizontalAlignment.Center, ToolTip = PartCols[i].Tip };
                    cb.Checked += (o, a) => entry.PartFlags |= part;
                    cb.Unchecked += (o, a) => entry.PartFlags &= ~part;
                    Put(cb, row, 4 + i);
                }
                if (e.Kind == "file")
                {
                    var rm = new Button { Content = "移除", Padding = new Thickness(4, 0, 4, 0) };
                    rm.Click += (o, a) => { _entries.Remove(entry); Fill(); };
                    Put(rm, row, 4 + PartCols.Length);
                }
                row++;
            }
        }

        private void AddFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "加入要一起檢討的模型檔（尚未連結者）",
                Filter = "Revit 專案 (*.rvt)|*.rvt",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) != true) return;
            foreach (string f in dlg.FileNames)
            {
                var ne = ModelSources.NewFileEntry(f);
                if (_entries.Any(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(this, $"{System.IO.Path.GetFileName(f)} 已在清單中（可能已是連結或目前檔案）。", "檢討範圍");
                    continue;
                }
                _entries.Add(ne);
            }
            Fill();
        }

        private bool Save()
        {
            if (!double.TryParse(_tol.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double tol) || tol < 10 || tol > 2000)
            {
                MessageBox.Show(this, "樓層合併容許高程差請輸入 10～2000 mm。", "檢討範圍");
                return false;
            }
            if (!_entries.Any(x => x.Include))
            {
                MessageBox.Show(this, "至少要納入一個模型。", "檢討範圍");
                return false;
            }
            if (!_entries.Any(x => x.Include && (x.PartFlags & SourceParts.Spaces) != 0))
            {
                if (MessageBox.Show(this, "沒有任何模型讀取「房間樓層」，大部分條文將顯示資料不足。仍要繼續嗎？", "檢討範圍", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return false;
            }
            _s.LevelToleranceMm = tol;
            _s.KeepDuplicates = _keepDup.IsChecked == true;
            // 只記錄使用者的選擇；未載入的連結保留先前設定
            var keep = (_s.Sources ?? new List<SourceEntry>()).Where(o => _entries.All(x => x.Key != o.Key) && o.Kind != "file").ToList();
            _s.Sources = keep.Concat(_entries.Select(x => new SourceEntry { Key = x.Key, Kind = x.Kind, Name = x.Name, Path = x.Path, Include = x.Include, Parts = x.Parts })).ToList();
            return true;
        }

        public static string Summary(IEnumerable<SourceEntry> entries)
        {
            var inc = entries.Where(x => x.Include && x.Available).ToList();
            if (inc.Count == 1 && inc[0].Kind == "host") return "目前檔案";
            return $"{inc.Count} 個模型（{string.Join("、", inc.Take(4).Select(x => x.Name))}{(inc.Count > 4 ? "…" : "")}）";
        }
    }
}
