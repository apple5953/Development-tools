using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace DevelopmentTools.CodeCheck
{
    public class FactItem
    {
        public string Scope;
        public string Label;
        public ElementId Id;              // 元素在其所屬模型中的 ID
        public ModelSource Source;        // 元素所屬模型（目前檔案或連結）
        public LevelFact Level;
        public VarBag Vars;
    }

    public class LevelFact : FactItem
    {
        public Level RevitLevel;          // 樓層基準模型中的樓層
        public List<string> Names = new List<string>();   // 各模型中對應的樓層名稱
        public double ElevFt;             // 目前檔案內部座標高程
        public int Index;               // 1 = 地面層（避難層），2…；地下 -1、-2…；屋頂層／屋突 0
        public bool IsBasement, IsRoof, IsRoofProjection;
        public ViewPlan Plan;
        public List<RoomFact> Rooms = new List<RoomFact>();
        public List<DeviceFact> Devices = new List<DeviceFact>();
        public List<DoorFact> Doors = new List<DoorFact>();
    }

    public class RoomFact : FactItem
    {
        public Room Room;
        public string Name, Role;
        public bool Habitable;
        public double AreaM2;
        public List<List<P2>> Loops = new List<List<P2>>();
        public double FloorZFt;
        public double MinX, MinY, MaxX, MaxY;   // 平面外框（公尺）
    }

    public class StairFact : FactItem
    {
        public Stairs Stairs;
        public double MinXm, MinYm, MaxXm, MaxYm, BaseZFt, TopZFt;
        public bool IsSafety, IsSpecial, IsOutdoor, IsAccessible;
        public List<XYZ> EntryPoints = new List<XYZ>();   // 各樓層的第一階位置（樓層高程）
        public int Shaft;
    }

    public class DoorFact : FactItem
    {
        public FamilyInstance Door;
        public XYZ Point;
        public RoomFact From, To;
        public P2 Normal;                 // 牆法線（平面）
        public double WidthM, DepthM;     // 門寬、穿越深度（牆厚＋餘裕）
    }

    public class DeviceFact : FactItem
    {
        public string Kind;
        public XYZ Point;
    }

    /// <summary>
    /// 從 Revit 模型讀出檢討所需的事實（樓層、房間、樓梯、門窗、欄杆、坡道、停車、昇降機、消防設備、防火區劃），
    /// 並把每個項目的變數放進 VarBag 供規則運算式使用。所有長度以 mm、面積 m²、距離 m 表示。
    /// </summary>
    public class ModelFacts
    {
        private const double FtToMm = 304.8, FtToM = 0.3048, Ft2ToM2 = 0.09290304;

        private readonly Document _doc;                 // 目前檔案（3D 視圖、平面圖所在）
        private readonly List<ModelSource> _sources;
        private readonly bool _multi;                   // 是否有連結或其他模型參與
        private readonly ProjectSettings _s;
        private readonly KeywordConfig _kw;
        private readonly View3D _view3D;
        private readonly Action<string> _progress;

        public VarBag Project;
        public List<LevelFact> Levels = new List<LevelFact>();
        public List<RoomFact> Rooms = new List<RoomFact>();
        public List<StairFact> Stairs = new List<StairFact>();
        public List<DoorFact> Doors = new List<DoorFact>();
        public List<FactItem> Windows = new List<FactItem>();
        public List<FactItem> Railings = new List<FactItem>();
        public List<FactItem> Ramps = new List<FactItem>();
        public List<FactItem> Parking = new List<FactItem>();
        public List<FactItem> Elevators = new List<FactItem>();
        public List<DeviceFact> Devices = new List<DeviceFact>();
        public List<FactItem> Areas = new List<FactItem>();
        public List<string> Notes = new List<string>();

        private LevelFact _ground;
        private readonly Dictionary<string, RoomFact> _roomByKey = new Dictionary<string, RoomFact>();
        private readonly Dictionary<string, LevelFact> _levelByKey = new Dictionary<string, LevelFact>();
        public int DuplicatesSkipped;

        public IReadOnlyList<ModelSource> Sources => _sources;

        public ModelFacts(Document doc, List<ModelSource> sources, ProjectSettings s, KeywordConfig kw, View3D view3D, Action<string> progress)
        {
            _doc = doc; _s = s; _kw = kw; _view3D = view3D; _progress = progress ?? (_ => { });
            _sources = sources != null && sources.Count > 0
                ? sources
                : new List<ModelSource> { new ModelSource { Index = 0, Key = "host", Name = doc.Title, Doc = doc, T = Transform.Identity, IsHost = true, Parts = SourceParts.All } };
            _multi = _sources.Count > 1 || !_sources[0].IsHost;
        }

        private static string K(ModelSource src, ElementId id) => src.Index + ":" + id;

        private IEnumerable<ModelSource> Reading(SourceParts p) => _sources.Where(x => x.Reads(p));

        public IEnumerable<FactItem> ItemsOf(string scope)
        {
            switch (scope)
            {
                case "project": return new[] { new FactItem { Scope = "project", Label = "整棟建築物", Vars = Project } };
                case "level": return Levels;
                case "room": return Rooms;
                case "stair": return Stairs;
                case "door": return Doors;
                case "window": return Windows;
                case "railing": return Railings;
                case "ramp": return Ramps;
                case "parking": return Parking;
                case "elevator": return Elevators;
                case "device": return Devices;
                case "area": return Areas;
            }
            return Enumerable.Empty<FactItem>();
        }

        // ───────────────────────── 主流程 ─────────────────────────

        public void Collect()
        {
            _progress("讀取樓層…");
            Project = new VarBag();
            CollectLevels();
            _progress("讀取房間與淨高…");
            CollectRooms();
            _progress("讀取門窗…");
            CollectDoors();
            CollectWindows();
            _progress("讀取樓梯、欄杆、坡道…");
            CollectStairs();
            CollectRailings();
            CollectRamps();
            _progress("讀取停車、昇降機、消防設備…");
            CollectParking();
            CollectEquipment();
            CollectAreas();
            _progress("計算專案條件…");
            FillProjectVars();
            FillLevelVars();
            if (_s.TravelDistance)
            {
                _progress(_multi ? "計算步行距離（合併各模型的房間與門）…" : "計算步行距離（Revit 行走路徑）…");
                ComputeTravel();
            }
            if (_multi)
            {
                Notes.Insert(0, "合併檢討模型：" + string.Join("、", _sources.Select(x => $"{x.Name}（{ModelSources.PartsText(x.Parts)}）")) + "。");
                if (DuplicatesSkipped > 0) Notes.Add($"各模型間有 {DuplicatesSkipped} 個重複元素（同位置的房間、門、樓梯、設備等）已只計一次。");
            }
        }

        // ───────────────────────── 樓層 ─────────────────────────

        private static readonly Regex RoofRx = new Regex(@"^(R\d*F?L?|RFL?|PR\d*F?|PH\d*|屋頂|屋突|ROOF)", RegexOptions.IgnoreCase);
        private static readonly Regex BasementRx = new Regex(@"^(B\d+|地下|BF)", RegexOptions.IgnoreCase);
        private static readonly Regex GroundRx = new Regex(@"^(1F|1FL|1 F|1樓|GL|GF|地面|一層|L1|LEVEL ?1)$", RegexOptions.IgnoreCase);

        private readonly Dictionary<int, double> _calib = new Dictionary<int, double>();

        /// <summary>
        /// 樓層在目前檔案內部座標的高程。Level.ProjectElevation 與內部原點之間若有差（專案基準點垂直移動），
        /// 以該模型的房間外框底（或樓板頂）反推校正。
        /// </summary>
        private double LevelZ(ModelSource src, Level l)
        {
            if (!_calib.TryGetValue(src.Index, out double c))
            {
                var d = new List<double>();
                try
                {
                    foreach (Room r in new FilteredElementCollector(src.Doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>().Where(r => r.Area > 1e-6).Take(40))
                    {
                        var bb = r.get_BoundingBox(null);
                        if (bb != null && src.Doc.GetElement(r.LevelId) is Level rl) d.Add(bb.Min.Z - r.BaseOffset - rl.ProjectElevation);
                    }
                    if (d.Count == 0)
                        foreach (Floor f in new FilteredElementCollector(src.Doc).OfClass(typeof(Floor)).Cast<Floor>().Take(40))
                        {
                            var bb = f.get_BoundingBox(null);
                            double off = f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.AsDouble() ?? 0;
                            if (bb != null && src.Doc.GetElement(f.LevelId) is Level fl) d.Add(bb.Max.Z - off - fl.ProjectElevation);
                        }
                }
                catch { }
                c = d.Count > 0 ? d.OrderBy(x => x).ElementAt(d.Count / 2) : 0;
                if (Math.Abs(c) < 0.01) c = 0;
                _calib[src.Index] = c;
            }
            return src.Z(l.ProjectElevation + c);
        }

        private void CollectLevels()
        {
            // 樓層基準：有讀「房間樓層」且確實有房間的模型（拆層的建築模型會各自提供自己的樓層）；
            // 結構、機電模型的樓層只用來對應元素所在層，不另外產生樓層。
            var basis = Reading(SourceParts.Spaces)
                .Where(src => new FilteredElementCollector(src.Doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount() > 0)
                .ToList();
            if (basis.Count == 0) basis = Reading(SourceParts.Spaces).Take(1).ToList();
            if (basis.Count == 0) basis = new List<ModelSource> { _sources[0] };
            double tol = Math.Max(10, _s.LevelToleranceMm) / FtToMm;

            var cand = new List<Tuple<ModelSource, Level, double>>();
            foreach (var src in basis)
                foreach (Level l in new FilteredElementCollector(src.Doc).OfClass(typeof(Level)).Cast<Level>())
                    cand.Add(Tuple.Create(src, l, LevelZ(src, l)));
            if (cand.Count == 0) return;

            // 依高程分群：高程差在容許範圍內、且來自不同模型者視為同一層
            var clusters = new List<List<Tuple<ModelSource, Level, double>>>();
            foreach (var c in cand.OrderBy(x => x.Item3).ThenBy(x => x.Item1.Index))
            {
                var last = clusters.LastOrDefault();
                if (last != null && Math.Abs(c.Item3 - last[0].Item3) <= tol && last.All(x => x.Item1 != c.Item1)) last.Add(c);
                else clusters.Add(new List<Tuple<ModelSource, Level, double>> { c });
            }

            var rep = clusters.Select(cl => cl.OrderBy(x => x.Item1.Index).First()).ToList();
            bool NameIs(List<Tuple<ModelSource, Level, double>> cl, Func<string, bool> f) => cl.Any(x => f(x.Item2.Name.Trim()));

            int gi = -1;
            if (!string.IsNullOrWhiteSpace(_s.GroundLevel)) gi = clusters.FindIndex(cl => NameIs(cl, n => n == _s.GroundLevel.Trim()));
            if (gi < 0) gi = clusters.FindIndex(cl => NameIs(cl, n => GroundRx.IsMatch(n)));
            if (gi < 0)
            {
                // 沒有 1F 命名：取最接近各模型標高 0 的樓層
                gi = Enumerable.Range(0, clusters.Count).OrderBy(i => Math.Abs(rep[i].Item2.Elevation)).First();
            }
            double groundZ = rep[gi].Item3;

            int above = 0, below = 0;
            for (int i = gi - 1; i >= 0; i--)
            {
                below++;
                Levels.Add(NewLevel(clusters[i], rep[i], -below, false, false, true));
            }
            bool roofSeen = false;
            for (int i = gi; i < clusters.Count; i++)
            {
                bool roofName = i != gi && NameIs(clusters[i], n => RoofRx.IsMatch(n));
                if (roofName || roofSeen)
                {
                    Levels.Add(NewLevel(clusters[i], rep[i], 0, !roofSeen, roofSeen, false));
                    roofSeen = true;
                }
                else Levels.Add(NewLevel(clusters[i], rep[i], ++above, false, false, false));
            }
            Levels = Levels.OrderBy(l => l.ElevFt).ToList();
            foreach (var lf in Levels) lf.Vars = new VarBag(Project);
            _ground = Levels.First(l => Math.Abs(l.ElevFt - groundZ) < 1e-6);

            // 每個模型（含結構、機電）的樓層對應到合併後最接近的樓層
            foreach (var src in _sources)
                foreach (Level l in new FilteredElementCollector(src.Doc).OfClass(typeof(Level)).Cast<Level>())
                {
                    double z = LevelZ(src, l);
                    var best = Levels.OrderBy(x => Math.Abs(x.ElevFt - z)).First();
                    if (Math.Abs(best.ElevFt - z) <= tol)
                    {
                        _levelByKey[K(src, l.Id)] = best;
                        if (!best.Names.Contains(l.Name)) best.Names.Add(l.Name);
                    }
                }
            if (_multi)
            {
                var odd = Levels.Where(l => l.Names.Count > 1).Select(l => $"{l.Label}＝{string.Join("／", l.Names)}").ToList();
                if (odd.Count > 0) Notes.Add("各模型樓層名稱不同、已依高程合併：" + string.Join("；", odd.Take(12)) + (odd.Count > 12 ? "…" : ""));
            }

            if (!_multi)
            {
                var plans = new FilteredElementCollector(_doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                    .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel != null).ToList();
                foreach (var lf in Levels)
                    lf.Plan = plans.Where(v => v.GenLevel.Id == lf.RevitLevel.Id)
                                   .OrderBy(v => v.CropBoxActive ? 1 : 0).FirstOrDefault();
            }
        }

        private LevelFact NewLevel(List<Tuple<ModelSource, Level, double>> cl, Tuple<ModelSource, Level, double> rep, int index, bool roof, bool proj, bool basement)
        {
            var lf = new LevelFact
            {
                Scope = "level", RevitLevel = rep.Item2, Source = rep.Item1, ElevFt = rep.Item3, Index = index,
                IsRoof = roof, IsRoofProjection = proj, IsBasement = basement, Label = rep.Item2.Name, Id = rep.Item2.Id
            };
            foreach (var x in cl) if (!lf.Names.Contains(x.Item2.Name)) lf.Names.Add(x.Item2.Name);
            return lf;
        }

        private LevelFact LevelAt(double zFt)
        {
            LevelFact best = null;
            foreach (var l in Levels) if (l.ElevFt <= zFt + 0.05) best = l;
            return best ?? Levels.FirstOrDefault();
        }

        /// <summary>元素所在樓層：先用元素的樓層參數對應，沒有再用（目前檔案座標的）高程判斷。p 為目前檔案座標。</summary>
        private LevelFact LevelOf(ModelSource src, Element e, XYZ p)
        {
            try
            {
                if (e.LevelId != null && e.LevelId != ElementId.InvalidElementId && _levelByKey.TryGetValue(K(src, e.LevelId), out var lf)) return lf;
            }
            catch { }
            return p != null ? LevelAt(p.Z) : null;
        }

        // ───────────────────────── 房間 ─────────────────────────

        private string RoleOf(string name, out bool habitable)
        {
            foreach (var r in _kw.Roles)
            {
                if (r.Keywords.Any(k => !string.IsNullOrEmpty(k) && name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    habitable = r.Habitable;
                    return r.Role;
                }
            }
            habitable = _kw.UnknownRoomIsHabitable;
            return habitable ? "habitable" : "other";
        }

        private void CollectRooms()
        {
            var opts = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };

            ReferenceIntersector ceilingRay = null, clearRay = null;
            if (_view3D != null)
            {
                // 天花板、樓板、梁、管線可能在不同模型（建築、室內、結構、機電），一律連結模型也算進去
                var cat = new List<BuiltInCategory> { BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs };
                ceilingRay = new ReferenceIntersector(new ElementMulticategoryFilter(cat), FindReferenceTarget.Face, _view3D) { FindReferencesInRevitLinks = true };
                var cat2 = new List<BuiltInCategory>(cat)
                {
                    BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit
                };
                clearRay = new ReferenceIntersector(new ElementMulticategoryFilter(cat2), FindReferenceTarget.Face, _view3D) { FindReferencesInRevitLinks = true };
            }

            foreach (var src in Reading(SourceParts.Spaces))
            {
                var rooms = new FilteredElementCollector(src.Doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                    .Cast<Room>().Where(r => r.Area > 1e-6 && r.Location != null).ToList();
                int k = 0;
                foreach (Room r in rooms)
                {
                    if (++k % 50 == 0) _progress($"讀取房間與淨高… {(_multi ? src.Name + " " : "")}{k}/{rooms.Count}");
                    string name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? r.Name ?? "";
                    string number = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    string role;
                    bool hab;
                    if (_s.RoomRoleOverrides.TryGetValue(r.UniqueId, out string ov) && !string.IsNullOrEmpty(ov))
                    {
                        role = ov;
                        hab = _kw.Roles.FirstOrDefault(x => x.Role == ov)?.Habitable ?? (ov == "habitable");
                    }
                    else role = RoleOf(name, out hab);

                    BoundingBoxXYZ rbb = src.Box(r.get_BoundingBox(null));
                    XYZ loc = src.P(((LocationPoint)r.Location).Point);
                    var lf = LevelOf(src, r, loc);
                    var rf = new RoomFact
                    {
                        Scope = "room", Room = r, Id = r.Id, Source = src, Name = name, Role = role, Habitable = hab, Level = lf,
                        Label = $"{lf?.Label} {number} {name}".Trim(), AreaM2 = r.Area * Ft2ToM2,
                        FloorZFt = rbb != null ? rbb.Min.Z : (lf?.ElevFt ?? 0) + r.BaseOffset
                    };
                    try
                    {
                        var segs = r.GetBoundarySegments(opts);
                        if (segs != null)
                            foreach (var loop in segs)
                            {
                                var pts = new List<P2>();
                                foreach (var seg in loop)
                                {
                                    var tess = seg.GetCurve().Tessellate();
                                    for (int i = 0; i < tess.Count - 1; i++)
                                    {
                                        XYZ q = src.P(tess[i]);
                                        pts.Add(new P2(q.X * FtToM, q.Y * FtToM));
                                    }
                                }
                                if (pts.Count >= 3) rf.Loops.Add(pts);
                            }
                    }
                    catch { }
                    if (rf.Loops.Count > 0)
                    {
                        var all = rf.Loops.SelectMany(x => x).ToList();
                        rf.MinX = all.Min(q => q.X); rf.MaxX = all.Max(q => q.X); rf.MinY = all.Min(q => q.Y); rf.MaxY = all.Max(q => q.Y);
                    }

                    if (!_s.KeepDuplicates && IsDuplicateRoom(rf))
                    {
                        DuplicatesSkipped++;
                        continue;
                    }

                    var v = new VarBag(lf?.Vars);
                    rf.Vars = v;
                    v["role"] = role;
                    v["isHabitable"] = hab;
                    v["areaM2"] = rf.AreaM2;
                    v["roomName"] = name;
                    string use = r.LookupParameter("使用類組")?.AsString();
                    v["roomUse"] = string.IsNullOrWhiteSpace(use) ? (_s.UseGroups.FirstOrDefault() ?? "") : use.Trim().ToUpperInvariant();

                    if (rf.Loops.Count > 0)
                    {
                        double w = Geo2D.MinWidth(rf.Loops, 0.5, out var samples);
                        v["minWidthMm"] = w > 0 ? (object)(w * 1000) : null;
                        double med = samples.Count > 0 ? samples.OrderBy(x => x).ElementAt(samples.Count / 2) : 0;
                        v["lengthM"] = med > 0 ? (object)(rf.AreaM2 / med) : null;
                        v["inscribedDiameterMm"] = Geo2D.InscribedDiameter(rf.Loops) * 1000;
                    }
                    else
                    {
                        v["minWidthMm"] = null; v["lengthM"] = null; v["inscribedDiameterMm"] = null;
                    }

                    MeasureHeights(rf, ceilingRay, clearRay);
                    Rooms.Add(rf);
                    lf?.Rooms.Add(rf);
                    _roomByKey[K(src, r.Id)] = rf;
                }
            }
        }

        /// <summary>不同模型在同一層、同位置、面積相近的房間視為重複（例如拆區模型交界處都畫了同一間樓梯間）。</summary>
        private bool IsDuplicateRoom(RoomFact rf)
        {
            if (rf.Level == null || rf.Loops.Count == 0) return false;
            P2 c = Geo2D.Centroid(rf.Loops.OrderByDescending(lp => Math.Abs(Geo2D.SignedArea(lp))).First());
            foreach (var o in rf.Level.Rooms)
            {
                if (o.Source == rf.Source || o.Loops.Count == 0) continue;
                if (c.X < o.MinX || c.X > o.MaxX || c.Y < o.MinY || c.Y > o.MaxY) continue;
                if (Math.Abs(o.AreaM2 - rf.AreaM2) > 0.1 * Math.Max(o.AreaM2, rf.AreaM2)) continue;
                if (Geo2D.Inside(o.Loops, c))
                {
                    _roomByKey[K(rf.Source, rf.Id)] = o;   // 重複房間的門窗仍對應到保留的那一間
                    return true;
                }
            }
            return false;
        }

        private void MeasureHeights(RoomFact rf, ReferenceIntersector ceilingRay, ReferenceIntersector clearRay)
        {
            var v = rf.Vars;
            if (ceilingRay == null || rf.Loops.Count == 0)
            {
                v["ceilingAvgMm"] = null; v["ceilingMinMm"] = null; v["clearMinMm"] = null; v["ceilingAbove2100Ratio"] = null;
                return;
            }
            var pts = Geo2D.SamplePoints(rf.Loops, 9, 0.3);
            var ceil = new List<double>();
            var clear = new List<double>();
            double z0 = rf.FloorZFt + 0.15;
            foreach (P2 p in pts)
            {
                var o = new XYZ(p.X / FtToM, p.Y / FtToM, z0);
                try
                {
                    var hit = ceilingRay.FindNearest(o, XYZ.BasisZ);
                    if (hit != null && hit.Proximity < 100) ceil.Add((hit.Proximity + 0.15) * FtToMm);
                    var hit2 = clearRay.FindNearest(o, XYZ.BasisZ);
                    if (hit2 != null && hit2.Proximity < 100) clear.Add((hit2.Proximity + 0.15) * FtToMm);
                }
                catch { }
            }
            v["ceilingAvgMm"] = ceil.Count > 0 ? (object)ceil.Average() : null;
            v["ceilingMinMm"] = ceil.Count > 0 ? (object)ceil.Min() : null;
            v["ceilingAbove2100Ratio"] = ceil.Count > 0 ? (object)(ceil.Count(h => h > 2100) / (double)ceil.Count) : null;
            v["clearMinMm"] = clear.Count > 0 ? (object)clear.Min() : (ceil.Count > 0 ? (object)ceil.Min() : null);
        }

        /// <summary>目前檔案座標點所在的房間（不分模型，以房間邊界判斷，取面積最小者）。</summary>
        private RoomFact RoomAt(XYZ p)
        {
            if (p == null) return null;
            var lf = LevelAt(p.Z);
            if (lf == null) return null;
            var q = new P2(p.X * FtToM, p.Y * FtToM);
            RoomFact best = null;
            foreach (var r in lf.Rooms)
            {
                if (r.Loops.Count == 0 || q.X < r.MinX || q.X > r.MaxX || q.Y < r.MinY || q.Y > r.MaxY) continue;
                if (Geo2D.Inside(r.Loops, q) && (best == null || r.AreaM2 < best.AreaM2)) best = r;
            }
            return best;
        }

        /// <summary>門窗兩側的房間：同一模型且讀得到 From/To 就直接用；否則（房間在別的模型、或階段不同）沿牆法線兩側探測。</summary>
        private void SidesOf(ModelSource src, FamilyInstance fi, XYZ hostPt, out RoomFact from, out RoomFact to, out P2 normal, out double wallM)
        {
            from = null; to = null;
            try { if (fi.FromRoom != null) _roomByKey.TryGetValue(K(src, fi.FromRoom.Id), out from); } catch { }
            try { if (fi.ToRoom != null) _roomByKey.TryGetValue(K(src, fi.ToRoom.Id), out to); } catch { }
            wallM = 0.2;
            try { if (fi.Host is Wall w) wallM = w.Width * FtToM; } catch { }
            XYZ f = null;
            try { f = src.V(fi.FacingOrientation); } catch { }
            normal = f != null ? new P2(f.X, f.Y).Unit : new P2(0, 0);
            if ((from != null && to != null) || f == null || normal.Len < 0.5) return;
            double off = (wallM / 2 + 0.3) / FtToM;
            var n3 = new XYZ(normal.X, normal.Y, 0);
            XYZ basePt = new XYZ(hostPt.X, hostPt.Y, (LevelAt(hostPt.Z)?.ElevFt ?? hostPt.Z) + 1.0);
            RoomFact a = RoomAt(basePt - n3 * off), b = RoomAt(basePt + n3 * off);
            if (from == null && to == null) { from = a; to = b; }
            else if (from == null) from = to == b ? a : b;
            else if (to == null) to = from == a ? b : a;
        }

        // ───────────────────────── 門 ─────────────────────────

        private static double ParamFt(Element e, params object[] keys)
        {
            foreach (object k in keys)
            {
                Parameter p = k is BuiltInParameter bip ? e.get_Parameter(bip) : e.LookupParameter((string)k);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 1e-6) return p.AsDouble();
            }
            return 0;
        }

        private static double TypeOrInstFt(FamilyInstance fi, params object[] keys)
        {
            double v = ParamFt(fi, keys);
            return v > 0 ? v : ParamFt(fi.Symbol, keys);
        }

        private static string FullName(FamilyInstance fi) => (fi.Symbol?.FamilyName ?? "") + " " + (fi.Symbol?.Name ?? "") + " " + (fi.Name ?? "");

        private static bool Has(string s, params string[] keys) => keys.Any(k => s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

        public static int ParseFireMinutes(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Trim();
            var m = Regex.Match(s, @"(\d+(\.\d+)?)\s*(小時|hr|h|HR|H)");
            if (m.Success) return (int)Math.Round(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60);
            if (s.Contains("半")) return 30;
            m = Regex.Match(s, @"(\d+)\s*(分|min)");
            if (m.Success) return int.Parse(m.Groups[1].Value);
            m = Regex.Match(s, @"^(\d+(\.\d+)?)$");
            if (m.Success)
            {
                double d = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                return d <= 4 ? (int)(d * 60) : (int)d;
            }
            if (Has(s, "甲種", "A種")) return 60;
            if (Has(s, "乙種", "B種")) return 30;
            return 0;
        }

        private void CollectDoors()
        {
            foreach (var src in Reading(SourceParts.Openings))
            foreach (var d in new FilteredElementCollector(src.Doc).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                XYZ p = src.P((d.Location as LocationPoint)?.Point);
                if (p == null) continue;
                var lf = LevelOf(src, d, p);
                if (!_s.KeepDuplicates && lf != null && lf.Doors.Any(o => o.Source != src && o.Point.DistanceTo(p) < 0.25 / FtToM))
                {
                    DuplicatesSkipped++;
                    continue;
                }
                SidesOf(src, d, p, out RoomFact from, out RoomFact to, out P2 normal, out double wallM);
                string name = FullName(d);
                double wFt = TypeOrInstFt(d, BuiltInParameter.DOOR_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM, "寬度", "Width");
                double hFt = TypeOrInstFt(d, BuiltInParameter.DOOR_HEIGHT, BuiltInParameter.FAMILY_HEIGHT_PARAM, "高度", "Height");
                string fire = d.get_Parameter(BuiltInParameter.DOOR_FIRE_RATING)?.AsString()
                              ?? d.Symbol?.get_Parameter(BuiltInParameter.DOOR_FIRE_RATING)?.AsString()
                              ?? d.LookupParameter("防火時效")?.AsString() ?? d.Symbol?.LookupParameter("防火時效")?.AsString() ?? "";
                int fireMin = ParseFireMinutes(fire);
                if (fireMin == 0 && Has(name, "防火")) fireMin = Has(name, "2小時", "2HR") ? 120 : 60;

                bool sliding = Has(name, "拉門", "滑門", "推拉", "橫拉", "Sliding");
                bool shutter = Has(name, "捲門", "鐵捲", "Shutter", "Rolling");
                bool dbl = Has(name, "雙開", "對開", "雙扇", "Double");
                double w = wFt * FtToMm, h = hFt * FtToMm;
                double leaf = dbl ? w / 2 : w;

                var df = new DoorFact
                {
                    Scope = "door", Door = d, Id = d.Id, Source = src, Point = p, From = from, To = to, Level = lf,
                    Normal = normal, WidthM = Math.Max(0.6, w / 1000), DepthM = wallM + 0.6
                };
                df.Label = $"{lf?.Label} 門 {d.Symbol?.Name}（{(from?.Name ?? "室外")} → {(to?.Name ?? "室外")}）";
                var v = new VarBag(lf?.Vars);
                df.Vars = v;
                v["widthMm"] = w > 0 ? (object)w : null;
                v["heightMm"] = h > 0 ? (object)h : null;
                v["clearWidthMm"] = w > 0 ? (object)(w - _s.DoorFrameDeductionMm) : null;
                v["leafWidthMm"] = leaf > 0 ? (object)leaf : null;
                v["leafAreaM2"] = leaf > 0 && h > 0 ? (object)(leaf * h / 1e6) : null;
                v["fireRatingMin"] = (double)fireMin;
                v["isFireDoor"] = fireMin > 0;
                v["isSliding"] = sliding;
                v["isRollingShutter"] = shutter;
                v["isExterior"] = from == null || to == null;
                string fr = from?.Role ?? "outside", tr = to?.Role ?? "outside";
                v["fromRole"] = fr; v["toRole"] = tr;
                bool stairSide = fr == "stair" || tr == "stair" || fr == "smokeLobby" || tr == "smokeLobby";
                v["servesStair"] = stairSide;
                bool safety = (from != null && (from.Role == "smokeLobby" || Has(from.Name, "安全梯"))) || (to != null && (to.Role == "smokeLobby" || Has(to.Name, "安全梯")));
                v["servesSafetyStair"] = safety;
                bool mainEntrance = (from == null || to == null) && lf != null && lf.Index == 1 &&
                                    !new[] { "parking", "mech", "storage", "stair" }.Contains(from?.Role ?? to?.Role ?? "");
                v["isMainEntrance"] = mainEntrance;
                string[] route = { "corridor", "lobby", "elevatorLobby" };
                bool onRoute = (route.Contains(fr) || route.Contains(tr) || mainEntrance) &&
                               !new[] { "mech", "storage", "closet" }.Contains(fr) && !new[] { "mech", "storage", "closet" }.Contains(tr);
                v["isOnAccessibleRoute"] = onRoute || Has(name, "無障礙");
                Doors.Add(df);
                lf?.Doors.Add(df);
            }
        }

        // ───────────────────────── 窗、採光、通風 ─────────────────────────

        private void CollectWindows()
        {
            var daylight = new Dictionary<RoomFact, double>();
            var vent = new Dictionary<RoomFact, double>();
            var seen = new List<Tuple<ModelSource, XYZ>>();
            foreach (var src in Reading(SourceParts.Openings))
            foreach (var w in new FilteredElementCollector(src.Doc).OfCategory(BuiltInCategory.OST_Windows).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                XYZ p = src.P((w.Location as LocationPoint)?.Point);
                if (p != null && !_s.KeepDuplicates)
                {
                    if (seen.Any(x => x.Item1 != src && x.Item2.DistanceTo(p) < 0.25 / FtToM)) { DuplicatesSkipped++; continue; }
                    seen.Add(Tuple.Create(src, p));
                }
                var lf = LevelOf(src, w, p);
                RoomFact from = null, to = null;
                if (p != null) SidesOf(src, w, p, out from, out to, out _, out _);
                string name = FullName(w);
                double wid = TypeOrInstFt(w, BuiltInParameter.WINDOW_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM, "寬度", "Width") * FtToM;
                double hei = TypeOrInstFt(w, BuiltInParameter.WINDOW_HEIGHT, BuiltInParameter.FAMILY_HEIGHT_PARAM, "高度", "Height") * FtToM;
                double sill = (w.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.AsDouble() ?? 0) * FtToM;
                bool exterior = from == null || to == null;
                bool fixedPane = Has(name, "固定", "Fixed", "FIX");
                RoomFact room = from ?? to;
                if (exterior && room != null && wid > 0 && hei > 0)
                {
                    double eff = wid * Math.Max(0, sill + hei - Math.Max(sill, 0.75));
                    daylight[room] = (daylight.TryGetValue(room, out double a) ? a : 0) + eff;
                    if (!fixedPane) vent[room] = (vent.TryGetValue(room, out double b) ? b : 0) + wid * hei * _s.OpenableRatio;
                }
                var fi = new FactItem { Scope = "window", Id = w.Id, Source = src, Level = lf, Label = $"{lf?.Label} 窗 {w.Symbol?.Name}（{room?.Name ?? "?"}）" };
                var v = new VarBag(lf?.Vars);
                fi.Vars = v;
                v["sillMm"] = sill * 1000;
                v["widthMm"] = wid * 1000;
                v["heightMm"] = hei * 1000;
                v["isExterior"] = exterior;
                v["isOperable"] = !fixedPane;
                v["hasGuardOutside"] = Has(name, "欄杆");
                v["roomRole"] = room?.Role ?? "";
                Windows.Add(fi);
            }
            foreach (var r in Rooms)
            {
                daylight.TryGetValue(r, out double dl);
                vent.TryGetValue(r, out double vt);
                r.Vars["daylightRatio"] = r.AreaM2 > 0 ? dl / r.AreaM2 : 0;
                r.Vars["ventRatio"] = r.AreaM2 > 0 ? vt / r.AreaM2 : 0;
                r.Vars["ventAreaM2"] = vt;
                r.Vars["hasMechanicalVentilation"] = r.Room.LookupParameter("機械通風")?.AsInteger() == 1;
            }
        }

        // ───────────────────────── 樓梯 ─────────────────────────

        private void CollectStairs()
        {
            foreach (var src in Reading(SourceParts.Circulation))
            {
            var railings = new FilteredElementCollector(src.Doc).OfClass(typeof(Railing)).Cast<Railing>().ToList();
            foreach (var st in new FilteredElementCollector(src.Doc).OfClass(typeof(Stairs)).Cast<Stairs>())
            {
                BoundingBoxXYZ bb = src.Box(st.get_BoundingBox(null));
                if (bb == null) continue;
                if (!_s.KeepDuplicates && Stairs.Any(o => o.Source != src && Math.Abs(o.BaseZFt - bb.Min.Z) < 1.0 &&
                        OverlapRatio(o, bb.Min.X * FtToM, bb.Min.Y * FtToM, bb.Max.X * FtToM, bb.Max.Y * FtToM) > 0.8))
                {
                    DuplicatesSkipped++;
                    continue;
                }
                string tname = src.Doc.GetElement(st.GetTypeId())?.Name ?? "";
                string info = tname + " " + st.Name + " " + (st.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "") +
                              " " + (st.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "");
                var center = (bb.Min + bb.Max) / 2;
                var roomAt = RoomAt(new XYZ(center.X, center.Y, bb.Min.Z + 1.5));
                string roomInfo = roomAt?.Name ?? "";
                var sf = new StairFact
                {
                    Scope = "stair", Stairs = st, Id = st.Id, Source = src,
                    MinXm = bb.Min.X * FtToM, MinYm = bb.Min.Y * FtToM, MaxXm = bb.Max.X * FtToM, MaxYm = bb.Max.Y * FtToM,
                    BaseZFt = bb.Min.Z, TopZFt = bb.Max.Z
                };
                sf.IsSpecial = Has(info + roomInfo, "特別安全梯");
                sf.IsSafety = sf.IsSpecial || Has(info + roomInfo, "安全梯");
                sf.IsOutdoor = Has(info + roomInfo, "戶外", "室外", "Exterior") || roomAt == null;
                sf.IsAccessible = Has(info, "無障礙");
                sf.Level = LevelAt(bb.Min.Z + 0.1);
                sf.Label = $"{sf.Level?.Label} 樓梯 {tname}{(sf.IsSpecial ? "（特別安全梯）" : sf.IsSafety ? "（安全梯）" : "")}";

                double riser = st.ActualRiserHeight * FtToMm, tread = st.ActualTreadDepth * FtToMm;
                double minRunW = double.MaxValue, maxFlight = 0;
                var runIds = st.GetStairsRuns();
                foreach (ElementId rid in runIds)
                {
                    if (!(src.Doc.GetElement(rid) is StairsRun run)) continue;
                    minRunW = Math.Min(minRunW, run.ActualRunWidth * FtToMm);
                    maxFlight = Math.Max(maxFlight, (run.TopElevation - run.BaseElevation) * FtToMm);
                    try
                    {
                        CurveLoop path = run.GetStairsPath();
                        var curves = path.ToList();
                        if (curves.Count > 0)
                        {
                            XYZ a = src.P(curves[0].GetEndPoint(0)), b = src.P(curves[curves.Count - 1].GetEndPoint(1));
                            double baseZ = bb.Min.Z + run.BaseElevation - st.BaseElevation;
                            sf.EntryPoints.Add(new XYZ(a.X, a.Y, baseZ));
                            sf.EntryPoints.Add(new XYZ(b.X, b.Y, baseZ + (run.TopElevation - run.BaseElevation)));
                        }
                    }
                    catch { }
                }
                double minLand = double.MaxValue;
                int landingCount = 0;
                foreach (ElementId lid in st.GetStairsLandings())
                {
                    if (!(src.Doc.GetElement(lid) is StairsLanding land)) continue;
                    landingCount++;
                    try
                    {
                        var pts = land.GetFootprintBoundary().SelectMany(c => c.Tessellate()).Select(q => src.P(q)).Select(q => new P2(q.X * FtToM, q.Y * FtToM)).ToList();
                        if (pts.Count >= 3)
                        {
                            P2 ax = Geo2D.PrincipalAxis(pts, out P2 m);
                            P2 perp = new P2(-ax.Y, ax.X);
                            double l1 = pts.Max(q => (q - m).Dot(ax)) - pts.Min(q => (q - m).Dot(ax));
                            double l2 = pts.Max(q => (q - m).Dot(perp)) - pts.Min(q => (q - m).Dot(perp));
                            minLand = Math.Min(minLand, Math.Min(l1, l2) * 1000);
                        }
                    }
                    catch { }
                }

                int handrails = railings.Count(rl => rl.HostId == st.Id);
                var v = new VarBag(sf.Level?.Vars);
                sf.Vars = v;
                v["riserMm"] = riser;
                v["treadMm"] = tread;
                v["twoRPlusTMm"] = 2 * riser + tread;
                v["minRunWidthMm"] = minRunW < double.MaxValue ? (object)minRunW : null;
                v["minLandingWidthMm"] = minLand < double.MaxValue ? (object)minLand : null;
                v["minLandingDepthMm"] = minLand < double.MaxValue ? (object)minLand : null;
                v["landingCount"] = (double)landingCount;
                v["maxFlightRiseMm"] = maxFlight;
                v["totalRiseMm"] = (bb.Max.Z - bb.Min.Z) * FtToMm;
                v["handrailCount"] = (double)handrails;
                v["hasWallOneSide"] = !sf.IsOutdoor;
                v["isSafety"] = sf.IsSafety;
                v["isSpecial"] = sf.IsSpecial;
                v["isOutdoor"] = sf.IsOutdoor;
                v["isServiceOnly"] = Has(info, "服務梯", "服務專用");
                v["isAccessibleStair"] = sf.IsAccessible;
                v["headroomMinMm"] = Headroom(src, st, runIds, bb);
                Stairs.Add(sf);
            }
            }

            // 依平面位置把各層樓梯串成「梯座」
            int shaft = 0;
            foreach (var a in Stairs)
            {
                if (a.Shaft != 0) continue;
                a.Shaft = ++shaft;
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (var b in Stairs.Where(x => x.Shaft == 0))
                    {
                        if (Stairs.Where(x => x.Shaft == shaft).Any(x => OverlapRatio(x, b) > 0.5))
                        {
                            b.Shaft = shaft; grew = true;
                        }
                    }
                }
            }
        }

        private static double OverlapRatio(StairFact a, StairFact b) => OverlapRatio(a, b.MinXm, b.MinYm, b.MaxXm, b.MaxYm);

        private static double OverlapRatio(StairFact a, double minX, double minY, double maxX, double maxY)
        {
            double ix = Math.Min(a.MaxXm, maxX) - Math.Max(a.MinXm, minX);
            double iy = Math.Min(a.MaxYm, maxY) - Math.Max(a.MinYm, minY);
            if (ix <= 0 || iy <= 0) return 0;
            double aa = (a.MaxXm - a.MinXm) * (a.MaxYm - a.MinYm), ab = (maxX - minX) * (maxY - minY);
            return ix * iy / Math.Max(1e-6, Math.Min(aa, ab));
        }

        private object Headroom(ModelSource src, Stairs st, ICollection<ElementId> runIds, BoundingBoxXYZ bb)
        {
            if (_view3D == null) return null;
            var cats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Stairs
            };
            var ri = new ReferenceIntersector(new ElementMulticategoryFilter(cats), FindReferenceTarget.Face, _view3D) { FindReferencesInRevitLinks = true };
            double min = double.MaxValue;
            foreach (ElementId rid in runIds)
            {
                if (!(src.Doc.GetElement(rid) is StairsRun run)) continue;
                try
                {
                    var curves = run.GetStairsPath().ToList();
                    double rise = run.TopElevation - run.BaseElevation;
                    double total = curves.Sum(c => c.Length);
                    if (total < 1e-6) continue;
                    double baseZ = bb.Min.Z + run.BaseElevation - st.BaseElevation;
                    for (int i = 1; i <= 5; i++)
                    {
                        double t = total * i / 6.0, acc = 0;
                        foreach (var c in curves)
                        {
                            if (acc + c.Length >= t)
                            {
                                XYZ p = src.P(c.Evaluate((t - acc) / c.Length, true));
                                double z = baseZ + rise * t / total + 0.1;
                                var hit = ri.FindNearest(new XYZ(p.X, p.Y, z), XYZ.BasisZ);
                                if (hit != null && hit.Proximity < 30) min = Math.Min(min, (hit.Proximity + 0.1) * FtToMm);
                                break;
                            }
                            acc += c.Length;
                        }
                    }
                }
                catch { }
            }
            return min < double.MaxValue ? (object)min : null;
        }

        // ───────────────────────── 欄杆、坡道 ─────────────────────────

        private void CollectRailings()
        {
            foreach (var src in Reading(SourceParts.Circulation))
            foreach (var rl in new FilteredElementCollector(src.Doc).OfClass(typeof(Railing)).Cast<Railing>())
            {
                BoundingBoxXYZ bb = src.Box(rl.get_BoundingBox(null));
                if (bb == null) continue;
                var type = src.Doc.GetElement(rl.GetTypeId()) as RailingType;
                double h = (type?.TopRailHeight ?? 0) * FtToMm;
                Element host = rl.HostId != ElementId.InvalidElementId ? src.Doc.GetElement(rl.HostId) : null;
                var hostStair = host != null ? Stairs.FirstOrDefault(s => s.Source == src && s.Id == host.Id) : null;
                bool onRamp = host?.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Ramps;
                var lf = LevelAt(bb.Min.Z + 0.1);
                var fi = new FactItem { Scope = "railing", Id = rl.Id, Source = src, Level = lf, Label = $"{lf?.Label} 欄杆 {type?.Name}" };
                var v = new VarBag(lf?.Vars);
                fi.Vars = v;
                v["heightMm"] = h > 0 ? (object)h : null;
                v["isHandrail"] = hostStair != null || onRamp;
                v["isGuard"] = (hostStair == null && !onRamp) || (hostStair != null && hostStair.IsOutdoor);
                v["isAccessible"] = (hostStair?.IsAccessible ?? false) || onRamp;
                v["isPrimarySchool"] = _s.UseGroups.Contains("D-3");
                Railings.Add(fi);
            }
        }

        private void CollectRamps()
        {
            foreach (var src in Reading(SourceParts.Circulation))
            {
            var els = new FilteredElementCollector(src.Doc).OfCategory(BuiltInCategory.OST_Ramps).WhereElementIsNotElementType().ToList();
            var floors = new FilteredElementCollector(src.Doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType()
                .Where(f => Has((f.Name ?? "") + " " + (src.Doc.GetElement(f.GetTypeId())?.Name ?? ""), "車道", "坡道", "Ramp")).ToList();
            var railings = new FilteredElementCollector(src.Doc).OfClass(typeof(Railing)).Cast<Railing>().ToList();
            foreach (var e in els.Concat(floors))
            {
                BoundingBoxXYZ bb = src.Box(e.get_BoundingBox(null));
                if (bb == null) continue;
                string name = (e.Name ?? "") + " " + (src.Doc.GetElement(e.GetTypeId())?.Name ?? "");
                double dx = (bb.Max.X - bb.Min.X) * FtToMm, dy = (bb.Max.Y - bb.Min.Y) * FtToMm;
                double thick = e is Floor fl ? (fl.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() ?? 0) * FtToMm : 0;
                double rise = Math.Max(0, (bb.Max.Z - bb.Min.Z) * FtToMm - thick);
                double len = Math.Max(dx, dy), wid = Math.Min(dx, dy);
                bool vehicle = Has(name, "車道", "汽車", "機車");
                var lf = LevelAt(bb.Min.Z + 0.1);
                var fi = new FactItem { Scope = "ramp", Id = e.Id, Source = src, Level = lf, Label = $"{lf?.Label} {(vehicle ? "車道" : "坡道")} {name.Trim()}" };
                var v = new VarBag(lf?.Vars);
                fi.Vars = v;
                v["riseMm"] = rise;
                v["slope"] = len > 0 ? rise / len : 0;
                v["widthMm"] = wid;
                v["isVehicle"] = vehicle;
                v["isAccessible"] = !vehicle && (Has(name, "無障礙") || _s.AccessibilityApplies);
                v["replacesStair"] = Has(name, "代替樓梯", "取代樓梯");
                v["maxRunRiseMm"] = rise;
                v["isTwoWayLane"] = Has(name, "雙向", "雙車道");
                v["handrailCount"] = (double)railings.Count(r => r.HostId == e.Id);
                Ramps.Add(fi);
            }
            }
            if (Ramps.Count > 0) Notes.Add("坡道坡度以外框高差 ÷ 長邊估算，折返或彎曲坡道請以剖面確認。");
        }

        // ───────────────────────── 停車、昇降機、設備 ─────────────────────────

        private void CollectParking()
        {
            var parkingPts = new List<Tuple<ModelSource, XYZ>>();
            foreach (var src in Reading(SourceParts.Parking))
            foreach (var p in new FilteredElementCollector(src.Doc).OfCategory(BuiltInCategory.OST_Parking).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                XYZ pt = src.P((p.Location as LocationPoint)?.Point);
                if (pt != null && !_s.KeepDuplicates)
                {
                    if (parkingPts.Any(o => o.Item1 != src && o.Item2.DistanceTo(pt) < 0.15 / FtToM)) { DuplicatesSkipped++; continue; }
                    parkingPts.Add(Tuple.Create(src, pt));
                }
                string name = FullName(p);
                double w = TypeOrInstFt(p, "寬度", "車位寬", "Width", "寬") * FtToMm;
                double l = TypeOrInstFt(p, "長度", "車位長", "Length", "Depth", "深度", "長") * FtToMm;
                BoundingBoxXYZ bb = p.get_BoundingBox(null);
                if ((w <= 0 || l <= 0) && bb != null)
                {
                    Transform t = p.GetTransform();
                    var corners = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, 0), new XYZ(bb.Max.X, bb.Min.Y, 0) };
                    double ex = corners.Max(c => c.DotProduct(t.BasisX)) - corners.Min(c => c.DotProduct(t.BasisX));
                    double ey = corners.Max(c => c.DotProduct(t.BasisY)) - corners.Min(c => c.DotProduct(t.BasisY));
                    w = Math.Min(ex, ey) * FtToMm; l = Math.Max(ex, ey) * FtToMm;
                }
                var lf = LevelOf(src, p, pt);
                var room = pt != null ? RoomAt(pt + new XYZ(0, 0, 1)) : null;
                var fi = new FactItem { Scope = "parking", Id = p.Id, Source = src, Level = lf, Label = $"{lf?.Label} 車位 {p.Symbol?.Name}" };
                var v = new VarBag(lf?.Vars);
                fi.Vars = v;
                v["widthMm"] = w > 0 ? (object)w : null;
                v["lengthMm"] = l > 0 ? (object)l : null;
                v["isAccessible"] = Has(name, "無障礙", "殘障", "身障", "Accessible");
                v["isMotorcycle"] = Has(name, "機車", "Motor");
                v["isMechanical"] = Has(name, "機械");
                v["isLarge"] = Has(name, "大客車", "大型", "Bus");
                v["isIndoor"] = (lf?.IsBasement ?? false) || room?.Role == "parking";
                double ang = 0;
                Parameter ap = p.LookupParameter("角度") ?? p.Symbol?.LookupParameter("角度");
                if (ap != null && ap.StorageType == StorageType.Double) ang = ap.AsDouble() * 180 / Math.PI;
                v["angleDeg"] = ang;
                Parking.Add(fi);
            }
        }

        private string DeviceKind(FamilyInstance fi)
        {
            if (fi.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Sprinklers) return "sprinkler";
            string name = FullName(fi);
            foreach (var d in _kw.Devices)
                if (d.Keywords.Any(k => !string.IsNullOrEmpty(k) && name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    return d.Kind;
            return null;
        }

        private void CollectEquipment()
        {
            var cats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_FireAlarmDevices, BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices, BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_SecurityDevices,
                BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_NurseCallDevices
            };
            var elevatorXY = new List<P2>();
            foreach (var src in Reading(SourceParts.Equipment))
            foreach (var fi in new FilteredElementCollector(src.Doc).WherePasses(new ElementMulticategoryFilter(cats))
                         .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                string kind = DeviceKind(fi);
                if (kind == null) continue;
                XYZ p = src.P((fi.Location as LocationPoint)?.Point);
                BoundingBoxXYZ bb = src.Box(fi.get_BoundingBox(null));
                if (p == null && bb != null) p = (bb.Min + bb.Max) / 2;
                if (p == null) continue;
                var lf = LevelOf(src, fi, p);
                if (kind != "elevator" && !_s.KeepDuplicates && lf != null &&
                    lf.Devices.Any(o => o.Kind == kind && o.Source != src && o.Point.DistanceTo(p) < 0.15 / FtToM))
                {
                    DuplicatesSkipped++;
                    continue;
                }
                string name = FullName(fi);

                if (kind == "elevator")
                {
                    var xy = new P2(p.X * FtToM, p.Y * FtToM);
                    if (elevatorXY.Any(q => q.Dist(xy) < 1.0)) continue;   // 同一昇降路只算一座
                    elevatorXY.Add(xy);
                    var ef = new FactItem { Scope = "elevator", Id = fi.Id, Source = src, Level = lf, Label = $"昇降機 {fi.Symbol?.Name}" };
                    var ev = new VarBag(lf?.Vars);
                    ef.Vars = ev;
                    ev["isEmergency"] = Has(name, "緊急");
                    ev["isAccessible"] = Has(name, "無障礙") || _s.AccessibilityApplies;
                    double dw = TypeOrInstFt(fi, "門寬", "出入口寬", "Door Width", "門淨寬") * FtToMm;
                    double cd = TypeOrInstFt(fi, "機廂深", "車廂深", "機廂深度", "Car Depth", "Cab Depth") * FtToMm;
                    ev["doorWidthMm"] = dw > 0 ? (object)dw : null;
                    ev["carDepthMm"] = cd > 0 ? (object)cd : null;
                    Parameter sp = fi.LookupParameter("速度") ?? fi.Symbol?.LookupParameter("速度");
                    ev["speedMpm"] = sp != null && sp.StorageType == StorageType.Double ? (object)sp.AsDouble() : null;
                    Elevators.Add(ef);
                    continue;
                }

                double lev = lf?.ElevFt ?? 0;
                var df = new DeviceFact { Scope = "device", Kind = kind, Id = fi.Id, Source = src, Point = p, Level = lf, Label = $"{lf?.Label} {kind} {fi.Symbol?.Name}" };
                var v = new VarBag(lf?.Vars);
                df.Vars = v;
                v["kind"] = kind;
                double elevParam = fi.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM)?.AsDouble() ?? double.NaN;
                v["mountHeightMm"] = (double.IsNaN(elevParam) ? (p.Z - lev) : elevParam) * FtToMm;
                v["topHeightMm"] = bb != null ? (object)((bb.Max.Z - lev) * FtToMm) : null;
                Parameter wp = fi.LookupParameter("重量") ?? fi.Symbol?.LookupParameter("重量");
                v["weightKg"] = wp != null && wp.StorageType == StorageType.Double ? (object)wp.AsDouble() : 10.0;
                double valve = TypeOrInstFt(fi, "開關高度", "Valve Height") * FtToMm;
                v["valveHeightMm"] = valve > 0 ? valve : 0.0;
                Devices.Add(df);
                lf?.Devices.Add(df);
            }
        }

        private void CollectAreas()
        {
            foreach (var src in Reading(SourceParts.Areas))
            foreach (var a in new FilteredElementCollector(src.Doc).OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType().OfType<Area>())
            {
                if (a.Area <= 1e-6) continue;
                string scheme = a.AreaScheme?.Name ?? "";
                if (!Has(scheme, "防火", "Fire")) continue;
                var lf = LevelOf(src, a, null);
                var fi = new FactItem { Scope = "area", Id = a.Id, Source = src, Level = lf, Label = $"{lf?.Label} 防火區劃 {a.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()}" };
                var v = new VarBag(lf?.Vars);
                fi.Vars = v;
                v["scheme"] = "fire";
                v["areaM2"] = a.Area * Ft2ToM2;
                Parameter fc = a.LookupParameter("耐燃等級");
                v["finishClass"] = fc != null && fc.StorageType == StorageType.Integer ? (double)fc.AsInteger() : 0.0;
                v["finishIncludesSubstrate"] = a.LookupParameter("含底材")?.AsInteger() == 1;
                string use = a.LookupParameter("使用類組")?.AsString();
                v["roomUse"] = string.IsNullOrWhiteSpace(use) ? (_s.UseGroups.FirstOrDefault() ?? "") : use.Trim().ToUpperInvariant();
                Areas.Add(fi);
            }
        }

        // ───────────────────────── 專案與樓層變數 ─────────────────────────

        private bool HasGroup(params string[] prefixes)
        {
            foreach (string g in _s.UseGroups)
            {
                string u = g.Trim().ToUpperInvariant();
                foreach (string p in prefixes)
                {
                    string q = p.Trim().ToUpperInvariant();
                    if (u == q || u.StartsWith(q + "-") || (q.Length == 1 && u.StartsWith(q))) return true;
                }
            }
            return false;
        }

        private bool Fire(string prefix) => _s.FireClasses.Any(f => f.StartsWith(prefix, StringComparison.Ordinal));

        private double FloorArea(LevelFact l)
        {
            double a = l.Rooms.Sum(r => r.AreaM2);
            if (a > 0) return a;
            double sum = 0;
            foreach (var src in Reading(SourceParts.Spaces))
                foreach (var f in new FilteredElementCollector(src.Doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType())
                    if (f.LevelId != null && _levelByKey.TryGetValue(K(src, f.LevelId), out var fl) && fl == l)
                        sum += (f.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0) * Ft2ToM2;
            return sum;
        }

        private void FillProjectVars()
        {
            var p = Project;
            var normal = Levels.Where(l => l.Index != 0).ToList();
            int floorsAbove = _s.FloorsAboveOverride > 0 ? _s.FloorsAboveOverride : normal.Count(l => l.Index > 0);
            int floorsBelow = _s.FloorsBelowOverride > 0 ? _s.FloorsBelowOverride : normal.Count(l => l.Index < 0);
            var roof = Levels.FirstOrDefault(l => l.IsRoof);
            double heightM;
            if (_s.HeightOverrideM > 0) heightM = _s.HeightOverrideM;
            else if (roof != null) heightM = (roof.ElevFt - _ground.ElevFt) * FtToM;
            else
            {
                var top = normal.Where(l => l.Index > 0).OrderBy(l => l.ElevFt).LastOrDefault();
                heightM = top != null ? (top.ElevFt - _ground.ElevFt) * FtToM + 3.0 : 0;
                Notes.Add("找不到屋頂層（RF），建築物高度以最高樓層 + 3 m 估算，可在專案條件中輸入。");
            }

            var areas = Levels.ToDictionary(l => l, FloorArea);

            p["floorsAbove"] = floorsAbove;
            p["floorsBelow"] = floorsBelow;
            p["buildingHeightM"] = heightM;
            p["isHighRise"] = heightM >= 50 || floorsAbove >= 16;
            p["totalFloorAreaM2"] = areas.Values.Sum();
            p["maxFloorAreaM2"] = areas.Where(kv => kv.Key.Index > 0).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
            p["maxBasementAreaM2"] = areas.Where(kv => kv.Key.Index < 0).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
            p["maxFloorAreaAbove10M2"] = areas.Where(kv => kv.Key.Index > 10).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
            p["sumFloorAreaAbove10M2"] = areas.Where(kv => kv.Key.Index > 10).Sum(kv => kv.Value);
            p["maxFloorAreaAbove5M2"] = areas.Where(kv => kv.Key.Index >= 5).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
            double habitable = Rooms.Where(r => r.Habitable).Sum(r => r.AreaM2);
            p["habitableAreaM2"] = habitable;
            p["roofProjectionAreaM2"] = areas.Where(kv => kv.Key.IsRoofProjection).Sum(kv => kv.Value);

            // 使用者輸入
            p["zone"] = _s.Zone ?? "";
            p["volumeControl"] = _s.VolumeControl;
            p["inUrbanPlan"] = _s.InUrbanPlan;
            p["roadWidthM"] = _s.RoadWidthM;
            p["setbackM"] = _s.SetbackM;
            p["siteAreaM2"] = _s.SiteAreaM2;
            p["buildingAreaM2"] = _s.BuildingAreaM2;
            p["maxCoverageRatio"] = _s.MaxCoverageRatio;
            p["maxFarRatio"] = _s.MaxFarRatio;
            p["slopeland"] = _s.Slopeland;
            p["fireproof"] = _s.Fireproof;
            p["accessibilityApplies"] = _s.AccessibilityApplies;
            p["isPublicBuilding"] = _s.PublicBuilding;
            p["isApartment"] = _s.IsApartment || (HasGroup("H-2") && Rooms.Count(r => r.Role == "living") >= 3);
            p["isElderlyHousing"] = _s.IsElderlyHousing;
            p["hasVoid"] = _s.HasVoid;
            p["tc23Exception"] = _s.Tc23Exception;
            p["tc24Exception"] = _s.Tc24Exception;
            p["hasLiveAudienceStudio"] = _s.LiveAudienceStudio;
            p["isTownhouseRule"] = false;
            p["isApartmentRule"] = HasGroup("H-2") && areas.Where(kv => kv.Key.Index >= 2).Select(kv => kv.Value).DefaultIfEmpty(0).Max() <= 240;
            p["sprinklerFastResponse"] = _s.SprinklerFastResponse;

            // 計數
            int sprinklers = Devices.Count(d => d.Kind == "sprinkler");
            p["sprinklered"] = _s.SprinklerMode == "yes" || (_s.SprinklerMode == "auto" && sprinklers > 0);
            p["elevatorCount"] = Elevators.Count;
            p["emergencyElevatorCount"] = Elevators.Count(e => Expr.Bool(e.Vars.Values["isEmergency"]));
            p["hasElevatorToRoof"] = Elevators.Count > 0;
            var speeds = Elevators.Select(e => e.Vars.Values["speedMpm"]).OfType<double>().ToList();
            p["elevatorSpeedMpm"] = speeds.Count > 0 ? (object)speeds.Max() : null;
            int parking = Parking.Count(x => !Expr.Bool(x.Vars.Values["isMotorcycle"]));
            p["parkingCount"] = parking;
            p["accessibleParkingCount"] = Parking.Count(x => !Expr.Bool(x.Vars.Values["isMotorcycle"]) && Expr.Bool(x.Vars.Values["isAccessible"]));
            p["requiredAccessibleParking"] = RequiredAccessibleParking(parking, HasGroup("H-2") && !HasGroup("A", "B", "C", "D", "E", "F", "G", "H-1", "I"));
            p["requiredParkingCount"] = RequiredParking();
            p["toiletRoomCount"] = Rooms.Count(r => r.Role == "toilet" || r.Role == "accessibleToilet");
            p["accessibleToiletCount"] = Rooms.Count(r => r.Role == "accessibleToilet");
            p["disasterCenterAreaM2"] = Rooms.Where(r => r.Role == "disasterCenter").Select(r => r.AreaM2).DefaultIfEmpty(0).Max();
            p["roofRefugeAreaM2"] = Rooms.Where(r => r.Role == "roofRefuge").Sum(r => r.AreaM2);
            p["refugeExitCount"] = Doors.Count(d => d.Level == _ground && Expr.Bool(d.Vars.Values["isExterior"]));
            p["publicUseAreaM2"] = HasGroup("A", "B", "D", "E", "F", "G", "H-1") ? areas.Values.Sum() : 0.0;
            p["hasKitchenOrBoiler"] = Rooms.Any(r => r.Role == "kitchen" || Has(r.Name, "鍋爐", "放映", "變電", "配電"));
            p["lightningDeviceCount"] = Devices.Count(d => d.Kind == "lightning");
            p["wcCount"] = Devices.Count(d => d.Kind == "wc");
            p["requiredWcCount"] = RequiredWc(habitable);
            p["parapetHeightMm"] = ParapetHeight();
            p["specialStairBuildingCount"] = Stairs.Where(s => s.IsSpecial).Select(s => s.Shaft).Distinct().Count();
            p["accessibleStairCount"] = Stairs.Where(s => s.IsAccessible || IsAccessibleGeometry(s)).Select(s => s.Shaft).Distinct().Count();

            // 消防場所
            p["fireA"] = Fire("甲");
            p["fireA1"] = _s.FireClasses.Contains("甲1");
            p["fireB"] = Fire("乙");
            p["fireB1"] = _s.FireClasses.Contains("乙1");
            p["fireC"] = Fire("丙") || Rooms.Any(r => r.Role == "parking");
            p["fireD"] = Fire("丁");
            p["fireUnderground"] = _s.FireClasses.Contains("戊3");
            p["fireCare"] = _s.FireClasses.Contains("甲6長照");
            p["fireA1AreaM2"] = _s.FireClasses.Contains("甲1") ? areas.Values.Sum() : 0.0;

            // §89 適用、樓梯表類別
            bool art89 = HasGroup("A", "B", "D", "E", "F", "G", "H") || floorsAbove >= 3 || areas.Values.Sum() > 1000 || floorsBelow > 0;
            p["art89Applies"] = art89;
            int stairClass = 4;
            if (HasGroup("D-3")) stairClass = 1;
            else if (HasGroup("D-4", "F-1", "A-1", "B-1") || (HasGroup("B-2") && areas.Values.Sum() >= 1500)) stairClass = 2;
            else if (Levels.Any(l => l.Index > 0 && l.Rooms.Where(r => r.Habitable).Sum(r => r.AreaM2) > 200) ||
                     Levels.Any(l => l.Index < 0 && areas[l] > 200)) stairClass = 3;
            p["stairClass"] = stairClass;

            // 函式
            p.Functions["has"] = args => HasGroup(args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)).ToArray());
            p.Functions["deviceCount"] = args => (double)Devices.Count(d => d.Kind == (string)args[0]) +
                                                 ((string)args[0] == "elevator" ? Elevators.Count : 0);

            // 樓層層級的面積變數
            foreach (var l in Levels)
            {
                var v = l.Vars;
                v["floorAreaM2"] = areas[l];
                v["levelHabitableAreaM2"] = l.Rooms.Where(r => r.Habitable).Sum(r => r.AreaM2);
            }
        }

        private bool IsAccessibleGeometry(StairFact s)
        {
            try
            {
                double r = Expr.Num(s.Vars.Values["riserMm"]), t = Expr.Num(s.Vars.Values["treadMm"]);
                double tw = 2 * r + t;
                return r <= 160 && t >= 260 && tw >= 550 && tw <= 650;
            }
            catch { return false; }
        }

        private double ParapetHeight()
        {
            double max = 0;
            foreach (var src in Reading(SourceParts.Spaces))
            foreach (var w in new FilteredElementCollector(src.Doc).OfClass(typeof(Wall)).Cast<Wall>())
            {
                string n = (w.Name ?? "") + " " + (w.WallType?.Name ?? "");
                if (!Has(n, "女兒牆", "Parapet")) continue;
                double h = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0;
                max = Math.Max(max, h * FtToMm);
            }
            return max;
        }

        public static double RequiredAccessibleParking(int n, bool residential)
        {
            if (n <= 0) return 0;
            if (n <= 50) return 1;
            if (residential)
            {
                if (n <= 550) return 2 + Math.Ceiling((n - 150) / 100.0 - 1e-9) * (n > 150 ? 1 : 0);
                return 6 + Math.Ceiling((n - 550) / 100.0);
            }
            if (n <= 550) return 2 + Math.Max(0, Math.Ceiling((n - 100) / 50.0));
            return 11 + Math.Ceiling((n - 550) / 50.0);
        }

        private double RequiredParking()
        {
            if (_s.RequiredParkingOverride > 0) return _s.RequiredParkingOverride;
            bool urban = _s.InUrbanPlan;
            double n = 0;
            if (_s.ParkingArea1 > 300) n += Math.Ceiling((_s.ParkingArea1 - 300) / (urban ? 150.0 : 250.0));
            if (_s.ParkingArea2 > 500) n += Math.Ceiling((_s.ParkingArea2 - 500) / (urban ? 150.0 : 300.0));
            if (_s.ParkingArea3 > 500) n += Math.Ceiling((_s.ParkingArea3 - 500) / (urban ? 200.0 : 350.0));
            if (_s.ParkingArea4 > 500) n += Math.Ceiling((_s.ParkingArea4 - 500) / (urban ? 250.0 : 350.0));
            return n;
        }

        /// <summary>建築設備編第 37 條：辦公廳（第四類）與其他供公眾使用（第九類）之大便器數量。</summary>
        private double RequiredWc(double habitableM2)
        {
            int type = _s.WcBuildingType;
            if (type == 0) type = HasGroup("G-2") ? 4 : (HasGroup("A", "B", "D", "E", "F", "G") ? 9 : 0);
            if (type != 4 && type != 9) return 0;
            double occ = _s.OccupantsOverride > 0 ? _s.OccupantsOverride : Math.Ceiling(habitableM2 * (type == 4 ? 0.1 : 0.2));
            if (occ <= 0) return 0;
            if (type == 4)
            {
                if (occ <= 15) return 2;
                if (occ <= 35) return 3;
                if (occ <= 80) return 4;
                if (occ <= 110) return 5;
                if (occ <= 150) return 8;
                double extra = occ - 150;
                return 8 + Math.Ceiling(extra / 2 / 120) + Math.Ceiling(extra / 2 / 30);
            }
            if (occ <= 50) return 3;
            if (occ <= 100) return 5;
            if (occ <= 200) return 9;
            double ex = occ - 200;
            return 9 + Math.Ceiling(ex / 2 / 120) + Math.Ceiling(ex / 2 / 30);
        }

        private bool StairServes(int shaft, LevelFact l)
        {
            var group = Stairs.Where(s => s.Shaft == shaft).ToList();
            double lo = group.Min(s => s.BaseZFt), hi = group.Max(s => s.TopZFt);
            double g = _ground.ElevFt;
            if (l.Index > 0 || l.IsRoof || l.IsRoofProjection) return lo <= g + 1.0 && hi >= l.ElevFt - 1.0;
            return hi >= g - 1.0 && lo <= l.ElevFt + 1.0;
        }

        private void FillLevelVars()
        {
            int floorsAbove = (int)Expr.Num(Project.Values["floorsAbove"]);
            var shafts = Stairs.Select(s => s.Shaft).Distinct().ToList();
            for (int i = 0; i < Levels.Count; i++)
            {
                var l = Levels[i];
                var v = l.Vars;
                v["levelIndex"] = (double)l.Index;
                v["isBasement"] = l.IsBasement;
                v["isRefugeLevel"] = l == _ground;
                v["isRoofLevel"] = l.IsRoof;
                v["isRoofProjection"] = l.IsRoofProjection;
                var next = i + 1 < Levels.Count ? Levels[i + 1] : null;
                v["storeyHeightMm"] = next != null ? (object)((next.ElevFt - l.ElevFt) * FtToMm) : null;
                var roof = Levels.FirstOrDefault(x => x.IsRoof);
                if (l.IsRoofProjection && roof != null)
                    v["projectionHeightMm"] = next != null ? (object)((next.ElevFt - roof.ElevFt) * FtToMm) : null;
                v["hasHabitable"] = l.Rooms.Any(r => r.Habitable);
                v["levelHasParking"] = l.Rooms.Any(r => r.Role == "parking");
                v["smokeLobbyAreaM2"] = l.Rooms.Where(r => r.Role == "smokeLobby").Sum(r => r.AreaM2);

                var serving = shafts.Where(s => StairServes(s, l)).ToList();
                v["directStairCount"] = (double)serving.Count;
                v["safetyStairCount"] = (double)serving.Count(s => Stairs.Any(x => x.Shaft == s && x.IsSafety));
                v["specialStairCount"] = (double)serving.Count(s => Stairs.Any(x => x.Shaft == s && x.IsSpecial));
                v["specialOrOutdoorStairCount"] = (double)serving.Count(s => Stairs.Any(x => x.Shaft == s && (x.IsSpecial || (x.IsOutdoor && x.IsSafety))));

                double area = Expr.Num(v.Values["floorAreaM2"]);
                int type = 0;
                bool exempt961 = _s.Fireproof && floorsAbove <= 5 && _s.UseGroups.Count > 0 && _s.UseGroups.All(g => g == "D-3" || g == "D-4" || g == "H-2");
                if (l.Index >= 3 && l.Index <= 5 && !exempt961) type = 1;
                if ((l.Index >= 6 && l.Index <= 14) || l.Index == -2) type = 2;
                if (l.Index >= 15 || l.Index <= -3) type = area <= 100 ? 2 : 3;
                v["requiredStairType"] = (double)type;

                bool needExitSign = Fire("甲") || HasGroup("F-3") || l.IsBasement || l.Index >= 11;
                v["levelRequiresExitSign"] = needExitSign;

                // 該層消防設備函式
                var lev = l;
                v.Functions["deviceCountOnLevel"] = a => (double)lev.Devices.Count(d => d.Kind == (string)a[0]);
                v.Functions["maxHorizToDevice"] = a => MaxHorizToDevice(lev, (string)a[0]);
                v.Functions["maxWalkToDevice"] = a => MaxWalk(lev, (string)a[0]);

                v["maxTravelToStairM"] = null;
                v["maxTravelStairToExitM"] = null;
                v["maxSharedPathM"] = null;
                v["maxExteriorOpeningGapM"] = null;
                v["allowedTravelM"] = null;
            }

            foreach (var r in Rooms)
            {
                var room = r;
                r.Vars.Functions["deviceCountInRoom"] = a => (double)(room.Level?.Devices.Count(d => d.Kind == (string)a[0] &&
                    Geo2D.Inside(room.Loops, new P2(d.Point.X * FtToM, d.Point.Y * FtToM))) ?? 0);
                r.Vars.Functions["maxHorizToDeviceInRoom"] = a => MaxHorizInRoom(room, (string)a[0]);
                r.Vars.Functions["maxSpacingAlongM"] = a =>
                {
                    if (room.Loops.Count == 0) throw new MissingDataException("房間邊界");
                    var devs = room.Level?.Devices.Where(d => d.Kind == (string)a[0] && Geo2D.Inside(room.Loops, new P2(d.Point.X * FtToM, d.Point.Y * FtToM)))
                        .Select(d => new P2(d.Point.X * FtToM, d.Point.Y * FtToM)).ToList() ?? new List<P2>();
                    double gap = Geo2D.MaxGapAlong(room.Loops[0], devs);
                    return gap == double.MaxValue ? 999.0 : gap;
                };
                r.Vars["needsSmokeDetector"] = r.Role != "toilet" && r.Role != "bath" && r.Role != "accessibleToilet" && r.Role != "stair" && r.Role != "corridor" && r.Role != "kitchen";
                r.Vars["isAccessibleRoute"] = _s.AccessibilityApplies && (r.Role == "corridor" || r.Role == "lobby" || r.Role == "elevatorLobby");
                r.Vars["isBasement"] = r.Level?.IsBasement ?? false;
                r.Vars["levelHabitableAreaM2"] = r.Level != null ? r.Level.Vars.Values["levelHabitableAreaM2"] : 0.0;
                r.Vars["floorSlope"] = 0.0;
                r.Vars["corridorServesClassroom"] = r.Role == "corridor" && Doors.Any(d => (d.From == r || d.To == r) && ((d.From?.Role == "classroom") || (d.To?.Role == "classroom")));
                r.Vars["isTwoSided"] = IsTwoSided(r);
            }

            foreach (var d in Doors)
            {
                var door = d;
                d.Vars.Functions["nearDeviceCount"] = a =>
                {
                    double dist = Expr.Num(a[1]) / FtToM;
                    return (double)Devices.Count(x => x.Kind == (string)a[0] && x.Point.DistanceTo(door.Point) <= dist);
                };
            }
        }

        private bool IsTwoSided(RoomFact corridor)
        {
            if (corridor.Role != "corridor" || corridor.Loops.Count == 0) return false;
            P2 axis = Geo2D.PrincipalAxis(corridor.Loops[0], out P2 mean);
            bool pos = false, neg = false;
            foreach (var d in Doors.Where(x => x.From == corridor || x.To == corridor))
            {
                var other = d.From == corridor ? d.To : d.From;
                if (other == null || !other.Habitable) continue;
                double side = axis.Cross(new P2(d.Point.X * FtToM, d.Point.Y * FtToM) - mean);
                if (side > 0) pos = true; else neg = true;
            }
            return pos && neg;
        }

        private static readonly HashSet<string> SprinklerExempt = new HashSet<string>
        { "toilet", "bath", "accessibleToilet", "accessibleBath", "stair", "smokeLobby", "emergencyElevatorLobby", "mech", "elevatorMachineRoom" };

        private object MaxHorizToDevice(LevelFact l, string kind)
        {
            var devs = l.Devices.Where(d => d.Kind == kind).Select(d => new P2(d.Point.X * FtToM, d.Point.Y * FtToM)).ToList();
            if (devs.Count == 0) throw new MissingDataException(kind, "該層沒有此設備");
            var rooms = l.Rooms.Where(r => r.Loops.Count > 0 && r.Role != "balcony");
            if (kind == "sprinkler") rooms = rooms.Where(r => !SprinklerExempt.Contains(r.Role));
            double worst = 0;
            foreach (var r in rooms)
                foreach (P2 p in Geo2D.SamplePoints(r.Loops, 16, 0.05))
                    worst = Math.Max(worst, devs.Min(d => d.Dist(p)));
            return worst;
        }

        private object MaxHorizInRoom(RoomFact r, string kind)
        {
            var devs = r.Level?.Devices.Where(d => d.Kind == kind).Select(d => new P2(d.Point.X * FtToM, d.Point.Y * FtToM)).ToList();
            if (devs == null || devs.Count == 0) throw new MissingDataException(kind, "該層沒有此設備");
            return Geo2D.SamplePoints(r.Loops, 16, 0.05).Select(p => devs.Min(d => d.Dist(p))).DefaultIfEmpty(0).Max();
        }

        // ───────────────────────── 步行距離（PathOfTravel） ─────────────────────────

        private readonly Dictionary<string, double?> _walkCache = new Dictionary<string, double?>();

        private object MaxWalk(LevelFact l, string kind)
        {
            string key = l.ElevFt.ToString("0.000", CultureInfo.InvariantCulture) + ":" + kind;
            if (!_walkCache.TryGetValue(key, out double? val))
            {
                var ends = l.Devices.Where(d => d.Kind == kind).Select(d => new XYZ(d.Point.X, d.Point.Y, l.ElevFt)).ToList();
                if (ends.Count == 0) throw new MissingDataException(kind, "該層沒有此設備");
                val = _s.TravelDistance ? MaxPath(l, StartPoints(l, true), ends) : null;
                _walkCache[key] = val;
            }
            if (val == null) throw new MissingDataException("步行距離", "無法計算該層的步行路徑（缺房間或房間未連通）");
            return val.Value;
        }

        private List<XYZ> StartPoints(LevelFact l, bool habitableOnly)
        {
            var pts = new List<XYZ>();
            foreach (var r in l.Rooms.Where(x => (!habitableOnly || x.Habitable) && x.Loops.Count > 0))
            {
                var outer = r.Loops.OrderByDescending(lp => Math.Abs(Geo2D.SignedArea(lp))).First();
                P2 c = Geo2D.Centroid(outer);
                foreach (P2 v in outer.Concat(new[] { c }))
                {
                    P2 q = v + (c - v).Unit * 0.4;
                    if (Geo2D.Inside(r.Loops, q)) pts.Add(new XYZ(q.X / FtToM, q.Y / FtToM, l.ElevFt));
                }
            }
            if (pts.Count > 400) pts = pts.Where((p, i) => i % (pts.Count / 400 + 1) == 0).ToList();
            return pts;
        }

        private void ComputeTravel()
        {
            foreach (var l in Levels)
            {
                var ends = new List<XYZ>();
                foreach (int shaft in Stairs.Select(s => s.Shaft).Distinct().Where(s => StairServes(s, l)))
                {
                    var group = Stairs.Where(s => s.Shaft == shaft).ToList();
                    bool safety = group.Any(s => s.IsSafety);
                    var stairDoors = l.Doors.Where(d => (d.From?.Role == "stair" || d.To?.Role == "stair" || d.From?.Role == "smokeLobby" || d.To?.Role == "smokeLobby") &&
                                                        group.Any(s => Near(d.Point, s, 3.0))).ToList();
                    if (safety && stairDoors.Count > 0) ends.AddRange(stairDoors.Select(d => new XYZ(d.Point.X, d.Point.Y, l.ElevFt)));
                    else
                    {
                        var pts = group.SelectMany(s => s.EntryPoints).Where(p => Math.Abs(p.Z - l.ElevFt) < 1.0).ToList();
                        if (pts.Count == 0) pts = group.Select(s => new XYZ((s.MinXm + s.MaxXm) / 2 / FtToM, (s.MinYm + s.MaxYm) / 2 / FtToM, l.ElevFt)).ToList();
                        ends.AddRange(pts.Select(p => new XYZ(p.X, p.Y, l.ElevFt)));
                    }
                }
                if (l == _ground)
                {
                    var exits = l.Doors.Where(d => Expr.Bool(d.Vars.Values["isExterior"])).Select(d => new XYZ(d.Point.X, d.Point.Y, l.ElevFt)).ToList();
                    if (exits.Count > 0 && ends.Count > 0)
                        l.Vars["maxTravelStairToExitM"] = MaxPath(l, ends, exits);
                    continue;
                }
                if (ends.Count == 0 || !l.Rooms.Any(r => r.Habitable)) continue;
                _progress($"計算步行距離… {l.Label}");
                var starts = StartPoints(l, true);
                l.Vars["maxTravelToStairM"] = MaxPath(l, starts, ends);
            }
        }

        /// <summary>
        /// 單一模型且有平面圖時用 Revit 行走路徑（PathOfTravel）；合併多個模型、或沒有平面圖時，
        /// 用各模型房間＋門組成的步行網格計算（PathOfTravel 只認得目前檔案的牆與門，跨模型會算錯）。
        /// </summary>
        private double? MaxPath(LevelFact l, List<XYZ> starts, List<XYZ> ends)
        {
            if (!_multi && l.Plan != null) return MaxShortestPath(l, starts, ends);
            return MaxGridPath(l, starts, ends);
        }

        private readonly Dictionary<LevelFact, WalkGrid> _grids = new Dictionary<LevelFact, WalkGrid>();

        private double? MaxGridPath(LevelFact l, List<XYZ> starts, List<XYZ> ends)
        {
            if (starts.Count == 0 || ends.Count == 0) return null;
            if (!_grids.TryGetValue(l, out WalkGrid g))
            {
                var rooms = l.Rooms.Where(r => r.Loops.Count > 0).Select(r => r.Loops).ToList();
                var doors = l.Doors.Where(d => d.Normal.Len > 0.5).Select(d => new DoorBridge
                {
                    Center = new P2(d.Point.X * FtToM, d.Point.Y * FtToM), Normal = d.Normal, Width = d.WidthM, Depth = d.DepthM
                }).ToList();
                g = rooms.Count > 0 ? WalkGrid.Build(rooms, doors) : null;
                _grids[l] = g;
                if (g != null && !_gridNoted)
                {
                    _gridNoted = true;
                    Notes.Add($"步行距離以房間範圍＋門開口組成的網格計算（格距約 {g.CellSize:0.##} m，16 方向，誤差約 3% 以內）；未畫房間的空間視為不可通行。");
                }
            }
            if (g == null) return null;
            P2 Q(XYZ p) => new P2(p.X * FtToM, p.Y * FtToM);
            double? d2 = g.MaxDistance(starts.Select(Q).ToList(), ends.Select(Q).ToList(), out int unreachable, out _);
            if (unreachable > 0) Notes.Add($"{l.Label}：{unreachable} 個取樣點找不到通往樓梯／出口的路徑（房間之間沒有門、門不在房間邊界上，或房間未連通）。");
            return d2;
        }

        private bool _gridNoted;

        private static bool Near(XYZ p, StairFact s, double m)
        {
            double x = p.X * FtToM, y = p.Y * FtToM;
            return x >= s.MinXm - m && x <= s.MaxXm + m && y >= s.MinYm - m && y <= s.MaxYm + m;
        }

        /// <summary>
        /// 各起點到最近終點的最短行走路徑，取最大值（公尺）。
        /// 優先使用 PathOfTravel.FindShortestPaths（以反射呼叫，版本沒有時改逐一建立 PathOfTravel 量長度，交易最後會回復）。
        /// </summary>
        private double? MaxShortestPath(LevelFact l, List<XYZ> starts, List<XYZ> ends)
        {
            if (starts.Count == 0 || ends.Count == 0) return null;
            double worst = 0;
            int unreachable = 0;
            MethodInfo find = typeof(Autodesk.Revit.DB.Analysis.PathOfTravel).GetMethod("FindShortestPaths", BindingFlags.Public | BindingFlags.Static);
            if (find != null)
            {
                try
                {
                    object res = find.Invoke(null, new object[] { l.Plan, starts, ends });
                    if (res is IEnumerable paths)
                    {
                        int n = 0;
                        foreach (object path in paths)
                        {
                            n++;
                            var pts = (path as IEnumerable)?.OfType<XYZ>().ToList();
                            if (pts == null || pts.Count < 2) { unreachable++; continue; }
                            double len = 0;
                            for (int i = 1; i < pts.Count; i++) len += pts[i].DistanceTo(pts[i - 1]);
                            worst = Math.Max(worst, len * FtToM);
                        }
                        if (n > 0)
                        {
                            if (unreachable > 0) Notes.Add($"{l.Label}：{unreachable} 個取樣點找不到通往樓梯／出口的路徑（可能被牆或門擋住，或房間未連通）。");
                            return worst;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Notes.Add($"{l.Label}：FindShortestPaths 失敗，改用逐點路徑（{ex.InnerException?.Message ?? ex.Message}）。");
                }
            }

            // 後備：每個起點挑最近兩個終點建立路徑
            foreach (XYZ s in starts.Take(120))
            {
                double best = double.MaxValue;
                foreach (XYZ e in ends.OrderBy(e => e.DistanceTo(s)).Take(2))
                {
                    try
                    {
                        Autodesk.Revit.DB.Analysis.PathOfTravel pot = Autodesk.Revit.DB.Analysis.PathOfTravel.Create(l.Plan, s, e);
                        if (pot == null) continue;
                        double len = pot.GetCurves().Sum(c => c.Length) * FtToM;
                        best = Math.Min(best, len);
                        _doc.Delete(pot.Id);
                    }
                    catch { }
                }
                if (best < double.MaxValue) worst = Math.Max(worst, best); else unreachable++;
            }
            if (unreachable > 0) Notes.Add($"{l.Label}：{unreachable} 個取樣點找不到行走路徑。");
            return worst > 0 ? worst : (double?)null;
        }
    }
}
