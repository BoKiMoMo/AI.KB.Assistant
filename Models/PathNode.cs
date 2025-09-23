using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 路徑樹節點（供 MainWindow 的 TreeView 使用）
    /// </summary>
    public sealed class PathNode : INotifyPropertyChanged
    {
        private string _name = "";
        private string _fullPath = "";
        private bool _isChecked;

        public string Name
        {
            get => _name; set { _name = value; OnPropertyChanged(); }
        }

        public string FullPath
        {
            get => _fullPath; set { _fullPath = value; OnPropertyChanged(); }
        }

        public bool IsChecked
        {
            get => _isChecked; set { _isChecked = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PathNode> Children { get; } = new();

        public void SetChecked(bool val)
        {
            IsChecked = val;
            foreach (var c in Children) c.SetChecked(val);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
