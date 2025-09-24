using System.Collections.ObjectModel;
using System.ComponentModel;

namespace AI.KB.Assistant.Models
{
    public class PathNode : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public ObservableCollection<PathNode> Children { get; set; } = new();

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new(nameof(IsChecked))); }
        }

        private bool _isExcluded;
        public bool IsExcluded
        {
            get => _isExcluded;
            set { _isExcluded = value; PropertyChanged?.Invoke(this, new(nameof(IsExcluded))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => IsExcluded ? $"{Name} (排除)" : Name;
    }
}
