using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.CodeCheck;
using DevelopmentTools.Core;

namespace DevelopmentTools.Commands
{
    /// <summary>
    /// 全條文法規檢討：收錄建築技術規則四編、無障礙設計規範、消防設備設置標準、建築法等條文，
    /// 模型量得到的自動判定，需要專案資訊的半自動計算，其餘列為人工檢討清單並可記錄結果。
    /// 大型案拆檔時，可把目前檔案、已連結模型與未連結的模型檔合併成一棟建築物一起檢討。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_CodeCheck : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!CommandAccessGuard.EnsureAuthorized("CodeReview", "法規檢討"))
                    return Result.Cancelled;

                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                Document doc = uiDoc?.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    TaskDialog.Show("法規檢討", "請在專案檔中使用。");
                    return Result.Cancelled;
                }

                CodeCheckData data;
                try { data = CodeCheckData.Load(); }
                catch (Exception ex)
                {
                    TaskDialog.Show("法規檢討", "法規資料載入失敗：" + ex.Message);
                    return Result.Failed;
                }

                string key = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
                ProjectSettings settings = ProjectSettings.Load(key, doc.Title);
                var levelNames = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).Select(l => l.Name).ToList();
                IntPtr owner = commandData.Application.MainWindowHandle;

                bool EditSettings(System.Windows.Window parent)
                {
                    var dlg = new CodeCheckSettingsDialog(settings, levelNames);
                    if (parent != null) dlg.Owner = parent;
                    else new System.Windows.Interop.WindowInteropHelper(dlg).Owner = owner;
                    if (dlg.ShowDialog() == true)
                    {
                        settings.Save(key);
                        return true;
                    }
                    return false;
                }

                if (settings.UseGroups.Count == 0 && !EditSettings(null))
                    return Result.Cancelled;

                KeywordConfig keywords = KeywordConfig.Load();
                var win = new CodeCheckWindow(data, settings);
                new System.Windows.Interop.WindowInteropHelper(win).Owner = owner;
                List<string> lastNotes = new List<string>();
                List<SourceEntry> entries = ModelSources.Discover(doc, settings);
                bool scopeAsked = settings.Sources.Count > 0;

                bool EditSources(System.Windows.Window parent)
                {
                    var dlg = new CodeCheckSourcesDialog(settings, ModelSources.Discover(doc, settings)) { Owner = parent };
                    scopeAsked = true;
                    if (dlg.ShowDialog() != true) return false;
                    settings.Save(key);
                    entries = ModelSources.Discover(doc, settings);
                    return true;
                }

                win.EditSettings = () => EditSettings(win);
                win.EditSources = () => EditSources(win);
                win.ScopeText = () => CodeCheckSourcesDialog.Summary(entries);
                win.SaveSettings = () => settings.Save(key);
                win.SelectElements = results => SelectResults(uiDoc, results);
                win.Run = progress =>
                {
                    // 第一次在有連結的檔案執行時，先確認要合併哪些模型
                    if (!scopeAsked && entries.Count > 1)
                    {
                        if (!EditSources(win)) return null;
                    }

                    CodeCheckRunner.Outcome outcome = CodeCheckRunner.Run(doc, data, settings, keywords, entries, progress);
                    lastNotes = outcome.Notes;
                    return outcome.Engine;
                };
                win.Export = articles =>
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "匯出法規檢討報告",
                        Filter = "HTML 報告 (*.html)|*.html",
                        FileName = $"法規檢討_{doc.Title}_{DateTime.Now:yyyyMMdd_HHmm}.html"
                    };
                    if (dlg.ShowDialog(win) != true) return;
                    CodeCheckReport.WriteHtml(dlg.FileName, doc.Title, settings, data, articles, lastNotes, CodeCheckSourcesDialog.Summary(entries));
                    string csv = Path.ChangeExtension(dlg.FileName, ".csv");
                    CodeCheckReport.WriteCsv(csv, articles);
                    try { System.Diagnostics.Process.Start(dlg.FileName); } catch { }
                };

                win.ShowDialog();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("法規檢討", "執行失敗：" + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>在模型中選取結果元素：目前檔案直接選取；連結模型中的元素以連結參照選取；暫時連結的模型檔無法選取，提示元素 ID。</summary>
        private static string SelectResults(UIDocument uiDoc, IList<RuleResult> results)
        {
            Document doc = uiDoc.Document;
            var hostIds = new List<ElementId>();
            var refs = new List<Reference>();
            var tempInfo = new List<string>();
            foreach (var r in results)
            {
                var src = r.Source;
                if (src == null || src.IsHost)
                {
                    if (!hostIds.Contains(r.ElementId)) hostIds.Add(r.ElementId);
                    continue;
                }
                if (src.IsTemporary)
                {
                    tempInfo.Add($"{src.Name} ID {r.ElementId}");
                    continue;
                }
                try
                {
                    var inst = doc.GetElement(src.LinkInstanceId) as RevitLinkInstance;
                    Element e = src.Doc.GetElement(r.ElementId);
                    if (inst == null || e == null) continue;
                    if (inst.GetLinkDocument() != null && inst.GetLinkDocument().Equals(src.Doc))
                        refs.Add(new Reference(e).CreateLinkReference(inst));
                    else if (!hostIds.Contains(inst.Id))
                        hostIds.Add(inst.Id);   // 巢狀連結：選取上層連結
                }
                catch { }
            }
            int linkCount = refs.Count, total = refs.Count + hostIds.Count;
            try
            {
                if (refs.Count > 0)
                {
                    foreach (ElementId id in hostIds)
                    {
                        Element he = doc.GetElement(id);
                        if (he != null) refs.Add(new Reference(he));
                    }
                    uiDoc.Selection.SetReferences(refs);
                }
                else if (hostIds.Count > 0)
                {
                    uiDoc.Selection.SetElementIds(hostIds);
                    uiDoc.ShowElements(hostIds);
                }
            }
            catch { }
            string msg = total > 0
                ? $"已在模型中選取 {total} 個元素" + (linkCount > 0 ? $"（含連結模型中的 {linkCount} 個）" : "") + "，關閉視窗後可在 Revit 中查看。"
                : "";
            if (tempInfo.Count > 0)
                msg += $"另有 {tempInfo.Count} 個元素位於未連結的模型檔，請開啟該檔以 ID 選取：" + string.Join("、", tempInfo.Distinct().Take(6)) + (tempInfo.Count > 6 ? "…" : "");
            return msg == "" ? "沒有可選取的元素。" : msg;
        }
    }
}
