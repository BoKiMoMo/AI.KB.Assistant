using System;
using System.Collections.Generic;

namespace AI.KB.Assistant.Models
{
    public class ActionBatch
    {
        public string Name { get; set; } = "";
        public Action Do { get; set; } = () => { };
        public Action Undo { get; set; } = () => { };
    }
}
