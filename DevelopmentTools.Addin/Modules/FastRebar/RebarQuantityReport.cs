using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DevelopmentTools.FastRebar.Saving;

namespace DevelopmentTools.FastRebar
{
    /// <summary>
    /// 鋼筋重量統計（樓層×號數、構件類別×號數），補 Revit 明細表算不出的重量。只讀模型。
    /// </summary>
    public static class RebarQuantityReport
    {
        private const double Mm = 1.0 / 304.8;

        public class Result
        {
            public QuantityTable ByLevel = new QuantityTable("樓層");
            public QuantityTable ByCategory = new QuantityTable("構件類別");
            public IComparer<string> LevelOrder;
            public int Rebars;
            public int Failed;
        }

        public static Result Build(Document doc, IEnumerable<Rebar> rebars)
        {
            var r = new Result();
            var elevations = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .GroupBy(l => l.Name).ToDictionary(g => g.Key, g => g.First().Elevation);
            r.LevelOrder = Comparer<string>.Create((a, b) =>
            {
                double ea, eb;
                bool ha = elevations.TryGetValue(a, out ea), hb = elevations.TryGetValue(b, out eb);
                if (ha && hb) return ea.CompareTo(eb);
                if (ha != hb) return ha ? -1 : 1;
                return string.CompareOrdinal(a, b);
            });

            var hostCache = new Dictionary<int, Tuple<string, string>>();
            foreach (Rebar bar in rebars.Where(x => x != null && x.IsValidObject))
            {
                try
                {
                    RebarBarType type = doc.GetElement(bar.GetTypeId()) as RebarBarType;
                    string size = type?.Name ?? "未知號數";
                    double dia = (type?.BarNominalDiameter ?? 0) / Mm;

                    int hostId = bar.GetHostId().IntegerValue;
                    Tuple<string, string> host;
                    if (!hostCache.TryGetValue(hostId, out host))
                    {
                        Element h = doc.GetElement(bar.GetHostId());
                        host = Tuple.Create(LevelName(doc, h), h?.Category?.Name ?? "（無主體）");
                        hostCache[hostId] = host;
                    }

                    double totalMm = 0;
                    int count = 0;
                    for (int i = 0; i < bar.NumberOfBarPositions; i++)
                    {
                        if (!bar.DoesBarExistAtPosition(i)) continue;
                        IList<Curve> cs = bar.GetTransformedCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, i);
                        if (cs == null) continue;
                        totalMm += cs.Sum(c => c.Length) / Mm;
                        count++;
                    }
                    r.ByLevel.Add(host.Item1, size, dia, totalMm, count);
                    r.ByCategory.Add(host.Item2, size, dia, totalMm, count);
                    r.Rebars++;
                }
                catch { r.Failed++; }
            }
            return r;
        }

        /// <summary>主體所在樓層：先用 LevelId，再查參考樓層／底部樓層參數。</summary>
        private static string LevelName(Document doc, Element host)
        {
            if (host == null) return "（無主體）";
            ElementId id = host.LevelId;
            if (id == null || id == ElementId.InvalidElementId)
            {
                foreach (BuiltInParameter bip in new[] { BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
                                                         BuiltInParameter.SCHEDULE_LEVEL_PARAM, BuiltInParameter.WALL_BASE_CONSTRAINT })
                {
                    Parameter p = host.get_Parameter(bip);
                    if (p != null && p.StorageType == StorageType.ElementId && p.AsElementId() != ElementId.InvalidElementId)
                    {
                        id = p.AsElementId();
                        break;
                    }
                }
            }
            Level level = id != null ? doc.GetElement(id) as Level : null;
            return level?.Name ?? "（未指定樓層）";
        }

        public static string WriteCsv(Result r, string projectName)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("鋼筋重量統計," + (projectName ?? "").Replace(",", " "));
                sb.AppendLine("重量 = 中心線長度（含彎鉤、彎折）× CNS 560 單位重，未含搭接與損耗");
                sb.AppendLine();
                r.ByLevel.AppendCsv(sb, r.LevelOrder);
                sb.AppendLine();
                r.ByCategory.AppendCsv(sb, null);

                string dir = Path.GetDirectoryName(FastRebarPresetStore.FilePath);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"鋼筋重量統計_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
