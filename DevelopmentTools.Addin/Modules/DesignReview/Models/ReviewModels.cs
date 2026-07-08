using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.DesignReview.Models
{
    public enum RuleCategory
    {
        CodeCompliance, // 法規檢討
        Constructability // 合理性檢討
    }

    public enum Severity
    {
        Error,
        Warning
    }

    public class ReviewRule : INotifyPropertyChanged
    {
        public string RuleId { get; set; }
        public string Name { get; set; }
        public RuleCategory Category { get; set; }
        public string Description { get; set; }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ReviewIssue : INotifyPropertyChanged
    {
        public string IssueId { get; set; }
        public string RuleName { get; set; }
        public Severity Severity { get; set; }
        public string Description { get; set; }
        public ElementId ElementId { get; set; }
        public string ElementName { get; set; }
        public string LevelName { get; set; }
        public XYZ Location { get; set; }
        public List<ElementId> RelatedElementIds { get; set; } = new List<ElementId>();

        public string SeverityIcon => Severity == Severity.Error ? "🔴" : "🟡";
        
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class EscapeDistanceIssue : ReviewIssue
    {
        public double ActualDistanceMeter { get; set; }
        public double LimitDistanceMeter { get; set; }
        public double OverDistanceMeter => Math.Max(0, ActualDistanceMeter - LimitDistanceMeter);
    }

    public class NetHeightIssue : ReviewIssue
    {
        public double ActualHeightMeter { get; set; }
        public double LimitHeightMeter { get; set; }
        public string HitElementName { get; set; }
    }

    public class SleevePenetrationIssue : ReviewIssue
    {
        public string MepCategoryName { get; set; }
        public string MepElementName { get; set; }
        public string MepSize { get; set; }
        public string StructureElementName { get; set; }
    }

    public class PreCheckItem
    {
        public string TargetName { get; set; }     // 檢查項目名稱
        public string Status { get; set; }         // 狀態字串 (如 ✅ 正常 / ⚠️ 缺失 / ❌ 嚴重缺失)
        public string Description { get; set; }    // 影響說明
        public bool IsCritical { get; set; }       // 是否為嚴重缺失 (阻礙執行)
        public List<ElementId> MissingElementIds { get; set; } = new List<ElementId>(); // 缺失的元件 IDs 方便高亮定位
    }

    public class RegulatoryCodeRule : INotifyPropertyChanged
    {
        public string CodeId { get; set; }
        public string Chapter { get; set; }
        public string Section { get; set; }
        public string RuleName { get; set; }
        public string Country { get; set; }
        public string Description { get; set; }
        public string CheckMethod { get; set; }     // 自動幾何運算 / 參數合規檢查 / 人工目視審查
        
        private string _healthStatus = "⌛ 診斷中";
        public string HealthStatus
        {
            get => _healthStatus;
            set { _healthStatus = value; OnPropertyChanged(); }
        }

        private string _runStatus = "⚪ 未執行";
        public string RunStatus
        {
            get => _runStatus;
            set { _runStatus = value; OnPropertyChanged(); }
        }

        public string CountryIcon => Country == "Taiwan" ? "🇹🇼" : (Country == "USA" ? "🇺🇸" : "🇯🇵");

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

