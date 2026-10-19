using System;
using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Models
{
    public class SyncOperation
    {
        public Guid OperationId { get; set; }

        public string AdapterKey { get; set; }

        public SyncAction Action { get; set; }

        public StableIdentity Source { get; set; }

        public StableIdentity Target { get; set; }

        public RiskLevel Risk { get; set; }

        public List<Guid> Dependencies { get; set; }

        public bool Selected { get; set; }

        public string Description { get; set; }

        public bool IsBlocked { get; set; }

        public string BlockReason { get; set; }

        public SyncOperation()
        {
            OperationId = Guid.NewGuid();
            Dependencies = new List<Guid>();
        }

        public string ActionText
        {
            get
            {
                switch (Action)
                {
                    case SyncAction.Ignore: return "跳過 / 相同";
                    case SyncAction.AddMissing: return "建立新增 (+)";
                    case SyncAction.MatchMaster: return "同步修改 (⟳)";
                    case SyncAction.Rename: return "更名對齊";
                    case SyncAction.Manual: return "手動處理 🚫";
                    default: return Action.ToString();
                }
            }
        }

        public string RiskText
        {
            get
            {
                switch (Risk)
                {
                    case RiskLevel.Low: return "低風險";
                    case RiskLevel.Medium: return "中風險";
                    case RiskLevel.High: return "高風險";
                    case RiskLevel.Critical: return "嚴重衝突";
                    default: return Risk.ToString();
                }
            }
        }
    }
}
