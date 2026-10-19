using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DevelopmentTools.CodeCheck
{
    /// <summary>
    /// 法規檢討主視窗：左側為全部法規條文（法規 → 章 → 條，標示檢討方式與狀態），
    /// 右側顯示條文全文、各規則量測結果，並可填寫人工檢討結果。
    /// 實際讀模型與套用規則由 Run 委派執行（在 Revit API 環境內）。
    /// </summary>
    public class CodeCheckWindow : Window
    {
        public Func<Action<string>, CodeCheckEngine> Run;
        public Func<IList<RuleResult>, string> SelectElements;   // 回傳狀態列訊息
        public Func<bool> EditSettings;
        public Func<bool> EditSources;                          // 檢討範圍（合併哪些模型）
        public Func<string> ScopeText;
        public Action SaveSettings;
        public Action<List<ArticleResult>> Export;

        private readonly CodeCheckData _data;
        private readonly ProjectSettings _settings;
        private CodeCheckEngine _engine;
        private List<ArticleResult> _articles = new List<ArticleResult>();

        private TreeView _tree;
        private TextBlock _status, _summary, _title, _kindText;
        private TextBox _lawText, _search, _note;
        private DataGrid _grid;
        private ComboBox _filter, _manualStatus;
        private ArticleResult _current;
        private DataGridColumn _modelCol;

        private static readonly Brush FailBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
        private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xB7, 0x79, 0x1F));
        private static readonly Brush NoDataBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x46, 0xC1));
        private static readonly Brush PassBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x85, 0x5A));
        private static readonly Brush ManualBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0));
        private static readonly Brush MuteBrush = new SolidColorBrush(Color.FromRgb(0x71, 0x80, 0x96));

        public CodeCheckWindow(CodeCheckData data, ProjectSettings settings)
        {
            _data = data;
            _settings = settings;
            Title = "法規檢討（全條文）";
            Width = 1280; Height = 820;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Microsoft JhengHei UI");
            FontSize = 13;
            Background = Brushes.White;
            Content = Build();
            ShowCatalogOnly();
            // 委派在建構後才指定，顯示時再更新一次檢討範圍
            Loaded += (o, e) => { if (_engine == null) _status.Text = $"已載入 {_data.Laws.Laws.Count} 部法規、{_articles.Count} 條條文、{_data.Rules.Rules.Count} 條可執行規則。檢討範圍：{ScopeText?.Invoke() ?? "目前檔案"}。按「執行檢討」讀取模型。"; };
        }

        private static Brush BrushOf(CheckStatus s)
        {
            switch (s)
            {
                case CheckStatus.Fail: return FailBrush;
                case CheckStatus.Warning: return WarnBrush;
                case CheckStatus.NoData: return NoDataBrush;
                case CheckStatus.Pass: return PassBrush;
                case CheckStatus.Manual: return ManualBrush;
                default: return MuteBrush;
            }
        }

        private UIElement Build()
        {
            var root = new DockPanel { Margin = new Thickness(10) };

            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(Btn("專案條件…", (o, e) => { if (EditSettings != null && EditSettings()) _status.Text = "專案條件已更新，請重新執行檢討。"; }));
            left.Children.Add(Btn("檢討範圍（模型）…", (o, e) => { if (EditSources != null && EditSources()) _status.Text = "檢討範圍已更新：" + ScopeText?.Invoke() + "。請重新執行檢討。"; }));
            left.Children.Add(Btn("執行檢討", (o, e) => DoRun(), bold: true));
            left.Children.Add(Btn("匯出報告…", (o, e) => Export?.Invoke(_articles)));
            DockPanel.SetDock(left, Dock.Left);
            bar.Children.Add(left);
            _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Foreground = MuteBrush, TextTrimming = TextTrimming.CharacterEllipsis };
            bar.Children.Add(_status);
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);

            _summary = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(_summary, Dock.Top);
            root.Children.Add(_summary);

            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftPane = new DockPanel();
            var filterRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            _filter = new ComboBox
            {
                Width = 140,
                ItemsSource = new[] { "全部條文", "不符合", "需確認", "資料不足", "符合", "人工檢討待辦", "自動／半自動", "已人工檢討" },
                SelectedIndex = 0
            };
            _filter.SelectionChanged += (o, e) => Rebuild();
            DockPanel.SetDock(_filter, Dock.Left);
            filterRow.Children.Add(_filter);
            _search = new TextBox { Margin = new Thickness(6, 0, 0, 0), ToolTip = "搜尋條號或內容" };
            _search.TextChanged += (o, e) => Rebuild();
            filterRow.Children.Add(_search);
            DockPanel.SetDock(filterRow, Dock.Top);
            leftPane.Children.Add(filterRow);
            _tree = new TreeView();
            VirtualizingPanel.SetIsVirtualizing(_tree, true);
            _tree.SelectedItemChanged += (o, e) => ShowArticle((_tree.SelectedItem as TreeViewItem)?.Tag as ArticleResult);
            leftPane.Children.Add(_tree);
            Grid.SetColumn(leftPane, 0);
            main.Children.Add(leftPane);

            var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
            Grid.SetColumn(splitter, 1);
            main.Children.Add(splitter);

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(160) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new StackPanel();
            _title = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            _kindText = new TextBlock { Foreground = MuteBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
            head.Children.Add(_title); head.Children.Add(_kindText);
            Grid.SetRow(head, 0); right.Children.Add(head);

            _lawText = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xFB)) };
            Grid.SetRow(_lawText, 1); right.Children.Add(_lawText);

            _grid = new DataGrid
            {
                AutoGenerateColumns = false, IsReadOnly = true, Margin = new Thickness(0, 8, 0, 0), HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended, CanUserAddRows = false, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
            };
            _grid.Columns.Add(new DataGridTextColumn { Header = "狀態", Binding = new System.Windows.Data.Binding("Status"), Width = 70 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "規則", Binding = new System.Windows.Data.Binding("Rule"), Width = 200 });
            _modelCol = new DataGridTextColumn { Header = "模型", Binding = new System.Windows.Data.Binding("Model"), Width = 110, Visibility = Visibility.Collapsed };
            _grid.Columns.Add(_modelCol);
            _grid.Columns.Add(new DataGridTextColumn { Header = "對象", Binding = new System.Windows.Data.Binding("Target"), Width = 230 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "結果", Binding = new System.Windows.Data.Binding("Detail"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _grid.MouseDoubleClick += (o, e) => SelectInModel();
            Grid.SetRow(_grid, 2); right.Children.Add(_grid);

            var manual = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            manual.Children.Add(new TextBlock { Text = "人工檢討：", VerticalAlignment = VerticalAlignment.Center });
            _manualStatus = new ComboBox { Width = 90, ItemsSource = new[] { "未檢", "符合", "不符合", "不適用" }, SelectedIndex = 0, Margin = new Thickness(4, 0, 6, 0) };
            manual.Children.Add(_manualStatus);
            var save = Btn("儲存紀錄", (o, e) => SaveManual());
            DockPanel.SetDock(save, Dock.Right);
            var sel = Btn("選取元素", (o, e) => SelectInModel());
            DockPanel.SetDock(sel, Dock.Right);
            manual.Children.Add(save);
            manual.Children.Add(sel);
            _note = new TextBox { ToolTip = "依據圖號、頁次或說明" };
            manual.Children.Add(_note);
            Grid.SetRow(manual, 3); right.Children.Add(manual);

            Grid.SetColumn(right, 2);
            main.Children.Add(right);
            root.Children.Add(main);
            return root;
        }

        private static Button Btn(string text, RoutedEventHandler h, bool bold = false)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
            if (bold) b.FontWeight = FontWeights.SemiBold;
            b.Click += h;
            return b;
        }

        private void Progress(string s)
        {
            _status.Text = s;
            Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }

        private void ShowCatalogOnly()
        {
            _articles = _data.Catalog.Items.Select(i =>
            {
                var law = _data.FindLaw(i.Law);
                var a = new ArticleResult
                {
                    Item = i, Law = law, Article = law?.Articles.FirstOrDefault(x => x.No == i.No),
                    Status = i.Kind == "manual" ? CheckStatus.Manual : i.Kind == "info" ? CheckStatus.Info : CheckStatus.NoData
                };
                return a;
            }).ToList();
            _status.Text = $"已載入 {_data.Laws.Laws.Count} 部法規、{_articles.Count} 條條文、{_data.Rules.Rules.Count} 條可執行規則。檢討範圍：{ScopeText?.Invoke() ?? "目前檔案"}。按「執行檢討」讀取模型。";
            UpdateSummary(false);
            Rebuild();
        }

        private void DoRun()
        {
            if (Run == null) return;
            Cursor = Cursors.Wait;
            try
            {
                _engine = Run(Progress);
                if (_engine != null)
                {
                    _articles = _engine.Articles;
                    UpdateSummary(true);
                    Rebuild();
                    bool multi = _engine.AllResults.Select(r => r.Source).Where(x => x != null).Distinct().Count() > 1 || _engine.AllResults.Any(r => r.Source != null && !r.Source.IsHost);
                    _modelCol.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
                    _status.Text = "檢討完成（" + (ScopeText?.Invoke() ?? "目前檔案") + "）。" + (_engine.RuleErrors.Count > 0 ? $"（{_engine.RuleErrors.Count} 條規則格式錯誤：{_engine.RuleErrors[0]}）" : "");
                }
            }
            catch (Exception ex)
            {
                _status.Text = "執行失敗：" + ex.Message;
                MessageBox.Show(this, ex.ToString(), "法規檢討");
            }
            finally { Cursor = null; }
        }

        private void UpdateSummary(bool ran)
        {
            int total = _articles.Count;
            int auto = _articles.Count(a => a.Item.Kind == "auto"), semi = _articles.Count(a => a.Item.Kind == "semi");
            int manual = _articles.Count(a => a.Item.Kind == "manual"), info = _articles.Count(a => a.Item.Kind == "info");
            string s = $"收錄 {total} 條：自動 {auto}、半自動 {semi}、人工 {manual}、非模型 {info}。";
            if (ran)
            {
                s += $"　結果：不符合 {_articles.Count(a => a.Status == CheckStatus.Fail)}、需確認 {_articles.Count(a => a.Status == CheckStatus.Warning)}、" +
                     $"資料不足 {_articles.Count(a => a.Status == CheckStatus.NoData)}、符合 {_articles.Count(a => a.Status == CheckStatus.Pass)}、" +
                     $"人工待辦 {_articles.Count(a => a.Status == CheckStatus.Manual)}。";
            }
            _summary.Text = s;
        }

        private bool Visible(ArticleResult a)
        {
            switch (_filter.SelectedIndex)
            {
                case 1: if (a.Status != CheckStatus.Fail) return false; break;
                case 2: if (a.Status != CheckStatus.Warning) return false; break;
                case 3: if (a.Status != CheckStatus.NoData) return false; break;
                case 4: if (a.Status != CheckStatus.Pass) return false; break;
                case 5: if (a.Status != CheckStatus.Manual) return false; break;
                case 6: if (a.Item.Kind != "auto" && a.Item.Kind != "semi") return false; break;
                case 7: if (!_settings.Manual.ContainsKey(a.Key)) return false; break;
            }
            string q = _search.Text?.Trim();
            if (!string.IsNullOrEmpty(q))
                return a.Item.No == q || (a.Article?.Text?.Contains(q) ?? false) || (a.Law?.Name?.Contains(q) ?? false);
            return true;
        }

        private void Rebuild()
        {
            if (_tree == null) return;
            _tree.Items.Clear();
            foreach (var g in _articles.Where(Visible).GroupBy(a => a.Item.Law))
            {
                var law = _data.FindLaw(g.Key);
                int fail = g.Count(a => a.Status == CheckStatus.Fail);
                var lawNode = new TreeViewItem
                {
                    Header = $"{law?.Name}（{g.Count()}）" + (fail > 0 ? $"　不符合 {fail}" : ""),
                    IsExpanded = _filter.SelectedIndex != 0 || !string.IsNullOrEmpty(_search.Text),
                    FontWeight = FontWeights.SemiBold
                };
                foreach (var ch in g.GroupBy(a => a.Article?.Chapter ?? ""))
                {
                    ItemsControl parent = lawNode;
                    if (!string.IsNullOrWhiteSpace(ch.Key))
                    {
                        var chNode = new TreeViewItem { Header = ch.Key.Replace("  ", " "), FontWeight = FontWeights.Normal, IsExpanded = lawNode.IsExpanded };
                        lawNode.Items.Add(chNode);
                        parent = chNode;
                    }
                    foreach (var a in ch)
                    {
                        var sp = new StackPanel { Orientation = Orientation.Horizontal };
                        sp.Children.Add(new TextBlock { Text = "● ", Foreground = BrushOf(a.Status) });
                        sp.Children.Add(new TextBlock { Text = CodeCheckData.ArticleLabel(a.Item.Law, a.Item.No) + "　", FontWeight = FontWeights.Normal });
                        sp.Children.Add(new TextBlock { Text = $"[{CodeCheckEngine.KindText(a.Item.Kind)}] ", Foreground = MuteBrush, FontWeight = FontWeights.Normal });
                        string hint = a.Item.Hint ?? "";
                        sp.Children.Add(new TextBlock { Text = hint.Length > 28 ? hint.Substring(0, 28) + "…" : hint, Foreground = MuteBrush, FontWeight = FontWeights.Normal });
                        parent.Items.Add(new TreeViewItem { Header = sp, Tag = a, FontWeight = FontWeights.Normal });
                    }
                }
                _tree.Items.Add(lawNode);
            }
        }

        private class Row
        {
            public string Status { get; set; }
            public string Rule { get; set; }
            public string Model { get; set; }
            public string Target { get; set; }
            public string Detail { get; set; }
            public RuleResult Result { get; set; }
        }

        private void ShowArticle(ArticleResult a)
        {
            _current = a;
            if (a == null) return;
            _title.Text = $"{a.Law?.Name}　{CodeCheckData.ArticleLabel(a.Item.Law, a.Item.No)}　—　{CodeCheckEngine.StatusText(a.Status)}";
            _title.Foreground = BrushOf(a.Status);
            string counts = a.Checked > 0 ? $"　檢查 {a.Checked} 項：不符合 {a.Failed}、需確認 {a.Warned}、資料不足 {a.NoData}、符合 {a.Passed}" : "";
            _kindText.Text = $"檢討方式：{CodeCheckEngine.KindText(a.Item.Kind)}｜{a.Item.Reason}{counts}";
            _lawText.Text = (a.Article?.Text ?? "").Replace(" / ", "\n");
            var rows = a.Results
                .OrderBy(r => r.Status)
                .Select(r => new Row
                {
                    Status = CodeCheckEngine.StatusText(r.Status),
                    Rule = r.Rule.Title,
                    Target = r.Target,
                    Detail = r.Detail + (r.Status == CheckStatus.Pass || r.Status == CheckStatus.NotApplicable ? "" : "｜" + r.Rule.Message),
                    Model = r.Source?.Name ?? "",
                    Result = r
                }).ToList();
            _grid.ItemsSource = rows;
            if (_settings.Manual.TryGetValue(a.Key, out ManualCheck m))
            {
                _manualStatus.SelectedItem = string.IsNullOrEmpty(m.Status) ? "未檢" : m.Status;
                _note.Text = m.Note ?? "";
            }
            else
            {
                _manualStatus.SelectedIndex = 0;
                _note.Text = "";
            }
        }

        private void SaveManual()
        {
            if (_current == null) return;
            _settings.Manual[_current.Key] = new ManualCheck
            {
                Status = _manualStatus.SelectedItem as string ?? "未檢",
                Note = _note.Text,
                By = Environment.UserName,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };
            SaveSettings?.Invoke();
            _engine?.Resummarize(_current);
            if (_engine == null)
            {
                var m = _settings.Manual[_current.Key];
                if (m.Status == "符合") _current.Status = CheckStatus.Pass;
                else if (m.Status == "不符合") _current.Status = CheckStatus.Fail;
                else if (m.Status == "不適用") _current.Status = CheckStatus.NotApplicable;
            }
            _status.Text = $"已儲存 {CodeCheckData.ArticleLabel(_current.Item.Law, _current.Item.No)} 的人工檢討紀錄。";
            UpdateSummary(_engine != null);
            ShowArticle(_current);
        }

        private void SelectInModel()
        {
            var results = _grid.SelectedItems.OfType<Row>().Select(r => r.Result).ToList();
            if (results.Count == 0 && _current != null)
                results = _current.Results.Where(r => r.Status == CheckStatus.Fail || r.Status == CheckStatus.Warning).ToList();
            results = results.Where(r => r.ElementId != null && r.ElementId != Autodesk.Revit.DB.ElementId.InvalidElementId).ToList();
            if (results.Count == 0) { _status.Text = "沒有可選取的元素。"; return; }
            _status.Text = SelectElements?.Invoke(results) ?? "";
        }
    }
}
