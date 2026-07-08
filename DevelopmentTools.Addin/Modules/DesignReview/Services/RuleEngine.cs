using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DevelopmentTools.Modules.DesignReview.Models;

namespace DevelopmentTools.Modules.DesignReview.Services
{
    public class RuleCodeConfig
    {
        public string RuleCode { get; set; }
        public string RuleName { get; set; }
        public string Category { get; set; }
        public string CheckType { get; set; } // Auto / SemiAuto / Manual
        public string Parameter { get; set; }
        public string Operator { get; set; }  // >=, <=, ==, !=, >, <, Contains
        public string Value { get; set; }
        public string Description { get; set; }
        public string Perspective { get; set; }
        public string LawArticle { get; set; }
        public string LawChapter { get; set; }
        public List<string> ApplicableCategories { get; set; } = new List<string>();
    }

    public class RuleEngine
    {
        public List<RuleCodeConfig> RuleConfigs { get; private set; } = new List<RuleCodeConfig>();

        public void LoadRules(string rulesDirectory)
        {
            RuleConfigs.Clear();
            if (!Directory.Exists(rulesDirectory)) return;

            var jsonFiles = Directory.GetFiles(rulesDirectory, "*.json");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            foreach (var file in jsonFiles)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var rules = JsonSerializer.Deserialize<List<RuleCodeConfig>>(json, options);
                    if (rules != null)
                    {
                        RuleConfigs.AddRange(rules);
                    }
                }
                catch (Exception)
                {
                    // 忽略單一檔案載入錯誤
                }
            }
        }

        public bool Evaluate(ExtractedElement elem, RuleCodeConfig config, out string message)
        {
            message = string.Empty;
            if (config == null || elem == null) return true;

            // 如果是 Manual 或者是 SemiAuto 且無指定參數，則預設通過，由人工檢討
            if (config.CheckType == "Manual" || string.IsNullOrEmpty(config.Parameter))
            {
                return true;
            }

            // 取得對應參數值
            string valStr = elem.GetStringParameter(config.Parameter);
            if (valStr == null)
            {
                message = $"缺少必要參數「{config.Parameter}」";
                return false;
            }

            bool isNum1 = double.TryParse(valStr, out double num1);
            bool isNum2 = double.TryParse(config.Value, out double num2);

            if (isNum1 && isNum2)
            {
                // 數值比對
                bool passed = false;
                switch (config.Operator)
                {
                    case ">=": passed = num1 >= num2; break;
                    case "<=": passed = num1 <= num2; break;
                    case "==": passed = Math.Abs(num1 - num2) < 0.001; break;
                    case "!=": passed = Math.Abs(num1 - num2) >= 0.001; break;
                    case ">": passed = num1 > num2; break;
                    case "<": passed = num1 < num2; break;
                    default:
                        passed = false;
                        message = $"未知的運算符號「{config.Operator}」";
                        return false;
                }

                if (!passed)
                {
                    message = $"檢測值 {num1:F1} 不符合法規要求「{config.Operator} {num2:F1}」";
                }
                return passed;
            }
            else
            {
                // 字串比對
                bool passed = false;
                switch (config.Operator)
                {
                    case "==":
                        passed = valStr.Equals(config.Value, StringComparison.OrdinalIgnoreCase);
                        break;
                    case "!=":
                        passed = !valStr.Equals(config.Value, StringComparison.OrdinalIgnoreCase);
                        break;
                    case "Contains":
                        passed = valStr.IndexOf(config.Value, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    default:
                        passed = false;
                        message = $"非數值類型不支援運算符號「{config.Operator}」";
                        return false;
                }

                if (!passed)
                {
                    message = $"數值「{valStr}」不符合法規要求「{config.Operator} {config.Value}」";
                }
                return passed;
            }
        }
    }
}
