using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BEAI.QueryHost.Core;
using DevelopmentTools.ProjectStandards.Cloud.ModelResolver;
using DevelopmentTools.ProjectStandards.Domain.Diff;
using DevelopmentTools.ProjectStandards.Domain.Identity;
using DevelopmentTools.ProjectStandards.Domain.Models;
using DevelopmentTools.ProjectStandards.Domain.Services;
using DevelopmentTools.ProjectStandards.Domain.Snapshots;
using DevelopmentTools.ProjectStandards.Revit2024.Scanner;

namespace DevelopmentTools.AgentTools
{
    /// <summary>
    /// 專案標準比對的 AI agent 入口（project_standards.compare）：只讀不改。
    /// 與人用「專案標準比對」視窗相同的掃描器與比對引擎，比較兩個已在 Revit 開啟的文件
    /// （基準 master 與目標 target，target 預設為目前作用中的文件）。不會自行開啟或關閉檔案；
    /// 同步（把差異套到目標模型）屬寫入，留待受控寫入流程。
    /// </summary>
    public static class ProjectStandardsAgentTool
    {
        public const string ToolName = "project_standards.compare";
        private const int DefaultMaxItems = 200;
        private const int MaxItemsLimit = 2000;

        public static void Register()
        {
            RealityToolRegistry.Register(new RealityToolDescriptor
            {
                Name = ToolName,
                Title = "專案標準比對",
                Description = "Compares project standards (parameters, view templates, filters, line styles, families, schedules, …) between two open Revit documents. Read-only.",
                ModelEffect = RealityModelEffect.None,
                TimeoutSeconds = 180,
                RequiresDocument = true
            }, Handle);
        }

        private static RealityToolResult Handle(UIApplication app, Document activeDoc, string argsJson)
        {
            string denied = AgentToolAccess.Check("DT_ProjectStandards", "專案標準比對");
            if (denied != null) return RealityToolResult.Fail("BEAI_PERMISSION_DENIED", denied);

            JsonObject args;
            try { args = string.IsNullOrWhiteSpace(argsJson) ? new JsonObject() : (JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject()); }
            catch (Exception ex) { return RealityToolResult.Fail("BEAI_INVALID_QUERY", "args must be a JSON object: " + ex.Message); }

            string masterName, targetName;
            int maxItems = DefaultMaxItems;
            bool includeEqual = false;
            try
            {
                masterName = args["master"]?.GetValue<string>();
                targetName = args["target"]?.GetValue<string>();
                if (args["maxItems"] != null) maxItems = args["maxItems"].GetValue<int>();
                if (args["includeEqual"] != null) includeEqual = args["includeEqual"].GetValue<bool>();
            }
            catch (Exception ex) { return RealityToolResult.Fail("BEAI_INVALID_QUERY", "Invalid args: " + ex.Message); }
            maxItems = Math.Max(1, Math.Min(maxItems, MaxItemsLimit));

            if (string.IsNullOrWhiteSpace(masterName))
                return RealityToolResult.Fail("BEAI_INVALID_QUERY", "args.master is required: the title or full path of the open standards (master) document.");

            List<Document> open = AgentToolDocuments.OpenProjects(app);
            Document master = AgentToolDocuments.Find(open, masterName);
            Document target = string.IsNullOrWhiteSpace(targetName) ? activeDoc : AgentToolDocuments.Find(open, targetName);
            if (master == null)
                return RealityToolResult.Fail("BEAI_NO_DOCUMENT", "Master document is not open in Revit: " + masterName + ". Open documents: " + AgentToolDocuments.Titles(open));
            if (target == null)
                return RealityToolResult.Fail("BEAI_NO_DOCUMENT", "Target document is not open in Revit: " + (targetName ?? "(active)"));
            if (master.Equals(target))
                return RealityToolResult.Fail("BEAI_INVALID_QUERY", "Master and target are the same document.");

            var resolver = new ModelSourceResolver();
            var canonicalizer = new CanonicalizationService();
            var signatures = new SignatureService(canonicalizer);
            var diffEngine = new DiffEngine(canonicalizer, signatures);
            var scanner = new RevitStandardsScanner();

            ProjectStandardsSnapshot masterSnap = scanner.Scan(master, Describe(resolver, master));
            ProjectStandardsSnapshot targetSnap = scanner.Scan(target, Describe(resolver, target));
            StandardsDiffResult diff = diffEngine.Compare(masterSnap, targetSnap);

            List<DiffItem> differing = diff.Items.Where(i => includeEqual || i.Status != DiffStatus.Equal)
                .OrderByDescending(i => (int)i.Risk).ThenBy(i => i.CategoryName).ToList();

            var result = new Dictionary<string, object>
            {
                ["master"] = master.Title,
                ["target"] = target.Title,
                ["signatureIdentical"] = diff.IsSignatureIdentical,
                ["totalDifferences"] = diff.TotalDifferences,
                ["byStatus"] = diff.Items.GroupBy(i => i.Status.ToString()).ToDictionary(g => g.Key, g => (object)g.Count()),
                ["categories"] = diff.CategorySummaries.Values.Select(c => new Dictionary<string, object>
                {
                    ["key"] = c.CategoryKey,
                    ["name"] = c.CategoryDisplayName,
                    ["masterItems"] = c.TotalMasterItems,
                    ["targetItems"] = c.TotalTargetItems,
                    ["equal"] = c.EqualCount,
                    ["modified"] = c.ModifiedCount,
                    ["missing"] = c.MissingCount,
                    ["extra"] = c.ExtraCount,
                    ["conflict"] = c.ConflictCount,
                    ["renameCandidates"] = c.RenameCount
                }).ToList(),
                ["items"] = differing.Take(maxItems).Select(ItemRow).ToList(),
                ["syncNote"] = "Applying differences to the target model is a write operation and is not available to agents yet; use the 專案標準比對 window."
            };

            RealityToolResult ok = RealityToolResult.Ok(result);
            if (differing.Count > maxItems)
                ok.Warnings.Add($"Only the first {maxItems} of {differing.Count} items are listed (highest risk first); raise args.maxItems (max {MaxItemsLimit}).");
            return ok;
        }

        private static ModelDescriptor Describe(ModelSourceResolver resolver, Document doc)
        {
            return resolver.ResolveAsync(new ModelReference
            {
                RawPath = doc.PathName,
                DisplayName = doc.Title,
                KnownIsCloudModel = doc.IsModelInCloud,
                KnownIsWorkshared = doc.IsWorkshared
            }).Result;
        }

        private static Dictionary<string, object> ItemRow(DiffItem i)
        {
            return new Dictionary<string, object>
            {
                ["category"] = i.CategoryName,
                ["status"] = i.Status.ToString(),
                ["risk"] = i.Risk.ToString(),
                ["master"] = Name(i.MasterIdentity),
                ["target"] = Name(i.TargetIdentity),
                ["suggestedAction"] = i.SuggestedAction.ToString(),
                ["conflictReason"] = i.ConflictReason,
                ["fields"] = (i.FieldDiffs ?? new List<FieldDiff>())
                    .Where(f => f.Status != DiffStatus.Equal)
                    .Take(50)
                    .Select(f => new Dictionary<string, object>
                    {
                        ["field"] = f.FieldName,
                        ["master"] = f.MasterValue,
                        ["target"] = f.TargetValue,
                        ["status"] = f.Status.ToString(),
                        ["description"] = f.Description
                    }).ToList()
            };
        }

        private static string Name(StableIdentity id)
        {
            if (id == null) return null;
            return string.IsNullOrEmpty(id.DisplayName) ? id.Key : id.DisplayName;
        }
    }
}
