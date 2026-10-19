using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DevelopmentTools.CodeCheck
{
    /// <summary>專案條件設定：使用類組、消防場所、分區、道路、構造等模型讀不到的資訊。</summary>
    public class CodeCheckSettingsDialog : Window
    {
        private static readonly string[] Groups =
        {
            "A-1", "A-2", "B-1", "B-2", "B-3", "B-4", "C-1", "C-2", "D-1", "D-2", "D-3", "D-4", "D-5",
            "E", "F-1", "F-2", "F-3", "F-4", "G-1", "G-2", "G-3", "H-1", "H-2", "I"
        };

        private static readonly Dictionary<string, string> GroupNames = new Dictionary<string, string>
        {
            { "A-1", "集會表演" }, { "A-2", "運輸場所" }, { "B-1", "娛樂場所" }, { "B-2", "商場百貨" }, { "B-3", "餐飲場所" }, { "B-4", "旅館" },
            { "C-1", "特殊工業" }, { "C-2", "一般工業" }, { "D-1", "健身休閒" }, { "D-2", "文教設施" }, { "D-3", "國小校舍" }, { "D-4", "校舍" },
            { "D-5", "補教托育" }, { "E", "宗教殯葬" }, { "F-1", "醫療照護" }, { "F-2", "社會福利" }, { "F-3", "兒童福利" }, { "F-4", "戒護場所" },
            { "G-1", "金融證券" }, { "G-2", "辦公場所" }, { "G-3", "店鋪診所" }, { "H-1", "宿舍安養" }, { "H-2", "住宅" }, { "I", "危險物品" }
        };

        private readonly ProjectSettings _s;
        private readonly List<CheckBox> _groupBoxes = new List<CheckBox>();
        private TextBox _fire, _road, _setback, _site, _bldgArea, _cov, _far, _ground, _floorsA, _floorsB, _height, _openable, _frame,
                        _p1, _p2, _p3, _p4, _pOverride, _occ;
        private ComboBox _zone, _sprinkler;
        private CheckBox _volume, _urban, _fireproof, _access, _public, _apartment, _elderly, _void, _tc23, _tc24, _slope, _fast, _travel;

        public CodeCheckSettingsDialog(ProjectSettings s, IEnumerable<string> levelNames)
        {
            _s = s;
            Title = "專案條件（法規檢討）";
            Width = 720; Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Microsoft JhengHei UI");
            FontSize = 13;
            Background = Brushes.White;

            var root = new DockPanel { Margin = new Thickness(12) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "儲存", Padding = new Thickness(18, 4, 18, 4), IsDefault = true };
            var cancel = new Button { Content = "取消", Padding = new Thickness(18, 4, 18, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            ok.Click += (o, e) => { if (Save()) DialogResult = true; };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var stack = new StackPanel();
            stack.Children.Add(Header("使用類組（建築物使用類組及變更使用辦法附表一，可複選）"));
            var wrap = new WrapPanel();
            foreach (string g in Groups)
            {
                var cb = new CheckBox { Content = $"{g} {GroupNames[g]}", Tag = g, Width = 140, Margin = new Thickness(0, 2, 0, 2), IsChecked = s.UseGroups.Contains(g) };
                _groupBoxes.Add(cb);
                wrap.Children.Add(cb);
            }
            stack.Children.Add(wrap);

            var grid = NewGrid();
            var fireRow = new DockPanel();
            var suggest = new Button { Content = "依類組推估", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(suggest, Dock.Right);
            _fire = new TextBox { Text = string.Join(",", s.FireClasses) };
            suggest.Click += (o, e) => _fire.Text = string.Join(",", ProjectSettings.SuggestFireClasses(SelectedGroups()));
            fireRow.Children.Add(suggest); fireRow.Children.Add(_fire);
            Row(grid, "消防場所分類", fireRow, "例：甲1、甲4、乙7、丙3。長照等場所填「甲6長照」，地下建築物填「戊3」。");
            stack.Children.Add(Header("消防"));
            stack.Children.Add(grid);

            var g2 = NewGrid();
            _zone = new ComboBox { ItemsSource = new[] { "住宅區", "商業區", "工業區", "行政區", "文教區", "其他使用區", "非都市土地" }, IsEditable = true, Text = s.Zone };
            Row(g2, "土地使用分區", _zone);
            _urban = Check("都市計畫區內", s.InUrbanPlan); Row(g2, "", _urban);
            _volume = Check("實施容積管制", s.VolumeControl); Row(g2, "", _volume);
            _road = Num(s.RoadWidthM); Row(g2, "面前道路寬度 (m)", _road, "0 = 未填，相關高度規定列為資料不足");
            _setback = Num(s.SetbackM); Row(g2, "建築物至建築線距離 D (m)", _setback, "第 164 條斜率檢討用");
            _site = Num(s.SiteAreaM2); Row(g2, "基地面積 (m²)", _site);
            _bldgArea = Num(s.BuildingAreaM2); Row(g2, "建築面積 (m²)", _bldgArea, "屋突面積比檢討用");
            _cov = Num(s.MaxCoverageRatio); Row(g2, "法定建蔽率 (0~1)", _cov);
            _far = Num(s.MaxFarRatio); Row(g2, "法定容積率 (倍)", _far);
            _slope = Check("山坡地", s.Slopeland); Row(g2, "", _slope);
            _tc23 = Check("符合第 23 條但書（寬道路或臨永久性空地）", s.Tc23Exception); Row(g2, "", _tc23);
            _tc24 = Check("符合第 24 條但書", s.Tc24Exception); Row(g2, "", _tc24);
            stack.Children.Add(Header("基地與分區"));
            stack.Children.Add(g2);

            var g3 = NewGrid();
            _fireproof = Check("主要構造為防火構造", s.Fireproof); Row(g3, "", _fireproof);
            _sprinkler = new ComboBox { ItemsSource = new[] { "auto", "yes", "no" }, SelectedItem = s.SprinklerMode };
            Row(g3, "設有自動撒水", _sprinkler, "auto = 模型中有撒水頭即視為設置");
            _fast = Check("撒水頭為快速反應型", s.SprinklerFastResponse); Row(g3, "", _fast);
            _access = Check("須設置無障礙設施（第 167 條）", s.AccessibilityApplies); Row(g3, "", _access);
            _public = Check("公共建築物（第 170 條）", s.PublicBuilding); Row(g3, "", _public);
            _apartment = Check("集合住宅", s.IsApartment); Row(g3, "", _apartment);
            _elderly = Check("老人住宅", s.IsElderlyHousing); Row(g3, "", _elderly);
            _void = Check("住宅有挑空設計（第 164-1 條）", s.HasVoid); Row(g3, "", _void);
            stack.Children.Add(Header("構造與用途"));
            stack.Children.Add(g3);

            var g4 = NewGrid();
            _ground = new TextBox { Text = s.GroundLevel ?? "" };
            Row(g4, "地面層（避難層）樓層名稱", new ComboBox { ItemsSource = levelNames.ToList(), IsEditable = true, Text = s.GroundLevel ?? "" }, "空白 = 自動判斷（1F、1FL、GL…）");
            _floorsA = Num(s.FloorsAboveOverride); Row(g4, "地上層數（0 = 自動）", _floorsA);
            _floorsB = Num(s.FloorsBelowOverride); Row(g4, "地下層數（0 = 自動）", _floorsB);
            _height = Num(s.HeightOverrideM); Row(g4, "建築物高度 m（0 = 自動）", _height);
            _openable = Num(s.OpenableRatio); Row(g4, "窗戶可開啟比例", _openable, "通風面積估算用，預設 0.5");
            _frame = Num(s.DoorFrameDeductionMm); Row(g4, "門淨寬扣除 (mm)", _frame, "門寬扣除門框得淨寬；族群寬度已是淨寬填 0");
            _travel = Check("計算步行距離（Revit 行走路徑，大型模型較久）", s.TravelDistance); Row(g4, "", _travel);
            stack.Children.Add(Header("模型判讀"));
            stack.Children.Add(g4);

            var g5 = NewGrid();
            _p1 = Num(s.ParkingArea1); Row(g5, "第一類樓地板面積 (m²)", _p1, "戲院、辦公、商場、餐廳等");
            _p2 = Num(s.ParkingArea2); Row(g5, "第二類 (m²)", _p2, "住宅、集合住宅");
            _p3 = Num(s.ParkingArea3); Row(g5, "第三類 (m²)", _p3, "旅館、醫院、文教、體育等");
            _p4 = Num(s.ParkingArea4); Row(g5, "第四類 (m²)", _p4, "倉庫、學校、工廠等");
            _pOverride = Num(s.RequiredParkingOverride); Row(g5, "法定停車數（直接指定）", _pOverride, "都市計畫另有規定時填入，0 = 依上列面積計算");
            _occ = Num(s.OccupantsOverride); Row(g5, "使用人數（衛生設備）", _occ, "0 = 依居室面積推算");
            stack.Children.Add(Header("停車與衛生設備（第 59 條、設備編第 37 條）"));
            stack.Children.Add(g5);

            _groundCombo = (ComboBox)g4.Children.OfType<ComboBox>().First();
            root.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;
        }

        private readonly ComboBox _groundCombo;

        private IEnumerable<string> SelectedGroups() => _groupBoxes.Where(c => c.IsChecked == true).Select(c => (string)c.Tag);

        private static TextBlock Header(string t) => new TextBlock { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };

        private static Grid NewGrid()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        private static void Row(Grid g, string label, FrameworkElement c, string tip = null)
        {
            int r = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3), TextWrapping = TextWrapping.Wrap };
            c.Margin = new Thickness(0, 3, 0, 3);
            if (tip != null) { t.ToolTip = tip; c.ToolTip = tip; }
            Grid.SetRow(t, r); Grid.SetRow(c, r); Grid.SetColumn(c, 1);
            g.Children.Add(t); g.Children.Add(c);
        }

        private static CheckBox Check(string text, bool v) => new CheckBox { Content = text, IsChecked = v };

        private static TextBox Num(double v) => new TextBox { Text = v.ToString("0.###", CultureInfo.InvariantCulture) };

        private static double D(TextBox t) => double.TryParse(t.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

        private bool Save()
        {
            var groups = SelectedGroups().ToList();
            if (groups.Count == 0)
            {
                MessageBox.Show(this, "請至少選擇一個使用類組。", "專案條件");
                return false;
            }
            _s.UseGroups = groups;
            _s.FireClasses = _fire.Text.Split(new[] { ',', '，', '、', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
            if (_s.FireClasses.Count == 0) _s.FireClasses = ProjectSettings.SuggestFireClasses(groups);
            _s.Zone = _zone.Text;
            _s.InUrbanPlan = _urban.IsChecked == true;
            _s.VolumeControl = _volume.IsChecked == true;
            _s.RoadWidthM = D(_road);
            _s.SetbackM = D(_setback);
            _s.SiteAreaM2 = D(_site);
            _s.BuildingAreaM2 = D(_bldgArea);
            _s.MaxCoverageRatio = D(_cov);
            _s.MaxFarRatio = D(_far);
            _s.Slopeland = _slope.IsChecked == true;
            _s.Tc23Exception = _tc23.IsChecked == true;
            _s.Tc24Exception = _tc24.IsChecked == true;
            _s.Fireproof = _fireproof.IsChecked == true;
            _s.SprinklerMode = _sprinkler.SelectedItem as string ?? "auto";
            _s.SprinklerFastResponse = _fast.IsChecked == true;
            _s.AccessibilityApplies = _access.IsChecked == true;
            _s.PublicBuilding = _public.IsChecked == true;
            _s.IsApartment = _apartment.IsChecked == true;
            _s.IsElderlyHousing = _elderly.IsChecked == true;
            _s.HasVoid = _void.IsChecked == true;
            _s.GroundLevel = _groundCombo.Text?.Trim();
            _s.FloorsAboveOverride = (int)D(_floorsA);
            _s.FloorsBelowOverride = (int)D(_floorsB);
            _s.HeightOverrideM = D(_height);
            _s.OpenableRatio = D(_openable);
            _s.DoorFrameDeductionMm = D(_frame);
            _s.TravelDistance = _travel.IsChecked == true;
            _s.ParkingArea1 = D(_p1); _s.ParkingArea2 = D(_p2); _s.ParkingArea3 = D(_p3); _s.ParkingArea4 = D(_p4);
            _s.RequiredParkingOverride = (int)D(_pOverride);
            _s.OccupantsOverride = (int)D(_occ);
            _s.Normalize();
            return true;
        }
    }
}
