using System.Collections.Generic;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 用於 Undo/Redo 的批次動作
    /// </summary>
    public class ActionBatch
    {
        public string Action { get; set; } = ""; // e.g. "MoveFiles", "UpdateTags"
        public List<Item> Items { get; set; } = new();
    }
}
