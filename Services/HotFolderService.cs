using System.Collections.Generic;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// HotFolder 相關處理。此版本只提供最小可編譯骨架，
    /// 主要目的為對齊 DbService 的簽名，避免參數不符錯誤。
    /// </summary>
    public class HotFolderService
    {
        private readonly DbService _db;

        public HotFolderService(DbService db)
        {
            _db = db;
        }

        /// <summary>
        /// 將項目標記到指定階段（僅寫入資料層，不做實體檔案搬移）。
        /// 對齊 DbService.StageOnlyAsync(items, stage) 簽名。
        /// </summary>
        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage)
        {
            return _db.StageOnlyAsync(items, stage);
        }

        /// <summary>
        /// 便捷單筆入口（舊碼相容）。
        /// </summary>
        public Task<int> StageOnlyAsync(Item item, int stage)
        {
            var list = new List<Item> { item };
            return _db.StageOnlyAsync(list, stage);
        }
    }
}
