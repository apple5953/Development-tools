using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BEAI.QueryHost.Core;
using DevelopmentTools.AgentTools;

namespace DevelopmentTools.CodeCheck
{
    /// <summary>
    /// 法規檢討的 AI agent 入口：透過既有 Reality Bridge 提供 code_check.run，不開任何視窗。
    /// 與 Cmd_CodeCheck 共用 CodeCheckRunner；暫存元素在交易中建立後一律回復，不改變模型。
    /// 回傳資料一律使用 Dictionary／List，避免混淆（Obfuscar）改名後序列化欄位跑掉。
    /// </summary>
    public static class CodeCheckAgentTool
    {
        public const string ToolName = "code_check.run";
        private const int DefaultMaxItems = 200;
        private const int MaxItemsLimit = 2000;

        public static void Register()
        {
            RealityToolRegistry.Register(new RealityToolDescriptor
            {
                Name = ToolName,
                Title = "法規檢討（全條文）",
                Description = "Runs the full Taiwan building-code check on the active model and returns failed, warning and no-data items. Scratch views and links are rolled back.",
                ModelEffect = RealityModelEffect.TemporaryRollback,
                TimeoutSeconds = 300,
                RequiresDocument = true
            }, Handle);
        }

        private static RealityToolResult Handle(UIApplication app, Document doc, string argsJson)
        {
            if (doc == null || doc.IsFamilyDocument)
                return RealityToolResult.Fail("BEAI_NO_DOCUMENT", "Open a project document (not a family) in Revit first.");
            if (doc.IsModifiable)
                return RealityToolResult.Fail("BEAI_REVIT_API_ERROR", "The document is already inside a transaction or edit mode; finish it in Revit and retry.");

            string denied = AgentToolAccess.Check("CodeReview", "法規檢討");
            if (denied != null) return RealityToolResult.Fail("BEAI_PERMISSION_DENIED", denied);

            JsonObject args;
            try { args = string.IsNullOrWhiteSpace(argsJson) ? new JsonObject() : (JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject()); }
            catch (Exception ex) { return RealityToolResult.Fail("BEAI_INVALID_QUERY", "args must be a JSON object: " + ex.Message); }

            int maxItems = ReadInt(args, "maxItems", DefaultMaxItems);
            maxItems = Math.Max(1, Math.Min(maxItems, MaxItemsLimit));
            bool writeReport = ReadBool(args, "writeReport", false);

            CodeCheckData data;
            try { data = CodeCheckData.Load(); }
            catch (Exception ex) { return RealityToolResult.Fail("BEAI_REVIT_API_ERROR", "Code-check rule data failed to load: " + ex.Message); }

            string key = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
            ProjectSettings settings = ProjectSettings.Load(key, doc.Title);
            bool overridden = false;
            if (args["settings"] is JsonObject overrides)
            {
                // 只用於本次檢討，不寫回專案設定檔；人或 agent 要保存設定請走日後的受控寫入流程。
                string error;
                settings = ApplyOverrides(settings, overrides, out error);
                if (settings == null) return RealityToolResult.Fail("BEAI_INVALID_QUERY", "settings override is invalid: " + error);
                overridden = true;
            }
            if (settings.UseGroups.Count == 0)
            {
                return RealityToolResult.Fail("BEAI_INVALID_QUERY",
                    "Project conditions are not set: pass args.settings with at least useGroups (e.g. [\"H-2\"]), zone, siteAreaM2, maxCoverageRatio and maxFarRatio, or set them once in the 法規檢討 window.");
            }

            KeywordConfig keywords = KeywordConfig.Load();
            List<SourceEntry> entries = ModelSources.Discover(doc, settings);
            var progressLog = new List<string>();

            CodeCheckRunner.Outcome outcome;
            try
            {
                outcome = CodeCheckRunner.Run(doc, data, settings, keywords, entries, msg => { if (progressLog.Count < 50) progressLog.Add(msg); });
            }
            catch (InvalidOperationException ex)
            {
                return RealityToolResult.Fail("BEAI_INVALID_QUERY", ex.Message);
            }

            CodeCheckEngine engine = outcome.Engine;
            var result = new Dictionary<string, object>
            {
                ["document"] = doc.Title,
                ["sources"] = outcome.SourceCount,
                ["scope"] = CodeCheckSourcesDialog.Summary(entries),
                ["settingsOverridden"] = overridden,
                ["summary"] = Summary(engine),
                ["articles"] = engine.Articles
                    .Where(a => a.Status == CheckStatus.Fail || a.Status == CheckStatus.Warning || a.Status == CheckStatus.NoData)
                    .Take(maxItems)
                    .Select(ArticleRow)
                    .ToList(),
                ["items"] = engine.AllResults
                    .Where(r => r.Status == CheckStatus.Fail || r.Status == CheckStatus.Warning || r.Status == CheckStatus.NoData)
                    .OrderBy(r => r.Status)
                    .Take(maxItems)
                    .Select(ItemRow)
                    .ToList(),
                ["ruleErrors"] = engine.RuleErrors.Take(100).ToList(),
                ["notes"] = outcome.Notes.Take(100).ToList(),
                ["humanReviewRequired"] = "Results are machine checks of the model only. The registered architect must review and sign; Manual and NoData items need human judgement."
            };

            var warnings = new List<string>();
            if (writeReport)
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEAI", "Reports", "CodeCheck");
                    Directory.CreateDirectory(dir);
                    string safeTitle = string.Concat(doc.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                    string html = Path.Combine(dir, $"法規檢討_{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                    CodeCheckReport.WriteHtml(html, doc.Title, settings, data, engine.Articles, outcome.Notes, CodeCheckSourcesDialog.Summary(entries));
                    string csv = Path.ChangeExtension(html, ".csv");
                    CodeCheckReport.WriteCsv(csv, engine.Articles);
                    result["report"] = new Dictionary<string, object> { ["html"] = html, ["csv"] = csv };
                }
                catch (Exception ex)
                {
                    warnings.Add("Report could not be written: " + ex.Message);
                }
            }

            int total = engine.AllResults.Count(r => r.Status == CheckStatus.Fail || r.Status == CheckStatus.Warning || r.Status == CheckStatus.NoData);
            if (total > maxItems) warnings.Add($"Only the first {maxItems} of {total} open items are listed; raise args.maxItems (max {MaxItemsLimit}) or use writeReport.");

            RealityToolResult ok = RealityToolResult.Ok(result);
            ok.Warnings.AddRange(warnings);
            return ok;
        }

