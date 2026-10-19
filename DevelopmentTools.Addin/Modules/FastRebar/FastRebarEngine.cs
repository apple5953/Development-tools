using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace DevelopmentTools.FastRebar
{
    public enum MemberKind { Beam, Column }

    /// <summary>
    /// 構件的局部座標：梁 X = 梁軸、Z = 上；柱 X/Y = 斷面方向、Z = 上。
    /// Min/Max 是實體幾何投影到局部軸的範圍（梁被柱切過時 X 範圍就是淨跨）。
    /// </summary>
    public class MemberFrame
    {
        public MemberKind Kind;
        public Element Element;
        public XYZ Origin, X, Y, Z;
        public double MinX, MaxX, MinY, MaxY, MinZ, MaxZ;

        public double LenX => MaxX - MinX;
        public double LenY => MaxY - MinY;
        public double LenZ => MaxZ - MinZ;

        public XYZ ToWorld(double x, double y, double z) => Origin + X * x + Y * y + Z * z;

        public void Project(IEnumerable<XYZ> points, out double minX, out double maxX, out double minY, out double maxY, out double minZ, out double maxZ)
        {
            minX = minY = minZ = double.PositiveInfinity;
            maxX = maxY = maxZ = double.NegativeInfinity;
            foreach (XYZ p in points)
            {
                XYZ v = p - Origin;
                double x = v.DotProduct(X), y = v.DotProduct(Y), z = v.DotProduct(Z);
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
        }

        /// <summary>斷面中心線（梁沿 X、柱沿 Z），用來判斷彎鉤是否朝內。</summary>
        public void CentreAxis(out XYZ point, out XYZ direction)
        {
            if (Kind == MemberKind.Beam)
            {
                point = ToWorld(0, (MinY + MaxY) / 2, (MinZ + MaxZ) / 2);
                direction = X;
            }
            else
            {
                point = ToWorld((MinX + MaxX) / 2, (MinY + MaxY) / 2, 0);
                direction = Z;
            }
        }

        public static MemberFrame TryCreate(Element e, out string reason)
        {
            reason = null;
            List<XYZ> pts = GeometryPoints.Collect(e);
            if (pts.Count < 4)
            {
                reason = "讀不到實體幾何";
                return null;
            }

            int cat = e.Category?.Id.IntegerValue ?? 0;
            var f = new MemberFrame { Element = e };
            if (cat == (int)BuiltInCategory.OST_StructuralFraming)
            {
                if (!(e.Location is LocationCurve lc) || !(lc.Curve is Line line))
                {
                    reason = "梁的定位線不是直線（弧形梁未支援）";
                    return null;
                }
                XYZ dir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
                if (Math.Abs(dir.Z) > 0.2)
                {
                    reason = "斜梁未支援";
                    return null;
                }
                f.Kind = MemberKind.Beam;
                f.X = new XYZ(dir.X, dir.Y, 0).Normalize();
                f.Z = XYZ.BasisZ;
                f.Y = f.Z.CrossProduct(f.X).Normalize();
                f.Origin = line.GetEndPoint(0);
            }
            else if (cat == (int)BuiltInCategory.OST_StructuralColumns)
            {
                if (e.Location is LocationCurve colCurve)
                {
                    XYZ d = (colCurve.Curve.GetEndPoint(1) - colCurve.Curve.GetEndPoint(0)).Normalize();
                    if (Math.Abs(d.Z) < 0.999)
                    {
                        reason = "斜柱未支援";
                        return null;
                    }
                }
                XYZ bx = XYZ.BasisX;
                if (e is FamilyInstance fi)
                {
                    XYZ t = fi.GetTransform().BasisX;
                    if (new XYZ(t.X, t.Y, 0).GetLength() > 1e-6) bx = new XYZ(t.X, t.Y, 0).Normalize();
                }
                f.Kind = MemberKind.Column;
                f.X = bx;
                f.Z = XYZ.BasisZ;
                f.Y = f.Z.CrossProduct(f.X).Normalize();
                f.Origin = (e.Location as LocationPoint)?.Point ?? pts[0];
            }
            else
            {
                reason = "不是結構梁或結構柱";
                return null;
            }

            f.Project(pts, out f.MinX, out f.MaxX, out f.MinY, out f.MaxY, out f.MinZ, out f.MaxZ);
            if (f.LenX < 1e-3 || f.LenY < 1e-3 || f.LenZ < 1e-3)
            {
                reason = "幾何尺寸異常";
                return null;
            }
            return f;
        }
    }

    internal static class GeometryPoints
    {
        public static List<XYZ> Collect(Element e)
        {
            var list = new List<XYZ>();
            var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
            GeometryElement ge = e.get_Geometry(opt);
            if (ge != null) Walk(ge, list);
            return list;
        }

        private static void Walk(GeometryElement ge, List<XYZ> list)
        {
            foreach (GeometryObject obj in ge)
            {
                if (obj is Solid s && s.Volume > 1e-9)
                {
                    foreach (Edge edge in s.Edges)
                    {
                        list.AddRange(edge.Tessellate());
                    }
                }
                else if (obj is GeometryInstance gi)
                {
                    GeometryElement inst = gi.GetInstanceGeometry();
                    if (inst != null) Walk(inst, list);
                }
            }
        }
    }

    /// <summary>一段等距排列：起點、間距、支數。</summary>
    public struct Run
    {
        public double Start;
        public double Spacing;
        public int Count;
        public Run(double start, double spacing, int count) { Start = start; Spacing = spacing; Count = count; }
        public double End => Start + Spacing * Math.Max(0, Count - 1);
    }

    public static class Stations
    {
        /// <summary>梁箍筋：兩端加密區、中段一般間距（全部單位 ft，局部 X）。</summary>
        public static List<Run> ForBeam(double clearStart, double clearEnd, double firstOffset, double endZone, double sEnd, double sMid, bool endZonesOnly)
        {
            var runs = new List<Run>();
            double span = clearEnd - clearStart;
            if (span <= 2 * firstOffset || sEnd <= 0) return runs;

            double a = clearStart + firstOffset;
            double b = clearEnd - firstOffset;

            if (2 * endZone >= span)
            {
                // 跨度短於兩個加密區：全跨用加密間距
                int n = (int)Math.Floor((b - a) / sEnd + 1e-6) + 1;
                runs.Add(new Run(a, n > 1 ? (b - a) / (n - 1) : sEnd, n));
                return runs;
            }

            int nEnd = (int)Math.Floor((endZone - firstOffset) / sEnd + 1e-6) + 1;
            nEnd = Math.Max(1, nEnd);
            var left = new Run(a, sEnd, nEnd);
            var right = new Run(b - sEnd * (nEnd - 1), sEnd, nEnd);
            runs.Add(left);

            if (!endZonesOnly && sMid > 0)
            {
                double gapStart = left.End;
                double gapEnd = right.Start;
                int gaps = (int)Math.Ceiling((gapEnd - gapStart) / sMid - 1e-6);
                if (gaps >= 2)
                {
                    double s = (gapEnd - gapStart) / gaps;
                    runs.Add(new Run(gapStart + s, s, gaps - 1));
                }
            }

            runs.Add(right);
            return runs;
        }

        /// <summary>柱箍筋：下端圍束區、中段、上端圍束區、梁柱接頭區（單位 ft，局部 Z）。</summary>
        public static List<Run> ForColumn(double bottom, double jointBottom, double top, double firstOffset, double confined, double sConf, double sMid, double sJoint)
        {
            var runs = new List<Run>();
            double clearTop = jointBottom;
            double a = bottom + firstOffset;
            double b = clearTop - firstOffset;
            if (b > a)
            {
                runs.AddRange(ForBeam(bottom, clearTop, firstOffset, confined, sConf, sMid, false));
            }

            if (top - jointBottom > 2 * firstOffset && sJoint > 0)
            {
                double ja = jointBottom + firstOffset;
                double jb = top - firstOffset;
                int n = (int)Math.Floor((jb - ja) / sJoint + 1e-6) + 1;
                runs.Add(new Run(ja, n > 1 ? (jb - ja) / (n - 1) : sJoint, Math.Max(1, n)));
            }
            return runs;
        }
    }

    public class FastRebarResult
    {
        public int MembersDone;
        public int RebarSetsCreated;
        public int HooksFlipped;
        public int LayoutsFlipped;
        public int WarningsSuppressed;
        public int ExistingDeleted;
        public List<string> Messages = new List<string>();
        public List<ElementId> Created = new List<ElementId>();
        public List<ElementId> Problem = new List<ElementId>();
    }

    /// <summary>
    /// 依樣板替一批梁柱建立鋼筋。所有鋼筋以「區段 Set」建立（一個加密區一組），
    /// 建立後再統一檢查：排列方向與彎鉤朝向（彎鉤尖端要朝斷面內側），不對就自動翻轉。
    /// </summary>
    public class FastRebarEngine
    {
        private const double Mm = 1.0 / 304.8;

        private readonly Document _doc;
        private readonly FastRebarPreset _p;
        private readonly FastRebarResult _r = new FastRebarResult();
        private readonly List<Pending> _pending = new List<Pending>();

        private RebarHookType _hook135;
        private RebarHookType _hook90Tie;
        private RebarHookType _hook90Main;

        private class Pending
        {
            public Rebar Bar;
            public MemberFrame Frame;
            public XYZ DistDir;
            public bool BarsOnNormalSide;
            public int Count;
            public double Spacing;      // >0：NumberWithSpacing；=0：FixedNumber
            public double ArrayLength;
        }

        public FastRebarEngine(Document doc, FastRebarPreset preset)
        {
            _doc = doc;
            _p = preset;
        }

        public FastRebarResult Result => _r;

        // ───────────────────────── 型號與彎鉤 ─────────────────────────

        public static List<RebarBarType> BarTypes(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .OrderBy(t => t.BarNominalDiameter).ToList();
        }

        public static List<RebarHookType> HookTypes(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                .OrderBy(h => h.Name).ToList();
        }

        public static RebarBarType FindBarType(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var types = BarTypes(doc);
            RebarBarType t = types.FirstOrDefault(x => string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (t != null) return t;

            // 名稱不同（例如樣板寫 32M、專案用 D32）時改用數字比對標稱直徑
            string digits = new string(name.Where(char.IsDigit).ToArray());
            if (double.TryParse(digits, out double mm) && mm > 0)
            {
                return types.OrderBy(x => Math.Abs(x.BarNominalDiameter / Mm - mm)).FirstOrDefault(x => Math.Abs(x.BarNominalDiameter / Mm - mm) < 2.5);
            }
            return null;
        }

        private RebarHookType FindHook(string preferredName, double angleDeg, RebarStyle style, bool seismic)
        {
            var hooks = HookTypes(_doc);
            if (!string.IsNullOrWhiteSpace(preferredName))
            {
                var byName = hooks.FirstOrDefault(h => h.Name == preferredName);
                if (byName != null) return byName;
            }

            var candidates = hooks.Where(h => Math.Abs(h.HookAngle * 180 / Math.PI - angleDeg) < 1.0 && h.Style == style).ToList();
            if (seismic)
            {
                var s = candidates.FirstOrDefault(h => h.Name.Contains("地震") || h.Name.IndexOf("seismic", StringComparison.OrdinalIgnoreCase) >= 0);
                if (s != null) return s;
            }
            if (candidates.Count > 0) return candidates[0];

            try
            {
                RebarHookType created = RebarHookType.Create(_doc, angleDeg * Math.PI / 180, angleDeg > 100 ? 6 : 12);
                created.Style = style;
                try { created.Name = $"快速配筋 {angleDeg:0}°" + (style == RebarStyle.StirrupTie ? " 箍筋" : ""); } catch { }
                _r.Messages.Add($"專案沒有 {angleDeg:0}° 彎鉤，已建立「{created.Name}」。");
                return created;
            }
            catch (Exception ex)
            {
                _r.Messages.Add($"找不到也無法建立 {angleDeg:0}° 彎鉤：{ex.Message}");
                return null;
            }
        }

        private void EnsureHooks()
        {
            if (_hook135 == null) _hook135 = FindHook(_p.Hook135Name, 135, RebarStyle.StirrupTie, true);
            if (_hook90Tie == null) _hook90Tie = FindHook(_p.Hook90TieName, 90, RebarStyle.StirrupTie, false);
            if (_hook90Main == null) _hook90Main = FindHook(_p.Hook90MainName, 90, RebarStyle.Standard, false);
        }

        // ───────────────────────── 共用 ─────────────────────────

        private double Cover(Element host, BuiltInParameter bip)
        {
            if (_p.CoverOverrideMm > 0) return _p.CoverOverrideMm * Mm;
            try
            {
                Parameter prm = host.get_Parameter(bip);
                if (prm != null && prm.StorageType == StorageType.ElementId)
                {
                    if (_doc.GetElement(prm.AsElementId()) is RebarCoverType ct) return ct.CoverDistance;
                }
            }
            catch { }
            return 40 * Mm;
        }

        public static IList<Rebar> ExistingRebar(Element host)
        {
            try
            {
                RebarHostData data = RebarHostData.GetRebarHostData(host);
                if (data != null) return data.GetRebarsInHost();
            }
            catch { }
            return new List<Rebar>();
        }

        private Rebar Create(RebarStyle style, RebarBarType type, RebarHookType startHook, RebarHookType endHook,
                             MemberFrame f, XYZ normal, IList<Curve> curves, string what)
        {
            try
            {
                Rebar bar = Rebar.CreateFromCurves(_doc, style, type, startHook, endHook, f.Element, normal, curves,
                    RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);
                if (bar == null)
                {
                    _r.Messages.Add($"{Describe(f)}：{what} 建立失敗（Revit 回傳空值）。");
                    return null;
                }
                _r.RebarSetsCreated++;
                _r.Created.Add(bar.Id);
                return bar;
            }
            catch (Exception ex)
            {
                _r.Messages.Add($"{Describe(f)}：{what} 建立失敗：{ex.Message}");
                if (!_r.Problem.Contains(f.Element.Id)) _r.Problem.Add(f.Element.Id);
                return null;
            }
        }

        /// <summary>把單支鋼筋展開成 Set。spacing &gt; 0 用「支數＋間距」，否則用「固定支數＋總長」。</summary>
        private void Layout(Rebar bar, MemberFrame f, XYZ distDir, int count, double spacing, double arrayLength)
        {
            if (bar == null) return;
            var pend = new Pending { Bar = bar, Frame = f, DistDir = distDir, BarsOnNormalSide = true, Count = count, Spacing = spacing, ArrayLength = arrayLength };
            if (count > 1) ApplyLayout(pend);
            _pending.Add(pend);
        }

        private static void ApplyLayout(Pending p)
        {
            RebarShapeDrivenAccessor acc = p.Bar.GetShapeDrivenAccessor();
            if (p.Spacing > 0)
                acc.SetLayoutAsNumberWithSpacing(p.Count, p.Spacing, p.BarsOnNormalSide, true, true);
            else
                acc.SetLayoutAsFixedNumber(p.Count, p.ArrayLength, p.BarsOnNormalSide, true, true);
        }

        private static string Describe(MemberFrame f)
        {
            string kind = f.Kind == MemberKind.Beam ? "梁" : "柱";
            return $"{kind} {f.Element.Name}（{f.Element.Id.IntegerValue}）";
        }

        private static Line L(XYZ a, XYZ b) => Line.CreateBound(a, b);

        private static List<Curve> Poly(params XYZ[] pts)
        {
            var list = new List<Curve>();
            for (int i = 0; i < pts.Length - 1; i++) list.Add(L(pts[i], pts[i + 1]));
            return list;
        }

        // ───────────────────────── 梁 ─────────────────────────

        private class Support
        {
            public double Min, Max;   // 柱在梁局部 X 的範圍
        }

        private void FindSupports(MemberFrame beam, out Support start, out Support end)
        {
            start = null;
            end = null;
            BoundingBoxXYZ bb = beam.Element.get_BoundingBox(null);
            if (bb == null) return;
            double pad = 300 * Mm;
            var outline = new Outline(bb.Min - new XYZ(pad, pad, pad), bb.Max + new XYZ(pad, pad, pad));
            var cols = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .ToElements();

            double mid = (beam.MinX + beam.MaxX) / 2;
            double tol = 60 * Mm;
            foreach (Element c in cols)
            {
                var pts = GeometryPoints.Collect(c);
                if (pts.Count == 0) continue;
                beam.Project(pts, out double cx0, out double cx1, out double cy0, out double cy1, out double cz0, out double cz1);
                // 柱要和梁斷面重疊（Y、Z 方向）才算支承
                if (cy1 < beam.MinY - tol || cy0 > beam.MaxY + tol) continue;
                if (cz1 < beam.MinZ - tol || cz0 > beam.MaxZ + tol) continue;
                double centre = (cx0 + cx1) / 2;
                if (centre < mid && cx1 >= beam.MinX - tol)
                {
                    if (start == null || cx1 > start.Max) start = new Support { Min = cx0, Max = cx1 };
                }
                else if (centre >= mid && cx0 <= beam.MaxX + tol)
                {
                    if (end == null || cx0 < end.Min) end = new Support { Min = cx0, Max = cx1 };
                }
            }
        }

        public void BuildBeam(MemberFrame f)
        {
            BeamRebarSettings s = _p.Beam;
            RebarBarType main = FindBarType(_doc, s.MainBarType);
            RebarBarType stir = FindBarType(_doc, s.StirrupBarType);
            if (main == null || stir == null)
            {
                _r.Messages.Add($"{Describe(f)}：找不到鋼筋型號（主筋 {s.MainBarType}／箍筋 {s.StirrupBarType}），已略過。");
                _r.Problem.Add(f.Element.Id);
                return;
            }
            EnsureHooks();

            double cTop = Cover(f.Element, BuiltInParameter.CLEAR_COVER_TOP);
            double cBot = Cover(f.Element, BuiltInParameter.CLEAR_COVER_BOTTOM);
            double cSide = Cover(f.Element, BuiltInParameter.CLEAR_COVER_OTHER);
            double db = main.BarNominalDiameter;
            double ds = stir.BarNominalDiameter;

            FindSupports(f, out Support sup0, out Support sup1);
            double clear0 = sup0 != null ? Math.Max(f.MinX, sup0.Max) : f.MinX;
            double clear1 = sup1 != null ? Math.Min(f.MaxX, sup1.Min) : f.MaxX;
            if (clear1 - clear0 < 300 * Mm)
            {
                _r.Messages.Add($"{Describe(f)}：淨跨小於 300 mm，已略過。");
                return;
            }

            // 錯層：讓位方向的梁，上層筋往下、下層筋往上各退一支主筋直徑
            bool isX = Math.Abs(f.X.DotProduct(XYZ.BasisX)) >= Math.Sqrt(0.5);
            double drop = 0;
            if ((s.CrossingYield == CrossingYield.XDirectionBeamsYield && isX) ||
                (s.CrossingYield == CrossingYield.YDirectionBeamsYield && !isX))
            {
                drop = db;
            }

            // ── 主筋 X 範圍與端部彎鉤
            double x0, x1;
            bool hook0, hook1;
            MainBarEnd(s, sup0, f.MinX, clear0, cSide, true, out x0, out hook0);
            MainBarEnd(s, sup1, f.MaxX, clear1, cSide, false, out x1, out hook1);

            double y0 = f.MinY + cSide + ds + db / 2;
            double y1 = f.MaxY - cSide - ds - db / 2;
            double zTop1 = f.MaxZ - cTop - ds - db / 2 - drop;
            double zBot1 = f.MinZ + cBot + ds + db / 2 + drop;
            double pitch = db + s.LayerClearMm * Mm;

            CheckClearSpacing(f, "上層", s.TopLayers, y1 - y0, db);
            CheckClearSpacing(f, "下層", s.BottomLayers, y1 - y0, db);

            for (int i = 0; i < s.TopLayers.Count; i++)
                MainLayer(f, main, s.TopLayers[i], x0, x1, y0, y1, zTop1 - i * pitch, hook0, hook1, $"上層第 {i + 1} 層主筋");
            for (int i = 0; i < s.BottomLayers.Count; i++)
                MainLayer(f, main, s.BottomLayers[i], x0, x1, y0, y1, zBot1 + i * pitch, hook0, hook1, $"下層第 {i + 1} 層主筋");

            // ── 腰筋
            if (s.SideBarsPerFace > 0)
            {
                RebarBarType side = FindBarType(_doc, s.SideBarType) ?? stir;
                double zA = zBot1 + (s.BottomLayers.Count - 1) * pitch;
                double zB = zTop1 - (s.TopLayers.Count - 1) * pitch;
                double step = (zB - zA) / (s.SideBarsPerFace + 1);
                double ySide0 = f.MinY + cSide + ds + side.BarNominalDiameter / 2;
                double ySide1 = f.MaxY - cSide - ds - side.BarNominalDiameter / 2;
                foreach (double y in new[] { ySide0, ySide1 })
                {
                    Rebar bar = Create(RebarStyle.Standard, side, null, null, f, f.Z,
                        new List<Curve> { L(f.ToWorld(clear0, y, zA + step), f.ToWorld(clear1, y, zA + step)) }, "腰筋");
                    Layout(bar, f, f.Z, s.SideBarsPerFace, step, 0);
                }
            }

            // ── 箍筋
            double ys0 = f.MinY + cSide + ds / 2, ys1 = f.MaxY - cSide - ds / 2;
            double zs0 = f.MinZ + cBot + ds / 2, zs1 = f.MaxZ - cTop - ds / 2;
            double depth = f.LenZ;
            var runs = Stations.ForBeam(clear0, clear1, s.FirstStirrupOffsetMm * Mm, s.EndZoneDepthFactor * depth,
                                        s.EndZoneSpacingMm * Mm, s.MidSpacingMm * Mm, s.EndZonesOnly);
            foreach (Run run in runs)
            {
                for (int k = 0; k < (s.DoubleStirrups ? 2 : 1); k++)
                {
                    double x = run.Start + k * ds;
                    List<Curve> curves;
                    if (s.StirrupForm == BeamStirrupForm.ClosedHoop)
                    {
                        curves = Poly(f.ToWorld(x, ys0, zs1), f.ToWorld(x, ys0, zs0), f.ToWorld(x, ys1, zs0),
                                      f.ToWorld(x, ys1, zs1), f.ToWorld(x, ys0, zs1));
                    }
                    else
                    {
                        curves = Poly(f.ToWorld(x, ys0, zs1), f.ToWorld(x, ys0, zs0), f.ToWorld(x, ys1, zs0), f.ToWorld(x, ys1, zs1));
                    }
                    Rebar st = Create(RebarStyle.StirrupTie, stir, _hook135, _hook135, f, f.X, curves,
                        s.StirrupForm == BeamStirrupForm.ClosedHoop ? "閉合箍" : "U 形箍");
                    Layout(st, f, f.X, run.Count, run.Spacing, 0);
                }

                if (s.StirrupForm == BeamStirrupForm.UWithCapTie)
                {
                    // 頂部繫筋：135° / 90° 逐組左右交替 → 兩組、各為兩倍間距
                    double xc = run.Start - ds;
                    int nA = (run.Count + 1) / 2, nB = run.Count / 2;
                    Rebar capA = Create(RebarStyle.StirrupTie, stir, _hook135, _hook90Tie, f, f.X,
                        new List<Curve> { L(f.ToWorld(xc, ys0, zs1), f.ToWorld(xc, ys1, zs1)) }, "頂部繫筋");
                    Layout(capA, f, f.X, nA, run.Spacing * 2, 0);
                    if (nB > 0)
                    {
                        double xb = xc + run.Spacing;
                        Rebar capB = Create(RebarStyle.StirrupTie, stir, _hook135, _hook90Tie, f, f.X,
                            new List<Curve> { L(f.ToWorld(xb, ys1, zs1), f.ToWorld(xb, ys0, zs1)) }, "頂部繫筋");
                        Layout(capB, f, f.X, nB, run.Spacing * 2, 0);
                    }
                }
            }

            _r.MembersDone++;
        }

        private void MainBarEnd(BeamRebarSettings s, Support sup, double memberEnd, double clearEnd, double cover, bool atStart, out double x, out bool hook)
        {
            double sign = atStart ? -1 : 1;   // 往構件外側的方向
            hook = false;
            if (sup == null)
            {
                x = memberEnd - sign * cover;
                hook = s.EndMode == BeamBarEndMode.AnchorInSupport;
                return;
            }
            double farFace = atStart ? sup.Min : sup.Max;
            switch (s.EndMode)
            {
                case BeamBarEndMode.ThroughSupport:
                    x = s.ExtendPastSupportMm > 0 ? farFace + sign * s.ExtendPastSupportMm * Mm : farFace - sign * cover;
                    break;
                case BeamBarEndMode.AnchorInSupport:
                    x = farFace - sign * (cover + 50 * Mm);
                    hook = true;
                    break;
                default:
                    x = clearEnd;
                    break;
            }
        }

        private void MainLayer(MemberFrame f, RebarBarType type, int n, double x0, double x1, double y0, double y1, double z,
                               bool hook0, bool hook1, string what)
        {
            if (n <= 0) return;
            double yFirst = n == 1 ? (y0 + y1) / 2 : y0;
            Rebar bar = Create(RebarStyle.Standard, type, hook0 ? _hook90Main : null, hook1 ? _hook90Main : null, f, f.Y,
                new List<Curve> { L(f.ToWorld(x0, yFirst, z), f.ToWorld(x1, yFirst, z)) }, what);
            Layout(bar, f, f.Y, n, 0, y1 - y0);
        }

        private void CheckClearSpacing(MemberFrame f, string label, List<int> layers, double width, double db)
        {
            int n = layers.Count > 0 ? layers.Max() : 0;
            if (n < 2) return;
            double clear = width / (n - 1) - db;
            double need = Math.Max(db, 25 * Mm);
            if (clear < need)
            {
                _r.Messages.Add($"{Describe(f)}：{label} {n} 支主筋淨距 {clear / Mm:0} mm，小於 {need / Mm:0} mm（db 與 25 mm 取大），請確認是否需要束筋或改層數。");
            }
        }

        // ───────────────────────── 柱 ─────────────────────────

        public void BuildColumn(MemberFrame f)
        {
            ColumnRebarSettings s = _p.Column;
            RebarBarType main = FindBarType(_doc, s.MainBarType);
            RebarBarType tie = FindBarType(_doc, s.TieBarType);
            if (main == null || tie == null)
            {
                _r.Messages.Add($"{Describe(f)}：找不到鋼筋型號（主筋 {s.MainBarType}／箍筋 {s.TieBarType}），已略過。");
                _r.Problem.Add(f.Element.Id);
                return;
            }
            EnsureHooks();

            double cSide = Cover(f.Element, BuiltInParameter.CLEAR_COVER_OTHER);
            double cTop = Cover(f.Element, BuiltInParameter.CLEAR_COVER_TOP);
            double cBot = Cover(f.Element, BuiltInParameter.CLEAR_COVER_BOTTOM);
            double db = main.BarNominalDiameter;
            double dt = tie.BarNominalDiameter;

            // 長邊、短邊的支數對應到局部 X/Y
            bool yIsLong = f.LenY >= f.LenX;
            int nAlongY = yIsLong ? s.BarsLongFace : s.BarsShortFace;   // x = 常數的兩個面上，沿 Y 排的支數
            int nAlongX = yIsLong ? s.BarsShortFace : s.BarsLongFace;   // y = 常數的兩個面上，沿 X 排的支數

            double bx0 = f.MinX + cSide + dt + db / 2, bx1 = f.MaxX - cSide - dt - db / 2;
            double by0 = f.MinY + cSide + dt + db / 2, by1 = f.MaxY - cSide - dt - db / 2;
            double zb = f.MinZ + cBot;
            double zt = s.ExtendAboveTopMm > 0 ? f.MaxZ + s.ExtendAboveTopMm * Mm : f.MaxZ - cTop;

            double clearX = (bx1 - bx0) / Math.Max(1, nAlongX - 1) - db;
            double clearY = (by1 - by0) / Math.Max(1, nAlongY - 1) - db;
            double need = Math.Max(1.5 * db, 40 * Mm);
            if (Math.Min(clearX, clearY) < need)
            {
                _r.Messages.Add($"{Describe(f)}：柱主筋淨距 {Math.Min(clearX, clearY) / Mm:0} mm，小於 {need / Mm:0} mm（1.5db 與 40 mm 取大）。");
            }

            // ── 主筋：y = 常數的兩面（含角筋）沿 X 排；x = 常數的兩面只放中間筋
            foreach (double y in new[] { by0, by1 })
            {
                Rebar bar = Create(RebarStyle.Standard, main, null, null, f, f.X,
                    new List<Curve> { L(f.ToWorld(bx0, y, zb), f.ToWorld(bx0, y, zt)) }, "柱主筋");
                Layout(bar, f, f.X, nAlongX, 0, bx1 - bx0);
            }
            int midY = nAlongY - 2;
            if (midY > 0)
            {
                double py = (by1 - by0) / (nAlongY - 1);
                foreach (double x in new[] { bx0, bx1 })
                {
                    Rebar bar = Create(RebarStyle.Standard, main, null, null, f, f.Y,
                        new List<Curve> { L(f.ToWorld(x, by0 + py, zb), f.ToWorld(x, by0 + py, zt)) }, "柱主筋");
                    Layout(bar, f, f.Y, midY, 0, py * (midY - 1));
                }
            }

            // ── 箍筋分區：下端圍束、中段、上端圍束、梁柱接頭
            double jointBottom = FindJointBottom(f);
            double clearH = jointBottom - f.MinZ;
            double lo = Math.Max(Math.Max(Math.Max(f.LenX, f.LenY), clearH / 6), s.ConfinedZoneMinMm * Mm);
            var runs = Stations.ForColumn(f.MinZ, jointBottom, f.MaxZ, s.FirstTieOffsetMm * Mm, lo,
                                          s.ConfinedSpacingMm * Mm, s.MidSpacingMm * Mm, s.JointSpacingMm * Mm);

            double hx0 = f.MinX + cSide + dt / 2, hx1 = f.MaxX - cSide - dt / 2;
            double hy0 = f.MinY + cSide + dt / 2, hy1 = f.MaxY - cSide - dt / 2;

            // 繫筋位置：每支（或隔支）中間主筋
            var tieAtX = new List<double>();   // 沿 Y 方向的繫筋，位於這些 x
            var tieAtY = new List<double>();   // 沿 X 方向的繫筋，位於這些 y
            double px = (bx1 - bx0) / Math.Max(1, nAlongX - 1);
            for (int i = 1; i < nAlongX - 1; i++)
                if (s.TieEveryIntermediateBar || i % 2 == 1) tieAtX.Add(bx0 + i * px);
            double pyy = (by1 - by0) / Math.Max(1, nAlongY - 1);
            for (int j = 1; j < nAlongY - 1; j++)
                if (s.TieEveryIntermediateBar || j % 2 == 1) tieAtY.Add(by0 + j * pyy);

            foreach (Run run in runs)
            {
                Rebar hoop = Create(RebarStyle.StirrupTie, tie, _hook135, _hook135, f, f.Z,
                    Poly(f.ToWorld(hx0, hy1, run.Start), f.ToWorld(hx0, hy0, run.Start), f.ToWorld(hx1, hy0, run.Start),
                         f.ToWorld(hx1, hy1, run.Start), f.ToWorld(hx0, hy1, run.Start)), "柱閉合箍");
                Layout(hoop, f, f.Z, run.Count, run.Spacing, 0);

                int nA = (run.Count + 1) / 2, nB = run.Count / 2;
                double zA = run.Start + dt;
                double zB = run.Start + run.Spacing + dt;
                foreach (double x in tieAtX)
                {
                    Rebar a = Create(RebarStyle.StirrupTie, tie, _hook135, _hook90Tie, f, f.Z,
                        new List<Curve> { L(f.ToWorld(x, hy0, zA), f.ToWorld(x, hy1, zA)) }, "柱繫筋");
                    Layout(a, f, f.Z, nA, run.Spacing * 2, 0);
                    if (nB > 0)
                    {
                        Rebar b = Create(RebarStyle.StirrupTie, tie, _hook135, _hook90Tie, f, f.Z,
                            new List<Curve> { L(f.ToWorld(x, hy1, zB), f.ToWorld(x, hy0, zB)) }, "柱繫筋");
                        Layout(b, f, f.Z, nB, run.Spacing * 2, 0);
                    }
                }
                foreach (double y in tieAtY)
                {
                    Rebar a = Create(RebarStyle.StirrupTie, tie, _hook135, _hook90Tie, f, f.Z,
                        new List<Curve> { L(f.ToWorld(hx0, y, zA), f.ToWorld(hx1, y, zA)) }, "柱繫筋");
                    Layout(a, f, f.Z, nA, run.Spacing * 2, 0);
                    if (nB > 0)
                    {
                        Rebar b = Create(RebarStyle.StirrupTie, tie, _hook135, _hook90Tie, f, f.Z,
                            new List<Curve> { L(f.ToWorld(hx1, y, zB), f.ToWorld(hx0, y, zB)) }, "柱繫筋");
                        Layout(b, f, f.Z, nB, run.Spacing * 2, 0);
                    }
                }
            }

            _r.MembersDone++;
        }

        /// <summary>柱頂有梁接入時，回傳最深那支梁的梁底（局部 Z）；沒有梁則回傳柱頂。</summary>
        private double FindJointBottom(MemberFrame col)
        {
            double result = col.MaxZ;
            BoundingBoxXYZ bb = col.Element.get_BoundingBox(null);
            if (bb == null) return result;
            double pad = 50 * Mm;
            var outline = new Outline(bb.Min - new XYZ(pad, pad, pad), bb.Max + new XYZ(pad, pad, pad));
            var beams = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .ToElements();
            foreach (Element b in beams)
            {
                var pts = GeometryPoints.Collect(b);
                if (pts.Count == 0) continue;
                col.Project(pts, out _, out _, out _, out _, out double z0, out double z1);
                // 只算梁頂接近柱頂的梁
                if (z1 >= col.MaxZ - 300 * Mm && z0 > col.MinZ + 300 * Mm && z0 < result) result = z0;
            }
            return result;
        }

        // ───────────────────────── 建立後檢查與自動修正 ─────────────────────────

        /// <summary>
        /// 必須在 Regenerate 之後呼叫。
        /// 1. Set 的排列方向不對（往構件外排）就改另一側。
        /// 2. 彎鉤尖端比彎鉤起點離斷面中心線更遠（朝外）就翻轉該端。
        /// </summary>
        public void FixUp()
        {
            foreach (Pending p in _pending)
            {
                try
                {
                    if (p.Count > 1 && p.Bar.NumberOfBarPositions > 1)
                    {
                        XYZ c0 = Centroid(p.Bar, 0);
                        XYZ cN = Centroid(p.Bar, p.Bar.NumberOfBarPositions - 1);
                        if (c0 != null && cN != null && (cN - c0).DotProduct(p.DistDir) < 0)
                        {
                            p.BarsOnNormalSide = !p.BarsOnNormalSide;
                            ApplyLayout(p);
                            _r.LayoutsFlipped++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    FastRebarLog.Write($"排列方向檢查失敗 {p.Bar.Id.IntegerValue}: {ex.Message}");
                }
            }
            _doc.Regenerate();

            foreach (Pending p in _pending)
            {
                try
                {
                    p.Frame.CentreAxis(out XYZ axisPt, out XYZ axisDir);
                    for (int end = 0; end < 2; end++)
                    {
                        if (p.Bar.GetHookTypeId(end) == ElementId.InvalidElementId) continue;
                        if (HookPointsOutward(p.Bar, end, axisPt, axisDir))
                        {
                            RebarHookOrientation o = p.Bar.GetHookOrientation(end);
                            p.Bar.SetHookOrientation(end, o == RebarHookOrientation.Left ? RebarHookOrientation.Right : RebarHookOrientation.Left);
                            _r.HooksFlipped++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    FastRebarLog.Write($"彎鉤方向檢查失敗 {p.Bar.Id.IntegerValue}: {ex.Message}");
                }
            }
            _doc.Regenerate();

            // 再檢查一次，仍朝外的列入問題清單
            foreach (Pending p in _pending)
            {
                try
                {
                    p.Frame.CentreAxis(out XYZ axisPt, out XYZ axisDir);
                    for (int end = 0; end < 2; end++)
                    {
                        if (p.Bar.GetHookTypeId(end) == ElementId.InvalidElementId) continue;
                        if (HookPointsOutward(p.Bar, end, axisPt, axisDir) && !_r.Problem.Contains(p.Bar.Id))
                        {
                            _r.Problem.Add(p.Bar.Id);
                            _r.Messages.Add($"鋼筋 {p.Bar.Id.IntegerValue}：彎鉤翻轉後仍朝外，請手動檢查。");
                        }
                    }
                }
                catch { }
            }
        }

        private static XYZ Centroid(Rebar bar, int index)
        {
            IList<Curve> curves = bar.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, index);
            if (curves == null || curves.Count == 0) return null;
            XYZ sum = XYZ.Zero;
            int n = 0;
            foreach (Curve c in curves)
            {
                sum += c.GetEndPoint(0) + c.GetEndPoint(1);
                n += 2;
            }
            return sum / n;
        }

        private static bool HookPointsOutward(Rebar bar, int end, XYZ axisPt, XYZ axisDir)
        {
            IList<Curve> withHooks = bar.GetTransformedCenterlineCurves(false, false, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            IList<Curve> noHooks = bar.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            if (withHooks == null || noHooks == null || withHooks.Count == 0 || noHooks.Count == 0) return false;

            XYZ tip = end == 0 ? withHooks[0].GetEndPoint(0) : withHooks[withHooks.Count - 1].GetEndPoint(1);
            XYZ root = end == 0 ? noHooks[0].GetEndPoint(0) : noHooks[noHooks.Count - 1].GetEndPoint(1);
            return DistanceToAxis(tip, axisPt, axisDir) > DistanceToAxis(root, axisPt, axisDir) + 1e-4;
        }

        private static double DistanceToAxis(XYZ p, XYZ axisPt, XYZ axisDir)
        {
            XYZ v = p - axisPt;
            return (v - axisDir * v.DotProduct(axisDir)).GetLength();
        }
    }

    /// <summary>把 Revit 的警告（例如鋼筋超出主體）收起來，數量列在報告裡，不讓對話框一個個跳出。</summary>
    public class SwallowWarnings : IFailuresPreprocessor
    {
        public int Count { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (FailureMessageAccessor m in accessor.GetFailureMessages())
            {
                if (m.GetSeverity() == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(m);
                    Count++;
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
