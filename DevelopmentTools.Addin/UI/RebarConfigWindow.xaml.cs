using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DevelopmentTools.UI
{
    public enum RebarElementKind
    {
        Beam,
        Column
    }

    public enum RebarConnectionMode
    {
        Continuous,
        LapSplice,
        Coupler
    }

    public enum RebarEndMode
    {
        Straight,
        Hook90,
        Hook135
    }

    public class RebarPreviewContext
    {
        public RebarElementKind Kind { get; set; }
        public string ElementName { get; set; }
        public double LengthMm { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
    }

    public class RebarPlacementOptions
    {
        public double TopBarDbMm { get; set; }
        public double BottomBarDbMm { get; set; }
        public double StirrupDbMm { get; set; }
        public int TopBarCount { get; set; }
        public int BottomBarCount { get; set; }
        public int SideBarCount { get; set; }
        public double CoverMm { get; set; }
        public double NormalSpacingMm { get; set; }
        public double DenseSpacingMm { get; set; }
        public double DenseLengthMm { get; set; }
        public double StartOffsetMm { get; set; }
        public double MaxSegmentLengthMm { get; set; }
        public double LapLengthMm { get; set; }
        public double CouplerGapMm { get; set; }
        public double AnchorageMm { get; set; }
        public RebarConnectionMode ConnectionMode { get; set; }
        public RebarEndMode EndMode { get; set; }
        public string StirrupStyle { get; set; }
    }

    public partial class RebarConfigWindow : Window
    {
        private readonly RebarPreviewContext _context;
        private bool _isLoaded;

        public RebarPlacementOptions Options { get; private set; }
        public bool IsConfirmed { get; private set; }

        public RebarConfigWindow(RebarPreviewContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            InitializeComponent();

            IsConfirmed = false;
            Loaded += (_, __) =>
            {
                _isLoaded = true;
                ApplySuggestedDefaults();
                UpdateElementLabels();
                UpdatePreview();
            };
        }

        private void ApplySuggestedDefaults()
        {
            TxtCover.Text = _context.Kind == RebarElementKind.Column ? "40" : "35";
            TxtNormalSpacing.Text = _context.Kind == RebarElementKind.Column ? "150" : "150";
            TxtDenseSpacing.Text = "100";
            TxtStartOffset.Text = "50";
            TxtMaxSegmentLength.Text = "12000";
            TxtCouplerGap.Text = "80";
            TxtAnchorage.Text = _context.Kind == RebarElementKind.Column ? "600" : "300";

            if (_context.Kind == RebarElementKind.Column)
            {
                int faceCount = _context.WidthMm >= 600 || _context.HeightMm >= 600 ? 3 : 2;
                TxtTopCount.Text = faceCount.ToString(CultureInfo.InvariantCulture);
                TxtBottomCount.Text = faceCount.ToString(CultureInfo.InvariantCulture);
                TxtSideCount.Text = "0";
                TxtDenseLength.Text = Math.Max(600, Math.Max(_context.WidthMm, _context.HeightMm)).ToString("0", CultureInfo.InvariantCulture);
                TxtLapLength.Text = "1000";
                ComboTopSize.SelectedIndex = 5;
                ComboBottomSize.SelectedIndex = 5;
                LblTopCount.Text = "X 向每側主筋數";
                LblBottomCount.Text = "Y 向每側主筋數";
                LblDenseLength.Text = "柱端圍束長度 mm";
            }
            else
            {
                TxtTopCount.Text = _context.WidthMm >= 450 ? "3" : "2";
                TxtBottomCount.Text = _context.WidthMm >= 450 ? "3" : "2";
                TxtSideCount.Text = _context.HeightMm >= 700 ? "2" : "0";
                TxtDenseLength.Text = Math.Max(600, _context.HeightMm * 2).ToString("0", CultureInfo.InvariantCulture);
                TxtLapLength.Text = "1000";
                ComboTopSize.SelectedIndex = 5;
                ComboBottomSize.SelectedIndex = 5;
                LblTopCount.Text = "頂筋支數";
                LblBottomCount.Text = "底筋支數";
                LblDenseLength.Text = "梁端加密長度 mm";
            }
        }

        private void UpdateElementLabels()
        {
            string kind = _context.Kind == RebarElementKind.Column ? "柱" : "樑";
            TxtElementSummary.Text = $"{kind}｜{_context.ElementName}｜長 {_context.LengthMm:0} mm，寬 {_context.WidthMm:0} mm，高 {_context.HeightMm:0} mm";
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ApplySuggestedDefaults();
            UpdatePreview();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "使用流程：\n\n" +
                "1. 先在 Revit 點選一支樑或柱，工具會讀取尺寸。\n" +
                "2. 左側調整主筋支數、箍筋間距、加密長度、保護層、搭接或續接器分段。\n" +
                "3. 右側預覽會即時更新，黃色提示會提醒可能衝突的位置。\n" +
                "4. 確認後再按「建立鋼筋」，才會真正寫入模型。\n\n" +
                "目前續接器先以分段與避讓間隙處理；若公司有標準續接器族，後續可指定族型並自動放置。",
                "鋼筋工具新手說明",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void InputChanged(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
            {
                UpdatePreview();
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadOptions(out var options, out var error))
            {
                MessageBox.Show(error, "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Options = options;
            IsConfirmed = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close();
        }

        private void UpdatePreview()
        {
            if (!_isLoaded || PreviewViewport == null)
            {
                return;
            }

            PreviewViewport.Children.Clear();

            if (!TryReadOptions(out var options, out _))
            {
                SetupCamera();
                AddLights();
                TxtPreviewStatus.Text = "請輸入有效數值後即可預覽。";
                TxtWarning.Text = "部分欄位尚未填妥或不是數字。";
                TxtFooter.Text = string.Empty;
                return;
            }

            SetupCamera();
            AddLights();

            double scale = 320.0 / Math.Max(_context.LengthMm, Math.Max(_context.WidthMm, _context.HeightMm));
            scale = Math.Max(0.025, Math.Min(scale, 0.18));

            double length = _context.LengthMm * scale;
            double width = _context.WidthMm * scale;
            double height = _context.HeightMm * scale;
            double cover = options.CoverMm * scale;
            double topDb = Math.Max(options.TopBarDbMm * scale, 2.6);
            double botDb = Math.Max(options.BottomBarDbMm * scale, 2.6);
            double stirrupDb = Math.Max(options.StirrupDbMm * scale, 1.8);

            AddBox(new Point3D(0, 0, 0), length, width, height, Color.FromArgb(45, 210, 216, 224), Color.FromArgb(70, 200, 205, 214));

            if (_context.Kind == RebarElementKind.Column)
            {
                DrawColumnPreview(options, scale, length, width, height, cover, topDb, stirrupDb);
            }
            else
            {
                DrawBeamPreview(options, scale, length, width, height, cover, topDb, botDb, stirrupDb);
            }

            var warnings = ValidatePracticality(options);
            TxtWarning.Text = warnings.Count == 0 ? "未偵測到明顯間距衝突。" : string.Join("\n", warnings.Take(4));
            TxtPreviewStatus.Text = BuildPreviewSummary(options);
            TxtFooter.Text = "預覽為配置意圖檢查；按下建立後會依同一組參數寫入 Revit 鋼筋。";
        }

        private void DrawBeamPreview(RebarPlacementOptions options, double scale, double length, double width, double height, double cover, double topDb, double botDb, double stirrupDb)
        {
            double yMin = -width / 2 + cover + stirrupDb + topDb / 2;
            double yMax = width / 2 - cover - stirrupDb - topDb / 2;
            double zTop = height / 2 - cover - stirrupDb - topDb / 2;
            double zBot = -height / 2 + cover + stirrupDb + botDb / 2;
            double xStart = -length / 2 - options.AnchorageMm * scale;
            double xEnd = length / 2 + options.AnchorageMm * scale;

            foreach (double y in EvenPositions(yMin, yMax, options.TopBarCount))
            {
                AddBarBox(new Point3D((xStart + xEnd) / 2, y, zTop), xEnd - xStart, topDb, topDb, Color.FromRgb(66, 153, 225));
                AddJointMarkers(options, scale, xStart, xEnd, y, zTop);
            }

            foreach (double y in EvenPositions(yMin, yMax, options.BottomBarCount))
            {
                AddBarBox(new Point3D((xStart + xEnd) / 2, y, zBot), xEnd - xStart, botDb, botDb, Color.FromRgb(66, 153, 225));
                AddJointMarkers(options, scale, xStart, xEnd, y, zBot);
            }

            if (options.SideBarCount > 0)
            {
                double zMidMin = zBot + (zTop - zBot) * 0.35;
                double zMidMax = zBot + (zTop - zBot) * 0.65;
                foreach (double z in EvenPositions(zMidMin, zMidMax, options.SideBarCount))
                {
                    AddBarBox(new Point3D((xStart + xEnd) / 2, yMin, z), xEnd - xStart, botDb, botDb, Color.FromRgb(99, 179, 237));
                    AddBarBox(new Point3D((xStart + xEnd) / 2, yMax, z), xEnd - xStart, botDb, botDb, Color.FromRgb(99, 179, 237));
                }
            }

            foreach (double x in GetStirrupPositions(_context.LengthMm, options).Take(90))
            {
                double px = -length / 2 + x * scale;
                DrawRectStirrupX(px, yMin - stirrupDb, yMax + stirrupDb, zBot - stirrupDb, zTop + stirrupDb, stirrupDb);
            }
        }

        private void DrawColumnPreview(RebarPlacementOptions options, double scale, double length, double width, double height, double cover, double topDb, double stirrupDb)
        {
            double xMin = -width / 2 + cover + stirrupDb + topDb / 2;
            double xMax = width / 2 - cover - stirrupDb - topDb / 2;
            double yMin = -height / 2 + cover + stirrupDb + topDb / 2;
            double yMax = height / 2 - cover - stirrupDb - topDb / 2;
            double zStart = -length / 2;
            double zEnd = length / 2 + options.AnchorageMm * scale;

            foreach (var pt in GetColumnBarPoints(xMin, xMax, yMin, yMax, options.TopBarCount, options.BottomBarCount))
            {
                AddBarBox(new Point3D(pt.X, pt.Y, (zStart + zEnd) / 2), topDb, topDb, zEnd - zStart, Color.FromRgb(66, 153, 225));
                AddJointMarkers(options, scale, zStart, zEnd, pt.X, pt.Y, true);
            }

            foreach (double z in GetStirrupPositions(_context.LengthMm, options).Take(90))
            {
                double pz = -length / 2 + z * scale;
                DrawRectStirrupZ(pz, xMin - stirrupDb, xMax + stirrupDb, yMin - stirrupDb, yMax + stirrupDb, stirrupDb);
            }
        }

        private void AddJointMarkers(RebarPlacementOptions options, double scale, double start, double end, double a, double b, bool vertical = false)
        {
            if (options.ConnectionMode == RebarConnectionMode.Continuous || options.MaxSegmentLengthMm <= 0)
            {
                return;
            }

            double maxLen = options.MaxSegmentLengthMm * scale;
            if (maxLen <= 0 || end - start <= maxLen)
            {
                return;
            }

            for (double p = start + maxLen; p < end - maxLen * 0.15; p += maxLen)
            {
                double marker = Math.Max(36 * scale, 5);
                if (vertical)
                {
                    AddBarBox(new Point3D(a, b, p), marker, marker, marker, Color.FromRgb(255, 204, 102));
                }
                else
                {
                    AddBarBox(new Point3D(p, a, b), marker, marker, marker, Color.FromRgb(255, 204, 102));
                }
            }
        }

        private void DrawRectStirrupX(double x, double yMin, double yMax, double zMin, double zMax, double db)
        {
            AddBarBox(new Point3D(x, (yMin + yMax) / 2, zMin), db, yMax - yMin, db, Color.FromRgb(0, 188, 212));
            AddBarBox(new Point3D(x, (yMin + yMax) / 2, zMax), db, yMax - yMin, db, Color.FromRgb(0, 188, 212));
            AddBarBox(new Point3D(x, yMin, (zMin + zMax) / 2), db, db, zMax - zMin, Color.FromRgb(0, 188, 212));
            AddBarBox(new Point3D(x, yMax, (zMin + zMax) / 2), db, db, zMax - zMin, Color.FromRgb(0, 188, 212));
        }

        private void DrawRectStirrupZ(double z, double xMin, double xMax, double yMin, double yMax, double db)
        {
            AddBarBox(new Point3D((xMin + xMax) / 2, yMin, z), xMax - xMin, db, db, Color.FromRgb(0, 188, 212));
            AddBarBox(new Point3D((xMin + xMax) / 2, yMax, z), xMax - xMin, db, db, Color.FromRgb(0, 188, 212));
            AddBarBox(new Point3D(xMin, (yMin + yMax) / 2, z), db, yMax - yMin, db, Color.FromRgb(0, 188, 212));
            AddBarBox(new Point3D(xMax, (yMin + yMax) / 2, z), db, yMax - yMin, db, Color.FromRgb(0, 188, 212));
        }

        private List<string> ValidatePracticality(RebarPlacementOptions options)
        {
            var warnings = new List<string>();
            double clearWidth = _context.WidthMm - options.CoverMm * 2 - options.StirrupDbMm * 2;
            int maxLineBars = _context.Kind == RebarElementKind.Column
                ? Math.Max(options.TopBarCount, options.BottomBarCount)
                : Math.Max(options.TopBarCount, options.BottomBarCount);
            double db = Math.Max(options.TopBarDbMm, options.BottomBarDbMm);
            if (maxLineBars > 1)
            {
                double clearSpacing = (clearWidth - maxLineBars * db) / (maxLineBars - 1);
                if (clearSpacing < Math.Max(25, db))
                {
                    warnings.Add($"主筋淨距約 {clearSpacing:0} mm，可能太小或互相衝突。");
                }
            }

            if (options.DenseSpacingMm < options.StirrupDbMm * 4)
            {
                warnings.Add("加密箍筋間距過小，施工與建模可能擁擠。");
            }

            if (options.DenseLengthMm * 2 > _context.LengthMm * 0.9)
            {
                warnings.Add("兩端加密區幾乎重疊，請確認是否整段都要加密。");
            }

            if (options.CoverMm < 25)
            {
                warnings.Add("保護層小於 25 mm，請確認專案規範。");
            }

            if (options.ConnectionMode != RebarConnectionMode.Continuous && options.MaxSegmentLengthMm <= options.LapLengthMm)
            {
                warnings.Add("最大單支長度需大於搭接/接頭控制長度。");
            }

            return warnings;
        }

        private string BuildPreviewSummary(RebarPlacementOptions options)
        {
            int stirrupCount = GetStirrupPositions(_context.LengthMm, options).Count;
            if (_context.Kind == RebarElementKind.Column)
            {
                int mainBars = GetColumnBarPoints(0, 1, 0, 1, options.TopBarCount, options.BottomBarCount).Count;
                return $"預估：柱主筋 {mainBars} 支，繫筋約 {stirrupCount} 道，接頭模式：{GetConnectionModeText(options.ConnectionMode)}";
            }

            int beamBars = options.TopBarCount + options.BottomBarCount + options.SideBarCount * 2;
            return $"預估：梁主筋 {beamBars} 支，箍筋約 {stirrupCount} 道，接頭模式：{GetConnectionModeText(options.ConnectionMode)}";
        }

        private bool TryReadOptions(out RebarPlacementOptions options, out string error)
        {
            options = null;
            error = string.Empty;

            if (!TryReadDouble(TxtCover, "保護層", out double cover, 1, 300) ||
                !TryReadDouble(TxtNormalSpacing, "一般區間距", out double normalSpacing, 1, 1000) ||
                !TryReadDouble(TxtDenseSpacing, "端部加密間距", out double denseSpacing, 1, 1000) ||
                !TryReadDouble(TxtDenseLength, "加密長度", out double denseLength, 0, 20000) ||
                !TryReadDouble(TxtStartOffset, "首支箍筋離端部", out double startOffset, 0, 1000) ||
                !TryReadDouble(TxtMaxSegmentLength, "最大單支長度", out double maxSegmentLength, 100, 30000) ||
                !TryReadDouble(TxtLapLength, "搭接長度", out double lapLength, 0, 5000) ||
                !TryReadDouble(TxtCouplerGap, "續接器避讓間隙", out double couplerGap, 0, 1000) ||
                !TryReadDouble(TxtAnchorage, "端部錨定外伸", out double anchorage, 0, 5000) ||
                !TryReadInt(TxtTopCount, LblTopCount.Text, out int topCount, 1, 20) ||
                !TryReadInt(TxtBottomCount, LblBottomCount.Text, out int bottomCount, 1, 20) ||
                !TryReadInt(TxtSideCount, "側筋 / 腰筋支數", out int sideCount, 0, 20))
            {
                error = _lastReadError;
                return false;
            }

            options = new RebarPlacementOptions
            {
                TopBarDbMm = ReadComboTag(ComboTopSize, 25.4),
                BottomBarDbMm = ReadComboTag(ComboBottomSize, 25.4),
                StirrupDbMm = ReadComboTag(ComboStirrupSize, 12.7),
                TopBarCount = topCount,
                BottomBarCount = bottomCount,
                SideBarCount = sideCount,
                CoverMm = cover,
                NormalSpacingMm = normalSpacing,
                DenseSpacingMm = denseSpacing,
                DenseLengthMm = denseLength,
                StartOffsetMm = startOffset,
                MaxSegmentLengthMm = maxSegmentLength,
                LapLengthMm = lapLength,
                CouplerGapMm = couplerGap,
                AnchorageMm = anchorage,
                ConnectionMode = (RebarConnectionMode)Math.Max(0, ComboConnectionMode.SelectedIndex),
                EndMode = (RebarEndMode)Math.Max(0, ComboEndMode.SelectedIndex),
                StirrupStyle = (ComboStirrupStyle.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "封閉箍"
            };

            return true;
        }

        private string _lastReadError;

        private bool TryReadDouble(TextBox box, string label, out double value, double min, double max)
        {
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
                !double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                _lastReadError = $"{label} 必須是數字。";
                return false;
            }

            if (value < min || value > max)
            {
                _lastReadError = $"{label} 必須介於 {min:0} 到 {max:0}。";
                return false;
            }

            return true;
        }

        private bool TryReadInt(TextBox box, string label, out int value, int min, int max)
        {
            if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
            {
                _lastReadError = $"{label} 必須是整數。";
                return false;
            }

            if (value < min || value > max)
            {
                _lastReadError = $"{label} 必須介於 {min} 到 {max}。";
                return false;
            }

            return true;
        }

        private static double ReadComboTag(ComboBox combo, double fallback)
        {
            if (combo?.SelectedItem is ComboBoxItem item &&
                item.Tag != null &&
                double.TryParse(item.Tag.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            return fallback;
        }

        private static IEnumerable<double> EvenPositions(double min, double max, int count)
        {
            if (count <= 1)
            {
                yield return (min + max) / 2;
                yield break;
            }

            for (int i = 0; i < count; i++)
            {
                yield return min + (max - min) * i / (count - 1);
            }
        }

        private static List<Point> GetColumnBarPoints(double xMin, double xMax, double yMin, double yMax, int xFaceCount, int yFaceCount)
        {
            var points = new List<Point>();
            foreach (double x in EvenPositions(xMin, xMax, Math.Max(2, xFaceCount)))
            {
                points.Add(new Point(x, yMin));
                points.Add(new Point(x, yMax));
            }

            foreach (double y in EvenPositions(yMin, yMax, Math.Max(2, yFaceCount)).Skip(1).Take(Math.Max(0, yFaceCount - 2)))
            {
                points.Add(new Point(xMin, y));
                points.Add(new Point(xMax, y));
            }

            return points
                .GroupBy(p => $"{p.X:0.###},{p.Y:0.###}")
                .Select(g => g.First())
                .ToList();
        }

        private static List<double> GetStirrupPositions(double lengthMm, RebarPlacementOptions options)
        {
            var values = new List<double>();
            double pos = Math.Max(0, options.StartOffsetMm);
            double end = Math.Max(0, lengthMm - options.StartOffsetMm);
            int guard = 0;
            while (pos <= end && guard++ < 2000)
            {
                values.Add(pos);
                bool dense = pos <= options.DenseLengthMm || pos >= lengthMm - options.DenseLengthMm;
                pos += dense ? options.DenseSpacingMm : options.NormalSpacingMm;
            }

            return values;
        }

        private static string GetConnectionModeText(RebarConnectionMode mode)
        {
            switch (mode)
            {
                case RebarConnectionMode.LapSplice:
                    return "搭接分段";
                case RebarConnectionMode.Coupler:
                    return "續接器分段";
                default:
                    return "連續鋼筋";
            }
        }

        private void SetupCamera()
        {
            PreviewViewport.Camera = new PerspectiveCamera
            {
                Position = new Point3D(360, -520, 330),
                LookDirection = new Vector3D(-360, 520, -300),
                UpDirection = new Vector3D(0, 0, 1),
                FieldOfView = 45
            };
        }

        private void AddLights()
        {
            var group = new Model3DGroup();
            group.Children.Add(new AmbientLight(Color.FromRgb(80, 88, 105)));
            group.Children.Add(new DirectionalLight(Color.FromRgb(235, 240, 250), new Vector3D(-0.45, 0.65, -0.6)));
            group.Children.Add(new DirectionalLight(Color.FromRgb(120, 190, 255), new Vector3D(0.35, -0.3, -0.7)));
            PreviewViewport.Children.Add(new ModelVisual3D { Content = group });
        }

        private void AddBarBox(Point3D center, double sx, double sy, double sz, Color color)
        {
            AddBox(center, Math.Max(sx, 1.2), Math.Max(sy, 1.2), Math.Max(sz, 1.2), color, color);
        }

        private void AddBox(Point3D center, double sx, double sy, double sz, Color fill, Color specular)
        {
            var mesh = CreateBoxMesh(center, sx, sy, sz);
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(new SolidColorBrush(fill)));
            material.Children.Add(new SpecularMaterial(new SolidColorBrush(specular), 26));

            var model = new GeometryModel3D
            {
                Geometry = mesh,
                Material = material,
                BackMaterial = material
            };

            PreviewViewport.Children.Add(new ModelVisual3D { Content = model });
        }

        private static MeshGeometry3D CreateBoxMesh(Point3D c, double sx, double sy, double sz)
        {
            double x = sx / 2;
            double y = sy / 2;
            double z = sz / 2;

            var p = new[]
            {
                new Point3D(c.X - x, c.Y - y, c.Z - z),
                new Point3D(c.X + x, c.Y - y, c.Z - z),
                new Point3D(c.X + x, c.Y + y, c.Z - z),
                new Point3D(c.X - x, c.Y + y, c.Z - z),
                new Point3D(c.X - x, c.Y - y, c.Z + z),
                new Point3D(c.X + x, c.Y - y, c.Z + z),
                new Point3D(c.X + x, c.Y + y, c.Z + z),
                new Point3D(c.X - x, c.Y + y, c.Z + z)
            };

            var mesh = new MeshGeometry3D();
            foreach (var point in p)
            {
                mesh.Positions.Add(point);
            }

            int[] triangles =
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1,
                1, 5, 6, 1, 6, 2,
                2, 6, 7, 2, 7, 3,
                3, 7, 4, 3, 4, 0
            };

            foreach (int index in triangles)
            {
                mesh.TriangleIndices.Add(index);
            }

            return mesh;
        }
    }
}