        private static Dictionary<string, object> Summary(CodeCheckEngine engine)
        {
            var byStatus = new Dictionary<string, object>();
            foreach (CheckStatus s in Enum.GetValues(typeof(CheckStatus)))
                byStatus[s.ToString()] = engine.Articles.Count(a => a.Status == s);
            return new Dictionary<string, object>
            {
                ["articles"] = engine.Articles.Count,
                ["articlesByStatus"] = byStatus,
                ["ruleResults"] = engine.AllResults.Count,
                ["failed"] = engine.AllResults.Count(r => r.Status == CheckStatus.Fail),
                ["warnings"] = engine.AllResults.Count(r => r.Status == CheckStatus.Warning),
                ["noData"] = engine.AllResults.Count(r => r.Status == CheckStatus.NoData),
                ["passed"] = engine.AllResults.Count(r => r.Status == CheckStatus.Pass),
                ["ruleErrors"] = engine.RuleErrors.Count
            };
        }

        private static Dictionary<string, object> ArticleRow(ArticleResult a)
        {
            return new Dictionary<string, object>
            {
                ["key"] = a.Key,
                ["law"] = a.Law?.Name ?? a.Item?.Law,
                ["article"] = a.Item?.No,
                ["kind"] = CodeCheckEngine.KindText(a.Item?.Kind),
                ["status"] = a.Status.ToString(),
                ["statusText"] = CodeCheckEngine.StatusText(a.Status),
                ["checked"] = a.Checked,
                ["failed"] = a.Failed,
                ["warned"] = a.Warned,
                ["noData"] = a.NoData,
                ["passed"] = a.Passed
            };
        }

        private static Dictionary<string, object> ItemRow(RuleResult r)
        {
            return new Dictionary<string, object>
            {
                ["status"] = r.Status.ToString(),
                ["ruleId"] = r.Rule?.Id,
                ["law"] = r.Rule?.Law,
                ["article"] = r.Rule?.Article,
                ["title"] = r.Rule?.Title,
                ["unit"] = r.Rule?.Unit,
                ["target"] = r.Target,
                ["elementId"] = r.ElementId == null || r.ElementId == ElementId.InvalidElementId ? (long?)null : ElementIdValue(r.ElementId),
                ["source"] = r.Source == null ? null : r.Source.Name,
                ["sourceIsHost"] = r.Source == null || r.Source.IsHost,
                ["measured"] = r.Measured,
                ["limit"] = r.Limit,
                ["detail"] = r.Detail
            };
        }

        private static long ElementIdValue(ElementId id)
        {
            return id.IntegerValue;   // 與 CodeCheckReport 相同；Revit 2024 目標
        }

        private static ProjectSettings ApplyOverrides(ProjectSettings current, JsonObject overrides, out string error)
        {
            error = null;
            try
            {
                var ser = new DataContractJsonSerializer(typeof(ProjectSettings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                JsonObject merged;
                using (var ms = new MemoryStream())
                {
                    ser.WriteObject(ms, current);
                    merged = JsonNode.Parse(Encoding.UTF8.GetString(ms.ToArray())) as JsonObject ?? new JsonObject();
                }
                foreach (var kv in overrides.ToList())
                {
                    if (kv.Key == "sources" || kv.Key == "manual" || kv.Key == "roomRoleOverrides") continue;   // 檢討範圍與人工判定只能由人設定
                    merged[kv.Key] = kv.Value == null ? null : JsonNode.Parse(kv.Value.ToJsonString());
                }
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(merged.ToJsonString())))
                {
                    var s = (ProjectSettings)ser.ReadObject(ms);
                    s.Normalize();
                    return s;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static int ReadInt(JsonObject args, string name, int fallback)
        {
            try { return args[name] == null ? fallback : args[name].GetValue<int>(); }
            catch { return fallback; }
        }

        private static bool ReadBool(JsonObject args, string name, bool fallback)
        {
            try { return args[name] == null ? fallback : args[name].GetValue<bool>(); }
            catch { return fallback; }
        }
    }
}
