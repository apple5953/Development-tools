using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevelopmentTools.Modules.SheetTransfer.Models
{
    public enum AssetType
    {
        Sheet,
        DraftingView,
        Legend,
        Schedule,
        ProjectParameter,
        ViewTemplate,
        ProjectInfoAndSymbol
    }

    public enum TransferStatus
    {
        Pending,
        Success,
        Warning,
        Failed,
        Skipped
    }

    public class TransferAsset : INotifyPropertyChanged
    {
        public AssetType Type { get; set; }
        public Autodesk.Revit.DB.ElementId ElementId { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public string Number { get; set; } // For Sheets

        public string Icon
        {
            get
            {
                switch (Type)
                {
                    case AssetType.Sheet: return "📄";
                    case AssetType.DraftingView: return "✏️";
                    case AssetType.Legend: return "🎨";
                    case AssetType.Schedule: return "📊";
                    case AssetType.ProjectParameter: return "⚙️";
                    case AssetType.ViewTemplate: return "📋";
                    case AssetType.ProjectInfoAndSymbol: return "🏢";
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

        public string ConflictStatus { get; set; }
        public TransferStatus Status { get; set; } = TransferStatus.Pending;
        public string StatusMessage { get; set; }

        public List<string> Dependencies { get; set; } = new List<string>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TransferAssetGroup : INotifyPropertyChanged
    {
        public string GroupName { get; set; }
        public ObservableCollection<TransferAsset> Assets { get; set; } = new ObservableCollection<TransferAsset>();

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
