using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DevelopmentTools.Core;
using DevelopmentTools.FastRebar;

namespace DevelopmentTools.Commands
{
    /// <summary>
    /// 快速配筋：一次選多支梁柱，套用樣板建立鋼筋（區段 Set、135° 耐震彎鉤、加密區），
    /// 建立後自動修正彎鉤朝向，最後跑一次鋼筋檢核。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_FastRebar : IExternalCommand
    {
        private const double Mm = 1.0 / 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!CommandAccessGuard.EnsureAuthorized("AutoRebar", "快速配筋"))
                    return Result.Cancelled;

                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                Document doc = uiDoc?.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    TaskDialog.Show("快速配筋", "請在專案檔中使用。");
                    return Result.Cancelled;
                }

                // 1. 取得構件：先用目前選取，沒有就讓使用者框選
                List<Element> picked = uiDoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(e => new ConcreteMemberFilter().AllowElement(e))
                    .ToList();
                if (picked.Count == 0)
                {
                    IList<Reference> refs = uiDoc.Selection.PickObjects(ObjectType.Element, new ConcreteMemberFilter(),
                        "選取要配筋的混凝土梁、柱（可框選多支），按「完成」");
                    picked = refs.Select(r => doc.GetElement(r)).Where(e => e != null).ToList();
                }
                if (picked.Count == 0) return Result.Cancelled;

                var frames = new List<MemberFrame>();
                var skipped = new List<string>();
                foreach (Element e in picked)
                {
                    if (!RebarHostData.IsValidHost(e))
                    {
                        skipped.Add($"{e.Name}（{e.Id.IntegerValue}）：Revit 不接受為鋼筋主體");
                        continue;
                    }
                    MemberFrame f = MemberFrame.TryCreate(e, out string reason);
                    if (f == null) skipped.Add($"{e.Name}（{e.Id.IntegerValue}）：{reason}");
                    else frames.Add(f);
                }
                if (frames.Count == 0)
                {
                    TaskDialog.Show("快速配筋", "選取的構件都無法配筋：\n" + string.Join("\n", skipped.Take(15)));
                    return Result.Cancelled;
                }

                // 2. 設定視窗
                var input = BuildInput(doc, frames, skipped);
                var win = new FastRebarWindow(input);
                new System.Windows.Interop.WindowInteropHelper(win).Owner = commandData.Application.MainWindowHandle;
                if (win.ShowDialog() != true || !win.Confirmed) return Result.Cancelled;

                FastRebarPreset preset = win.Preset;
                ExistingRebarPolicy policy = win.Policy;
                var sw = Stopwatch.StartNew();

                // 3. 建立
                var engine = new FastRebarEngine(doc, preset);
                int skippedExisting = 0;
                using (var t = new Transaction(doc, "快速配筋"))
                {
                    var swallow = new SwallowWarnings();
                    FailureHandlingOptions fho = t.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(swallow);
                    t.SetFailureHandlingOptions(fho);
                    t.Start();

                    foreach (MemberFrame f in frames)
                    {
                        IList<Rebar> existing = FastRebarEngine.ExistingRebar(f.Element);
                        if (existing.Count > 0)
                        {
                            if (policy == ExistingRebarPolicy.Skip) { skippedExisting++; continue; }
                            if (policy == ExistingRebarPolicy.Replace)
                            {
                                doc.Delete(existing.Select(r => r.Id).ToList());
                                engine.Result.ExistingDeleted += existing.Count;
                            }
                        }

                        if (f.Kind == MemberKind.Beam) engine.BuildBeam(f);
                        else engine.BuildColumn(f);
                    }

                    doc.Regenerate();
                    engine.FixUp();
                    t.Commit();
                    engine.Result.WarningsSuppressed = swallow.Count;
                }
                sw.Stop();

                // 4. 檢核剛建立的鋼筋
                var created = engine.Result.Created.Select(id => doc.GetElement(id) as Rebar).Where(r => r != null).ToList();
                var checker = new RebarModelChecker(doc);
                checker.Run(created);

                ShowReport(uiDoc, engine.Result, checker, frames.Count, skipped, skippedExisting, sw.Elapsed.TotalSeconds);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                FastRebarLogBridge.Write("快速配筋失敗：" + ex);
                message = ex.Message;
                TaskDialog.Show("快速配筋", "執行失敗：" + ex.Message);
                return Result.Failed;
            }
        }

        private static FastRebarWindowInput BuildInput(Document doc, List<MemberFrame> frames, List<string> skipped)
        {
            var input = new FastRebarWindowInput();
            input.Presets = FastRebarPresetStore.Load(out string last);
            input.LastUsed = last;
            foreach (RebarBarType t in FastRebarEngine.BarTypes(doc))
            {
                if (!input.BarDiametersMm.ContainsKey(t.Name)) input.BarDiametersMm[t.Name] = t.BarNominalDiameter / Mm;
            }
            foreach (RebarHookType h in FastRebarEngine.HookTypes(doc))
            {
                double deg = h.HookAngle * 180 / Math.PI;
                if (Math.Abs(deg - 135) < 1 && h.Style == RebarStyle.StirrupTie) input.Hook135Names.Add(h.Name);
                if (Math.Abs(deg - 90) < 1 && h.Style == RebarStyle.StirrupTie) input.Hook90TieNames.Add(h.Name);
                if (Math.Abs(deg - 90) < 1 && h.Style == RebarStyle.Standard) input.Hook90MainNames.Add(h.Name);
            }

            var beams = frames.Where(f => f.Kind == MemberKind.Beam).ToList();
            var cols = frames.Where(f => f.Kind == MemberKind.Column).ToList();
            input.BeamCount = beams.Count;
            input.ColumnCount = cols.Count;
            if (beams.Count > 0)
            {
                input.SampleBeamWidthMm = beams[0].LenY / Mm;
                input.SampleBeamDepthMm = beams[0].LenZ / Mm;
            }
            if (cols.Count > 0)
            {
                input.SampleColumnAMm = cols[0].LenX / Mm;
                input.SampleColumnBMm = cols[0].LenY / Mm;
                input.SampleColumnHeightMm = cols[0].LenZ / Mm;
            }

            int withRebar = frames.Count(f => FastRebarEngine.ExistingRebar(f.Element).Count > 0);
            var sb = new StringBuilder();
            sb.Append($"已選 {beams.Count} 支梁、{cols.Count} 支柱");
            var beamSizes = beams.GroupBy(f => $"{f.LenY / Mm:0}×{f.LenZ / Mm:0}").Select(g => $"{g.Key}×{g.Count()}");
            var colSizes = cols.GroupBy(f => $"{Math.Min(f.LenX, f.LenY) / Mm:0}×{Math.Max(f.LenX, f.LenY) / Mm:0}").Select(g => $"{g.Key}×{g.Count()}");
            if (beams.Count > 0) sb.Append($"｜梁斷面 {string.Join("、", beamSizes)}");
            if (cols.Count > 0) sb.Append($"｜柱斷面 {string.Join("、", colSizes)}");
            if (withRebar > 0) sb.Append($"\n其中 {withRebar} 支已有鋼筋（處理方式在「保護層與彎鉤」分頁）。");
            if (skipped.Count > 0) sb.Append($"\n略過 {skipped.Count} 支：{string.Join("；", skipped.Take(3))}{(skipped.Count > 3 ? "…" : "")}");
            input.SelectionSummary = sb.ToString();
            return input;
        }

        private static void ShowReport(UIDocument uiDoc, FastRebarResult r, RebarModelChecker checker, int memberCount,
                                       List<string> skipped, int skippedExisting, double seconds)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"完成 {r.MembersDone}／{memberCount} 支構件，建立 {r.RebarSetsCreated} 組鋼筋（{seconds:0.0} 秒）。");
            if (r.ExistingDeleted > 0) sb.AppendLine($"刪除原有鋼筋 {r.ExistingDeleted} 組。");
            if (skippedExisting > 0) sb.AppendLine($"已有鋼筋而略過 {skippedExisting} 支。");
            if (r.HooksFlipped + r.LayoutsFlipped > 0) sb.AppendLine($"自動修正：彎鉤朝向 {r.HooksFlipped} 處、排列方向 {r.LayoutsFlipped} 組。");
            if (r.WarningsSuppressed > 0) sb.AppendLine($"Revit 警告 {r.WarningsSuppressed} 則已收起（多為鋼筋延伸出主體，穿柱筋屬正常）。");
            sb.AppendLine();
            sb.AppendLine("【建立後檢核】");
            sb.AppendLine(checker.Summary(8));
            if (r.Messages.Count > 0)
            {
                sb.AppendLine("【訊息】");
                foreach (string m in r.Messages.Take(10)) sb.AppendLine("・" + m);
                if (r.Messages.Count > 10) sb.AppendLine($"…另有 {r.Messages.Count - 10} 則，見記錄檔。");
            }

            string reportPath = WriteReport(sb.ToString(), r, checker);
            foreach (string m in r.Messages) FastRebarLogBridge.Write(m);

            var problemIds = r.Problem.Concat(checker.Issues.SelectMany(i => i.Ids)).Distinct().ToList();
            var td = new TaskDialog("快速配筋")
            {
                MainInstruction = problemIds.Count == 0 ? "配筋完成" : "配筋完成，有項目需要確認",
                MainContent = sb.ToString(),
                FooterText = reportPath != null ? "報告：" + reportPath : null
            };
            if (r.Created.Count > 0) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "選取剛建立的鋼筋");
            if (problemIds.Count > 0) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"選取需要確認的元素（{problemIds.Count}）");
            td.CommonButtons = TaskDialogCommonButtons.Close;
            TaskDialogResult res = td.Show();
            try
            {
                if (res == TaskDialogResult.CommandLink1) uiDoc.Selection.SetElementIds(r.Created);
                else if (res == TaskDialogResult.CommandLink2) uiDoc.Selection.SetElementIds(problemIds);
            }
            catch { }
        }

        private static string WriteReport(string summary, FastRebarResult r, RebarModelChecker checker)
        {
            try
            {
                string dir = Path.GetDirectoryName(FastRebarPresetStore.FilePath);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"配筋報告_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var sb = new StringBuilder();
                sb.Append(checker.ToCsv());
                foreach (string m in r.Messages) sb.AppendLine($"\"訊息\",\"{m.Replace("\"", "'")}\",\"\"");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return path;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>鋼筋檢核：只讀不改，檢查選取構件（或整個模型）的鋼筋。</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_RebarCheck : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!CommandAccessGuard.EnsureAuthorized("AutoRebar", "鋼筋檢核"))
                    return Result.Cancelled;

                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                Document doc = uiDoc?.Document;
                if (doc == null) return Result.Cancelled;

                var rebars = new List<Rebar>();
                var sel = uiDoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
                string scope;
                if (sel.Count > 0)
                {
                    foreach (Element e in sel)
                    {
                        if (e is Rebar rb) rebars.Add(rb);
                        else rebars.AddRange(FastRebarEngine.ExistingRebar(e));
                    }
                    rebars = rebars.GroupBy(r => r.Id.IntegerValue).Select(g => g.First()).ToList();
                    scope = $"選取範圍（{sel.Count} 個元素）";
                }
                else
                {
                    rebars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().ToList();
                    scope = "整個模型";
                }

                if (rebars.Count == 0)
                {
                    TaskDialog.Show("鋼筋檢核", $"{scope}沒有鋼筋。先選取梁柱或鋼筋再執行，或不選取以檢查整個模型。");
                    return Result.Cancelled;
                }

                var sw = Stopwatch.StartNew();
                var checker = new RebarModelChecker(doc);
                checker.Run(rebars);
                sw.Stop();

                string path = null;
                try
                {
                    string dir = Path.GetDirectoryName(FastRebarPresetStore.FilePath);
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, $"鋼筋檢核_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    File.WriteAllText(path, checker.ToCsv(), new UTF8Encoding(true));
                }
                catch { path = null; }

                var ids = checker.Issues.SelectMany(i => i.Ids).Distinct().ToList();
                var td = new TaskDialog("鋼筋檢核")
                {
                    MainInstruction = ids.Count == 0 ? $"{scope}：沒有發現問題" : $"{scope}：發現 {checker.Issues.Count} 項需要確認",
                    MainContent = checker.Summary(12) + $"\n（{sw.Elapsed.TotalSeconds:0.0} 秒）",
                    FooterText = path != null ? "報告：" + path : null
                };
                if (ids.Count > 0) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"選取有問題的鋼筋（{ids.Count}）");
                td.CommonButtons = TaskDialogCommonButtons.Close;
                if (td.Show() == TaskDialogResult.CommandLink1) uiDoc.Selection.SetElementIds(ids);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("鋼筋檢核", "執行失敗：" + ex.Message);
                return Result.Failed;
            }
        }
    }

    /// <summary>只允許混凝土（含預鑄）結構梁、結構柱。</summary>
    public class ConcreteMemberFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem?.Category == null) return false;
            int cat = elem.Category.Id.IntegerValue;
            if (cat != (int)BuiltInCategory.OST_StructuralFraming && cat != (int)BuiltInCategory.OST_StructuralColumns) return false;
            if (elem is FamilyInstance fi)
            {
                try
                {
                    StructuralMaterialType m = fi.StructuralMaterialType;
                    return m == StructuralMaterialType.Concrete || m == StructuralMaterialType.PrecastConcrete || m == StructuralMaterialType.Undefined;
                }
                catch { return true; }
            }
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    internal static class FastRebarLogBridge
    {
        public static void Write(string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(FastRebarPresetStore.FilePath);
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "fast-rebar.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
