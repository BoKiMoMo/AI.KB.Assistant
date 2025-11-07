namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// V7.5 重構：
    /// 用於 TreeView 資料繫結的節點模型。
    /// (注意：保持在 Views 命名空間以便 XAML 存取，或稍後調整 XAML)
    /// </summary>
    public sealed class FolderNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public override string ToString() => Name;
    }
}
