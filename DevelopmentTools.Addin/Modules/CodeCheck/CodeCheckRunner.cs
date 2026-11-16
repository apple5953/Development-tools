using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DevelopmentTools.CodeCheck
{
    /// <summary>
    /// 法規檢討的無介面核心：人用的 Cmd_CodeCheck 視窗與 AI agent 工具（code_check.run）共用同一段流程。
    /// 暫時連結模型檔、3D 視圖、平面圖、行走路徑全部在一個交易中完成後回復，不留下任何變更。
    /// </summary>
    public static class CodeCheckRunner
    {
        public sealed class Outcome
        {
            public CodeCheckEngine Engine;
            public List<string> Notes = new List<string>();
            public int SourceCount;
        }

        public static Outcome Run(Document doc, CodeCheckData data, ProjectSettings settings, KeywordConfig keywords,
                                  List<SourceEntry> entries, Action<string> progress)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            using (var t = new Transaction(doc, "法規檢討（暫存）"))
            {
                var fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new QuietFailures());
                fho.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(fho);
                t.Start();
                try
                {
                    var notes = new List<string>();
                    List<ModelSource> sources = ModelSources.Build(doc, entries, notes, progress);
                    if (sources.Count == 0) throw new InvalidOperationException("檢討範圍內沒有可讀取的模型，請在「檢討範圍（模型）」中勾選。");
                    doc.Regenerate();
                    View3D v3 = Get3DView(doc, sources);
                    bool single = sources.Count == 1 && sources[0].IsHost;
                    if (single) EnsurePlans(doc, settings);
                    doc.Regenerate();
                    var facts = new ModelFacts(doc, sources, settings, keywords, v3, progress);
                    facts.Notes.AddRange(notes);
                    facts.Collect();
                    var engine = new CodeCheckEngine(data, facts, settings);
                    engine.Run(progress);
                    return new Outcome { Engine = engine, Notes = facts.Notes, SourceCount = sources.Count };
                }
                finally
                {
                    t.RollBack();
                }
            }
        }

        /// <summary>
        /// 建立暫時的 3D 視圖供淨高射線使用：不套樣板、不開剖面框，避免既有視圖隱藏了天花板或連結；
        /// 未納入檢討的連結模型在此視圖中隱藏，不影響量測。
        /// </summary>
        private static View3D Get3DView(Document doc, List<ModelSource> sources)
        {
            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            View3D v = null;
            if (vft != null)
            {
                try
                {
                    v = View3D.CreateIsometric(doc, vft.Id);
                    if (v.ViewTemplateId != ElementId.InvalidElementId) v.ViewTemplateId = ElementId.InvalidElementId;
                    if (v.IsSectionBoxActive) v.IsSectionBoxActive = false;
                }
                catch { v = null; }
            }
            if (v == null)
            {
                v = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                    .FirstOrDefault(x => !x.IsTemplate && !x.IsPerspective && !x.IsSectionBoxActive);
                return v;
            }
            try
            {
                var used = new HashSet<ElementId>(sources.Where(s => s.LinkInstanceId != ElementId.InvalidElementId).Select(s => s.LinkInstanceId));
                var hide = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).ToElementIds().Where(id => !used.Contains(id)).ToList();
                if (hide.Count > 0) v.HideElements(hide);
            }
            catch { }
            return v;
        }

        /// <summary>沒有平面圖的樓層暫時建立平面圖（交易回復後即消失），供行走路徑計算。</summary>
        private static void EnsurePlans(Document doc, ProjectSettings s)
        {
            if (!s.TravelDistance) return;
            var plans = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel != null)
                .Select(v => v.GenLevel.Id).ToHashSet();
            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.FloorPlan);
            if (vft == null) return;
            foreach (Level l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
            {
                if (plans.Contains(l.Id)) continue;
                try { ViewPlan.Create(doc, vft.Id, l.Id); } catch { }
            }
        }
    }
}
