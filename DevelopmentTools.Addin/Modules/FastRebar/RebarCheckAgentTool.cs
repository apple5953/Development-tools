using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using BEAI.QueryHost.Core;
using DevelopmentTools.AgentTools;

namespace DevelopmentTools.FastRebar
{
    /// <summary>
    /// 鋼筋檢核的 AI agent 入口（rebar.check）：只讀不改，與人用 Cmd_RebarCheck 使用同一個 RebarModelChecker。
    /// 指定 elementIds（梁柱或鋼筋）時只檢查那些構件，否則檢查整個模型。
    /// </summary>
    public static class RebarCheckAgentTool
    {
        public const string ToolName = "rebar.check";
        private const int MaxElementIds = 2000;
        private const int DefaultMaxItems = 200;
        private const int MaxItemsLimit = 2000;

        public static void Register()
        {
            RealityToolRegistry.Register(new RealityToolDescriptor
            {
                Name = ToolName,
                Title = "鋼筋檢核",
                Description = "Checks rebar in the given beams/columns/rebar (or the whole model) and returns issues with element IDs. Read-only.",
                ModelEffect = RealityModelEffect.None,
                TimeoutSeconds = 180,
                RequiresDocument = true
            }, Handle);
        }

        private static RealityToolResult Handle(UIApplication app, Document doc, string argsJson)
        {
            if (doc == null || doc.IsFamilyDocument)
                return RealityToolResult.Fail("BEAI_NO_DOCUMENT", "Open a project document (not a family) in Revit first.");

            string denied = AgentToolAccess.Check("AutoRebar", "鋼筋檢核");
            if (denied != null) return RealityToolResult.Fail("BEAI_PERMISSION_DENIED", denied);

            JsonObject args;
            try { args = string.IsNullOrWhiteSpace(argsJson) ? new JsonObject() : (JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject()); }
            catch (Exception ex) { return RealityToolResult.Fail("BEAI_INVALID_QUERY", "args must be a JSON object: " + ex.Message); }

            int maxItems = DefaultMaxItems;
            bool writeReport = false;
            var requested = new List<int>();
            try
            {
                if (args["maxItems"] != null) maxItems = args["maxItems"].GetValue<int>();
                if (args["writeReport"] != null) writeReport = args["writeReport"].GetValue<bool>();
                if (args["elementIds"] is JsonArray arr)
                {
                    if (arr.Count > MaxElementIds) return RealityToolResult.Fail("BEAI_INVALID_QUERY", $"elementIds accepts at most {MaxElementIds} IDs.");
                    foreach (JsonNode n in arr) requested.Add(n.GetValue<int>());
                }
            }
            catch (Exception ex) { return RealityToolResult.Fail("BEAI_INVALID_QUERY", "Invalid args: " + ex.Message); }
            maxItems = Math.Max(1, Math.Min(maxItems, MaxItemsLimit));

            var rebars = new List<Rebar>();
            var missing = new List<int>();
            string scope;
            if (requested.Count > 0)
            {
                foreach (int id in requested.Distinct())
                {
                    Element e = doc.GetElement(new ElementId(id));
                    if (e == null) { missing.Add(id); continue; }
                    if (e is Rebar rb) rebars.Add(rb);
                    else rebars.AddRange(FastRebarEngine.ExistingRebar(e));
                }
                rebars = rebars.GroupBy(r => r.Id.IntegerValue).Select(g => g.First()).ToList();
                scope = "elements";
            }
            else
            {
                rebars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().ToList();
                scope = "model";
            }

            var checker = new RebarModelChecker(doc);
            if (rebars.Count > 0) checker.Run(rebars);

            var result = new Dictionary<string, object>
            {
                ["document"] = doc.Title,
                ["scope"] = scope,
                ["rebarCount"] = rebars.Count,
                ["hostsChecked"] = checker.HostsChecked,
                ["barsChecked"] = checker.BarsChecked,
                ["issueCount"] = checker.Issues.Count,
                ["issuesByCategory"] = checker.Issues.GroupBy(i => i.Category ?? "").ToDictionary(g => g.Key, g => (object)g.Count()),
                ["issues"] = checker.Issues.Take(maxItems).Select(i => new Dictionary<string, object>
                {
                    ["category"] = i.Category,
                    ["text"] = i.Text,
                    ["elementIds"] = i.Ids.Select(x => (long)x.IntegerValue).ToList()
                }).ToList(),
                ["missingElementIds"] = missing,
                ["humanReviewRequired"] = "Model checks only; the structural engineer must review and sign."
            };

            var warnings = new List<string>();
            if (rebars.Count == 0) warnings.Add(scope == "model" ? "The model has no rebar." : "The given elements have no rebar.");
            if (checker.Issues.Count > maxItems) warnings.Add($"Only the first {maxItems} of {checker.Issues.Count} issues are listed; raise args.maxItems (max {MaxItemsLimit}) or use writeReport.");
            if (writeReport && rebars.Count > 0)
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEAI", "Reports", "RebarCheck");
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, $"鋼筋檢核_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    File.WriteAllText(path, checker.ToCsv(), new UTF8Encoding(true));
                    result["report"] = new Dictionary<string, object> { ["csv"] = path };
                }
                catch (Exception ex) { warnings.Add("Report could not be written: " + ex.Message); }
            }

            RealityToolResult ok = RealityToolResult.Ok(result);
            ok.Warnings.AddRange(warnings);
            return ok;
        }
    }
}
