using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace DevelopmentTools.FastRebar
{
    public class RebarIssue
    {
        public string Category;
        public string Text;
        public List<ElementId> Ids = new List<ElementId>();
    }

    /// <summary>
    /// 鋼筋模型檢核（只讀，不改模型）：
    /// 重疊鋼筋、同構件同形狀箍筋混用號數、箍筋沒有彎鉤、箍筋超出主體、主筋淨距不足。
    /// 規則是從參考模型實際找到的問題歸納的；淨距門檻取 max(db, 25 mm)，屬常用下限，正式值依結構設計。
    /// </summary>
    public class RebarModelChecker
    {
        private const double Mm = 1.0 / 304.8;
        private readonly Document _doc;
        public List<RebarIssue> Issues { get; } = new List<RebarIssue>();
        public int BarsChecked { get; private set; }
        public int HostsChecked { get; private set; }

        private class BarLine
        {
            public Rebar Bar;
            public int Index;
            public XYZ A, B;
            public double Db;
            public ElementId TypeId;
        }

        public RebarModelChecker(Document doc)
        {
            _doc = doc;
        }

        public void Run(IEnumerable<Rebar> rebars)
        {
            var byHost = rebars.Where(r => r != null && r.IsValidObject)
                               .GroupBy(r => r.GetHostId().IntegerValue)
                               .ToList();
            HostsChecked = byHost.Count;
            foreach (var g in byHost)
            {
                Element host = _doc.GetElement(new ElementId(g.Key));
                CheckHost(host, g.ToList());
            }
        }

        private void Add(string cat, string text, IEnumerable<ElementId> ids)
        {
            var issue = new RebarIssue { Category = cat, Text = text };
            issue.Ids.AddRange(ids.Distinct());
            Issues.Add(issue);
        }

        private static string HostLabel(Element host)
        {
            if (host == null) return "（無主體）";
            return $"{host.Category?.Name} {host.Name}（{host.Id.IntegerValue}）";
        }

        private void CheckHost(Element host, List<Rebar> bars)
        {
            var lines = new List<BarLine>();
            var stirrups = new List<Rebar>();
            BoundingBoxXYZ hb = host?.get_BoundingBox(null);

            foreach (Rebar r in bars)
            {
                RebarBarType type = _doc.GetElement(r.GetTypeId()) as RebarBarType;
                double db = type?.BarNominalDiameter ?? 0;
                RebarStyle style = RebarStyle.Standard;
                try { style = r.GetShapeId() != ElementId.InvalidElementId && _doc.GetElement(r.GetShapeId()) is RebarShape sh ? sh.RebarStyle : RebarStyle.Standard; } catch { }
                if (style == RebarStyle.StirrupTie) stirrups.Add(r);

                int n = r.NumberOfBarPositions;
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        if (!r.DoesBarExistAtPosition(i)) continue;
                        IList<Curve> cs = r.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, i);
                        if (cs == null || cs.Count == 0) continue;
                        BarsChecked++;
                        if (style == RebarStyle.Standard && cs.Count == 1 && cs[0] is Line)
                        {
                            lines.Add(new BarLine { Bar = r, Index = i, A = cs[0].GetEndPoint(0), B = cs[0].GetEndPoint(1), Db = db, TypeId = r.GetTypeId() });
                        }
                        if (style == RebarStyle.StirrupTie && hb != null)
                        {
                            double tol = 10 * Mm;
                            bool outside = cs.Any(c => Outside(c.GetEndPoint(0), hb, tol) || Outside(c.GetEndPoint(1), hb, tol));
                            if (outside && i == 0)
                            {
                                Add("箍筋超出主體", $"{HostLabel(host)}：箍筋／繫筋 {r.Id.IntegerValue} 有部分在主體外。", new[] { r.Id });
                            }
                        }
                    }
                    catch { }
                }
            }

            CheckDuplicates(host, bars);
            CheckStirrupMix(host, stirrups);
            CheckStirrupHooks(host, stirrups);
            CheckClearSpacing(host, lines);
        }

        private static bool Outside(XYZ p, BoundingBoxXYZ b, double tol)
        {
            return p.X < b.Min.X - tol || p.Y < b.Min.Y - tol || p.Z < b.Min.Z - tol ||
                   p.X > b.Max.X + tol || p.Y > b.Max.Y + tol || p.Z > b.Max.Z + tol;
        }

        /// <summary>同主體、同號數、中心線兩端點都在 5 mm 內 → 重疊。</summary>
        private void CheckDuplicates(Element host, List<Rebar> bars)
        {
            var sigs = new List<Tuple<Rebar, int, XYZ, XYZ, int>>();
            foreach (Rebar r in bars)
            {
                for (int i = 0; i < r.NumberOfBarPositions; i++)
                {
                    try
                    {
                        if (!r.DoesBarExistAtPosition(i)) continue;
                        IList<Curve> cs = r.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, i);
                        if (cs == null || cs.Count == 0) continue;
                        sigs.Add(Tuple.Create(r, i, cs[0].GetEndPoint(0), cs[cs.Count - 1].GetEndPoint(1), cs.Count));
                    }
                    catch { }
                }
            }

            double tol = 5 * Mm;
            var cells = new Dictionary<string, List<int>>();
            double cell = 50 * Mm;
            Func<XYZ, string> key = p => $"{Math.Floor(p.X / cell)},{Math.Floor(p.Y / cell)},{Math.Floor(p.Z / cell)}";
            for (int k = 0; k < sigs.Count; k++)
            {
                XYZ mid = (sigs[k].Item3 + sigs[k].Item4) / 2;
                string kk = key(mid);
                if (!cells.TryGetValue(kk, out var list)) cells[kk] = list = new List<int>();
                list.Add(k);
            }

            var reported = new HashSet<string>();
            foreach (var list in cells.Values)
            {
                for (int a = 0; a < list.Count; a++)
                {
                    for (int b = a + 1; b < list.Count; b++)
                    {
                        var s1 = sigs[list[a]];
                        var s2 = sigs[list[b]];
                        if (s1.Item1.Id == s2.Item1.Id && s1.Item2 == s2.Item2) continue;
                        if (s1.Item1.GetTypeId() != s2.Item1.GetTypeId() || s1.Item5 != s2.Item5) continue;
                        bool same = (s1.Item3.DistanceTo(s2.Item3) < tol && s1.Item4.DistanceTo(s2.Item4) < tol) ||
                                    (s1.Item3.DistanceTo(s2.Item4) < tol && s1.Item4.DistanceTo(s2.Item3) < tol);
                        if (!same) continue;
                        string sig = s1.Item1.Id.IntegerValue < s2.Item1.Id.IntegerValue
                            ? $"{s1.Item1.Id.IntegerValue}-{s2.Item1.Id.IntegerValue}"
                            : $"{s2.Item1.Id.IntegerValue}-{s1.Item1.Id.IntegerValue}";
                        if (!reported.Add(sig)) continue;
                        XYZ p = s1.Item3;
                        Add("重疊鋼筋", $"{HostLabel(host)}：鋼筋 {s1.Item1.Id.IntegerValue} 與 {s2.Item1.Id.IntegerValue} 位置重疊（約 x {p.X / Mm:0}, y {p.Y / Mm:0}, z {p.Z / Mm:0}）。常見原因是複製後放錯邊，請檢查對側是否少一支。",
                            new[] { s1.Item1.Id, s2.Item1.Id });
                    }
                }
            }
        }

        private void CheckStirrupMix(Element host, List<Rebar> stirrups)
        {
            foreach (var g in stirrups.GroupBy(r => r.GetShapeId().IntegerValue))
            {
                var types = g.Select(r => r.GetTypeId()).Distinct().ToList();
                if (types.Count < 2) continue;
                string names = string.Join("、", types.Select(t => _doc.GetElement(t)?.Name ?? "?"));
                string shape = _doc.GetElement(new ElementId(g.Key))?.Name ?? "?";
                Add("箍筋號數混用", $"{HostLabel(host)}：同一形狀 {shape} 的箍筋用了 {names}，請確認是否為刻意變更。",
                    g.Select(r => r.Id));
            }
        }

        private void CheckStirrupHooks(Element host, List<Rebar> stirrups)
        {
            var noHook = stirrups.Where(r =>
                r.GetHookTypeId(0) == ElementId.InvalidElementId &&
                r.GetHookTypeId(1) == ElementId.InvalidElementId).ToList();
            if (noHook.Count == 0) return;
            Add("箍筋無彎鉤", $"{HostLabel(host)}：{noHook.Count} 組箍筋／繫筋兩端都沒有彎鉤（耐震構件通常需 135°）。", noHook.Select(r => r.Id));
        }

        /// <summary>平行直筋（含不同 Set）兩兩比對：投影重疊且淨距 &lt; max(較大 db, 25 mm)。</summary>
        private void CheckClearSpacing(Element host, List<BarLine> lines)
        {
            if (lines.Count > 600) lines = lines.Take(600).ToList();
            var flagged = new HashSet<string>();
            int count = 0;
            var ids = new List<ElementId>();
            double worst = double.MaxValue;

            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    BarLine a = lines[i], b = lines[j];
                    XYZ da = (a.B - a.A);
                    XYZ dbv = (b.B - b.A);
                    if (da.GetLength() < 1e-6 || dbv.GetLength() < 1e-6) continue;
                    XYZ ua = da.Normalize();
                    if (Math.Abs(ua.DotProduct(dbv.Normalize())) < 0.999) continue;

                    // 沿軸向是否有重疊
                    double t0 = (b.A - a.A).DotProduct(ua), t1 = (b.B - a.A).DotProduct(ua);
                    double lo = Math.Max(0, Math.Min(t0, t1)), hi = Math.Min(da.GetLength(), Math.Max(t0, t1));
                    if (hi - lo < 100 * Mm) continue;

                    XYZ v = b.A - a.A;
                    double dist = (v - ua * v.DotProduct(ua)).GetLength();
                    double clear = dist - (a.Db + b.Db) / 2;
                    double need = Math.Max(Math.Max(a.Db, b.Db), 25 * Mm);
                    if (dist < 5 * Mm) continue;   // 完全重疊由重疊檢查負責
                    if (clear < need - 1 * Mm)
                    {
                        string sig = $"{a.Bar.Id.IntegerValue}:{a.Index}-{b.Bar.Id.IntegerValue}:{b.Index}";
                        if (!flagged.Add(sig)) continue;
                        count++;
                        worst = Math.Min(worst, clear);
                        ids.Add(a.Bar.Id);
                        ids.Add(b.Bar.Id);
                    }
                }
            }

            if (count > 0)
            {
                Add("主筋淨距不足", $"{HostLabel(host)}：{count} 對平行主筋淨距小於 max(db, 25 mm)，最小 {worst / Mm:0} mm。若為束筋請忽略。", ids);
            }
        }

        public string Summary(int maxLines)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"檢查 {HostsChecked} 個主體、{BarsChecked} 支鋼筋。");
            if (Issues.Count == 0)
            {
                sb.AppendLine("沒有發現問題。");
                return sb.ToString();
            }
            foreach (var g in Issues.GroupBy(i => i.Category))
            {
                sb.AppendLine($"・{g.Key}：{g.Count()} 項");
            }
            sb.AppendLine();
            foreach (var issue in Issues.Take(maxLines))
            {
                sb.AppendLine(issue.Text);
            }
            if (Issues.Count > maxLines) sb.AppendLine($"…另有 {Issues.Count - maxLines} 項，完整清單見報告檔。");
            return sb.ToString();
        }

        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("類別,說明,元素 ID");
            foreach (var i in Issues)
            {
                sb.AppendLine($"\"{i.Category}\",\"{i.Text.Replace("\"", "'")}\",\"{string.Join(" ", i.Ids.Select(x => x.IntegerValue))}\"");
            }
            return sb.ToString();
        }
    }
}
