using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 檔案匯入與搬移服務。
    /// 負責「收件夾 → 資料庫暫存 → 實際分類搬移」的主要流程。
    /// </summary>
    public partial class IntakeService
    {
        private readonly DbService _db;
        private readonly RoutingService _routing;

        public IntakeService(DbService db, RoutingService routing)
        {
            _db = db;
            _routing = routing;
        }

        /// <summary>
        /// 僅將檔案掃描加入暫存（不實際搬移）。
        /// </summary>
        public async Task StageOnlyAsync(IEnumerable<string> files)
        {
            if (files == null) return;

            var items = new List<Item>();

            foreach (var path in files.Where(File.Exists))
            {
                var fi = new FileInfo(path);
                var item = new Item
                {
                    FileName = Path.GetFileNameWithoutExtension(path),
                    Ext = Path.GetExtension(path),
                    Path = path,
                    SourcePath = path,
                    ProposedPath = _routing.PreviewDestPath(path, null),
                    CreatedAt = fi.CreationTime,
                    Status = ItemStatus.New
                };
                items.Add(item);
            }

            await _db.InsertItemsAsync(items);
        }

        /// <summary>
        /// 執行「正式搬檔」流程。
        /// overwritePolicy 可傳入： "overwrite" / "skip" / "rename"（大小寫不拘）。
        /// 若為 null 則使用 ConfigService.Cfg.Import.OverwritePolicy 的值。
        /// </summary>
        public async Task<int> CommitPendingAsync(IEnumerable<Item> items, string? overwritePolicy = null)
        {
            if (items == null) return 0;

            var cfg = ConfigService.Cfg;
            int success = 0;

            // 讀取策略（以字串方式，避免相依列舉型別）
            var policy = Normalize(cfg?.Import?.OverwritePolicy);
            var runtimePolicy = Normalize(overwritePolicy) ?? policy ?? "rename";

            // 移動或複製策略
            var moveMode = Normalize(cfg?.Import?.MoveMode) ?? "move";

            foreach (var item in items)
            {
                try
                {
                    string src = item.SourcePath;
                    if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
                    {
                        item.Status = ItemStatus.Failed;
                        continue;
                    }

                    // 目的路徑與資料夾
                    string finalPath = item.ProposedPath;
                    if (string.IsNullOrWhiteSpace(finalPath))
                    {
                        // 若沒有預先計算 ProposedPath，就用 RoutingService 再算一次
                        finalPath = _routing.PreviewDestPath(src, null);
                    }

                    string destDir = Path.GetDirectoryName(finalPath) ?? (cfg?.Routing?.RootDir ?? "");
                    if (string.IsNullOrWhiteSpace(destDir))
                    {
                        item.Status = ItemStatus.Failed;
                        continue;
                    }

                    Directory.CreateDirectory(destDir);

                    // 目標檔名衝突處理
                    finalPath = ResolveCollision(finalPath, runtimePolicy);

                    // 實際搬移或複製
                    bool doOverwrite = string.Equals(runtimePolicy, "overwrite", StringComparison.OrdinalIgnoreCase);

                    if (string.Equals(moveMode, "move", StringComparison.OrdinalIgnoreCase))
                    {
                        // .NET 的 File.Move 沒有 overwrite 參數，若要覆蓋需先刪除
                        if (doOverwrite && File.Exists(finalPath))
                            File.Delete(finalPath);

                        File.Move(src, finalPath);
                    }
                    else
                    {
                        // copy
                        File.Copy(src, finalPath, overwrite: doOverwrite);
                    }

                    item.Status = ItemStatus.Moved;
                    item.DestPath = finalPath;
                    success++;
                }
                catch (Exception ex)
                {
                    item.Status = ItemStatus.Failed;
                    Console.WriteLine($"搬移失敗：{item?.FileName} -> {ex.Message}");
                }
            }

            await _db.UpdateItemsAsync(items);
            return success;
        }

        /// <summary>
        /// 根據字串策略處理目標檔案衝突。
        /// policy: "overwrite" / "skip" / "rename"
        /// </summary>
        public static string ResolveCollision(string finalPath, string policy)
        {
            try
            {
                if (!File.Exists(finalPath)) return finalPath;

                var p = Normalize(policy) ?? "rename";

                switch (p)
                {
                    case "skip":
                        // 交由上層決定要不要真的跳過；這裡保留原檔名
                        return finalPath;

                    case "overwrite":
                        // 讓上層以 overwrite 方式 Copy，或在 Move 前先刪除
                        return finalPath;

                    case "rename":
                    default:
                        string dir = Path.GetDirectoryName(finalPath) ?? "";
                        string baseName = Path.GetFileNameWithoutExtension(finalPath);
                        string ext = Path.GetExtension(finalPath);
                        int counter = 1;
                        string newName;
                        do
                        {
                            newName = Path.Combine(dir, $"{baseName}_{counter}{ext}");
                            counter++;
                        } while (File.Exists(newName));
                        return newName;
                }
            }
            catch
            {
                return finalPath;
            }
        }

        /// <summary>
        /// 取得指定資料夾下的所有合法檔案清單（排除黑名單副檔名）。
        /// </summary>
        public static IEnumerable<string> EnumerateFiles(string folder, bool includeSubdir, IEnumerable<string>? blacklistExts)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return Enumerable.Empty<string>();

            var searchOption = includeSubdir ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(folder, "*", searchOption);

            if (blacklistExts != null && blacklistExts.Any())
            {
                var bl = blacklistExts.Select(e => e.StartsWith(".") ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                                      .ToHashSet();
                files = files.Where(f => !bl.Contains(Path.GetExtension(f).ToLowerInvariant()));
            }

            return files;
        }

        private static string? Normalize(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToLowerInvariant();
    }
}
