using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 檔案項目資料模型（對應清單顯示、資料庫記錄與搬檔流程）
    /// </summary>
    public class Item
    {
        // ========= 基本資訊 =========
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>來源完整路徑（原始位置）</summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>最終儲存路徑（搬檔後）</summary>
        public string? Path { get; set; }

        /// <summary>檔名（不含副檔名）</summary>
        public string Filename { get; set; } = string.Empty;

        /// <summary>副檔名（含 .）</summary>
        public string Ext { get; set; } = string.Empty;

        /// <summary>檔案建立時間</summary>
        public DateTime CreatedTs { get; set; } = DateTime.Now;

        [JsonIgnore]
        public DateTime CreatedAt => CreatedTs; // 舊屬性相容

        // ========= 分類欄位 =========

        /// <summary>目前狀態：inbox / processing / done / blacklist / autosort 等</summary>
        public string Status { get; set; } = "inbox";

        /// <summary>所屬專案</summary>
        public string? Project { get; set; }

        /// <summary>標籤（以逗號分隔）</summary>
        public string? Tags { get; set; }

        /// <summary>AI 分類信心值（0~1）</summary>
        public double Confidence { get; set; } = 0;

        /// <summary>AI 預測的類別或目標資料夾</summary>
        public string? PredictedCategory { get; set; }

        /// <summary>AI 建議的目標路徑（未實際搬檔前）</summary>
        public string? ProposedPath { get; set; }

        /// <summary>人工確認是否採用 AI 建議</summary>
        public bool IsConfirmed { get; set; } = false;

        /// <summary>搬檔錯誤訊息（若有）</summary>
        public string? Error { get; set; }

        // ========= 內部控制 =========

        /// <summary>資料建立時間（資料庫內用）</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>最近更新時間</summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>是否被刪除（軟刪除）</summary>
        public bool IsDeleted { get; set; } = false;

        // ========= 建構子 =========

        public Item() { }

        /// <summary>
        /// 由檔案路徑初始化基本資訊
        /// </summary>
        public Item(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            SourcePath = Path.GetFullPath(path);
            Filename = System.IO.Path.GetFileNameWithoutExtension(path);
            Ext = System.IO.Path.GetExtension(path);

            try
            {
                if (File.Exists(path))
                    CreatedTs = File.GetCreationTime(path);
                else
                    CreatedTs = DateTime.Now;
            }
            catch
            {
                CreatedTs = DateTime.Now;
            }
        }

        // ========= 靜態方法 =========

        /// <summary>
        /// 由檔案清單批次建立 Item 集合
        /// </summary>
        public static List<Item> FromFiles(IEnumerable<string> paths)
        {
            var list = new List<Item>();
            foreach (var path in paths)
            {
                try { list.Add(new Item(path)); }
                catch { /* 忽略壞檔名 */ }
            }
            return list;
        }

        // ========= 複製 / 比對 =========

        public Item Clone()
        {
            return new Item
            {
                Id = this.Id,
                SourcePath = this.SourcePath,
                Path = this.Path,
                Filename = this.Filename,
                Ext = this.Ext,
                CreatedTs = this.CreatedTs,
                Status = this.Status,
                Project = this.Project,
                Tags = this.Tags,
                Confidence = this.Confidence,
                PredictedCategory = this.PredictedCategory,
                ProposedPath = this.ProposedPath,
                IsConfirmed = this.IsConfirmed,
                Error = this.Error,
                CreatedUtc = this.CreatedUtc,
                UpdatedUtc = this.UpdatedUtc,
                IsDeleted = this.IsDeleted
            };
        }

        public override string ToString() => $"{Filename}{Ext} ({Status})";
    }
}
