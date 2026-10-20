// 讀取目前視圖的情境：專案、視圖、樓層、選取元件（含參數）、柱線（LINE(4-5, A)）、最近結構柱（柱位置標記）
// 定位原則：以「選取物件」＋「周圍結構柱與柱線」為準，不依賴元件類型名稱（模型修正後常改名，圖說不會跟著改）。
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RfiDeckAgent.Core
{
    public class ModelContext
    {
        public string ProjectTitle = "";
        public string ProjectPath = "";
        public string ViewName = "";
        public string ViewType = "";
        public string LevelName = "";
        public string Building = "";        // 依樓層名稱推得（LB_ 實驗棟、OB_ 行政棟）
        public string GridText = "";        // LINE(8-9, B'-C')
        public string CellPos = "";         // 在柱線格內的相對位置 "fx,fy"（0~1，依 LINE 內標示順序）
        public string PositionNote = "";    // 距 8 軸 2.35m、距 B' 軸 1.20m
        public string Anchors = "";         // 周圍結構柱：B'-8、C'-8、B'-9、C'-9
        public string LocationText = "";
        public string PointSource = "";     // 位置依據：選取元件／剖面框中心／裁切框／畫面中心
        public List<string> ElementIds = new List<string>();
        public List<string> ElementDescriptions = new List<string>();

        const double FT = 0.3048;

        /// <summary>樓層命名慣例：LB_ 實驗棟（Lab Building）、OB_ 行政棟（Office Building）</summary>
        static readonly string[][] BuildingPrefix = {
            new[] { "LB_", "實驗棟" }, new[] { "OB_", "行政棟" }, new[] { "HB_", "歷史建築" } };

        public static ModelContext Collect(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;
            ModelContext c = new ModelContext();
            c.ProjectTitle = doc.Title;
            c.ProjectPath = doc.PathName;
            c.ViewName = view.Name;
            c.ViewType = view.ViewType.ToString();

            // ---------- 選取物件（模型群組展開成成員）
            List<Element> picked = new List<Element>();
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;
                Group grp = e as Group;
                if (grp != null)
                {
                    foreach (ElementId mid in grp.GetMemberIds())
                    {
                        Element m = doc.GetElement(mid);
                        if (m != null && m.Category != null) picked.Add(m);
                    }
                }
                else picked.Add(e);
            }

            XYZ point = null;
            BoundingBoxXYZ union = null;
            Level elemLevel = null;
            int n = 0;
            foreach (Element e in picked)
            {
                if (n < 30)
                {
                    c.ElementIds.Add(IdText(e.Id));
                    c.ElementDescriptions.Add(Describe(doc, e));
                }
                n++;
                if (elemLevel == null) elemLevel = LevelOf(doc, e);
                BoundingBoxXYZ bb = e.get_BoundingBox(null);
                if (bb == null) continue;
                if (union == null) { union = new BoundingBoxXYZ(); union.Min = bb.Min; union.Max = bb.Max; }
                else
                {
                    union.Min = new XYZ(Math.Min(union.Min.X, bb.Min.X), Math.Min(union.Min.Y, bb.Min.Y), Math.Min(union.Min.Z, bb.Min.Z));
                    union.Max = new XYZ(Math.Max(union.Max.X, bb.Max.X), Math.Max(union.Max.Y, bb.Max.Y), Math.Max(union.Max.Z, bb.Max.Z));
                }
            }
            if (union != null) { point = (union.Min + union.Max) / 2.0; c.PointSource = "選取元件"; }

            // ---------- 沒有選取：小剖面框中心 → 裁切框中心 → 畫面中心射線
            if (point == null)
            {
                try
                {
                    View3D v3 = view as View3D;
                    if (v3 != null && v3.IsSectionBoxActive)
                    {
                        BoundingBoxXYZ sb = v3.GetSectionBox();
                        XYZ dg = sb.Max - sb.Min;
                        if (Math.Sqrt(dg.X * dg.X + dg.Y * dg.Y) * FT < 30)
                        {
                            point = sb.Transform.OfPoint((sb.Min + sb.Max) / 2.0);
                            c.PointSource = "剖面框中心";
                        }
                    }
                    else if (!(view is View3D) && view.CropBoxActive)
                    {
                        BoundingBoxXYZ cb = view.CropBox;
                        point = cb.Transform.OfPoint((cb.Min + cb.Max) / 2.0);
                        c.PointSource = "裁切框中心";
                    }
                }
                catch { }
            }
            if (point == null)
            {
                View3D v3 = view as View3D;
                if (v3 != null && !v3.IsTemplate)
                {
                    try
                    {
                        ViewOrientation3D o = v3.GetOrientation();
                        ReferenceIntersector ri = new ReferenceIntersector(v3);
                        ri.FindReferencesInRevitLinks = true;
                        ReferenceWithContext rwc = ri.FindNearest(o.EyePosition, o.ForwardDirection);
                        if (rwc != null) { point = rwc.GetReference().GlobalPoint; c.PointSource = "畫面中心"; }
                    }
                    catch { }
                }
            }

            // ---------- 樓層：選取物件的參考樓層 > 視圖樓層 > 點位下方最近樓層
            if (elemLevel != null) c.LevelName = elemLevel.Name;
            else if (view.GenLevel != null) c.LevelName = view.GenLevel.Name;
            else if (point != null) c.LevelName = NearestLevelBelow(doc, point.Z);
            foreach (string[] bp in BuildingPrefix)
                if (c.LevelName.StartsWith(bp[0], StringComparison.OrdinalIgnoreCase)) { c.Building = bp[1]; break; }

            // ---------- 柱線＋格內位置＋周圍結構柱
            if (point != null)
            {
                GridResult g = GridLabel(doc, point);
                c.GridText = g.Text;
                c.CellPos = g.CellPos;
                c.PositionNote = g.Note;
                try { c.Anchors = NearbyColumns(doc, point); } catch { }
            }

            string loc = c.LevelName;
            if (c.GridText.Length > 0) loc = (loc.Length > 0 ? loc + "_" : "") + c.GridText;
            if (c.PositionNote.Length > 0) loc += "（" + c.PositionNote + "）";
            c.LocationText = loc;
            return c;
        }

        // ------------------------------------------------------------ 元件描述（含參數，不拿來比對圖說）
        static string Describe(Document doc, Element e)
        {
            List<string> parts = new List<string>();
            parts.Add(e.Category != null ? e.Category.Name : "");
            FamilyInstance fi = e as FamilyInstance;
            if (fi != null && fi.Symbol != null) parts.Add(fi.Symbol.FamilyName);
            ElementId tid = e.GetTypeId();
            if (tid != null && tid != ElementId.InvalidElementId)
            {
                Element te = doc.GetElement(tid);
                if (te != null) parts.Add(te.Name);
            }
            string size = P(e, BuiltInParameter.RBS_CALCULATED_SIZE);
            if (size.Length > 0) parts.Add(size);
            string sys = P(e, BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
            if (sys.Length > 0) parts.Add("系統 " + sys);
            string off = P(e, BuiltInParameter.RBS_OFFSET_PARAM);
            if (off.Length > 0) parts.Add("中心高 " + off);
            string mark = P(e, BuiltInParameter.ALL_MODEL_MARK);
            if (mark.Length > 0) parts.Add("標記 " + mark);
            string colMark = P(e, BuiltInParameter.COLUMN_LOCATION_MARK);
            if (colMark.Length > 0) parts.Add("柱位 " + colMark);
            parts.Add("[ID " + IdText(e.Id) + "]");
            return string.Join(" ", parts.Where(s => !string.IsNullOrEmpty(s)).ToArray());
        }

        static string P(Element e, BuiltInParameter bip)
        {
            try
            {
                Parameter p = e.get_Parameter(bip);
                if (p == null || !p.HasValue) return "";
                string s = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                return (s ?? "").Trim();
            }
            catch { return ""; }
        }

        static Level LevelOf(Document doc, Element e)
        {
            try
            {
                if (e.LevelId != null && e.LevelId != ElementId.InvalidElementId)
                {
                    Level l = doc.GetElement(e.LevelId) as Level;
                    if (l != null) return l;
                }
                foreach (BuiltInParameter bip in new[] { BuiltInParameter.RBS_START_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
                                                          BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                                                          BuiltInParameter.FAMILY_BASE_LEVEL_PARAM })
                {
                    Parameter p = e.get_Parameter(bip);
                    if (p != null && p.StorageType == StorageType.ElementId)
                    {
                        Level l = doc.GetElement(p.AsElementId()) as Level;
                        if (l != null) return l;
                    }
                }
            }
            catch { }
            return null;
        }

        static string IdText(ElementId id)
        {
#if REVIT2024_OR_GREATER
            return id.Value.ToString();
#else
            return id.IntegerValue.ToString();
#endif
        }

        static string NearestLevelBelow(Document doc, double z)
        {
            List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(q => q.Elevation).ToList();
            Level best = null;
            foreach (Level lv in levels)
            {
                if (lv.Elevation <= z + 0.5) best = lv;
            }
            if (best == null && levels.Count > 0) best = levels[0];
            return best != null ? best.Name : "";
        }

        // ------------------------------------------------------------ 周圍結構柱（主模型＋連結模型）
        static string NearbyColumns(Document doc, XYZ p)
        {
            List<Tuple<double, string>> found = new List<Tuple<double, string>>();
            Action<Document, Transform> scan = (d, tf) =>
            {
                foreach (Element col in new FilteredElementCollector(d).OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType())
                {
                    BoundingBoxXYZ bb = col.get_BoundingBox(null);
                    if (bb == null) continue;
                    XYZ mn = bb.Min, mx = bb.Max;
                    if (tf != null) { mn = tf.OfPoint(mn); mx = tf.OfPoint(mx); }
                    double z0 = Math.Min(mn.Z, mx.Z) - 1.5, z1 = Math.Max(mn.Z, mx.Z) + 1.5;   // 前後各放寬約 0.45m
                    if (p.Z < z0 || p.Z > z1) continue;
                    double cx = (mn.X + mx.X) / 2, cy = (mn.Y + mx.Y) / 2;
                    double dist = Math.Sqrt((cx - p.X) * (cx - p.X) + (cy - p.Y) * (cy - p.Y)) * FT;
                    if (dist > 15) continue;
                    string mark = P(col, BuiltInParameter.COLUMN_LOCATION_MARK);
                    if (mark.Length == 0) continue;
                    found.Add(Tuple.Create(dist, mark));
                }
            };
            scan(doc, null);
            foreach (RevitLinkInstance li in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document ld = li.GetLinkDocument();
                if (ld == null) continue;
                try { scan(ld, li.GetTotalTransform()); } catch { }
            }
            List<string> marks = found.OrderBy(t => t.Item1).Select(t => t.Item2).Distinct().Take(4).ToList();
            return string.Join("、", marks.ToArray());
        }

        // ------------------------------------------------------------ 柱線
        class GridHit
        {
            public string Name;
            public double Dist;    // 帶正負的垂直距離（ft）
            public double Out;     // 點超出柱線端點的距離（ft，0＝在柱線長度範圍內）
        }

        public class GridResult
        {
            public string Text = "";
            public string CellPos = "";
            public string Note = "";
        }

        class AxisResult
        {
            public string Label = "";   // "8-9" 或 "8"
            public double Frac = 0.5;   // 從第一個柱線往第二個柱線的比例
            public string Note = "";    // 距 8 軸 2.35m
            public bool Numeric;
        }

        /// <summary>依點位找柱線，輸出 LINE(8-9, B'-C')、格內相對位置、距最近柱線的距離</summary>
        public static GridResult GridLabel(Document doc, XYZ p)
        {
            List<GridHit> vert = new List<GridHit>();
            List<GridHit> horz = new List<GridHit>();
            foreach (Grid g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
            {
                Line ln = g.Curve as Line;
                if (ln == null) continue;
                XYZ d = ln.Direction;
                XYZ o = ln.GetEndPoint(0);
                bool isVert = Math.Abs(d.Y) >= Math.Abs(d.X);
                if (isVert && d.Y < 0) d = d.Negate();
                if (!isVert && d.X < 0) d = d.Negate();
                XYZ v = p - o;
                GridHit h = new GridHit();
                h.Name = g.Name;
                h.Dist = d.X * v.Y - d.Y * v.X;
                // 柱線只在自己的長度範圍內有效（例：實驗棟用 C'、D'，行政棟的 C、D 不延伸過來）
                XYZ e0 = ln.GetEndPoint(0), u = ln.Direction;
                double along = (p.X - e0.X) * u.X + (p.Y - e0.Y) * u.Y;
                h.Out = Math.Max(0, Math.Max(-along, along - ln.Length));
                if (isVert) vert.Add(h); else horz.Add(h);
            }
            AxisResult a = Axis(vert), b = Axis(horz);
            // 慣例數字軸在前：LINE(8-9, G'-H)
            if (a.Label.Length > 0 && b.Label.Length > 0 && !a.Numeric && b.Numeric)
            {
                AxisResult t = a; a = b; b = t;
            }
            GridResult r = new GridResult();
            if (a.Label.Length == 0 && b.Label.Length == 0) return r;
            if (a.Label.Length == 0) { r.Text = "LINE(" + b.Label + ")"; r.Note = b.Note; return r; }
            if (b.Label.Length == 0) { r.Text = "LINE(" + a.Label + ")"; r.Note = a.Note; return r; }
            r.Text = "LINE(" + a.Label + ", " + b.Label + ")";
            r.CellPos = a.Frac.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "," +
                        b.Frac.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            r.Note = string.Join("、", new[] { a.Note, b.Note }.Where(s => s.Length > 0).ToArray());
            return r;
        }

        static AxisResult Axis(List<GridHit> hits)
        {
            AxisResult r = new AxisResult();
            List<GridHit> inside = hits.Where(h => h.Out * FT <= 1.5).ToList();
            if (inside.Count > 0) hits = inside;
            if (hits.Count == 0) return r;
            GridHit nearest = hits.OrderBy(h => Math.Abs(h.Dist)).First();
            r.Numeric = nearest.Name.Length > 0 && char.IsDigit(nearest.Name[0]);
            double nd = Math.Abs(nearest.Dist) * FT;
            if (nd < 0.3)
            {
                r.Label = nearest.Name;
                r.Note = nearest.Name + " 軸上";
                return r;
            }
            GridHit neg = hits.Where(h => h.Dist < 0).OrderByDescending(h => h.Dist).FirstOrDefault();
            GridHit pos = hits.Where(h => h.Dist > 0).OrderBy(h => h.Dist).FirstOrDefault();
            r.Note = string.Format("距 {0} 軸 {1:0.00}m", nearest.Name, nd);
            if (neg != null && pos != null)
            {
                r.Label = neg.Name + "-" + pos.Name;
                double dn = Math.Abs(neg.Dist), dp = Math.Abs(pos.Dist);
                r.Frac = dn / (dn + dp);
            }
            else r.Label = nearest.Name;
            return r;
        }
    }
}
