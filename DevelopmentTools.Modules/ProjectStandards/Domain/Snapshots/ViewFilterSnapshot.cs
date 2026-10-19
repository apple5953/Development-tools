using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class FilterRuleSnapshot
    {
        public string ParameterName { get; set; }

        public string ParameterStableKey { get; set; }

        public string RuleEvaluator { get; set; }

        public string RuleValue { get; set; }

        public double? Tolerance { get; set; }
    }

    public class ViewFilterSnapshot
    {
        public string Name { get; set; }

        public List<string> TargetCategories { get; set; }

        public List<FilterRuleSnapshot> Rules { get; set; }

        public ViewFilterSnapshot()
        {
            TargetCategories = new List<string>();
            Rules = new List<FilterRuleSnapshot>();
        }

        public string CategorySignature
        {
            get
            {
                if (TargetCategories == null || TargetCategories.Count == 0) return string.Empty;
                var sorted = new List<string>(TargetCategories);
                sorted.Sort();
                return string.Join(",", sorted.ToArray());
            }
        }

        public string RuleSignature
        {
            get
            {
                if (Rules == null || Rules.Count == 0) return string.Empty;
                var list = new List<string>();
                foreach (var r in Rules)
                {
                    list.Add(string.Format("{0}:{1}:{2}", r.ParameterStableKey, r.RuleEvaluator, r.RuleValue));
                }
                list.Sort();
                return string.Join(";", list.ToArray());
            }
        }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForViewFilter(Name, CategorySignature, RuleSignature); }
        }
    }
}
