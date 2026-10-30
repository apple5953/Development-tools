using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DevelopmentTools.FastRebar.Saving;

namespace DevelopmentTools.FastRebar
{
    /// <summary>調整減料參數的小視窗。</summary>
    public class RebarSavingOptionsWindow : Window
    {
        private readonly TextBox _stock, _lap, _kerf, _offcut, _fc, _fy;
        public SavingOptions Options { get; private set; }

        public RebarSavingOptionsWindow(SavingOptions current)
        {
            Title = "減料參數";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var g = new Grid { Margin = new Thickness(14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            _stock = Row(g, 0, "定尺長度 (mm)", string.Join(", ", current.StockLengthsMm.Select(x => x.ToString("0"))), "可採購的長度，以逗號分隔");
            _lap = Row(g, 1, "搭接倍數 (db)", current.LapFactor.ToString("0.##"), "0 = 依規範受拉乙級 1.3 ld；大於 0 = 倍數×標稱直徑");
            _kerf = Row(g, 2, "每刀損耗 (mm)", current.KerfMm.ToString("0.##"), "剪斷機一般為 0");
            _offcut = Row(g, 3, "可回收餘料下限 (mm)", current.MinReusableOffcutMm.ToString("0"), "餘料達此長度視為回收再用，不計入損耗");
            _fc = Row(g, 4, "fc′ (kgf/cm²)", current.FcKgf.ToString("0"), "依規範計算搭接長度用");
            _fy = Row(g, 5, "fy (kgf/cm²)", current.FyKgf.ToString("0"), "依規範計算搭接長度用");

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "確定", Width = 72, IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 72, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            ok.Click += (s, e) => Confirm();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            g.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(buttons, 6);
            Grid.SetColumnSpan(buttons, 2);
            g.Children.Add(buttons);
            Content = g;
        }

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

        private void Confirm()
        {
            try
            {
                var o = new SavingOptions
                {
                    StockLengthsMm = SavingOptions.ParseLengths(_stock.Text),
                    LapFactor = double.Parse(_lap.Text.Trim()),
                    KerfMm = double.Parse(_kerf.Text.Trim()),
                    MinReusableOffcutMm = double.Parse(_offcut.Text.Trim()),
                    FcKgf = double.Parse(_fc.Text.Trim()),
                    FyKgf = double.Parse(_fy.Text.Trim())
                };
                o.Validate();
                Options = o;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "參數有誤：" + ex.Message, "減料參數");
            }
        }
    }
}
