using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// [V20.13.5] 混合視野修復版 (Hybrid Vision Fix)
    /// 1. Fix TreeView Empty: 恢復 GetDatabaseItemsAsync 回傳所有項目，解決左側點選空白問題。
    /// 2. Retain Locking: 保留 V20.13.3 的動態錨點鎖定邏輯。
    /// 3. Note: 視野過濾邏輯移交回 MainWindow.xaml.cs 的 ApplyListFilters 處理。
    /// </summary>
    public class MainWindowViewModel
    {
        // 服務參考
        private readonly DbService _db;
        private readonly RoutingService _router;
        private readonly LlmService _llm;
        private readonly HotFolderService _hotFolder;
        private readonly AppConfig _cfg; // 快取設定

        public MainWindowViewModel(DbService db, RoutingService router, LlmService llm, HotFolderService hotFolder, AppConfig cfg)
        {
            _db = db;
            _router = router;
            _llm = llm;
            _hotFolder = hotFolder;
            _cfg = cfg;
        }

        // ================================================================
        // [MC核心邏輯] 路徑最終裁決者 (Path Arbiter)
        // ================================================================

        private string ResolveFinalPath(Item item, string? lockedProjectName, string originalProposedPath)
        {
            // 1. [例外規則] 收件夾例外
            string hotFolder = _cfg.Import.HotFolder;
            if (!string.IsNullOrWhiteSpace(hotFolder) && !string.IsNullOrWhiteSpace(originalProposedPath))
            {
                string fullProposed = System.IO.Path.GetFullPath(originalProposedPath);
                string fullHot = System.IO.Path.GetFullPath(hotFolder);
                if (fullProposed.StartsWith(fullHot, StringComparison.OrdinalIgnoreCase))
                {
                    return originalProposedPath;
                }
            }

            // 2. [金性紀律] 專案鎖定邏輯 (動態錨點)
            if (!string.IsNullOrWhiteSpace(lockedProjectName) && lockedProjectName != "[所有專案]")
            {
                var routing = _cfg.Routing;
                string root = routing.RootDir;
                if (string.IsNullOrWhiteSpace(root)) root = _cfg.App.RootDir;

                if (string.IsNullOrWhiteSpace(root)) return originalProposedPath;

                var order = (routing.FolderOrder ?? new List<string> { "year", "month", "project", "category" })
                            .Select(x => x.ToLowerInvariant())
                            .ToList();

                int projectIndex = order.IndexOf("project");

                if (projectIndex == -1)
                {
                    return System.IO.Path.Combine(root, lockedProjectName, item.FileName);
                }

                var pathParts = new List<string> { root };

                for (int i = 0; i < order.Count; i++)
                {
                    string token = order[i];

                    if (token == "project")
                    {
                        pathParts.Add(lockedProjectName);
                        continue;
                    }

                    if (i > projectIndex)
                    {
                        if (token == "year" && routing.UseYear)
                            pathParts.Add(item.CreatedAt.Year.ToString());
                        else if (token == "month" && routing.UseMonth)
                            pathParts.Add(item.CreatedAt.ToString("MM"));
                        else if (token == "category" && routing.UseCategory)
                        {
                            string ext = System.IO.Path.GetExtension(item.FileName);
                            string cat = _router.MapExtensionToCategoryConfig(ext, _cfg);
                            if (!string.IsNullOrWhiteSpace(cat)) pathParts.Add(cat);
                        }
                    }
                }

                pathParts.Add(item.FileName);
                return System.IO.Path.Combine(pathParts.ToArray());
            }

            return originalProposedPath;
        }

        // ================================================================
        // 主要功能邏輯 (Commit / Scan)
        // ================================================================

        public async Task<int> AddFilesAsync(string[] sourceFiles, string hotPath)
        {
            int copiedCount = 0;
            await Task.Run(() =>
            {
                foreach (var srcFile in sourceFiles)
                {
                    try
                    {
                        var destFile = System.IO.Path.Combine(hotPath, System.IO.Path.GetFileName(srcFile));
                        if (File.Exists(destFile)) continue;
                        File.Copy(srcFile, destFile);
                        copiedCount++;
                    }
                    catch (Exception) { }
                }
            });
            return copiedCount;
        }

        public async Task ScanHotFolderAsync(SearchOption scanMode, bool scanOnlyHotFolder)
        {
            if (_hotFolder != null)
            {
                await _hotFolder.ScanAsync(scanMode, scanOnlyHotFolder);
            }
        }

        public async Task<(int okCount, List<Item> updatedItems, int processedCount)> CommitFilesAsync(IEnumerable<UiRow> selectedRows, string? lockedProjectName)
        {
            return await CommitCoreAsync(selectedRows.ToList(), lockedProjectName, null);
        }

        public async Task<(int okCount, List<Item> updatedItems, int processedCount)> CommitFilesAsync(IEnumerable<UiRow> allRows, string? lockedProjectName, string hotFolderPath)
        {
            List<UiRow> rowsToProcess;
            try
            {
                string hotFolderFullPath = System.IO.Path.GetFullPath(hotFolderPath);
                rowsToProcess = allRows.Where(r =>
                    (r.Status ?? "intaked").Equals("intaked", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(r.SourcePath) &&
                    System.IO.Path.GetFullPath(r.SourcePath).StartsWith(hotFolderFullPath, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"檢查 HotFolder 路徑時發生錯誤: {ex.Message}");
            }

            return await CommitCoreAsync(rowsToProcess, lockedProjectName, hotFolderPath);
        }

        private async Task<(int okCount, List<Item> updatedItems, int processedCount)> CommitCoreAsync(List<UiRow> rowsToProcess, string? lockedProjectName, string? hotFolderPath)
        {
            int ok = 0;
            var itemsToUpdate = new List<Item>();

            if (rowsToProcess.Count == 0) return (0, new List<Item>(), 0);

            await Task.Run(async () =>
            {
                foreach (var row in rowsToProcess)
                {
                    if (row.Status?.Equals("blacklisted", StringComparison.OrdinalIgnoreCase) == true) continue;

                    var it = row.Item;

                    string proposal = it.ProposedPath;
                    if (string.IsNullOrWhiteSpace(proposal))
                    {
                        proposal = _router.PreviewDestPath(it.Path);
                    }

                    string finalDestPath = ResolveFinalPath(it, lockedProjectName, proposal);

                    if (!string.IsNullOrWhiteSpace(lockedProjectName) && lockedProjectName != "[所有專案]")
                    {
                        bool isException = false;
                        if (!string.IsNullOrWhiteSpace(_cfg.Import.HotFolder))
                        {
                            if (System.IO.Path.GetFullPath(finalDestPath).StartsWith(System.IO.Path.GetFullPath(_cfg.Import.HotFolder), StringComparison.OrdinalIgnoreCase))
                            {
                                isException = true;
                            }
                        }

                        if (!isException)
                        {
                            it.Project = lockedProjectName;
                            it.ProposedPath = finalDestPath;
                        }
                    }
                    else
                    {
                        it.ProposedPath = finalDestPath;
                    }

                    var final = _router.Commit(it);

                    if (!string.IsNullOrWhiteSpace(final))
                    {
                        it.Status = "committed";
                        it.ProposedPath = final;

                        row.Status = "committed";
                        row.DestPath = final;
                        if (!string.IsNullOrWhiteSpace(it.Project))
                        {
                            row.Project = it.Project;
                        }

                        itemsToUpdate.Add(it);
                        ok++;
                    }
                }

                if (ok > 0)
                {
                    await _db.UpdateItemsAsync(itemsToUpdate);
                }
            });

            return (ok, itemsToUpdate, rowsToProcess.Count);
        }

        public async Task<(int deletedFiles, int failedFiles, List<string> deletedIds)> ClearCommittedFilesAsync(string hotPath)
        {
            string hotFolderFullPath = System.IO.Path.GetFullPath(hotPath);

            var allItems = await Task.Run(() => _db.QueryAllAsync());
            var committedInInbox = allItems.Where(it =>
                (it.Status == "committed") &&
                !string.IsNullOrWhiteSpace(it.Path) &&
                System.IO.Path.GetFullPath(it.Path).StartsWith(hotFolderFullPath, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (committedInInbox.Count == 0) return (0, 0, new List<string>());

            int deletedFiles = 0;
            int failedFiles = 0;

            await Task.Run(() =>
            {
                foreach (var item in committedInInbox)
                {
                    try
                    {
                        if (File.Exists(item.Path))
                        {
                            File.Delete(item.Path);
                            deletedFiles++;
                        }
                    }
                    catch (Exception) { failedFiles++; }
                }
            });

            var deletedIds = committedInInbox.Select(it => it.Id!).ToList();
            await Task.Run(() => _db.DeleteItemsAsync(deletedIds));

            return (deletedFiles, failedFiles, deletedIds);
        }

        public async Task<int> ResetInboxAsync()
        {
            return await Task.Run(() => _db.DeleteNonCommittedAsync());
        }

        // ================================================================
        // 資料庫與標籤邏輯 (修正回傳範圍)
        // ================================================================

        /// <summary>
        /// [MC修正] 資料庫視野復原
        /// 恢復回傳所有資料 (Pending + Committed)，讓 View 層自己決定要顯示什麼。
        /// 這樣左側 TreeView 點選時才能找到對應的已歸檔資料。
        /// </summary>
        public async Task<(List<Item> items, AppConfig cfg)> GetDatabaseItemsAsync()
        {
            // 1. 無條件抓取所有資料
            var allItems = await Task.Run(() => _db.QueryAllAsync());

            // 2. 僅根據時間排序，不做過濾
            // 過濾邏輯移交給 MainWindow.xaml.cs 的 ApplyListFilters
            var sortedItems = allItems.OrderByDescending(x => x.CreatedAt).ToList();

            return (sortedItems, _cfg);
        }

        public async Task<bool> ApplyTagSetAsync(IEnumerable<UiRow> rows, List<string> newTags)
        {
            if (!rows.Any()) return false;

            var newTagsNormalized = newTags
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var r in rows)
            {
                r.Item.Tags = newTagsNormalized;
                r.Tags = string.Join(",", newTagsNormalized);
            }

            await Task.Run(() => _db.UpdateItemsAsync(rows.Select(x => x.Item).ToArray()));
            return true;
        }

        public async Task<bool> ModifyTagsAsync(IEnumerable<UiRow> rows, string tag, bool add, bool exclusive)
        {
            if (!rows.Any()) return false;

            var exclusiveTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "InProgress", "Backlog", "Pending" };

            foreach (var r in rows)
            {
                var set = (r.Item.Tags ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (exclusive) set.RemoveWhere(t => exclusiveTags.Contains(t));

                if (add) set.Add(tag);
                else set.Remove(tag);

                r.Item.Tags = set.ToList();
                r.Tags = string.Join(",", set);
            }

            await Task.Run(() => _db.UpdateItemsAsync(rows.Select(x => x.Item).ToArray()));
            return true;
        }

        public async Task<bool> ApplyProjectAsync(UiRow row, string? newProjectName)
        {
            if (row == null || string.IsNullOrWhiteSpace(newProjectName)) return false;

            row.Project = newProjectName;
            row.Item.Project = newProjectName;

            await Task.Run(() => _db.UpdateItemsAsync(new[] { row.Item }));
            return true;
        }

        // ================================================================
        // AI 邏輯
        // ================================================================

        public async Task<string> GenerateSummaryAsync(UiRow row)
        {
            return await _llm.SummarizeAsync(row.FileName);
        }

        public async Task<double> AnalyzeConfidenceAsync(UiRow row)
        {
            return await _llm.AnalyzeConfidenceAsync(row.FileName);
        }

        public async Task<string> GenerateProjectAsync(UiRow row)
        {
            return await _llm.SuggestProjectAsync(row.FileName);
        }

        public async Task<bool> GenerateTagsAsync(IEnumerable<UiRow> rows)
        {
            foreach (var row in rows)
            {
                var tags = await _llm.SuggestTagsAsync(row.FileName);
                var set = (row.Item.Tags ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                set.UnionWith(tags);
                row.Item.Tags = set.ToList();
                row.Tags = string.Join(",", set);
            }

            await Task.Run(() => _db.UpdateItemsAsync(rows.Select(r => r.Item)));
            return true;
        }

        // ================================================================
        // 檔案 I/O 邏輯
        // ================================================================

        public async Task MoveOrCopyFolderToInboxAsync(string sourcePath, string hotPath, bool isMove)
        {
            string folderName = new DirectoryInfo(sourcePath).Name;
            string destPath = System.IO.Path.Combine(hotPath, folderName);

            await Task.Run(() =>
            {
                if (isMove) Directory.Move(sourcePath, destPath);
                else CopyDirectoryRecursively(sourcePath, destPath);
            });
        }

        private void CopyDirectoryRecursively(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) throw new DirectoryNotFoundException($"來源資料夾不存在: {dir.FullName}");

            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = System.IO.Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            foreach (DirectoryInfo subDir in dirs)
            {
                string newDestinationDir = System.IO.Path.Combine(destinationDir, subDir.Name);
                CopyDirectoryRecursively(subDir.FullName, newDestinationDir);
            }
        }

        public async Task CreateFolderAsync(string parentPath, string newName)
        {
            var newPath = System.IO.Path.Combine(parentPath, newName);
            if (Directory.Exists(newPath)) throw new IOException($"資料夾 '{newName}' 已存在。");
            await Task.Run(() => Directory.CreateDirectory(newPath));
        }

        public async Task RenameFolderAsync(string oldPath, string newName, string parentDir)
        {
            var newPath = System.IO.Path.Combine(parentDir, newName);
            await Task.Run(() => Directory.Move(oldPath, newPath));
            await UpdateDbPathsAsync(oldPath, newPath);
        }

        public async Task DeleteFolderAsync(string oldPath)
        {
            await Task.Run(() => Directory.Delete(oldPath, recursive: true));
            await DeleteDbPathsAsync(oldPath);
        }

        public async Task RenameFileAsync(string oldPath, string newName, string parentDir, Item item)
        {
            var newPath = System.IO.Path.Combine(parentDir, newName);
            await Task.Run(() => File.Move(oldPath, newPath));
            item.Path = newPath;
            await _db.UpdateItemsAsync(new[] { item });
        }

        public async Task<(List<string> deletedIds, List<Item> itemsToRefresh)> DeleteFilesAsync(IEnumerable<UiRow> rows)
        {
            var deletedIds = new List<string>();
            var itemsToRefresh = new List<Item>();

            foreach (var row in rows)
            {
                try
                {
                    if (File.Exists(row.SourcePath)) File.Delete(row.SourcePath);
                    if (row.Item.Id != null) deletedIds.Add(row.Item.Id);
                }
                catch (Exception) { itemsToRefresh.Add(row.Item); }
            }

            if (deletedIds.Count > 0) await _db.DeleteItemsAsync(deletedIds);
            return (deletedIds, itemsToRefresh);
        }

        public async Task DeleteRecordsAsync(IEnumerable<string> ids)
        {
            await _db.DeleteItemsAsync(ids);
        }

        // ================================================================
        // I/O 輔助方法 (DB 同步)
        // ================================================================

        public async Task<int> UpdateDbPathsAsync(string oldFolderPath, string newFolderPath)
        {
            var itemsToUpdate = new List<Item>();
            var allItems = await Task.Run(() => _db.QueryAllAsync());

            foreach (var item in allItems)
            {
                if (item.Path.StartsWith(oldFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.Path = System.IO.Path.Combine(newFolderPath, item.Path.Substring(oldFolderPath.Length + 1));
                    itemsToUpdate.Add(item);
                }
            }

            if (itemsToUpdate.Count > 0) await _db.UpdateItemsAsync(itemsToUpdate);
            return itemsToUpdate.Count;
        }

        public async Task<int> DeleteDbPathsAsync(string folderPath)
        {
            var idsToDelete = new List<string>();
            var allItems = await Task.Run(() => _db.QueryAllAsync());

            foreach (var item in allItems)
            {
                if (item.Path.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    idsToDelete.Add(item.Id!);
                }
            }

            if (idsToDelete.Count > 0) await _db.DeleteItemsAsync(idsToDelete);
            return idsToDelete.Count;
        }
    }
}