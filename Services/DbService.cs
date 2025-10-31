using System.Collections.Generic;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 資料庫服務骨架，提供查詢、寫入與更新接口。
    /// 現階段為最小實作版本，確保可編譯通過。
    /// </summary>
    public class DbService
    {
        // TODO: 若有 SQLite 連線邏輯可自行加入
        // 目前只保留骨架以讓所有呼叫端正常編譯。

        /// <summary>查詢所有項目。</summary>
        public Task<List<Item>> QueryAllAsync()
        {
            return Task.FromResult(new List<Item>());
        }

        /// <summary>插入多筆資料。</summary>
        public Task<int> InsertItemsAsync(IEnumerable<Item> items)
        {
            return Task.FromResult(0);
        }

        /// <summary>更新多筆資料。</summary>
        public Task<int> UpdateItemsAsync(IEnumerable<Item> items)
        {
            return Task.FromResult(0);
        }

        /// <summary>僅標記階段，不進行實際搬移。</summary>
        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage)
        {
            return Task.FromResult(0);
        }

        // 若呼叫端有使用舊多載（不帶 stage），也可選擇補一個簡單入口：
        public Task<int> StageOnlyAsync(IEnumerable<Item> items)
        {
            return StageOnlyAsync(items, 0);
        }
    }
}
