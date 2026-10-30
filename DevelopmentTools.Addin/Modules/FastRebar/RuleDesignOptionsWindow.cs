using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DevelopmentTools.FastRebar.Saving;

namespace DevelopmentTools.FastRebar
{
    /// <summary>依規範觀念自動配筋的參數，與樣板存在同一資料夾。</summary>
    public static class RuleDesignOptionsStore
    {
        public static string FilePath => Path.Combine(Path.GetDirectoryName(FastRebarPresetStore.FilePath), "design-options.txt");

        public static DesignOptions Load()
        {
            try
            {
                if (File.Exists(FilePath)) return DesignOptions.FromText(File.ReadAllText(FilePath, Encoding.UTF8));
            }
            catch { }
            return new DesignOptions();
        }

        public static void Save(DesignOptions o)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, o.ToText(), Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>規範觀念自動配筋參數視窗。</summary>
    public class RuleDesignOptionsWindow : Window
    {
        private readonly TextBox _fc, _fy, _top, _bottom, _column, _cover;
        public DesignOptions Options { get; private set; }

        public RuleDesignOptionsWindow(DesignOptions current)
        {
            Title = "依斷面自動配筋";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var g = new Grid { Margin = new Thickness(14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

            var note = new TextBlock
            {
                Text = "沒有載重資料，主筋量依目標配筋率推估（不低於規範最小量）；箍筋、圍束區、間距依規範耐震細則。結果適合建模與估量，不能取代結構設計。",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(0, 0, 0, 10)
            };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumnSpan(note, 2);
            g.Children.Add(note);

            _fc = Row(g, 1, "fc′ (kgf/cm²)", current.FcKgf.ToString("0"), "混凝土設計強度");
            _fy = Row(g, 2, "fy (kgf/cm²)", current.FyKgf.ToString("0"), "主筋降伏強度");
            _top = Row(g, 3, "梁上層配筋率 (%)", Pct(current.BeamTopRatio), "以 b×d 計；不低於規範最小量");
            _bottom = Row(g, 4, "梁下層配筋率 (%)", Pct(current.BeamBottomRatio), "以 b×d 計；不少於上層的一半");
            _column = Row(g, 5, "柱配筋率 (%)", Pct(current.ColumnRatio), "以 Ag 計；規範 1%～6%");
            _cover = Row(g, 6, "設計用保護層 (mm)", current.CoverMm.ToString("0"), "只用於計算可排支數；建模仍讀構件的保護層設定");

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "下一步", Width = 72, IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 72, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            ok.Click += (s, e) => Confirm();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            g.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(buttons, 7);
            Grid.SetColumnSpan(buttons, 2);
            g.Children.Add(buttons);
            Content = g;
        }

        private static string Pct(double ratio) => (ratio * 100).ToString("0.##", CultureInfo.InvariantCulture);

        private static TextBox Row(Grid g, int row, string label, string value, string tip)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            var t = new TextBox { Text = value, ToolTip = tip, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(l, row);
            Grid.SetRow(t, row);
            Grid.SetColumn(t, 1);
            g.Children.Add(l);
            g.Children.Add(t);
            return t;
        }

        private static double Num(TextBox t) => double.Parse(t.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

        private void Confirm()
        {
            try
            {
                var o = new DesignOptions
                {
                    FcKgf = Num(_fc),
                    FyKgf = Num(_fy),
                    BeamTopRatio = Num(_top) / 100,
                    BeamBottomRatio = Num(_bottom) / 100,
                    ColumnRatio = Num(_column) / 100,
                    CoverMm = Num(_cover)
                };
                o.Validate();
                Options = o;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "參數有誤：" + ex.Message, "依斷面自動配筋");
            }
        }
    }
}
