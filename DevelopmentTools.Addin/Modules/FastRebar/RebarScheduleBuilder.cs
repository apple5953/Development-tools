using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DevelopmentTools.FastRebar
{
    /// <summary>
    /// 建立鋼筋明細表（Revit 明細表會隨模型自動更新，所以同名的已存在就保留不動）。
    /// 呼叫端需自行開 Transaction。
    /// </summary>
    public static class RebarScheduleBuilder
    {
        public const string Prefix = "RTS-鋼筋-";

        private class Col
        {
            public string Heading;
            public BuiltInParameter[] Params;
            public bool Total;
            public Col(string heading, bool total, params BuiltInParameter[] ps) { Heading = heading; Total = total; Params = ps; }
        }

        private class Spec
        {
            public string Name;
            public bool Itemized;
            public List<Col> Cols = new List<Col>();
            public List<int> GroupBy = new List<int>();   // Cols 的索引
            public List<int> SortBy = new List<int>();
        }

        private static readonly Col Type = new Col("號數", false, BuiltInParameter.ELEM_TYPE_PARAM, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM);
        private static readonly Col Diameter = new Col("直徑", false, BuiltInParameter.REBAR_BAR_DIAMETER);
        private static readonly Col Quantity = new Col("支數", true, BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS);
        private static readonly Col BarLength = new Col("單支長度", false, BuiltInParameter.REBAR_ELEM_LENGTH);
        private static readonly Col TotalLength = new Col("總長度", true, BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH);
        private static readonly Col HostCategory = new Col("主體類別", false, BuiltInParameter.REBAR_HOST_CATEGORY);
        private static readonly Col HostMark = new Col("主體標記", false, BuiltInParameter.REBAR_ELEM_HOST_MARK);
        private static readonly Col Shape = new Col("形狀", false, BuiltInParameter.REBAR_SHAPE);
        private static readonly Col Partition = new Col("分區", false, BuiltInParameter.NUMBER_PARTITION_PARAM);

        private static List<Spec> Specs()
        {
            var bySize = new Spec { Name = Prefix + "總量（依號數）", Itemized = false };
            bySize.Cols.AddRange(new[] { Type, Diameter, Quantity, TotalLength });
            bySize.GroupBy.Add(0);

            var byHost = new Spec { Name = Prefix + "明細（依構件）", Itemized = true };
            byHost.Cols.AddRange(new[] { HostCategory, HostMark, Type, Shape, BarLength, Quantity, TotalLength });
            byHost.GroupBy.Add(0);
            byHost.SortBy.AddRange(new[] { 1, 2 });

            var byPartition = new Spec { Name = Prefix + "總量（依分區）", Itemized = false };
            byPartition.Cols.AddRange(new[] { Partition, Type, Quantity, TotalLength });
            byPartition.GroupBy.AddRange(new[] { 0, 1 });

            return new List<Spec> { bySize, byHost, byPartition };
        }

        public class Outcome
        {
            public List<string> Created = new List<string>();
            public List<string> Existing = new List<string>();
            public List<string> Messages = new List<string>();
            public List<ElementId> ScheduleIds = new List<ElementId>();
        }

        public static Outcome EnsureSchedules(Document doc)
        {
            var outcome = new Outcome();
            var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(v => !v.IsTemplate).ToDictionary(v => v.Name, v => v, StringComparer.Ordinal);

            foreach (Spec spec in Specs())
            {
                ViewSchedule found;
                if (existing.TryGetValue(spec.Name, out found))
                {
                    outcome.Existing.Add(spec.Name);
                    outcome.ScheduleIds.Add(found.Id);
                    continue;
                }
                try
                {
                    ViewSchedule vs = Build(doc, spec, outcome.Messages);
                    outcome.Created.Add(spec.Name);
                    outcome.ScheduleIds.Add(vs.Id);
                }
                catch (Exception ex)
                {
                    outcome.Messages.Add($"{spec.Name}：建立失敗（{ex.Message}）");
                }
            }
            return outcome;
        }

        private static ViewSchedule Build(Document doc, Spec spec, List<string> messages)
        {
            ViewSchedule vs = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Rebar));
            vs.Name = spec.Name;
            ScheduleDefinition def = vs.Definition;
            IList<SchedulableField> available = def.GetSchedulableFields();

            var fieldIds = new Dictionary<int, ScheduleFieldId>();
            for (int i = 0; i < spec.Cols.Count; i++)
            {
                Col col = spec.Cols[i];
                SchedulableField sf = Find(available, col.Params);
                if (sf == null)
                {
                    messages.Add($"{spec.Name}：找不到「{col.Heading}」欄位，已略過。");
                    continue;
                }
                ScheduleField f = def.AddField(sf);
                f.ColumnHeading = col.Heading;
                if (col.Total)
                {
                    try { f.DisplayType = ScheduleFieldDisplayType.Totals; } catch { }
                }
                fieldIds[i] = f.FieldId;
            }

            foreach (int i in spec.GroupBy.Concat(spec.SortBy))
            {
                ScheduleFieldId id;
                if (!fieldIds.TryGetValue(i, out id)) continue;
                bool group = spec.GroupBy.Contains(i);
                var sg = new ScheduleSortGroupField(id, ScheduleSortOrder.Ascending);
                if (group && spec.Itemized)
                {
                    sg.ShowHeader = true;
                    sg.ShowFooter = true;
                }
                def.AddSortGroupField(sg);
            }

            def.IsItemized = spec.Itemized;
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            return vs;
        }

        private static SchedulableField Find(IList<SchedulableField> available, BuiltInParameter[] ps)
        {
            foreach (BuiltInParameter p in ps)
            {
                SchedulableField sf = available.FirstOrDefault(f => f.ParameterId != null && f.ParameterId.IntegerValue == (int)p);
                if (sf != null) return sf;
            }
            return null;
        }
    }
}
