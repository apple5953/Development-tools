using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DevelopmentTools.FastRebar.Saving;

namespace DevelopmentTools.FastRebar
{
    /// <summary>
    /// 依斷面與規範觀念替每支構件算出配筋，套進快速配筋引擎使用的樣板，並寫出配筋計算書。
    /// 構件的做法（穿柱或錨定、箍筋型式、彎鉤、保護層）沿用使用者在快速配筋視窗選的樣板。
    /// </summary>
    public class RuleDesignApplier
    {
        private const double Mm = 1.0 / 304.8;
        public const string SourceTag = "RTS:規範觀念";

        private readonly Document _doc;
        private readonly DesignOptions _o;
        private readonly FastRebarPreset _preset;
        private readonly BeamRebarSettings _baseBeam;
        private readonly ColumnRebarSettings _baseColumn;
        private readonly List<BarSize> _bars;
        private readonly List<BoundingBoxXYZ> _columnBoxes;
        private readonly StringBuilder _sheet = new StringBuilder();

        public int Members { get; private set; }
        public List<string> Notes { get; } = new List<string>();

        public RuleDesignApplier(Document doc, FastRebarPreset preset, DesignOptions options)
        {
            _doc = doc;
            _o = options;
            _preset = preset;
            FastRebarPreset copy = preset.Clone();
            _baseBeam = copy.Beam;
            _baseColumn = copy.Column;
            _bars = FastRebarEngine.BarTypes(doc).Select(t => new BarSize(t.Name, t.BarNominalDiameter / Mm)).ToList();
            _columnBoxes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType().Select(e => e.get_BoundingBox(null)).Where(b => b != null).ToList();
            _sheet.AppendLine("構件,編號,樓層,斷面 (mm),長度或高度 (mm),配筋來源,主筋,上層／長邊,下層／短邊,箍筋,端部或圍束區間距 (mm),中段間距 (mm),圍束區長度 (mm),主筋延伸 (mm),備註");
        }

        /// <summary>算出這支構件的配筋並寫進樣板；之後呼叫 BuildBeam／BuildColumn 即套用。</summary>
        public void Prepare(MemberFrame f)
        {
            Members++;
            if (f.Kind == MemberKind.Beam) PrepareBeam(f);
            else PrepareColumn(f);
        }

        /// <summary>還原使用者原本的樣板。</summary>
        public void Restore()
        {
            _preset.Beam = Clone(_baseBeam);
            _preset.Column = Clone(_baseColumn);
        }

        private void PrepareBeam(MemberFrame f)
        {
            double b = f.LenY / Mm, h = f.LenZ / Mm;
            BeamDesign d = RuleDesigner.Beam(b, h, _bars, _o);
            BeamRebarSettings s = Clone(_baseBeam);
            s.MainBarType = d.Main.Name;
            s.StirrupBarType = d.Stirrup.Name;
            s.TopLayers = d.TopLayers;
            s.BottomLayers = d.BottomLayers;
            s.LayerClearMm = d.LayerClearMm;
            s.SideBarsPerFace = d.SideBarsPerFace;
            if (d.Side != null) s.SideBarType = d.Side.Name;
            s.EndZoneDepthFactor = d.EndZoneDepthFactor;
            s.EndZoneSpacingMm = d.EndZoneSpacingMm;
            s.MidSpacingMm = d.MidSpacingMm;
            s.EndZonesOnly = false;
            _preset.Beam = s;

            Row(f, $"{b:0}×{h:0}", f.LenX / Mm, d.Main.Name,
                string.Join("/", d.TopLayers) + " 支", string.Join("/", d.BottomLayers) + " 支",
                d.Stirrup.Name, d.EndZoneSpacingMm, d.MidSpacingMm, 2 * h, s.ExtendPastSupportMm,
                (d.SideBarsPerFace > 0 ? $"腰筋每側 {d.SideBarsPerFace}-{d.Side.Name}；" : "") + string.Join("；", d.Notes));
        }

