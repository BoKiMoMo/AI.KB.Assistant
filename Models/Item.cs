// Item.cs
// --------------------------------------
// 定義資料項目結構（不再使用 partial）。
// 與 DbService / IntakeService / RoutingService
// 皆為同一結構基礎物件。
// --------------------------------------

using System;
using System.IO;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 基礎檔案項目模型，用於檔案分類、搬移與紀錄。
    /// </summary>
    public partial class Item
    {
        /// <summary>唯一識別碼。</summary>
        public long Id { get; set; }

        /// <summary>檔名（含副檔名）。</summary>
        public string Filename { get; set; } = string.Empty;

        /// <summary>副檔名（不含.）。</summary>
        public string Ext { get; set; } = string.Empty;

        /// <summary>來源所在資料夾路徑。</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>目的地資料夾路徑。</summary>
        public string DestPath { get; set; } = string.Empty;

        /// <summary>所屬專案名稱。</summary>
        public string Project { get; set; } = string.Empty;

        /// <summary>標籤（以逗號分隔）。</summary>
        public string Tags { get; set; } = string.Empty;

        /// <summary>建立時間戳。</summary>
        public DateTime CreatedTs { get; set; }

        /// <summary>分類或處理狀態。</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>完整來源路徑。</summary>
        public string SourcePath => System.IO.Path.Combine(Path ?? string.Empty, Filename ?? string.Empty);

        /// <summary>
        /// 複製基礎資訊（淺複製）。
        /// </summary>
        public Item Clone() => (Item)this.MemberwiseClone();

        /// <summary>
        /// 取得項目的顯示名稱。
        /// </summary>
        public override string ToString() => $"{Filename} ({Status})";
    }
}
