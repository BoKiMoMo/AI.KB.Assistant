using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 收件匣/前處理服務。此版本保持最小可編譯骨架，
    /// 主要確保所有路徑處理都使用 string，並對齊 DbService 簽名。
    /// </summary>
    public class IntakeService
    {
        private readonly DbService _db;

        public IntakeService(DbService db)
        {
            _db = db;
        }

        // === 對齊 DbService 的非同步 API（舊碼相容） ===

        public Task<List<Item>> QueryAllAsync()
        {
            return _db.QueryAllAsync();
        }

        public Task<int> InsertItemsAsync(IEnumerable<Item> items)
        {
            return _db.InsertItemsAsync(items);
        }

        public Task<int> UpdateItemsAsync(IEnumerable<Item> items)
        {
            return _db.UpdateItemsAsync(items);
        }

        /// <summary>
        /// 將一批項目標記為指定階段（僅資料層標記，不搬檔）。
        /// </summary>
        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage)
        {
            return _db.StageOnlyAsync(items, stage);
        }

        // === 常用路徑輔助（僅接受 string，避免 string[] vs string 問題） ===

        public static string GetDirName(Item item)
            => Path.GetDirectoryName(item?.Path ?? string.Empty) ?? string.Empty;

        public static string GetFileName(Item item)
            => Path.GetFileName(item?.Path ?? string.Empty) ?? string.Empty;

        public static string GetExtension(Item item)
            => Path.GetExtension(item?.Path ?? string.Empty)?.TrimStart('.') ?? string.Empty;

        /// <summary>
        /// 以新副檔名重組路徑（不含點的副檔名可直接傳入，如 "pdf"）。
        /// </summary>
        public static string WithExtension(Item item, string newExtWithoutDot)
        {
            var ext = string.IsNullOrWhiteSpace(newExtWithoutDot)
                ? string.Empty
                : (newExtWithoutDot.StartsWith(".") ? newExtWithoutDot : "." + newExtWithoutDot);
            return Path.ChangeExtension(item?.Path ?? string.Empty, ext);
        }
    }
}
