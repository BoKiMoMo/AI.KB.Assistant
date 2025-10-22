using System;
using System.Collections.Generic;
using System.Linq;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    internal static class DbServiceExtensions
    {
        public static IEnumerable<Item> QueryByTag(this DbService db, string tag)
        {
            return db.QuerySince(0).Where(i => (i.Tags ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Contains(tag, StringComparer.OrdinalIgnoreCase));
        }
    }
}