        private void PrepareColumn(MemberFrame f)
        {
            double a = f.LenX / Mm, b = f.LenY / Mm, height = f.LenZ / Mm;
            ColumnDesign d = RuleDesigner.Column(a, b, height, _bars, _o);
            ColumnRebarSettings s = Clone(_baseColumn);
            s.MainBarType = d.Main.Name;
            s.TieBarType = d.Tie.Name;
            s.BarsLongFace = d.BarsLongFace;
            s.BarsShortFace = d.BarsShortFace;
            s.ConfinedZoneMinMm = d.ConfinedZoneMinMm;
            s.ConfinedSpacingMm = d.ConfinedSpacingMm;
            s.MidSpacingMm = d.MidSpacingMm;
            s.JointSpacingMm = d.ConfinedSpacingMm;
            s.TieEveryIntermediateBar = d.TieEveryIntermediateBar;

            // 上面還有柱才往上延伸搭接；D36 以上不得搭接，停在柱頂、改用續接器
            var notes = new List<string>(d.Notes);
            if (!HasColumnAbove(f.Element)) s.ExtendAboveTopMm = 0;
            else if (!RebarCode.LapAllowed(d.Main.DiameterMm))
            {
                s.ExtendAboveTopMm = 0;
                notes.Add($"{d.Main.Name} 不得搭接，上層柱需用續接器");
            }
            else s.ExtendAboveTopMm = Math.Ceiling(RebarCode.LapClassBMm(d.Main.DiameterMm, _o.FcKgf, _o.FyKgf, false) / 10) * 10;
            _preset.Column = s;

            double rho = d.TotalBars * d.Main.AreaMm2 / (a * b);
            notes.Insert(0, "配筋率 " + rho.ToString("P2", CultureInfo.InvariantCulture));
            Row(f, $"{Math.Max(a, b):0}×{Math.Min(a, b):0}", height, $"{d.TotalBars}-{d.Main.Name}",
                $"每面 {d.BarsLongFace} 支", $"每面 {d.BarsShortFace} 支", d.Tie.Name,
                d.ConfinedSpacingMm, d.MidSpacingMm, d.ConfinedZoneMinMm, s.ExtendAboveTopMm,
                (d.TieEveryIntermediateBar ? "每支中間主筋加繫筋；" : "隔支加繫筋；") + string.Join("；", notes));
        }

        private bool HasColumnAbove(Element column)
        {
            BoundingBoxXYZ me = column.get_BoundingBox(null);
            if (me == null) return false;
            double tol = 100 * Mm;
            return _columnBoxes.Any(o =>
                Math.Abs(o.Min.Z - me.Max.Z) < tol &&
                o.Min.X < me.Max.X - tol && o.Max.X > me.Min.X + tol &&
                o.Min.Y < me.Max.Y - tol && o.Max.Y > me.Min.Y + tol);
        }

        /// <summary>在剛建立的鋼筋「註解」寫上配筋來源。</summary>
        public static void Tag(Document doc, IEnumerable<ElementId> ids, string source)
        {
            foreach (ElementId id in ids)
            {
                try
                {
                    Parameter p = doc.GetElement(id)?.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (p != null && !p.IsReadOnly) p.Set(source);
                }
                catch { }
            }
        }

        private void Row(MemberFrame f, string section, double lengthMm, string main, string first, string second, string tie,
                         double endSpacing, double midSpacing, double zone, double extend, string note)
        {
            Element e = f.Element;
            string mark = Mark(e);
            string kind = f.Kind == MemberKind.Beam ? "梁" : "柱";
            _sheet.AppendLine(string.Join(",", new[]
            {
                kind, mark, RebarQuantityReport.LevelName(_doc, e), section, lengthMm.ToString("0"), "規範觀念",
                main, first, second, tie, endSpacing.ToString("0"), midSpacing.ToString("0"), zone.ToString("0"), extend.ToString("0"), note
            }.Select(Csv)));
        }

        /// <summary>構件編號：名稱編號 → 標記 → 類型名稱，最後附元件 ID。</summary>
        internal static string Mark(Element e)
        {
            string v = e.LookupParameter("名稱編號")?.AsString();
            if (string.IsNullOrWhiteSpace(v)) v = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            if (string.IsNullOrWhiteSpace(v)) v = e.Name;
            return $"{v}（{e.Id.IntegerValue}）";
        }

        private static string Csv(string v)
        {
            if (v == null) return "";
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? v : "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        public string WriteSheet(string projectName)
        {
            try
            {
                string dir = Path.GetDirectoryName(FastRebarPresetStore.FilePath);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"配筋計算書_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var head = new StringBuilder();
                head.AppendLine("配筋計算書（依斷面與規範觀念推估，非結構計算）," + Csv(projectName));
                head.AppendLine($"fc′={_o.FcKgf:0} kgf/cm²,fy={_o.FyKgf:0} kgf/cm²,梁上層 {_o.BeamTopRatio:P2},梁下層 {_o.BeamBottomRatio:P2},柱 {_o.ColumnRatio:P2}");
                head.AppendLine("梁：主筋取目標配筋率與 max(0.8√fc′/fy, 14/fy) 較大者，下層不少於上層一半；端部 2h 箍距 ≤ d/4、8db、24 倍箍筋直徑、300 mm，中段 ≤ d/2；梁深 > 900 mm 加腰筋");
                head.AppendLine("柱：配筋率 1%～6%，主筋淨距 ≥ 1.5db 且 ≥ 40 mm；圍束區長度 ≥ 長邊、淨高/6、450 mm；圍束區箍距 ≤ 短邊/4、6db、100+(350−hx)/3；上層有柱時主筋延伸受拉乙級搭接長度");
                head.AppendLine();
                File.WriteAllText(path, head.ToString() + _sheet, new UTF8Encoding(true));
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static T Clone<T>(T value)
        {
            var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, value);
                ms.Position = 0;
                return (T)ser.ReadObject(ms);
            }
        }
    }
}
