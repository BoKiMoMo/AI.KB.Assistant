using AI.KB.Assistant.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 負責檔案的「加入 → 預分類 → 提交搬檔」流程。
    /// 依賴：DbService（Upsert/Query）、RoutingService（路徑規則）、LlmService（可選）、AppConfig（設定）。
    /// </summary>
    public class IntakeService : IDisposable
    {
        private DbService _db;
        private RoutingService _routing;
        private LlmService _llm;
        private AppConfig _cfg;

        public IntakeService(DbService db, RoutingService routing, LlmService llm, AppConfig cfg)
        {
            _db = db;
            _routing = routing;
            _llm = llm;
            _cfg = cfg;
        }

        public void UpdateConfig(AppConfig cfg)
        {
            _cfg = cfg;
            _routing.ApplyConfig(cfg);
            _llm.UpdateConfig(cfg);
        }

        public void Dispose()
        {
            try { _db?.Dispose(); } catch { }
        }

        /// <summary>
        /// 僅加入一個檔案到 DB，狀態設為 inbox，不做搬檔與實體動作。
        /// - 排除 Import.BlacklistExts
        /// - 若 DB 已存在同路徑，則更新必要欄位（保持使用者變更）
        /// </summary>
        public async Task<bool> StageOnlyAsync(string srcFullPath, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                if (ct.IsCancellationRequested) return false;
                if (string.IsNullOrWhiteSpace(srcFullPath) || !File.Exists(srcFullPath)) return false;

                if (IsHiddenOrSystem(srcFullPath)) return false;

                var ext = Path.GetExtension(srcFullPath).TrimStart('.').ToLowerInvariant();
                var blExt = (_cfg.Import?.BlacklistExts ?? Array.Empty<string>())
                            .Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
                            .ToHashSet();
                if (blExt.Contains(ext)) return false;

                var fi = new FileInfo(srcFullPath);

                // 先從 DB 取舊資料（若有）
                var existing = _db.QueryByPath(srcFullPath).FirstOrDefault();

                var item = existing ?? new Item
                {
                    CreatedTs = new DateTimeOffset(fi.CreationTimeUtc).ToUnixTimeSeconds(),
                };

                item.Filename = fi.Name;
                item.Ext = ext;
                item.Path = fi.FullName;
                item.Status = "inbox";

                // 讓 ProposedPath 可被 UI 顯示（只是暫存，DB 欄位若沒有可忽略）
                try { item.ProposedPath = _routing.PreviewDestPath(item.Path!, _cfg.App?.ProjectLock); } catch { }

                _db.Upsert(item);
                return true;
            }, ct);
        }

        /// <summary>
        /// 僅執行分類（不搬檔）：計算預計路徑、可選擇呼叫 LLM 豐富標籤/建議。
        /// - 狀態由 inbox → staged
        /// </summary>
        public async Task<bool> ClassifyOnlyAsync(string srcFullPath, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                if (ct.IsCancellationRequested) return false;

                var item = _db.QueryByPath(srcFullPath).FirstOrDefault();
                if (item == null)
                {
                    // 若 DB 尚無，先加入
                    StageOnlyAsync(srcFullPath, ct).GetAwaiter().GetResult();
                    item = _db.QueryByPath(srcFullPath).FirstOrDefault();
                    if (item == null) return false;
                }

                // 可在此塞入你的分類邏輯（例如：依檔名解析 Project/Category）
                // 目前先維持 Project 走「鎖定優先」策略，Category 由副檔名群組推估
                var locked = FirstNonEmpty(_cfg.App?.ProjectLock, item.Project);
                if (!string.IsNullOrWhiteSpace(locked))
                    item.Project = locked;

                item.Category = ResolveCategoryByExt(item.Ext);
                item.Confidence = Math.Max(item.Confidence, 0.65); // 給預設中性信心

                // 低信心時可呼叫 LLM 產出建議（不改 DB，僅供 UI 顯示或之後套用）
                // 這裡保留掛鉤點；若需要立即落 DB，請在此對 item.Tags 等欄位寫入後 Upsert
                var isLow = item.Confidence < (_cfg.Classification?.ConfidenceThreshold ?? 0.75);
                if (isLow && _cfg.OpenAI?.EnableWhenLowConfidence == true)
                {
                    try
                    {
                        // var suggestion = _llm.Suggest(item); // 若你有此 API，可打開
                        // item.Tags = string.IsNullOrWhiteSpace(item.Tags) ? suggestion.Tags : item.Tags;
                    }
                    catch { /* LLM 失敗不阻斷流程 */ }
                }

                // 提供 UI 顯示的預計路徑
                try { item.ProposedPath = _routing.PreviewDestPath(item.Path!, _cfg.App?.ProjectLock); } catch { }

                item.Status = "staged";
                _db.Upsert(item);
                return true;
            }, ct);
        }

        /// <summary>
        /// 提交所有待搬檔（staged）的檔案，依 MoveMode 與 OverwritePolicy 實體搬檔。
        /// - 成功搬檔後，更新 DB：Path = 新路徑、Status = sorted、CreatedTs 不變
        /// - 回傳成功搬檔數量
        /// </summary>
        public async Task<int> CommitPendingAsync(CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                if (ct.IsCancellationRequested) return 0;
                var staged = _db.QueryByStatus("staged").ToList();
                if (staged.Count == 0) return 0;

                int success = 0;

                foreach (var it in staged)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        if (string.IsNullOrWhiteSpace(it.Path) || !File.Exists(it.Path))
                            continue;

                        var low = it.Confidence < (_cfg.Classification?.ConfidenceThreshold ?? 0.75);
                        var dest = _routing.BuildDestination(it, isBlacklist: false, isLowConfidence: low, lockedProject: _cfg.App?.ProjectLock);
                        if (string.IsNullOrWhiteSpace(dest)) continue;

                        var destDir = Path.GetDirectoryName(dest)!;
                        Directory.CreateDirectory(destDir);

                        var finalDest = ResolveCollision(dest, _cfg.Import?.OverwritePolicy ?? OverwritePolicy.Rename);

                        // Move or Copy
                        if ((_cfg.Import?.MoveMode ?? MoveMode.Move) == MoveMode.Move)
                        {
                            // 自己移動到自己：跳過
                            if (!Path.GetFullPath(it.Path).Equals(Path.GetFullPath(finalDest), StringComparison.OrdinalIgnoreCase))
                            {
                                SafeDelete(finalDest); // Replace 情境會先刪
                                File.Move(it.Path!, finalDest, overwrite: false);
                            }
                        }
                        else
                        {
                            SafeDelete(finalDest); // Replace 情境會先刪
                            File.Copy(it.Path!, finalDest, overwrite: false);
                        }

                        // 更新 DB
                        it.Path = finalDest;
                        it.Status = "sorted";
                        it.ProposedPath = finalDest;
                        _db.Upsert(it);
                        success++;
                    }
                    catch
                    {
                        // 單筆失敗不影響其他
                    }
                }

                return success;
            }, ct);
        }

        // ----------------- Helpers -----------------

        private static bool IsHiddenOrSystem(string path)
        {
            try
            {
                var a = File.GetAttributes(path);
                return (a & FileAttributes.Hidden) != 0 || (a & FileAttributes.System) != 0;
            }
            catch { return false; }
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v!;
            return string.Empty;
        }

        /// <summary>
        /// 由副檔名推估類別（用 Import.ExtGroupMap）
        /// </summary>
        private string ResolveCategoryByExt(string? ext)
        {
            if (_cfg?.Import?.ExtGroupMap == null || string.IsNullOrWhiteSpace(ext)) return string.Empty;
            foreach (var kv in _cfg.Import.ExtGroupMap)
                if (kv.Value.Contains(ext.Trim('.')))
                    return kv.Key;
            return "其他";
        }

        /// <summary>
        /// 依 OverwritePolicy 處理碰撞並回傳最終可寫入目的地。
        /// Replace：刪除舊檔 → 回傳原路徑
        /// Rename：以 (1)…(n) 產生唯一檔名
        /// Skip：回傳空字串（上層會跳過）
        /// </summary>
        private string ResolveCollision(string destFullPath, OverwritePolicy policy)
        {
            if (!File.Exists(destFullPath)) return destFullPath;

            switch (policy)
            {
                case OverwritePolicy.Replace:
                    return destFullPath; // 上層會先 SafeDelete 再 Move/Copy

                case OverwritePolicy.Rename:
                    {
                        var dir = Path.GetDirectoryName(destFullPath)!;
                        var name = Path.GetFileNameWithoutExtension(destFullPath);
                        var ext = Path.GetExtension(destFullPath);
                        int i = 1;
                        string candidate;
                        do
                        {
                            candidate = Path.Combine(dir, $"{name} ({i++}){ext}");
                        } while (File.Exists(candidate));
                        return candidate;
                    }

                case OverwritePolicy.Skip:
                default:
                    return string.Empty;
            }
        }

        private static void SafeDelete(string maybeExistsFile)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(maybeExistsFile) && File.Exists(maybeExistsFile))
                    File.Delete(maybeExistsFile);
            }
            catch { }
        }
    }
}
