using System;
using System.Collections.Generic;
using System.IO;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 匯入/分類/搬檔流程中的檔案項目。
    /// 與 RoutingService 對齊：新增 Category / Timestamp 兩個屬性。
    /// </summary>
    public class Item
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>完整來源路徑（單一路徑）。</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>預測或建議路徑（分類預覽或搬檔結果使用）。</summary>
        public string ProposedPath { get; set; } = string.Empty;

        /// <summary>相容舊代碼：等同於 Path。</summary>
        public string SourcePath => Path;

        /// <summary>檔名（不含路徑）。</summary>
        public string FileName
        {
            get => System.IO.Path.GetFileName(Path) ?? string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(Path)) return;
                var dir = System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
                Path = System.IO.Path.Combine(dir, value);
            }
        }

        /// <summary>副檔名（不含點）。</summary>
        public string Ext
        {
            get => System.IO.Path.GetExtension(Path)?.TrimStart('.') ?? string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(Path)) return;
                var newExt = string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : (value.StartsWith(".") ? value : "." + value);
                Path = System.IO.Path.ChangeExtension(Path, newExt);
            }
        }

        /// <summary>建立時間（預設 UTC Now）。</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>更新時間（預設 UTC Now）。</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>語意標籤。</summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>分類狀態或分類階段。</summary>
        public string? Status { get; set; }

        /// <summary>隸屬專案（用於路徑層級 Project）。</summary>
        public string? Project { get; set; }

        /// <summary>
        /// 類別（用於路徑層級 Category；可由規則/AI/人工指定）。
        /// </summary>
        public string? Category { get; set; }

        private DateTime? _timestamp;

        /// <summary>
        /// 內容/檔案代表性時間（Routing 用於 Year/Month）。
        /// 若未設定，會以 <see cref="CreatedAt"/> 作為 fallback。
        /// </summary>
        public DateTime? Timestamp
        {
            get => _timestamp ?? CreatedAt;
            set => _timestamp = value;
        }

        /// <summary>備註或系統註解。</summary>
        public string? Note { get; set; }
    }
}
