using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using DevelopmentTools.ProjectStandards.Domain.Snapshots;

namespace DevelopmentTools.ProjectStandards.Domain.Diff
{
    public class CategoryDiffSummary : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

        public string CategoryKey { get; set; }

        public string CategoryDisplayName { get; set; }

        public int TotalMasterItems { get; set; }

        public int TotalTargetItems { get; set; }

        public int EqualCount { get; set; }

        public int ModifiedCount { get; set; }

        public int MissingCount { get; set; }

        public int ExtraCount { get; set; }

        public int ConflictCount { get; set; }

        public int RenameCount { get; set; }

        public bool HasDifferences
        {
            get { return (ModifiedCount + MissingCount + ExtraCount + ConflictCount + RenameCount) > 0; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class StandardsDiffResult
    {
        public SnapshotMetadata MasterMetadata { get; set; }

        public SnapshotMetadata TargetMetadata { get; set; }

        public DateTime DiffCalculatedTime { get; set; }

        public bool IsSignatureIdentical { get; set; }

        public List<DiffItem> Items { get; set; }

        public Dictionary<string, CategoryDiffSummary> CategorySummaries { get; set; }

        public StandardsDiffResult()
        {
            DiffCalculatedTime = DateTime.UtcNow;
            Items = new List<DiffItem>();
            CategorySummaries = new Dictionary<string, CategoryDiffSummary>();
        }

        public int TotalDifferences
        {
            get { return Items.Count(i => i.Status != DiffStatus.Equal); }
        }

        public int CriticalConflictCount
        {
            get { return Items.Count(i => i.Status == DiffStatus.Conflict || i.Risk == Models.RiskLevel.Critical); }
        }

        public void RecomputeSummaries()
        {
            CategorySummaries.Clear();

            var groups = Items.GroupBy(i => i.AdapterKey ?? i.CategoryName ?? "General");
            foreach (var group in groups)
            {
                var first = group.FirstOrDefault();
                var summary = new CategoryDiffSummary
                {
                    CategoryKey = group.Key,
                    CategoryDisplayName = first != null ? first.CategoryName : group.Key,
                    EqualCount = group.Count(i => i.Status == DiffStatus.Equal),
                    ModifiedCount = group.Count(i => i.Status == DiffStatus.Modified),
                    MissingCount = group.Count(i => i.Status == DiffStatus.Missing),
                    ExtraCount = group.Count(i => i.Status == DiffStatus.Extra),
                    ConflictCount = group.Count(i => i.Status == DiffStatus.Conflict),
                    RenameCount = group.Count(i => i.Status == DiffStatus.RenameCandidate)
                };
                CategorySummaries[group.Key] = summary;
            }
        }
    }
}
