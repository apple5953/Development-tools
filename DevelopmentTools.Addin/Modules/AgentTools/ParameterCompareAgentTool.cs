using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BEAI.QueryHost.Core;
using DevelopmentTools.Modules.ParameterCompareCopy.Models;
using DevelopmentTools.Modules.ParameterCompareCopy.Services;

namespace DevelopmentTools.AgentTools
{
    /// <summary>
    /// 參數比對的 AI agent 入口（parameters.compare）：只讀不改，使用人用「參數比對複製」視窗的 CompareAnalyzer。
    /// 比較兩個已開啟文件的視圖樣板、明細表、專案參數、線型、材料等；「複製」屬寫入，留待受控寫入流程。
    /// </summary>
    public static class ParameterCompareAgentTool
    {
        public const string ToolName = "parameters.compare";
        private const int DefaultMaxItems = 200;
        private const int MaxItemsLimit = 2000;

        public static void Register()
        {
            RealityToolRegistry.Register(new RealityToolDescriptor
            {
                Name = ToolName,
                Title = "參數比對",
                Description = "Compares view templates, schedules, project parameters, line styles and materials between two open Revit documents (source → target). Read-only.",
                ModelEffect = RealityModelEffect.None,
                TimeoutSeconds = 120,
                RequiresDocument = true
            }, Handle);
        }

        private static RealityToolResult Handle(UIApplication app, Document activeDoc, string argsJson)
        {
            string denied = AgentToolAccess.Check("ProjectCompareCopy", "專案參數比對複製");
            if (denied != null) return RealityToolResult.Fail("BEAI_PERMISSION_DENIED", denied);

            JsonObject args;
            try { args = string.IsNullOrWhiteSpace(argsJson) ? new JsonObject() : (JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject()); }
            catch (Exception ex) { return RealityToolResult.Fail("BEAI_INVALID_QUERY", "args must be a JSON object: " + ex.Message); }

            string sourceName, targetName;
            int maxItems = DefaultMaxItems;
            bool includeIdentical = false;
            try
            {
                sourceName = args["source"]?.GetValue<string>();
                targetName = args["target"]?.GetValue<string>();
                if (args["maxItems"] != null) maxItems = args["maxItems"].GetValue<int>();
                if (args["includeIdentical"] != null) includeIdentical = args["includeIdentical"].GetValue<bool>();
            }
            catch (Exception ex) { return RealityToolResult.Fail("BEAI_INVALID_QUERY", "Invalid args: " + ex.Message); }
            maxItems = Math.Max(1, Math.Min(maxItems, MaxItemsLimit));

            if (string.IsNullOrWhiteSpace(sourceName))
                return RealityToolResult.Fail("BEAI_INVALID_QUERY", "args.source is required: the title or full path of the open source document.");

            List<Document> open = AgentToolDocuments.OpenProjects(app);
            Document source = AgentToolDocuments.Find(open, sourceName);
            Document target = string.IsNullOrWhiteSpace(targetName) ? activeDoc : AgentToolDocuments.Find(open, targetName);
            if (source == null)
                return RealityToolResult.Fail("BEAI_NO_DOCUMENT", "Source document is not open in Revit: " + sourceName + ". Open documents: " + AgentToolDocuments.Titles(open));
            if (target == null)
                return RealityToolResult.Fail("BEAI_NO_DOCUMENT", "Target document is not open in Revit: " + (targetName ?? "(active)"));
            if (source.Equals(target))
                return RealityToolResult.Fail("BEAI_INVALID_QUERY", "Source and target are the same document.");

            List<CompareAsset> assets;
            try { assets = new CompareAnalyzer(source, target).AnalyzeAssets(); }
            catch (ArgumentException ex)
            {
                // 例如目標文件中有同名樣板，與人用視窗相同的限制
                return RealityToolResult.Fail("BEAI_REVIT_API_ERROR", "Comparison failed (duplicate names in a document?): " + ex.Message);
            }

            List<CompareAsset> listed = assets.Where(a => includeIdentical || a.Comparison != AssetComparison.Identical)
                .OrderBy(a => a.Comparison).ThenBy(a => a.Type).ThenBy(a => a.Name).ToList();

            var result = new Dictionary<string, object>
            {
                ["source"] = source.Title,
                ["target"] = target.Title,
                ["total"] = assets.Count,
                ["byComparison"] = assets.GroupBy(a => a.Comparison.ToString()).ToDictionary(g => g.Key, g => (object)g.Count()),
                ["byType"] = assets.GroupBy(a => a.Type.ToString()).ToDictionary(g => g.Key, g => (object)new Dictionary<string, object>
                {
                    ["onlyInSource"] = g.Count(a => a.Comparison == AssetComparison.New),
                    ["mismatch"] = g.Count(a => a.Comparison == AssetComparison.Mismatch),
                    ["identical"] = g.Count(a => a.Comparison == AssetComparison.Identical)
                }),
                ["items"] = listed.Take(maxItems).Select(a => new Dictionary<string, object>
                {
                    ["type"] = a.Type.ToString(),
                    ["name"] = a.Name,
                    ["comparison"] = a.Comparison == AssetComparison.New ? "OnlyInSource" : a.Comparison.ToString(),
                    ["sourceElementId"] = a.ElementId == null ? (long?)null : a.ElementId.IntegerValue,
                    ["differences"] = a.DiffDetails.Where(d => d.IsDifferent).Take(50).Select(d => new Dictionary<string, object>
                    {
                        ["parameter"] = d.ParamName,
                        ["source"] = d.SourceValue,
                        ["target"] = d.TargetValue
                    }).ToList()
                }).ToList(),
                ["copyNote"] = "Copying assets into the target model is a write operation and is not available to agents yet; use the 參數比對複製 window."
            };

            RealityToolResult ok = RealityToolResult.Ok(result);
            if (listed.Count > maxItems)
                ok.Warnings.Add($"Only the first {maxItems} of {listed.Count} items are listed; raise args.maxItems (max {MaxItemsLimit}).");
            return ok;
        }
    }
}
