using System;
using System.Collections.Generic;
using System.Linq;
using DevelopmentTools.ProjectStandards.Domain.Diff;
using DevelopmentTools.ProjectStandards.Domain.Identity;
using DevelopmentTools.ProjectStandards.Domain.Models;
using DevelopmentTools.ProjectStandards.Domain.Snapshots;

namespace DevelopmentTools.ProjectStandards.Domain.Services
{
    public class DiffEngine : IDiffEngine
    {
        private readonly ICanonicalizationService _canonicalizer;
        private readonly ISignatureService _signatureService;

        public DiffEngine(ICanonicalizationService canonicalizer, ISignatureService signatureService)
        {
            if (canonicalizer == null) throw new ArgumentNullException("canonicalizer");
            if (signatureService == null) throw new ArgumentNullException("signatureService");
            _canonicalizer = canonicalizer;
            _signatureService = signatureService;
        }

        private static Dictionary<string, T> BuildSafeDictionary<T>(IEnumerable<T> items, Func<T, string> keySelector)
        {
            var dict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            if (items == null) return dict;
            foreach (var item in items)
            {
                var key = keySelector(item);
                if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key))
                {
                    dict[key] = item;
                }
            }
            return dict;
        }

        public StandardsDiffResult Compare(ProjectStandardsSnapshot master, ProjectStandardsSnapshot target)
        {
            var result = new StandardsDiffResult();
            result.MasterMetadata = master != null ? master.Metadata : null;
            result.TargetMetadata = target != null ? target.Metadata : null;

            if (master == null && target == null)
            {
                result.IsSignatureIdentical = true;
                return result;
            }

            var canonicalMaster = _canonicalizer.Canonicalize(master ?? new ProjectStandardsSnapshot());
            var canonicalTarget = _canonicalizer.Canonicalize(target ?? new ProjectStandardsSnapshot());

            var masterSig = _signatureService.ComputeSnapshotSignature(canonicalMaster);
            var targetSig = _signatureService.ComputeSnapshotSignature(canonicalTarget);

            if (string.Equals(masterSig, targetSig, StringComparison.OrdinalIgnoreCase))
            {
                result.IsSignatureIdentical = true;
            }

            CompareWorksets(canonicalMaster.Worksets, canonicalTarget.Worksets, result);
            CompareParameters(canonicalMaster.Parameters, canonicalTarget.Parameters, result);
            CompareLevels(canonicalMaster.Levels, canonicalTarget.Levels, result);
            CompareLinePatterns(canonicalMaster.LinePatterns, canonicalTarget.LinePatterns, result);
            CompareLineStyles(canonicalMaster.LineStyles, canonicalTarget.LineStyles, result);
            CompareFillPatterns(canonicalMaster.FillPatterns, canonicalTarget.FillPatterns, result);
            CompareObjectStyles(canonicalMaster.ObjectStyles, canonicalTarget.ObjectStyles, result);
            CompareViewFilters(canonicalMaster.ViewFilters, canonicalTarget.ViewFilters, result);
            CompareViewTemplates(canonicalMaster.ViewTemplates, canonicalTarget.ViewTemplates, result);
            CompareSchedules(canonicalMaster.Schedules, canonicalTarget.Schedules, result);
            CompareTextTypes(canonicalMaster.TextTypes, canonicalTarget.TextTypes, result);
            CompareDimensionTypes(canonicalMaster.DimensionTypes, canonicalTarget.DimensionTypes, result);
            CompareFamilyTypes(canonicalMaster.FamilyTypes, canonicalTarget.FamilyTypes, result);
            CompareLegends(canonicalMaster.Legends, canonicalTarget.Legends, result);
            CompareSheets(canonicalMaster.Sheets, canonicalTarget.Sheets, result);
            CompareBrowserOrganizations(canonicalMaster.BrowserOrganizations, canonicalTarget.BrowserOrganizations, result);
            CompareCoordinates(canonicalMaster.Coordinates, canonicalTarget.Coordinates, result);

            result.RecomputeSummaries();
            return result;
        }

        private void CompareWorksets(List<WorksetSnapshot> masterList, List<WorksetSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<WorksetSnapshot>();
            targetList = targetList ?? new List<WorksetSnapshot>();

            var targetMap = BuildSafeDictionary(targetList, w => w.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                WorksetSnapshot t;
                if (!targetMap.TryGetValue(m.Name, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "Workset";
                    item.CategoryName = "工作集 (Worksets)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Low;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "Workset";
                        item.CategoryName = "工作集 (Worksets)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (m.IsVisibleByDefault != t.IsVisibleByDefault)
                            fieldDiffs.Add(new FieldDiff { FieldName = "預設可見性 (IsVisibleByDefault)", MasterValue = m.IsVisibleByDefault.ToString(), TargetValue = t.IsVisibleByDefault.ToString(), Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "Workset";
                        item.CategoryName = "工作集 (Worksets)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Medium;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetMap.Remove(m.Name);
                }
            }

            foreach (var extra in targetMap.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "Workset";
                item.CategoryName = "工作集 (Worksets)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareParameters(List<ProjectParameterSnapshot> masterList, List<ProjectParameterSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<ProjectParameterSnapshot>();
            targetList = targetList ?? new List<ProjectParameterSnapshot>();

            var targetByName = BuildSafeDictionary(targetList, p => p.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                ProjectParameterSnapshot t;

                if (!targetByName.TryGetValue(m.Name, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "ProjectParameter";
                    item.CategoryName = "專案參數 (Parameters)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.High;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);

                    if (m.IsShared && t.IsShared && m.SharedParameterGuid.HasValue && t.SharedParameterGuid.HasValue &&
                        !m.SharedParameterGuid.Value.Equals(t.SharedParameterGuid.Value))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "ProjectParameter";
                        item.CategoryName = "專案參數 (Parameters)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Conflict;
                        item.Risk = RiskLevel.Critical;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.ConflictReason = string.Format("共用參數 GUID 不一致：基準=[{0:D}]，目標=[{1:D}]。已阻斷自動同步。", m.SharedParameterGuid.Value, t.SharedParameterGuid.Value);
                        item.SuggestedAction = SyncAction.Manual;
                        item.FieldDiffs.Add(new FieldDiff
                        {
                            FieldName = "共用參數 GUID (SharedParameterGuid)",
                            MasterValue = m.SharedParameterGuid.Value.ToString("D"),
                            TargetValue = t.SharedParameterGuid.Value.ToString("D"),
                            Status = DiffStatus.Conflict,
                            Description = "嚴重衝突：GUID 不相同"
                        });
                        result.Items.Add(item);
                    }
                    else if (m.IsShared != t.IsShared)
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "ProjectParameter";
                        item.CategoryName = "專案參數 (Parameters)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Conflict;
                        item.Risk = RiskLevel.Critical;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.ConflictReason = string.Format("參數型態不一致：基準為 {0} 參數，目標為 {1} 參數。", m.IsShared ? "共用" : "專案", t.IsShared ? "共用" : "專案");
                        item.SuggestedAction = SyncAction.Manual;
                        result.Items.Add(item);
                    }
                    else if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "ProjectParameter";
                        item.CategoryName = "專案參數 (Parameters)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (!string.Equals(m.DataType, t.DataType, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "資料型態 (DataType)", MasterValue = m.DataType, TargetValue = t.DataType, Status = DiffStatus.Modified });

                        if (!string.Equals(m.ParameterGroup, t.ParameterGroup, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "參數群組 (ParameterGroup)", MasterValue = m.ParameterGroup, TargetValue = t.ParameterGroup, Status = DiffStatus.Modified });

                        if (m.IsInstance != t.IsInstance)
                            fieldDiffs.Add(new FieldDiff { FieldName = "實體/類型 (IsInstance)", MasterValue = m.IsInstance ? "實體 (Instance)" : "類型 (Type)", TargetValue = t.IsInstance ? "實體 (Instance)" : "類型 (Type)", Status = DiffStatus.Modified });

                        var mCats = string.Join(",", (m.BoundCategories ?? new List<string>()).ToArray());
                        var tCats = string.Join(",", (t.BoundCategories ?? new List<string>()).ToArray());
                        if (!string.Equals(mCats, tCats, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "綁定品類 (BoundCategories)", MasterValue = mCats, TargetValue = tCats, Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "ProjectParameter";
                        item.CategoryName = "專案參數 (Parameters)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.High;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }

                    targetByName.Remove(m.Name);
                }
            }

            foreach (var extra in targetByName.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "ProjectParameter";
                item.CategoryName = "專案參數 (Parameters)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareLevels(List<LevelSnapshot> masterList, List<LevelSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<LevelSnapshot>();
            targetList = targetList ?? new List<LevelSnapshot>();

            var targetMap = BuildSafeDictionary(targetList, l => l.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                LevelSnapshot t;

                if (!targetMap.TryGetValue(m.Name, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "Level";
                    item.CategoryName = "樓層 (Levels)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.High;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "Level";
                        item.CategoryName = "樓層 (Levels)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (Math.Abs(m.Elevation - t.Elevation) > 1e-6)
                            fieldDiffs.Add(new FieldDiff { FieldName = "高程 (Elevation)", MasterValue = m.Elevation.ToString("F4"), TargetValue = t.Elevation.ToString("F4"), Status = DiffStatus.Modified });

                        if (m.UseStory != t.UseStory)
                            fieldDiffs.Add(new FieldDiff { FieldName = "建築樓層 (BuildingStory)", MasterValue = m.UseStory.ToString(), TargetValue = t.UseStory.ToString(), Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "Level";
                        item.CategoryName = "樓層 (Levels)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.High;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetMap.Remove(m.Name);
                }
            }

            foreach (var extra in targetMap.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "Level";
                item.CategoryName = "樓層 (Levels)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareLinePatterns(List<LinePatternSnapshot> masterList, List<LinePatternSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<LinePatternSnapshot>();
            targetList = targetList ?? new List<LinePatternSnapshot>();

            var targetByName = BuildSafeDictionary(targetList, lp => lp.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                LinePatternSnapshot t;

                if (!targetByName.TryGetValue(m.Name, out t))
                {
                    var renameCandidate = targetList.FirstOrDefault(targetItem =>
                        string.Equals(targetItem.PatternDefinition, m.PatternDefinition, StringComparison.OrdinalIgnoreCase) &&
                        !masterList.Any(ml => string.Equals(ml.Name, targetItem.Name, StringComparison.OrdinalIgnoreCase)));

                    if (renameCandidate != null)
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "LinePattern";
                        item.CategoryName = "線條型樣 (Line Patterns)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = renameCandidate.Identity;
                        item.Status = DiffStatus.RenameCandidate;
                        item.Risk = RiskLevel.Medium;
                        item.MasterSignature = mSig;
                        item.TargetSignature = _signatureService.ComputeItemSignature(renameCandidate);
                        item.SuggestedAction = SyncAction.Rename;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "LinePattern";
                        item.CategoryName = "線條型樣 (Line Patterns)";
                        item.MasterIdentity = m.Identity;
                        item.Status = DiffStatus.Missing;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.SuggestedAction = SyncAction.AddMissing;
                        result.Items.Add(item);
                    }
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "LinePattern";
                        item.CategoryName = "線條型樣 (Line Patterns)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "LinePattern";
                        item.CategoryName = "線條型樣 (Line Patterns)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs.Add(new FieldDiff
                        {
                            FieldName = "型樣定義 (PatternDefinition)",
                            MasterValue = m.PatternDefinition,
                            TargetValue = t.PatternDefinition,
                            Status = DiffStatus.Modified
                        });
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetByName.Remove(m.Name);
                }
            }

            foreach (var extra in targetByName.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "LinePattern";
                item.CategoryName = "線條型樣 (Line Patterns)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareLineStyles(List<LineStyleSnapshot> masterList, List<LineStyleSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<LineStyleSnapshot>();
            targetList = targetList ?? new List<LineStyleSnapshot>();

            var targetByName = BuildSafeDictionary(targetList, ls => ls.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                LineStyleSnapshot t;

                if (!targetByName.TryGetValue(m.Name, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "LineStyle";
                    item.CategoryName = "線條型式 (Line Styles)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Low;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "LineStyle";
                        item.CategoryName = "線條型式 (Line Styles)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (m.LineWeight != t.LineWeight)
                            fieldDiffs.Add(new FieldDiff { FieldName = "線重 (LineWeight)", MasterValue = m.LineWeight.ToString(), TargetValue = t.LineWeight.ToString(), Status = DiffStatus.Modified });

                        if (!string.Equals(m.ColorRgb, t.ColorRgb, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "顏色 (ColorRgb)", MasterValue = m.ColorRgb, TargetValue = t.ColorRgb, Status = DiffStatus.Modified });

                        if (!string.Equals(m.LinePatternName, t.LinePatternName, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "線條型樣 (LinePatternName)", MasterValue = m.LinePatternName, TargetValue = t.LinePatternName, Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "LineStyle";
                        item.CategoryName = "線條型式 (Line Styles)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetByName.Remove(m.Name);
                }
            }

            foreach (var extra in targetByName.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "LineStyle";
                item.CategoryName = "線條型式 (Line Styles)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareFillPatterns(List<FillPatternSnapshot> masterList, List<FillPatternSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<FillPatternSnapshot>();
            targetList = targetList ?? new List<FillPatternSnapshot>();

            var targetMap = BuildSafeDictionary(targetList, fp => string.Format("{0}::{1}", fp.TargetType, fp.Name));

            foreach (var m in masterList)
            {
                var key = string.Format("{0}::{1}", m.TargetType, m.Name);
                var mSig = _signatureService.ComputeItemSignature(m);
                FillPatternSnapshot t;

                if (!targetMap.TryGetValue(key, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "FillPattern";
                    item.CategoryName = "填滿樣式 (Fill Patterns)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Low;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "FillPattern";
                        item.CategoryName = "填滿樣式 (Fill Patterns)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "FillPattern";
                        item.CategoryName = "填滿樣式 (Fill Patterns)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetMap.Remove(key);
                }
            }

            foreach (var extra in targetMap.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "FillPattern";
                item.CategoryName = "填滿樣式 (Fill Patterns)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareObjectStyles(List<ObjectStyleSnapshot> masterList, List<ObjectStyleSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<ObjectStyleSnapshot>();
            targetList = targetList ?? new List<ObjectStyleSnapshot>();

            var targetMap = BuildSafeDictionary(targetList, os => os.BuiltInCategory ?? os.CategoryName);

            foreach (var m in masterList)
            {
                var key = m.BuiltInCategory ?? m.CategoryName;
                var mSig = _signatureService.ComputeItemSignature(m);
                ObjectStyleSnapshot t;

                if (!targetMap.TryGetValue(key, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "ObjectStyle";
                    item.CategoryName = "物件型式 (Object Styles)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Medium;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "ObjectStyle";
                        item.CategoryName = "物件型式 (Object Styles)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (m.ProjectionLineWeight != t.ProjectionLineWeight)
                            fieldDiffs.Add(new FieldDiff { FieldName = "投影線重 (ProjectionLineWeight)", MasterValue = m.ProjectionLineWeight.ToString(), TargetValue = t.ProjectionLineWeight.ToString(), Status = DiffStatus.Modified });

                        if (m.CutLineWeight != t.CutLineWeight)
                            fieldDiffs.Add(new FieldDiff { FieldName = "剖面線重 (CutLineWeight)", MasterValue = m.CutLineWeight.ToString(), TargetValue = t.CutLineWeight.ToString(), Status = DiffStatus.Modified });

                        if (!string.Equals(m.LineColorRgb, t.LineColorRgb, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "線條顏色 (LineColorRgb)", MasterValue = m.LineColorRgb, TargetValue = t.LineColorRgb, Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "ObjectStyle";
                        item.CategoryName = "物件型式 (Object Styles)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Medium;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetMap.Remove(key);
                }
            }

            foreach (var extra in targetMap.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "ObjectStyle";
                item.CategoryName = "物件型式 (Object Styles)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareViewFilters(List<ViewFilterSnapshot> masterList, List<ViewFilterSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<ViewFilterSnapshot>();
            targetList = targetList ?? new List<ViewFilterSnapshot>();

            var targetByName = BuildSafeDictionary(targetList, vf => vf.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                ViewFilterSnapshot t;

                if (!targetByName.TryGetValue(m.Name, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "ViewFilter";
                    item.CategoryName = "視圖過濾器 (View Filters)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Medium;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "ViewFilter";
                        item.CategoryName = "視圖過濾器 (View Filters)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (!string.Equals(m.CategorySignature, t.CategorySignature, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "品類簽章 (Categories)", MasterValue = m.CategorySignature, TargetValue = t.CategorySignature, Status = DiffStatus.Modified });

                        if (!string.Equals(m.RuleSignature, t.RuleSignature, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "規則簽章 (Rules)", MasterValue = m.RuleSignature, TargetValue = t.RuleSignature, Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "ViewFilter";
                        item.CategoryName = "視圖過濾器 (View Filters)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Medium;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetByName.Remove(m.Name);
                }
            }

            foreach (var extra in targetByName.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "ViewFilter";
                item.CategoryName = "視圖過濾器 (View Filters)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareViewTemplates(List<ViewTemplateSnapshot> masterList, List<ViewTemplateSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<ViewTemplateSnapshot>();
            targetList = targetList ?? new List<ViewTemplateSnapshot>();

            var targetByName = BuildSafeDictionary(targetList, vt => vt.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                ViewTemplateSnapshot t;

                if (!targetByName.TryGetValue(m.Name, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "ViewTemplate";
                    item.CategoryName = "視圖樣板 (View Templates)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Medium;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "ViewTemplate";
                        item.CategoryName = "視圖樣板 (View Templates)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (m.Scale != t.Scale)
                            fieldDiffs.Add(new FieldDiff { FieldName = "比例 (Scale)", MasterValue = m.Scale.ToString(), TargetValue = t.Scale.ToString(), Status = DiffStatus.Modified });

                        if (!string.Equals(m.Discipline, t.Discipline, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "專業領域 (Discipline)", MasterValue = m.Discipline, TargetValue = t.Discipline, Status = DiffStatus.Modified });

                        if (!string.Equals(m.DetailLevel, t.DetailLevel, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "細緻程度 (DetailLevel)", MasterValue = m.DetailLevel, TargetValue = t.DetailLevel, Status = DiffStatus.Modified });

                        if (!string.Equals(m.PhaseFilter, t.PhaseFilter, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "階段過濾器 (PhaseFilter)", MasterValue = m.PhaseFilter, TargetValue = t.PhaseFilter, Status = DiffStatus.Modified });

                        var mFilters = m.FilterOverrides != null ? m.FilterOverrides.Select(f => f.FilterName).ToList() : new List<string>();
                        var tFilters = t.FilterOverrides != null ? t.FilterOverrides.Select(f => f.FilterName).ToList() : new List<string>();
                        var missingFilters = mFilters.Except(tFilters, StringComparer.OrdinalIgnoreCase).ToList();
                        if (missingFilters.Count > 0)
                        {
                            fieldDiffs.Add(new FieldDiff
                            {
                                FieldName = "過濾器清單 (Filters)",
                                MasterValue = string.Join(",", mFilters.ToArray()),
                                TargetValue = string.Join(",", tFilters.ToArray()),
                                Status = DiffStatus.DependencyMissing,
                                Description = string.Format("目標樣板缺漏過濾器：{0}", string.Join(", ", missingFilters.ToArray()))
                            });
                        }

                        var item = new DiffItem();
                        item.AdapterKey = "ViewTemplate";
                        item.CategoryName = "視圖樣板 (View Templates)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Medium;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetByName.Remove(m.Name);
                }
            }

            foreach (var extra in targetByName.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "ViewTemplate";
                item.CategoryName = "視圖樣板 (View Templates)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareSchedules(List<ScheduleSnapshot> masterList, List<ScheduleSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<ScheduleSnapshot>();
            targetList = targetList ?? new List<ScheduleSnapshot>();

            var targetByName = BuildSafeDictionary(targetList, s => s.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                ScheduleSnapshot t;

                if (!targetByName.TryGetValue(m.Name, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "Schedule";
                    item.CategoryName = "明細表 (Schedules)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Medium;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "Schedule";
                        item.CategoryName = "明細表 (Schedules)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (!string.Equals(m.Category, t.Category, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "品類 (Category)", MasterValue = m.Category, TargetValue = t.Category, Status = DiffStatus.Modified });

                        if (m.ItemizeEveryInstance != t.ItemizeEveryInstance)
                            fieldDiffs.Add(new FieldDiff { FieldName = "列出每個實體 (ItemizeEveryInstance)", MasterValue = m.ItemizeEveryInstance.ToString(), TargetValue = t.ItemizeEveryInstance.ToString(), Status = DiffStatus.Modified });

                        var mFields = m.Fields != null ? string.Join(",", m.Fields.Select(f => f.Heading).ToArray()) : string.Empty;
                        var tFields = t.Fields != null ? string.Join(",", t.Fields.Select(f => f.Heading).ToArray()) : string.Empty;
                        if (!string.Equals(mFields, tFields, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "欄位清單 (Fields)", MasterValue = mFields, TargetValue = tFields, Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "Schedule";
                        item.CategoryName = "明細表 (Schedules)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Medium;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetByName.Remove(m.Name);
                }
            }

            foreach (var extra in targetByName.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "Schedule";
                item.CategoryName = "明細表 (Schedules)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareTextTypes(List<TextTypeSnapshot> masterList, List<TextTypeSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<TextTypeSnapshot>();
            targetList = targetList ?? new List<TextTypeSnapshot>();

            var targetByName = BuildSafeDictionary(targetList, tt => tt.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                TextTypeSnapshot t;

                if (!targetByName.TryGetValue(m.Name, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "TextType";
                    item.CategoryName = "文字型式 (Text Types)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Low;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "TextType";
                        item.CategoryName = "文字型式 (Text Types)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (!string.Equals(m.FontName, t.FontName, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "字型 (FontName)", MasterValue = m.FontName, TargetValue = t.FontName, Status = DiffStatus.Modified });

                        if (Math.Abs(m.TextSize - t.TextSize) > 1e-6)
                            fieldDiffs.Add(new FieldDiff { FieldName = "文字大小 (TextSize)", MasterValue = m.TextSize.ToString("F4"), TargetValue = t.TextSize.ToString("F4"), Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "TextType";
                        item.CategoryName = "文字型式 (Text Types)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetByName.Remove(m.Name);
                }
            }

            foreach (var extra in targetByName.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "TextType";
                item.CategoryName = "文字型式 (Text Types)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareDimensionTypes(List<DimensionTypeSnapshot> masterList, List<DimensionTypeSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<DimensionTypeSnapshot>();
            targetList = targetList ?? new List<DimensionTypeSnapshot>();

            var targetByName = BuildSafeDictionary(targetList, dt => dt.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                DimensionTypeSnapshot t;

                if (!targetByName.TryGetValue(m.Name, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "DimensionType";
                    item.CategoryName = "標註型式 (Dimension Types)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Low;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "DimensionType";
                        item.CategoryName = "標註型式 (Dimension Types)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (!string.Equals(m.FontName, t.FontName, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "字型 (FontName)", MasterValue = m.FontName, TargetValue = t.FontName, Status = DiffStatus.Modified });

                        if (Math.Abs(m.TextSize - t.TextSize) > 1e-6)
                            fieldDiffs.Add(new FieldDiff { FieldName = "文字大小 (TextSize)", MasterValue = m.TextSize.ToString("F4"), TargetValue = t.TextSize.ToString("F4"), Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "DimensionType";
                        item.CategoryName = "標註型式 (Dimension Types)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetByName.Remove(m.Name);
                }
            }

            foreach (var extra in targetByName.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "DimensionType";
                item.CategoryName = "標註型式 (Dimension Types)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareCoordinates(SharedCoordinateSnapshot masterCoord, SharedCoordinateSnapshot targetCoord, StandardsDiffResult result)
        {
            if (masterCoord == null && targetCoord == null) return;

            if (masterCoord != null && targetCoord == null)
            {
                var item = new DiffItem();
                item.AdapterKey = "SharedCoordinate";
                item.CategoryName = "共用座標 (Shared Coordinates)";
                item.MasterIdentity = masterCoord.Identity;
                item.Status = DiffStatus.Missing;
                item.Risk = RiskLevel.Critical;
                item.SuggestedAction = SyncAction.Manual;
                result.Items.Add(item);
                return;
            }

            if (masterCoord == null && targetCoord != null)
            {
                var item = new DiffItem();
                item.AdapterKey = "SharedCoordinate";
                item.CategoryName = "共用座標 (Shared Coordinates)";
                item.TargetIdentity = targetCoord.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Critical;
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
                return;
            }

            var mSig = _signatureService.ComputeItemSignature(masterCoord);
            var tSig = _signatureService.ComputeItemSignature(targetCoord);

            if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
            {
                var item = new DiffItem();
                item.AdapterKey = "SharedCoordinate";
                item.CategoryName = "共用座標 (Shared Coordinates)";
                item.MasterIdentity = masterCoord.Identity;
                item.TargetIdentity = targetCoord.Identity;
                item.Status = DiffStatus.Equal;
                item.Risk = RiskLevel.Low;
                item.MasterSignature = mSig;
                item.TargetSignature = tSig;
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
            else
            {
                var fieldDiffs = new List<FieldDiff>();
                if (Math.Abs(masterCoord.OriginX - targetCoord.OriginX) > 1e-4)
                    fieldDiffs.Add(new FieldDiff { FieldName = "東/西座標原點 (OriginX)", MasterValue = masterCoord.OriginX.ToString("F4"), TargetValue = targetCoord.OriginX.ToString("F4"), Status = DiffStatus.Conflict });

                if (Math.Abs(masterCoord.OriginY - targetCoord.OriginY) > 1e-4)
                    fieldDiffs.Add(new FieldDiff { FieldName = "南/北座標原點 (OriginY)", MasterValue = masterCoord.OriginY.ToString("F4"), TargetValue = targetCoord.OriginY.ToString("F4"), Status = DiffStatus.Conflict });

                if (Math.Abs(masterCoord.TrueNorthAngleRadians - targetCoord.TrueNorthAngleRadians) > 1e-4)
                    fieldDiffs.Add(new FieldDiff { FieldName = "真北旋轉角度 (TrueNorthAngle)", MasterValue = masterCoord.TrueNorthAngleRadians.ToString("F4"), TargetValue = targetCoord.TrueNorthAngleRadians.ToString("F4"), Status = DiffStatus.Conflict });

                var item = new DiffItem();
                item.AdapterKey = "SharedCoordinate";
                item.CategoryName = "共用座標 (Shared Coordinates)";
                item.MasterIdentity = masterCoord.Identity;
                item.TargetIdentity = targetCoord.Identity;
                item.Status = DiffStatus.Conflict;
                item.Risk = RiskLevel.Critical;
                item.MasterSignature = mSig;
                item.TargetSignature = tSig;
                item.ConflictReason = "共用座標與測量基準點不同。修改共用座標將影響地理對齊與模型連結定位，系統已自動阻斷自動同步。";
                item.FieldDiffs = fieldDiffs;
                item.SuggestedAction = SyncAction.Manual;
                result.Items.Add(item);
            }
        }

        private void CompareFamilyTypes(List<FamilyTypeSnapshot> masterList, List<FamilyTypeSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<FamilyTypeSnapshot>();
            targetList = targetList ?? new List<FamilyTypeSnapshot>();

            var targetMap = BuildSafeDictionary(targetList, ft => string.Format("{0}::{1}::{2}", ft.CategoryName ?? "General", ft.FamilyName, ft.TypeName));

            foreach (var m in masterList)
            {
                var key = string.Format("{0}::{1}::{2}", m.CategoryName ?? "General", m.FamilyName, m.TypeName);
                var mSig = _signatureService.ComputeItemSignature(m);
                var categoryDisplay = string.IsNullOrEmpty(m.CategoryName) ? "族群元件 (Families)" : m.CategoryName;
                var adapterKey = string.Format("Family_{0}", (m.CategoryName ?? "General").Replace(" ", "_"));

                FamilyTypeSnapshot t;
                if (!targetMap.TryGetValue(key, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = adapterKey;
                    item.CategoryName = categoryDisplay;
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Low;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = adapterKey;
                        item.CategoryName = categoryDisplay;
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        var mParams = BuildSafeDictionary(m.Parameters ?? new List<FamilyTypeParameterSnapshot>(), p => p.Name);
                        var tParams = BuildSafeDictionary(t.Parameters ?? new List<FamilyTypeParameterSnapshot>(), p => p.Name);

                        foreach (var mp in mParams.Values)
                        {
                            FamilyTypeParameterSnapshot tp;
                            if (!tParams.TryGetValue(mp.Name, out tp))
                            {
                                fieldDiffs.Add(new FieldDiff
                                {
                                    FieldName = mp.Name,
                                    MasterValue = mp.Value,
                                    TargetValue = "(缺漏)",
                                    Status = DiffStatus.Missing
                                });
                            }
                            else if (!string.Equals(mp.Value, tp.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                fieldDiffs.Add(new FieldDiff
                                {
                                    FieldName = mp.Name,
                                    MasterValue = mp.Value,
                                    TargetValue = tp.Value,
                                    Status = DiffStatus.Modified
                                });
                            }
                        }

                        var item = new DiffItem();
                        item.AdapterKey = adapterKey;
                        item.CategoryName = categoryDisplay;
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetMap.Remove(key);
                }
            }

            foreach (var extra in targetMap.Values)
            {
                var categoryDisplay = string.IsNullOrEmpty(extra.CategoryName) ? "族群元件 (Families)" : extra.CategoryName;
                var adapterKey = string.Format("Family_{0}", (extra.CategoryName ?? "General").Replace(" ", "_"));

                var item = new DiffItem();
                item.AdapterKey = adapterKey;
                item.CategoryName = categoryDisplay;
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareLegends(List<LegendSnapshot> masterList, List<LegendSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<LegendSnapshot>();
            targetList = targetList ?? new List<LegendSnapshot>();

            var targetByName = BuildSafeDictionary(targetList, l => l.Name);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                LegendSnapshot t;

                if (!targetByName.TryGetValue(m.Name, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "Legend";
                    item.CategoryName = "圖例 (Legend Views)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Low;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "Legend";
                        item.CategoryName = "圖例 (Legend Views)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (m.Scale != t.Scale)
                            fieldDiffs.Add(new FieldDiff { FieldName = "比例 (Scale)", MasterValue = string.Format("1:{0}", m.Scale), TargetValue = string.Format("1:{0}", t.Scale), Status = DiffStatus.Modified });

                        if (m.ElementCount != t.ElementCount)
                            fieldDiffs.Add(new FieldDiff { FieldName = "元件數量 (ElementCount)", MasterValue = m.ElementCount.ToString(), TargetValue = t.ElementCount.ToString(), Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "Legend";
                        item.CategoryName = "圖例 (Legend Views)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetByName.Remove(m.Name);
                }
            }

            foreach (var extra in targetByName.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "Legend";
                item.CategoryName = "圖例 (Legend Views)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareSheets(List<SheetSnapshot> masterList, List<SheetSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<SheetSnapshot>();
            targetList = targetList ?? new List<SheetSnapshot>();

            var targetByNumber = BuildSafeDictionary(targetList, s => s.SheetNumber);

            foreach (var m in masterList)
            {
                var mSig = _signatureService.ComputeItemSignature(m);
                SheetSnapshot t;

                if (!targetByNumber.TryGetValue(m.SheetNumber, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "Sheet";
                    item.CategoryName = "圖紙 (Sheets & Viewports)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Medium;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "Sheet";
                        item.CategoryName = "圖紙 (Sheets & Viewports)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (!string.Equals(m.SheetName, t.SheetName, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "圖紙名稱 (SheetName)", MasterValue = m.SheetName, TargetValue = t.SheetName, Status = DiffStatus.Modified });

                        var mTb = string.Format("{0} : {1}", m.TitleBlockFamilyName, m.TitleBlockTypeName);
                        var tTb = string.Format("{0} : {1}", t.TitleBlockFamilyName, t.TitleBlockTypeName);
                        if (!string.Equals(mTb, tTb, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "圖框 (TitleBlock)", MasterValue = mTb, TargetValue = tTb, Status = DiffStatus.Modified });

                        var mVps = m.Viewports != null ? string.Join(", ", m.Viewports.Select(v => v.ViewName).ToArray()) : string.Empty;
                        var tVps = t.Viewports != null ? string.Join(", ", t.Viewports.Select(v => v.ViewName).ToArray()) : string.Empty;
                        if (!string.Equals(mVps, tVps, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "放置視圖清單 (Viewports)", MasterValue = mVps, TargetValue = tVps, Status = DiffStatus.Modified });

                        var mScheds = m.PlacedSchedules != null ? string.Join(", ", m.PlacedSchedules.Select(s => s.ScheduleName).ToArray()) : string.Empty;
                        var tScheds = t.PlacedSchedules != null ? string.Join(", ", t.PlacedSchedules.Select(s => s.ScheduleName).ToArray()) : string.Empty;
                        if (!string.Equals(mScheds, tScheds, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "放置明細表 (PlacedSchedules)", MasterValue = mScheds, TargetValue = tScheds, Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "Sheet";
                        item.CategoryName = "圖紙 (Sheets & Viewports)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Medium;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetByNumber.Remove(m.SheetNumber);
                }
            }

            foreach (var extra in targetByNumber.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "Sheet";
                item.CategoryName = "圖紙 (Sheets & Viewports)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }

        private void CompareBrowserOrganizations(List<BrowserOrganizationSnapshot> masterList, List<BrowserOrganizationSnapshot> targetList, StandardsDiffResult result)
        {
            masterList = masterList ?? new List<BrowserOrganizationSnapshot>();
            targetList = targetList ?? new List<BrowserOrganizationSnapshot>();

            var targetByKey = BuildSafeDictionary(targetList, bo => string.Format("{0}::{1}", bo.OrganizationType ?? "Views", bo.Name));

            foreach (var m in masterList)
            {
                var key = string.Format("{0}::{1}", m.OrganizationType ?? "Views", m.Name);
                var mSig = _signatureService.ComputeItemSignature(m);
                BrowserOrganizationSnapshot t;

                if (!targetByKey.TryGetValue(key, out t))
                {
                    var item = new DiffItem();
                    item.AdapterKey = "BrowserOrganization";
                    item.CategoryName = "專案瀏覽器組織 (Browser Organization)";
                    item.MasterIdentity = m.Identity;
                    item.Status = DiffStatus.Missing;
                    item.Risk = RiskLevel.Low;
                    item.MasterSignature = mSig;
                    item.SuggestedAction = SyncAction.AddMissing;
                    result.Items.Add(item);
                }
                else
                {
                    var tSig = _signatureService.ComputeItemSignature(t);
                    if (string.Equals(mSig, tSig, StringComparison.OrdinalIgnoreCase))
                    {
                        var item = new DiffItem();
                        item.AdapterKey = "BrowserOrganization";
                        item.CategoryName = "專案瀏覽器組織 (Browser Organization)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Equal;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.SuggestedAction = SyncAction.Ignore;
                        result.Items.Add(item);
                    }
                    else
                    {
                        var fieldDiffs = new List<FieldDiff>();
                        if (m.IsCurrentActive != t.IsCurrentActive)
                            fieldDiffs.Add(new FieldDiff { FieldName = "作用中組織 (IsCurrentActive)", MasterValue = m.IsCurrentActive ? "是 (Active)" : "否", TargetValue = t.IsCurrentActive ? "是 (Active)" : "否", Status = DiffStatus.Modified });

                        var mGroup = string.Join(" ➔ ", (m.FolderGroupingParameters ?? new List<string>()).ToArray());
                        var tGroup = string.Join(" ➔ ", (t.FolderGroupingParameters ?? new List<string>()).ToArray());
                        if (!string.Equals(mGroup, tGroup, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "資料夾分組規則 (FolderGrouping)", MasterValue = string.IsNullOrEmpty(mGroup) ? "(無分組)" : mGroup, TargetValue = string.IsNullOrEmpty(tGroup) ? "(無分組)" : tGroup, Status = DiffStatus.Modified });

                        var mFilters = string.Join(", ", (m.FilterRules ?? new List<string>()).ToArray());
                        var tFilters = string.Join(", ", (t.FilterRules ?? new List<string>()).ToArray());
                        if (!string.Equals(mFilters, tFilters, StringComparison.OrdinalIgnoreCase))
                            fieldDiffs.Add(new FieldDiff { FieldName = "過濾規則 (FilterRules)", MasterValue = string.IsNullOrEmpty(mFilters) ? "(無過濾)" : mFilters, TargetValue = string.IsNullOrEmpty(tFilters) ? "(無過濾)" : tFilters, Status = DiffStatus.Modified });

                        var item = new DiffItem();
                        item.AdapterKey = "BrowserOrganization";
                        item.CategoryName = "專案瀏覽器組織 (Browser Organization)";
                        item.MasterIdentity = m.Identity;
                        item.TargetIdentity = t.Identity;
                        item.Status = DiffStatus.Modified;
                        item.Risk = RiskLevel.Low;
                        item.MasterSignature = mSig;
                        item.TargetSignature = tSig;
                        item.FieldDiffs = fieldDiffs;
                        item.SuggestedAction = SyncAction.MatchMaster;
                        result.Items.Add(item);
                    }
                    targetByKey.Remove(key);
                }
            }

            foreach (var extra in targetByKey.Values)
            {
                var item = new DiffItem();
                item.AdapterKey = "BrowserOrganization";
                item.CategoryName = "專案瀏覽器組織 (Browser Organization)";
                item.TargetIdentity = extra.Identity;
                item.Status = DiffStatus.Extra;
                item.Risk = RiskLevel.Low;
                item.TargetSignature = _signatureService.ComputeItemSignature(extra);
                item.SuggestedAction = SyncAction.Ignore;
                result.Items.Add(item);
            }
        }
    }
}
