using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DevelopmentTools.FinishBuildUp
{
    /// <summary>房間內某一塊結構頂面（樓板或梁）與面層底之間的關係。</summary>
    public class BuildUpPiece
    {
        public string Kind;             // 板上／梁上
        public Element Source;          // 結構元素（可能在連結模型）
        public bool SourceIsHost;
        public string SourceLabel;
        public double TopZFt;           // 結構頂（目前檔案座標）
        public double GapMm;            // 面層底 − 結構頂；正＝要增築，負＝結構太高要降板／降梁
        public Solid Footprint;         // 薄片實體（結構頂面 ∩ 房間）
        public double AreaM2;
        public bool Selected = true;

        public string Action(BuildUpSettings s)
        {
            if (GapMm > s.MinGapMm) return GapMm > s.ReviewGapMm ? $"{Kind}增築 {BuildUpSettings.Cm(GapMm)}cm（需確認）" : $"{Kind}增築 {BuildUpSettings.Cm(GapMm)}cm";
            if (GapMm < -s.MinGapMm) return (Kind == "梁上" ? "降梁 " : "降板 ") + BuildUpSettings.Cm(-GapMm) + "cm";
            return "符合";
        }
    }

    public class RoomBuildUp
    {
        public Room Room;
        public string Label, LevelName, Code;
        public FinishSpec Spec;
        public double LevelZFt, FlZFt, BottomZFt;
        public List<BuildUpPiece> Pieces = new List<BuildUpPiece>();
        public List<string> Notes = new List<string>();
        public List<CurveLoop> Loops = new List<CurveLoop>();
    }

    /// <summary>降板／降梁需求：同一結構元素被多個房間要求的下降量。</summary>
    public class DropNeed
    {
        public Element Element;
        public string Kind;
        public List<double> DropsMm = new List<double>();
        public List<string> Rooms = new List<string>();
        public double MaxDrop => DropsMm.Count > 0 ? DropsMm.Max() : 0;
        public bool Consistent(double tol) => DropsMm.Count > 0 && DropsMm.Max() - DropsMm.Min() <= tol;
    }

    /// <summary>
    /// 依裝修表做降梁降板增築：
    ///   面層底 = 完成面 −（退縮面層厚度）；不退裝修的面層底 = 完成面。
    ///   在房間範圍內，由上往下找最上層的結構頂面（樓板頂、梁頂），
    ///   差距 = 面層底 − 結構頂：&gt; 0 建立增築（板上／梁上分開）；&lt; 0 表示結構太高，列為降板／降梁。
    /// </summary>
    public class BuildUpEngine
    {
        private const double FtToMm = 304.8, Ft2ToM2 = 0.09290304;
        private readonly Document _doc;
        private readonly BuildUpSettings _s;
        private readonly Action<string> _progress;
        private readonly Dictionary<ElementId, double> _levelZ = new Dictionary<ElementId, double>();
        private List<Candidate> _cands;

        public List<RoomBuildUp> Rooms { get; } = new List<RoomBuildUp>();
        public List<string> Log { get; } = new List<string>();

        private class Candidate
        {
            public Element E;
            public bool IsHost;
            public string Kind, Label;
            public Transform T;
            public BoundingBoxXYZ Box;
            public List<Tuple<double, List<CurveLoop>>> TopFaces;   // (Z, loops)，延後計算
        }

        public BuildUpEngine(Document doc, BuildUpSettings s, Action<string> progress)
        {
            _doc = doc; _s = s; _progress = progress ?? (_ => { });
        }

        // ───────────────────────── 分析 ─────────────────────────

        public void Analyze(IList<Room> rooms)
        {
            Rooms.Clear();
            Log.Clear();
            _progress("收集結構樓板與梁…");
            _cands = CollectCandidates();
            Log.Add($"結構候選：{_cands.Count} 個（樓板、梁、基礎；排除名稱含 {string.Join("、", _s.ExcludeKeywords)} 者）。");
            int i = 0;
            foreach (Room r in rooms)
            {
                if (++i % 20 == 0) _progress($"分析房間… {i}/{rooms.Count}");
                try { Rooms.Add(AnalyzeRoom(r)); }
                catch (Exception ex) { Log.Add($"{RoomLabel(r)}：分析失敗（{ex.Message}）"); }
            }
        }

        private static string RoomLabel(Room r)
        {
            string num = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
            string name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? r.Name ?? "";
            return (num + " " + name).Trim();
        }

        private bool IsExcluded(Element e)
        {
            string n = (e.Name ?? "") + " " + (_doc.GetElement(e.GetTypeId())?.Name ?? "");
            if (e.Document != _doc) n = (e.Name ?? "") + " " + (e.Document.GetElement(e.GetTypeId())?.Name ?? "");
            return _s.ExcludeKeywords.Any(k => !string.IsNullOrEmpty(k) && n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private List<Candidate> CollectCandidates()
        {
            var cats = new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralFoundation };
            var list = new List<Candidate>();
            void Add(Document d, Transform t, bool host, string prefix)
            {
                foreach (Element e in new FilteredElementCollector(d).WherePasses(new ElementMulticategoryFilter(cats)).WhereElementIsNotElementType())
                {
                    if (IsExcluded(e)) continue;
                    BoundingBoxXYZ bb = e.get_BoundingBox(null);
                    if (bb == null) continue;
                    if (t != null && !t.IsIdentity) bb = TransformBox(bb, t);
                    bool beam = e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming;
                    string tn = d.GetElement(e.GetTypeId())?.Name ?? e.Name;
                    list.Add(new Candidate { E = e, IsHost = host, Kind = beam ? "梁上" : "板上", T = t, Box = bb, Label = prefix + tn });
                }
            }
            Add(_doc, Transform.Identity, true, "");
            if (_s.IncludeLinks)
            {
                foreach (RevitLinkInstance li in new FilteredElementCollector(_doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    Document ld = null;
                    try { ld = li.GetLinkDocument(); } catch { }
                    if (ld == null) continue;
                    Add(ld, li.GetTotalTransform(), false, "[" + ld.Title + "] ");
                }
            }
            return list;
        }

        private static BoundingBoxXYZ TransformBox(BoundingBoxXYZ bb, Transform t)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = t.OfPoint(new XYZ((i & 1) == 0 ? bb.Min.X : bb.Max.X, (i & 2) == 0 ? bb.Min.Y : bb.Max.Y, (i & 4) == 0 ? bb.Min.Z : bb.Max.Z));
                x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
                x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
            }
            return new BoundingBoxXYZ { Min = new XYZ(x0, y0, z0), Max = new XYZ(x1, y1, z1) };
        }

        /// <summary>樓層在內部座標的高程：以該樓層上樓板的頂面反推（不受專案基準點設定影響）。</summary>
        public double LevelZ(Level lv)
        {
            if (_levelZ.TryGetValue(lv.Id, out double z)) return z;
            var d = new List<double>();
            foreach (Floor f in new FilteredElementCollector(_doc).OfClass(typeof(Floor)).Cast<Floor>())
            {
                if (f.LevelId != lv.Id) continue;
                var bb = f.get_BoundingBox(null);
                double off = f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.AsDouble() ?? 0;
                if (bb != null) d.Add(bb.Max.Z - off);
                if (d.Count >= 25) break;
            }
            z = d.Count > 0 ? d.OrderBy(x => x).ElementAt(d.Count / 2) : lv.ProjectElevation;
            _levelZ[lv.Id] = z;
            return z;
        }

        private RoomBuildUp AnalyzeRoom(Room r)
        {
            var rb = new RoomBuildUp { Room = r, Label = RoomLabel(r) };
            var lv = _doc.GetElement(r.LevelId) as Level;
            rb.LevelName = lv?.Name ?? "";
            rb.Code = r.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR)?.AsString()?.Trim() ?? "";
            rb.Spec = FinishTable.Find(_s.Finishes, rb.Code);
            if (lv == null) { rb.Notes.Add("房間沒有樓層"); return rb; }
            if (string.IsNullOrEmpty(rb.Code)) { rb.Notes.Add("房間未填「地板裝修」代碼"); return rb; }
            if (rb.Spec == null) { rb.Notes.Add($"裝修表找不到代碼 {rb.Code}"); return rb; }

            rb.LevelZFt = LevelZ(lv);
            double baseOff = r.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET)?.AsDouble() ?? 0;
            rb.FlZFt = rb.LevelZFt + baseOff + rb.Spec.FlOffsetMm / FtToMm;
            rb.BottomZFt = rb.FlZFt + rb.Spec.BottomBelowFlMm / FtToMm;

            rb.Loops = RoomLoops(r, out string loopNote);
            if (loopNote != null) rb.Notes.Add(loopNote);
            if (rb.Loops.Count == 0) { rb.Notes.Add("取不到房間邊界（未封閉或面積為 0）"); return rb; }

            double depth = _s.SearchDepthMm / FtToMm, above = 0.15 / 0.3048;
            double zLow = rb.FlZFt - depth, zHigh = rb.FlZFt + above;
            Solid roomPrism = Prism(rb.Loops, zLow, zHigh - zLow);
            if (roomPrism == null) { rb.Notes.Add("房間邊界無法建立實體"); return rb; }
            BoundingBoxXYZ rbb = roomPrism.GetBoundingBox();
            Transform rt = rbb.Transform;
            XYZ bmin = rt.OfPoint(rbb.Min), bmax = rt.OfPoint(rbb.Max);

            // 候選結構面（由上往下）
            var faces = new List<Tuple<double, Candidate, List<CurveLoop>>>();
            foreach (var c in _cands)
            {
                if (c.Box.Max.X < bmin.X || c.Box.Min.X > bmax.X || c.Box.Max.Y < bmin.Y || c.Box.Min.Y > bmax.Y) continue;
                if (c.Box.Max.Z < zLow || c.Box.Min.Z > zHigh) continue;
                if (c.TopFaces == null) c.TopFaces = TopFaces(c);
                foreach (var f in c.TopFaces)
                    if (f.Item1 >= zLow && f.Item1 <= zHigh) faces.Add(Tuple.Create(f.Item1, c, f.Item2));
            }
            if (faces.Count == 0) { rb.Notes.Add($"完成面下 {_s.SearchDepthMm:0} mm 內找不到結構樓板或梁"); return rb; }

            Solid covered = null;
            double thin = 2 / FtToMm;
            foreach (var f in faces.OrderByDescending(x => x.Item1))
            {
                Solid piece;
                try
                {
                    Solid fp = Prism(f.Item3, f.Item1 - thin, thin);
                    if (fp == null) continue;
                    piece = BooleanOperationsUtils.ExecuteBooleanOperation(fp, roomPrism, BooleanOperationsType.Intersect);
                    if (piece == null || piece.Volume < 1e-9) continue;
                    if (covered != null)
                    {
                        piece = BooleanOperationsUtils.ExecuteBooleanOperation(piece, covered, BooleanOperationsType.Difference);
                        if (piece == null || piece.Volume < 1e-9) continue;
                    }
                }
                catch { continue; }

                double area = piece.Volume / thin * Ft2ToM2;
                if (area < 0.01) continue;   // 小於 0.01 m² 的碎片略過
                double gap = (rb.BottomZFt - f.Item1) * FtToMm;
                gap = Math.Round(gap / _s.RoundMm) * _s.RoundMm;
                rb.Pieces.Add(new BuildUpPiece
                {
                    Kind = f.Item2.Kind, Source = f.Item2.E, SourceIsHost = f.Item2.IsHost, SourceLabel = f.Item2.Label,
                    TopZFt = f.Item1, GapMm = gap, Footprint = piece, AreaM2 = area,
                    Selected = Math.Abs(gap) > _s.MinGapMm
                });

                try
                {
                    Solid col = Column(piece, zLow - 1, zHigh - zLow + 2);
                    if (col != null)
                        covered = covered == null ? col : BooleanOperationsUtils.ExecuteBooleanOperation(covered, col, BooleanOperationsType.Union);
                }
                catch { }
            }

            // 同一來源、同一差距的碎片合併成一列
            rb.Pieces = rb.Pieces.GroupBy(p => new { Id = p.Source.Id.IntegerValue, Doc = p.Source.Document.GetHashCode(), p.GapMm, p.Kind })
                .Select(g =>
                {
                    var first = g.First();
                    Solid u = first.Footprint;
                    foreach (var o in g.Skip(1))
                        try { u = BooleanOperationsUtils.ExecuteBooleanOperation(u, o.Footprint, BooleanOperationsType.Union); } catch { }
                    first.Footprint = u;
                    first.AreaM2 = g.Sum(x => x.AreaM2);
                    return first;
                }).OrderByDescending(p => p.AreaM2).ToList();

            // 房間內已有的增築（不是本工具建立的）：避免重複
            // 只看完成面下 1.5 m 內，避免算到下一層的增築
            double zx = rb.FlZFt - 1.5 / 0.3048;
            Solid near = Prism(rb.Loops, zx, zHigh - zx);
            int existing = near == null ? 0 : ExistingBuildUps(near, new XYZ(bmin.X, bmin.Y, zx), bmax);
            if (existing > 0)
            {
                rb.Notes.Add($"房間內已有 {existing} 塊增築樓板（非本工具建立）" + (_s.SkipRoomsWithExisting ? "，預設不勾選" : ""));
                if (_s.SkipRoomsWithExisting) foreach (var p in rb.Pieces) p.Selected = false;
            }

            double roomArea = r.Area * Ft2ToM2, got = rb.Pieces.Sum(p => p.AreaM2);
            if (roomArea > 0.5 && got < roomArea * 0.9)
                rb.Notes.Add($"結構只涵蓋房間 {got / roomArea * 100:0}% 面積（其餘範圍下方沒有樓板或梁，或為開口）");
            return rb;
        }

        private List<Floor> _existing;

        private int ExistingBuildUps(Solid roomPrism, XYZ bmin, XYZ bmax)
        {
            if (_existing == null)
            {
                Schema sc = Schema.Lookup(SchemaGuid);
                _existing = new FilteredElementCollector(_doc).OfClass(typeof(Floor)).Cast<Floor>()
                    .Where(f => (_doc.GetElement(f.GetTypeId())?.Name ?? "").Contains("增築"))
                    .Where(f =>
                    {
                        if (sc == null) return true;
                        try { return !f.GetEntity(sc).IsValid(); } catch { return true; }
                    }).ToList();
            }
            int n = 0;
            foreach (var f in _existing)
            {
                var bb = f.get_BoundingBox(null);
                if (bb == null || bb.Max.X < bmin.X || bb.Min.X > bmax.X || bb.Max.Y < bmin.Y || bb.Min.Y > bmax.Y || bb.Max.Z < bmin.Z || bb.Min.Z > bmax.Z) continue;
                try
                {
                    var c = new ElementIntersectsSolidFilter(roomPrism);
                    if (c.PassesFilter(f)) n++;
                }
                catch { n++; }
            }
            return n;
        }

        /// <summary>房間完成面邊界 → 封閉 CurveLoop。</summary>
        private static List<CurveLoop> RoomLoops(Room r, out string note)
        {
            note = null;
            var loops = new List<CurveLoop>();
            var opts = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
            IList<IList<BoundarySegment>> segs = null;
            try { segs = r.GetBoundarySegments(opts); } catch { }
            if (segs == null) return loops;
            foreach (var loop in segs)
            {
                try
                {
                    var cl = new CurveLoop();
                    foreach (var s in loop) cl.Append(s.GetCurve());
                    if (!cl.IsOpen()) loops.Add(cl);
                    else note = "房間邊界有未閉合的段落，已略過該圈";
                }
                catch { note = "房間邊界有無法串接的段落，已略過該圈"; }
            }
            return loops;
        }

        /// <summary>把平面迴圈移到 z 後往上拉伸 h。</summary>
        private static Solid Prism(IList<CurveLoop> loops, double z, double h)
        {
            if (loops == null || loops.Count == 0 || h <= 0) return null;
            var moved = new List<CurveLoop>();
            foreach (var l in loops)
            {
                double z0 = l.First().GetEndPoint(0).Z;
                moved.Add(CurveLoop.CreateViaTransform(l, Transform.CreateTranslation(new XYZ(0, 0, z - z0))));
            }
            try { return GeometryCreationUtilities.CreateExtrusionGeometry(moved, XYZ.BasisZ, h); }
            catch { return null; }
        }

        /// <summary>把薄片的平面範圍拉成整個高度的柱體，用來遮蔽下方較低的結構面。</summary>
        private static Solid Column(Solid piece, double z, double h)
        {
            Solid u = null;
            foreach (var loops in TopLoops(piece))
            {
                Solid s = Prism(loops, z, h);
                if (s == null) continue;
                u = u == null ? s : BooleanOperationsUtils.ExecuteBooleanOperation(u, s, BooleanOperationsType.Union);
            }
            return u;
        }

        /// <summary>實體所有朝上平面的迴圈（每個面一組：外圈＋內圈）。</summary>
        public static List<IList<CurveLoop>> TopLoops(Solid s)
        {
            var r = new List<IList<CurveLoop>>();
            if (s == null) return r;
            foreach (Face f in s.Faces)
                if (f is PlanarFace pf && pf.FaceNormal.Z > 0.99)
                    try { r.Add(pf.GetEdgesAsCurveLoops()); } catch { }
            return r;
        }

        /// <summary>結構元素朝上的水平面（樓板頂、梁頂），轉成目前檔案座標。</summary>
        private static List<Tuple<double, List<CurveLoop>>> TopFaces(Candidate c)
        {
            var list = new List<Tuple<double, List<CurveLoop>>>();
            var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
            GeometryElement ge = null;
            try { ge = c.E.get_Geometry(opt); } catch { }
            if (ge == null) return list;
            foreach (Solid s in Solids(ge))
            {
                Solid ss = s;
                if (c.T != null && !c.T.IsIdentity)
                    try { ss = SolidUtils.CreateTransformed(s, c.T); } catch { continue; }
                foreach (Face f in ss.Faces)
                {
                    if (!(f is PlanarFace pf) || pf.FaceNormal.Z < 0.995 || pf.Area < 0.05) continue;
                    try { list.Add(Tuple.Create(pf.Origin.Z, pf.GetEdgesAsCurveLoops().ToList())); } catch { }
                }
            }
            return list;
        }

        private static IEnumerable<Solid> Solids(GeometryElement ge)
        {
            foreach (GeometryObject o in ge)
            {
                if (o is Solid s && s.Volume > 1e-6) yield return s;
                else if (o is GeometryInstance gi)
                {
                    GeometryElement inner = null;
                    try { inner = gi.GetInstanceGeometry(); } catch { }
                    if (inner != null) foreach (var x in Solids(inner)) yield return x;
                }
            }
        }

        // ───────────────────────── 降板／降梁需求 ─────────────────────────

        public List<DropNeed> DropNeeds()
        {
            var map = new Dictionary<ElementId, DropNeed>();
            foreach (var rb in Rooms)
                foreach (var p in rb.Pieces.Where(x => x.SourceIsHost && x.GapMm < -_s.MinGapMm))
                {
                    if (!map.TryGetValue(p.Source.Id, out var d))
                        map[p.Source.Id] = d = new DropNeed { Element = p.Source, Kind = p.Kind == "梁上" ? "降梁" : "降板" };
                    d.DropsMm.Add(-p.GapMm);
                    if (!d.Rooms.Contains(rb.Label)) d.Rooms.Add(rb.Label);
                }
            return map.Values.OrderBy(d => d.Kind).ThenByDescending(d => d.MaxDrop).ToList();
        }

        // ───────────────────────── 建立 ─────────────────────────

        private static readonly Guid SchemaGuid = new Guid("6A1C4E2B-8F3D-4C59-9B7E-2D41F0A6C3B8");

        private static Schema MarkSchema()
        {
            Schema s = Schema.Lookup(SchemaGuid);
            if (s != null) return s;
            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName("RTS_FinishBuildUp");
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.AddSimpleField("RoomUid", typeof(string));
            sb.AddSimpleField("Role", typeof(string));   // buildup / finish
            return sb.Finish();
        }

        private static void Mark(Element e, string roomUid, string role)
        {
            var sc = MarkSchema();
            var ent = new Entity(sc);
            ent.Set("RoomUid", roomUid);
            ent.Set("Role", role);
            e.SetEntity(ent);
        }

        /// <summary>找出外掛先前為這些房間建立的增築／面層（重跑時先刪除，避免重複）。</summary>
        public List<ElementId> PreviouslyCreated(IEnumerable<RoomBuildUp> rooms, string role)
        {
            var uids = new HashSet<string>(rooms.Select(r => r.Room.UniqueId));
            var ids = new List<ElementId>();
            Schema sc = Schema.Lookup(SchemaGuid);
            if (sc == null) return ids;
            foreach (Floor f in new FilteredElementCollector(_doc).OfClass(typeof(Floor)).Cast<Floor>())
            {
                Entity ent;
                try { ent = f.GetEntity(sc); } catch { continue; }
                if (!ent.IsValid()) continue;
                if (uids.Contains(ent.Get<string>("RoomUid")) && ent.Get<string>("Role") == role) ids.Add(f.Id);
            }
            return ids;
        }

        public class ApplyResult
        {
            public int Created, Deleted, Failed, TypesCreated, FinishCreated;
            public List<ElementId> NewIds = new List<ElementId>();
            public List<string> Messages = new List<string>();
        }

        /// <summary>建立勾選的增築（以及選擇性建立地坪面層）。呼叫端負責交易。</summary>
        public ApplyResult CreateBuildUps(IList<RoomBuildUp> rooms)
        {
            var res = new ApplyResult();
            var old = PreviouslyCreated(rooms, "buildup");
            if (_s.CreateFinishFloors) old.AddRange(PreviouslyCreated(rooms, "finish"));
            if (old.Count > 0) { _doc.Delete(old); res.Deleted = old.Count; }

            WorksetId ws = FindWorkset(_s.WorksetKeyword);
            foreach (var rb in rooms)
            {
                var lv = _doc.GetElement(rb.Room.LevelId) as Level;
                if (lv == null) continue;
                foreach (var grp in rb.Pieces.Where(p => p.Selected && p.GapMm > _s.MinGapMm).GroupBy(p => new { p.GapMm, p.Kind }))
                {
                    Solid u = null;
                    foreach (var p in grp)
                        try { u = u == null ? p.Footprint : BooleanOperationsUtils.ExecuteBooleanOperation(u, p.Footprint, BooleanOperationsType.Union); } catch { }
                    if (u == null) continue;
                    FloorType ft = GetOrCreateType(grp.Key.GapMm, res);
                    if (ft == null) { res.Failed++; res.Messages.Add($"{rb.Label}：無法建立 {BuildUpSettings.Cm(grp.Key.GapMm)}cm 增築類型"); continue; }
                    foreach (var loops in TopLoops(u))
                    {
                        try
                        {
                            var atLevel = loops.Select(l => CurveLoop.CreateViaTransform(l, Transform.CreateTranslation(new XYZ(0, 0, rb.LevelZFt - l.First().GetEndPoint(0).Z)))).ToList();
                            Floor fl = Floor.Create(_doc, atLevel, ft.Id, lv.Id);
                            fl.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(rb.BottomZFt - rb.LevelZFt);
                            var sp = fl.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                            if (sp != null && !sp.IsReadOnly) sp.Set(0);
                            SetText(fl, _s.InfoParam, $"{grp.Key.Kind}增築{BuildUpSettings.Cm(grp.Key.GapMm)}cm");
                            if (ws != null) { var wp = fl.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM); if (wp != null && !wp.IsReadOnly) wp.Set(ws.IntegerValue); }
                            Mark(fl, rb.Room.UniqueId, "buildup");
                            res.Created++;
                            res.NewIds.Add(fl.Id);
                        }
                        catch (Exception ex)
                        {
                            res.Failed++;
                            res.Messages.Add($"{rb.Label} {grp.Key.Kind}{BuildUpSettings.Cm(grp.Key.GapMm)}cm：{ex.Message}");
                        }
                    }
                }

                if (_s.CreateFinishFloors && rb.Spec != null && rb.Loops.Count > 0)
                {
                    var ftype = new FilteredElementCollector(_doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault(t => t.Name == rb.Spec.FloorType);
                    if (ftype == null) { res.Messages.Add($"{rb.Label}：找不到地坪類型「{rb.Spec.FloorType}」，未建立面層"); continue; }
                    try
                    {
                        var atLevel = rb.Loops.Select(l => CurveLoop.CreateViaTransform(l, Transform.CreateTranslation(new XYZ(0, 0, rb.LevelZFt - l.First().GetEndPoint(0).Z)))).ToList();
                        Floor fl = Floor.Create(_doc, atLevel, ftype.Id, lv.Id);
                        double top = rb.FlZFt - rb.LevelZFt + (rb.Spec.Recessed ? 0 : rb.Spec.ThicknessMm / FtToMm);
                        fl.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(top);
                        var sp = fl.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                        if (sp != null && !sp.IsReadOnly) sp.Set(0);
                        Mark(fl, rb.Room.UniqueId, "finish");
                        res.FinishCreated++;
                        res.NewIds.Add(fl.Id);
                    }
                    catch (Exception ex) { res.Messages.Add($"{rb.Label} 面層：{ex.Message}"); }
                }
            }
            return res;
        }

        private static void SetText(Element e, string name, string v)
        {
            if (string.IsNullOrEmpty(name)) return;
            Parameter p = e.LookupParameter(name);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(v);
        }

        private WorksetId FindWorkset(string keyword)
        {
            if (!_doc.IsWorkshared || string.IsNullOrEmpty(keyword)) return null;
            var ws = new FilteredWorksetCollector(_doc).OfKind(WorksetKind.UserWorkset).FirstOrDefault(w => w.Name.Contains(keyword));
            return ws?.Id;
        }

        private readonly Dictionary<double, FloorType> _typeCache = new Dictionary<double, FloorType>();

        /// <summary>取得「樓板_Xcm 增築」類型；沒有就從來源類型複製並改厚度與材料。</summary>
        private FloorType GetOrCreateType(double mm, ApplyResult res)
        {
            if (_typeCache.TryGetValue(mm, out var c)) return c;
            string name = _s.TypeName(mm);
            var types = new FilteredElementCollector(_doc).OfClass(typeof(FloorType)).Cast<FloorType>().ToList();
            FloorType ft = types.FirstOrDefault(t => t.Name == name);
            if (ft == null)
            {
                FloorType src = types.FirstOrDefault(t => t.Name == _s.BaseType)
                                ?? types.FirstOrDefault(t => t.Name.Contains("增築") && (t.GetCompoundStructure()?.LayerCount ?? 0) == 1)
                                ?? types.FirstOrDefault(t => t.Name.Contains("增築"))
                                ?? types.FirstOrDefault(t => (t.GetCompoundStructure()?.LayerCount ?? 0) == 1);
                if (src == null) return null;
                ft = src.Duplicate(name) as FloorType;
                if (ft == null) return null;
                var cs = ft.GetCompoundStructure();
                if (cs != null)
                {
                    int idx = 0;
                    double others = 0;
                    if (cs.LayerCount > 1)
                    {
                        idx = Enumerable.Range(0, cs.LayerCount).OrderByDescending(i => cs.GetLayerWidth(i)).First();
                        others = Enumerable.Range(0, cs.LayerCount).Where(i => i != idx).Sum(i => cs.GetLayerWidth(i));
                    }
                    double w = mm / FtToMm - others;
                    if (w <= 0) w = mm / FtToMm;
                    cs.SetLayerWidth(idx, w);
                    var mat = new FilteredElementCollector(_doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name == _s.MaterialName(mm));
                    if (mat != null) cs.SetMaterialId(idx, mat.Id);
                    ft.SetCompoundStructure(cs);
                }
                res.TypesCreated++;
            }
            _typeCache[mm] = ft;
            return ft;
        }

        /// <summary>套用降板／降梁：只處理所有房間要求一致的元素（差 ≤ 容許值），其餘列為需拆板／需人工。呼叫端負責交易。</summary>
        public List<string> ApplyDrops(IEnumerable<DropNeed> needs)
        {
            var msg = new List<string>();
            foreach (var n in needs)
            {
                if (!n.Consistent(_s.MinGapMm))
                {
                    msg.Add($"{n.Kind} {n.Element.Name}（ID {n.Element.Id}）：各房間要求 {n.DropsMm.Min():0}～{n.DropsMm.Max():0} mm 不一致，需拆板或人工調整。");
                    continue;
                }
                double d = n.MaxDrop / FtToMm;
                try
                {
                    if (n.Element is Floor)
                    {
                        var p = n.Element.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                        if (p == null || p.IsReadOnly) { msg.Add($"降板 ID {n.Element.Id}：高度偏移不可修改。"); continue; }
                        p.Set(p.AsDouble() - d);
                    }
                    else
                    {
                        var p0 = n.Element.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION);
                        var p1 = n.Element.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION);
                        if (p0 == null || p1 == null || p0.IsReadOnly || p1.IsReadOnly)
                        {
                            var pz = n.Element.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE);
                            if (pz == null || pz.IsReadOnly) { msg.Add($"降梁 ID {n.Element.Id}：起訖點偏移不可修改。"); continue; }
                            pz.Set(pz.AsDouble() - d);
                        }
                        else
                        {
                            p0.Set(p0.AsDouble() - d);
                            p1.Set(p1.AsDouble() - d);
                        }
                    }
                    msg.Add($"{n.Kind} {n.Element.Name}（ID {n.Element.Id}）下降 {n.MaxDrop:0} mm（{string.Join("、", n.Rooms.Take(3))}{(n.Rooms.Count > 3 ? "…" : "")}）");
                }
                catch (Exception ex) { msg.Add($"{n.Kind} ID {n.Element.Id}：{ex.Message}"); }
            }
            return msg;
        }
    }
}
