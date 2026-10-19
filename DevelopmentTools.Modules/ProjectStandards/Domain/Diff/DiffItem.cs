using System.Collections.Generic;
using System.ComponentModel;
using DevelopmentTools.ProjectStandards.Domain.Identity;
using DevelopmentTools.ProjectStandards.Domain.Models;

namespace DevelopmentTools.ProjectStandards.Domain.Diff
{
    public class DiffItem : INotifyPropertyChanged
    {
        private bool _isSelectedForSync;
        private string _confirmationState = "待處理";
        private string _rowBackground = "#FFFFFF";
        private string _syncMessage = "";

        public string AdapterKey { get; set; }

        public string CategoryName { get; set; }

        public StableIdentity MasterIdentity { get; set; }

        public StableIdentity TargetIdentity { get; set; }

        public DiffStatus Status { get; set; }

        public RiskLevel Risk { get; set; }

        public string MasterSignature { get; set; }

        public string TargetSignature { get; set; }

        public List<FieldDiff> FieldDiffs { get; set; }

        public string ConflictReason { get; set; }

        public SyncAction SuggestedAction { get; set; }

        public bool IsSelectedForSync
        {
            get { return _isSelectedForSync; }
            set
            {
                if (_isSelectedForSync != value)
                {
                    _isSelectedForSync = value;
                    OnPropertyChanged("IsSelectedForSync");
                }
            }
        }

        public string ConfirmationState
        {
            get { return _confirmationState; }
            set
            {
                if (_confirmationState != value)
                {
                    _confirmationState = value;
                    OnPropertyChanged("ConfirmationState");
                }
            }
        }

        public string RowBackground
        {
            get { return _rowBackground; }
            set
            {
                if (_rowBackground != value)
                {
                    _rowBackground = value;
                    OnPropertyChanged("RowBackground");
                }
            }
        }

        public string SyncMessage
        {
            get { return _syncMessage; }
            set
            {
                if (_syncMessage != value)
                {
                    _syncMessage = value;
                    OnPropertyChanged("SyncMessage");
                }
            }
        }

        public DiffItem()
        {
            FieldDiffs = new List<FieldDiff>();
            _isSelectedForSync = true;
        }

        public string ItemDisplayName
        {
            get
            {
                if (MasterIdentity != null) return MasterIdentity.DisplayName;
                if (TargetIdentity != null) return TargetIdentity.DisplayName;
                return "未知項目";
            }
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case DiffStatus.Equal: return "完全相同 ✓";
                    case DiffStatus.Modified: return "內容差異 ⚠";
                    case DiffStatus.Missing: return "目標缺漏 ✕";
                    case DiffStatus.Extra: return "目標額外存在";
                    case DiffStatus.Conflict: return "嚴重衝突 🚫";
                    case DiffStatus.RenameCandidate: return "名稱變更候選 🔄";
                    case DiffStatus.DependencyMissing: return "依賴缺漏 ⚠";
                    case DiffStatus.Stale: return "版本過期 ⏳";
                    case DiffStatus.ReadOnlySource: return "唯讀來源 🔒";
                    default: return Status.ToString();
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

        public string ActionText
        {
            get
            {
                switch (SuggestedAction)
                {
                    case SyncAction.Ignore: return "忽略 (相同)";
                    case SyncAction.AddMissing: return "新增缺漏 (+)";
                    case SyncAction.MatchMaster: return "更新內容 (⟳)";
                    case SyncAction.Rename: return "更名對齊";
                    case SyncAction.Manual: return "手動處理 🚫";
                    default: return SuggestedAction.ToString();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
