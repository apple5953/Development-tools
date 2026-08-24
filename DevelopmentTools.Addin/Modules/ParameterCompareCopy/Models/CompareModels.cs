using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevelopmentTools.Modules.ParameterCompareCopy.Models
{
    public enum CompareAssetType
    {
        ViewTemplate,
        Schedule,
        ProjectParameter,
        LineStyle,
        Material,
        SymbolSetting,
        ElementParameter
    }

    public enum TransferStatus
    {
        Pending,
        Success,
        Warning,
        Failed,
        Skipped
    }

    public enum AssetComparison
    {
        New,
        Mismatch,
        Identical
    }

    public class ParameterDiffItem : INotifyPropertyChanged
    {
        private string _paramName;
        public string ParamName
        {
            get => _paramName;
            set { _paramName = value; OnPropertyChanged(); }
        }

        private string _sourceValue;
        public string SourceValue
        {
            get => _sourceValue;
            set { _sourceValue = value; OnPropertyChanged(); }
        }

        private string _targetValue;
        public string TargetValue
        {
            get => _targetValue;
            set { _targetValue = value; OnPropertyChanged(); }
        }

        private bool _isDifferent;
        public bool IsDifferent
        {
            get => _isDifferent;
            set { _isDifferent = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CompareAsset : INotifyPropertyChanged
    {
        public CompareAssetType Type { get; set; }
        public Autodesk.Revit.DB.ElementId ElementId { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }

        public string Icon
        {
            get
            {
                switch (Type)
                {
                    case CompareAssetType.ViewTemplate: return "📋";
                    case CompareAssetType.Schedule: return "📊";
                    case CompareAssetType.ProjectParameter: return "⚙️";
                    case CompareAssetType.LineStyle: return "✏️";
                    case CompareAssetType.Material: return "🧱";
                    case CompareAssetType.SymbolSetting: return "🔣";
                    case CompareAssetType.ElementParameter: return "📝";
                    default: return "📦";
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public TransferStatus Status { get; set; } = TransferStatus.Pending;
        public string StatusMessage { get; set; }

        private AssetComparison _comparison = AssetComparison.New;
        public AssetComparison Comparison
        {
            get => _comparison;
            set { _comparison = value; OnPropertyChanged(); OnPropertyChanged(nameof(ComparisonText)); }
        }

        public string ComparisonText
        {
            get
            {
                switch (Comparison)
                {
                    case AssetComparison.New: return "[➕ 僅來源]";
                    case AssetComparison.Mismatch: return "[⚠️ 差異]";
                    case AssetComparison.Identical: return "[✅ 一致]";
                    default: return "";
                }
            }
        }

        public ObservableCollection<ParameterDiffItem> DiffDetails { get; } = new ObservableCollection<ParameterDiffItem>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CompareAssetGroup : INotifyPropertyChanged
    {
        public string GroupName { get; set; }
        public ObservableCollection<CompareAsset> Assets { get; set; } = new ObservableCollection<CompareAsset>();

        private bool? _isChecked = false;
        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                    if (_isChecked.HasValue)
                    {
                        foreach (var asset in Assets)
                            asset.IsSelected = _isChecked.Value;
                    }
                }
            }
        }

        public void UpdateCheckState()
        {
            int checkedCount = 0;
            foreach (var asset in Assets)
            {
                if (asset.IsSelected) checkedCount++;
            }
            if (checkedCount == 0) _isChecked = false;
            else if (checkedCount == Assets.Count) _isChecked = true;
            else _isChecked = null;
            OnPropertyChanged(nameof(IsChecked));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
