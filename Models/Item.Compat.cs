using System.IO;

namespace AI.KB.Assistant.Models
{
    // 兼容層：只保留「主檔 Item.cs 沒有」的成員，避免重複定義
    public partial class Item
    {
        // 若你的 Item.cs 已有 ProposedPath，請把這行刪掉
        public string ProposedPath { get; set; } = "";

        // 統一給舊程式取檔名；依 SourcePath 取 FileName，不影響你的主檔欄位
        public string FileName
        {
            get => string.IsNullOrEmpty(SourcePath) ? "" : Path.GetFileName(SourcePath);
            set { /* no-op for backward-compat */ }
        }
    }
}
