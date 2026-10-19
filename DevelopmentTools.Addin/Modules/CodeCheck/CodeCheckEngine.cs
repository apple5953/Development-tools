using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace DevelopmentTools.CodeCheck
{
    public enum CheckStatus
    {
        Fail = 0,          // 不符合
        Warning = 1,       // 需確認（警示等級規則不符合）
        NoData = 2,        // 資料不足（模型缺資訊或需輸入專案條件）
        Pass = 3,          // 符合
        NotApplicable = 4, // 不適用
        Manual = 5,        // 人工檢討
        Info = 6           // 不適用模型（定義、程序、施工、結構計算）
    }

    public class RuleResult
    {
        public CodeRule Rule;
        public CheckStatus Status;
        public string Target;
        public ElementId ElementId;
        public ModelSource Source;       // 元素所屬模型（合併檢討時可能是連結模型）
        public double? Measured;
        public double? Limit;
        public string Detail;
    }

    public class ArticleResult
    {
        public CatalogItem Item;
        public LawArticle Article;
        public Law Law;
        public CheckStatus Status;
        public List<RuleResult> Results = new List<RuleResult>();
        public int Checked, Failed, Warned, NoData, Passed;
        public string Key => Item.Law + "#" + Item.No;
    }

    /// <summary>把規則套到模型事實上，並依條文彙整結果。</summary>
    public class CodeCheckEngine
    {
        private readonly CodeCheckData _data;
        private readonly ModelFacts _facts;
        private readonly ProjectSettings _settings;

        public List<ArticleResult> Articles { get; } = new List<ArticleResult>();
        public List<RuleResult> AllResults { get; } = new List<RuleResult>();
        public List<string> RuleErrors { get; } = new List<string>();

        public CodeCheckEngine(CodeCheckData data, ModelFacts facts, ProjectSettings settings)
        {
            _data = data; _facts = facts; _settings = settings;
        }

        public static string StatusText(CheckStatus s)
        {
            switch (s)
            {
                case CheckStatus.Fail: return "不符合";
                case CheckStatus.Warning: return "需確認";
                case CheckStatus.NoData: return "資料不足";
                case CheckStatus.Pass: return "符合";
                case CheckStatus.NotApplicable: return "不適用";
                case CheckStatus.Manual: return "人工檢討";
                default: return "非模型項目";
            }
        }

        public static string KindText(string kind)
        {
            switch (kind)
            {
                case "auto": return "自動";
                case "semi": return "半自動";
                case "manual": return "人工";
                default: return "非模型";
            }
        }

        public void Run(Action<string> progress)
        {
            var byId = new Dictionary<string, List<RuleResult>>();
            int n = 0;
            foreach (CodeRule rule in _data.Rules.Rules)
            {
                if (++n % 10 == 0) progress?.Invoke($"套用規則… {n}/{_data.Rules.Rules.Count}");
                var list = new List<RuleResult>();
                try
                {
                    foreach (FactItem item in _facts.ItemsOf(rule.Scope))
                    {
                        RuleResult r = Evaluate(rule, item);
                        if (r != null) list.Add(r);
                    }
                }
                catch (ExprSyntaxException ex)
                {
                    RuleErrors.Add($"{rule.Id}：{ex.Message}");
                }
                byId[rule.Id] = list;
                AllResults.AddRange(list);
            }

            foreach (CatalogItem item in _data.Catalog.Items)
            {
                var law = _data.FindLaw(item.Law);
                var ar = new ArticleResult { Item = item, Law = law, Article = law?.Articles.FirstOrDefault(a => a.No == item.No) };
                if (item.Rules != null)
                    foreach (string id in item.Rules)
                        if (byId.TryGetValue(id, out var rs)) ar.Results.AddRange(rs);
                Summarize(ar);
                Articles.Add(ar);
            }
        }

        private void Summarize(ArticleResult ar)
        {
            var rs = ar.Results.Where(r => r.Status != CheckStatus.NotApplicable).ToList();
            ar.Checked = rs.Count;
            ar.Failed = rs.Count(r => r.Status == CheckStatus.Fail);
            ar.Warned = rs.Count(r => r.Status == CheckStatus.Warning);
            ar.NoData = rs.Count(r => r.Status == CheckStatus.NoData);
            ar.Passed = rs.Count(r => r.Status == CheckStatus.Pass);

            if (ar.Item.Kind == "manual" || ar.Item.Kind == "info")
            {
                ar.Status = ar.Item.Kind == "manual" ? CheckStatus.Manual : CheckStatus.Info;
            }
            else if (rs.Count == 0)
            {
                ar.Status = ar.Results.Count > 0 ? CheckStatus.NotApplicable : CheckStatus.NoData;
            }
            else
            {
                ar.Status = rs.Min(r => r.Status);
            }

            // 人工檢討紀錄（含自動項目的人工覆核）
            if (_settings.Manual.TryGetValue(ar.Key, out ManualCheck m) && !string.IsNullOrEmpty(m.Status) && m.Status != "未檢")
            {
                if (ar.Status == CheckStatus.Manual || ar.Status == CheckStatus.NoData || ar.Status == CheckStatus.Warning || ar.Status == CheckStatus.Info)
                {
                    if (m.Status == "符合") ar.Status = CheckStatus.Pass;
                    else if (m.Status == "不符合") ar.Status = CheckStatus.Fail;
                    else if (m.Status == "不適用") ar.Status = CheckStatus.NotApplicable;
                }
            }
        }

        public void Resummarize(ArticleResult ar) => Summarize(ar);

        private static double Num(object o) => Expr.Num(o);

        private RuleResult Evaluate(CodeRule rule, FactItem item)
        {
            var v = item.Vars;
            if (v == null) return null;
            var res = new RuleResult { Rule = rule, Target = item.Label, ElementId = item.Id, Source = item.Source };

            // 1. 篩選：不成立就不列入
            try
            {
                if (!Expr.Bool(Expr.Eval(rule.Where, v))) return null;
            }
            catch (MissingDataException)
            {
                return null;
            }

            // 2. 適用條件
            try
            {
                if (!Expr.Bool(Expr.Eval(rule.Applies, v)))
                {
                    res.Status = CheckStatus.NotApplicable;
                    res.Detail = "適用條件不成立";
                    return res;
                }
            }
            catch (MissingDataException ex)
            {
                res.Status = CheckStatus.NoData;
                res.Detail = "無法判定是否適用：" + ex.Message;
                return res;
            }

            // 3. 門檻與量測
            try
            {
                string limitExpr = rule.Limit;
                if (rule.Cases != null && rule.Cases.Count > 0)
                {
                    limitExpr = null;
                    foreach (var c in rule.Cases)
                    {
                        if (c.Count >= 2 && Expr.Bool(Expr.Eval(c[0], v))) { limitExpr = c[1]; break; }
                    }
                    if (limitExpr == null) { res.Status = CheckStatus.NotApplicable; res.Detail = "沒有符合的門檻情形"; return res; }
                }
                double limit = Num(Expr.Eval(limitExpr, v));
                double measured = Num(Expr.Eval(rule.Measure, v));
                res.Limit = limit;
                res.Measured = measured;
                bool ok;
                switch (rule.Op)
                {
                    case ">=": ok = measured >= limit - 1e-6; break;
                    case "<=": ok = measured <= limit + 1e-6; break;
                    case ">": ok = measured > limit; break;
                    case "<": ok = measured < limit; break;
                    case "==": ok = Math.Abs(measured - limit) < 1e-6; break;
                    case "!=": ok = Math.Abs(measured - limit) >= 1e-6; break;
                    default: throw new ExprSyntaxException("未知比較 " + rule.Op);
                }
                if (ok) res.Status = CheckStatus.Pass;
                else res.Status = rule.Severity == "warning" ? CheckStatus.Warning : CheckStatus.Fail;
                res.Detail = Describe(rule, measured, limit);
            }
            catch (MissingDataException ex)
            {
                res.Status = CheckStatus.NoData;
                res.Detail = ex.Message;
            }
            return res;
        }

        public static string Fmt(double v, string unit)
        {
            if (unit == "mm") return v.ToString("0", CultureInfo.InvariantCulture) + " mm";
            if (unit == "m" || unit == "m²") return v.ToString("0.##", CultureInfo.InvariantCulture) + " " + unit;
            if (string.IsNullOrEmpty(unit))
            {
                if (v > 0 && v < 1 && Math.Abs(1 / v - Math.Round(1 / v)) < 0.02) return "1/" + Math.Round(1 / v).ToString(CultureInfo.InvariantCulture);
                return v.ToString("0.###", CultureInfo.InvariantCulture);
            }
            return v.ToString("0.##", CultureInfo.InvariantCulture) + " " + unit;
        }

        private static string Describe(CodeRule rule, double measured, double limit)
        {
            // 比值或 0/1 判斷的規則只顯示結論，避免「1 ≥ 1」這種看不懂的數字
            if (string.IsNullOrEmpty(rule.Unit) && rule.Op == "==")
                return Math.Abs(measured - limit) < 1e-6 ? "符合規定" : "未符合規定（見說明）";
            if (string.IsNullOrEmpty(rule.Unit) && Math.Abs(limit - 1) < 1e-9 && rule.Measure.StartsWith("min("))
                return measured >= limit - 1e-6 ? "各項尺寸均達標準" : $"有尺寸未達標準（最不利項為規定值的 {measured * 100:0}%）";
            string op = rule.Op == ">=" ? "≥" : rule.Op == "<=" ? "≤" : rule.Op;
            return $"量測 {Fmt(measured, rule.Unit)}，規定 {op} {Fmt(limit, rule.Unit)}";
        }
    }
}
