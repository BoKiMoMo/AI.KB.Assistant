using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 匯入 / 分類 / 搬檔流程。
    /// </summary>
    public sealed class IntakeService
    {
        private readonly DbService _db;
        private RoutingService _routing;
        private LlmService? _llm;
        private AppConfig _cfg;

        public IntakeService(DbService db, RoutingService routing, LlmService? llm, AppConfig cfg)
        {
            _db = db;
            _routing = routing;
            _llm = llm;
            _cfg = cfg ?? new AppConfig();
        }

        public void UpdateConfig(AppConfig cfg)
        {
            _cfg = cfg ?? _cfg;
            _routing.ApplyConfig(_cfg);
        }

        // ==================== Stage ====================

        /// <summary>
        /// 僅加入 Inbox，不移動檔案。
        /// </summary>
        public async Task StageOnlyAsync(string path, CancellationToken token)
        {
            await Task.Yield();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            var info = new FileInfo(path);

            var it = _db.TryGetByPath(path) ?? new Item();
            it.Path = path;
            it.Filename = info.Name;
            it.Ext = (info.Extension ?? "").Trim('.').ToLowerInvariant();
            it.Status = "inbox";
            if (it.CreatedTs <= 0)
                it.CreatedTs = new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeSeconds();

            _db.UpsertItem(it);
        }

        // ==================== Classify (預分類，不搬檔) ====================

        /// <summary>
        /// 對單筆進行預分類，將狀態從 inbox 設為 autosort-staging，寫入預估的 Project/Category/Tags/Confidence。
        /// </summary>
        public async Task ClassifyOnlyAsync(string filePath, CancellationToken token)
        {
            await Task.Yield();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

            // 找到 DB 記錄（由 MainWindow 事先可能已指定 Project）
            var it = _db.TryGetByPath(filePath) ?? new Item
            {
                Path = filePath,
                Filename = Path.GetFileName(filePath),
                Ext = Path.GetExtension(filePath)?.Trim('.').ToLowerInvariant(),
                CreatedTs = new DateTimeOffset(File.GetCreationTimeUtc(filePath)).ToUnixTimeSeconds()
            };

            // 類別：由副檔名群組推論
            var cat = _routing.GetCategoryForExtension(it.Ext);

            // 專案：若空，先走預設 yyyyMM
            var createdUtc = DateTimeOffset.FromUnixTimeSeconds(it.CreatedTs > 0 ? it.CreatedTs : DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcDateTime;
            var project = string.IsNullOrWhiteSpace(it.Project)
                ? _routing.GetDefaultProjectName(createdUtc)
                : it.Project!.Trim();

            // 信心：簡單啟發式
            // 已知群組：0.85；未知：0.55
            var confidence = string.Equals(cat, "Others", StringComparison.OrdinalIgnoreCase) ? 0.55 : 0.85;

            // （若未來要接 LLM，可在此覆寫 cat/project/confidence）

            it.Project = project;
            it.Category = cat;
            it.Confidence = confidence;
            it.Status = "autosort-staging";

            _db.UpsertItem(it);
        }

        // ==================== Commit (實搬檔) ====================

        /// <summary>
        /// 將 autosort-staging 的檔案依設定搬到最終位置；回傳成功處理的數量。
        /// </summary>
        public async Task<int> CommitPendingAsync(CancellationToken token)
        {
            await Task.Yield();

            var items = _db.QueryByStatus("autosort-staging").ToList();

            int ok = 0;
            foreach (var it in items)
            {
                if (token.IsCancellationRequested) break;

                try
                {
                    if (string.IsNullOrWhiteSpace(it.Path) || !File.Exists(it.Path))
                    {
                        // 原始檔不在：直接標記為 auto-sorted 以免卡住（或也可刪除）
                        it.Status = "auto-sorted";
                        _db.UpsertItem(it);
                        continue;
                    }

                    var moveMode = NormalizeMoveMode(_cfg?.Import?.MoveMode);
                    var policy = NormalizeOverwritePolicy(_cfg?.Import?.OverwritePolicy);

                    // 判斷是否信心不足（低於門檻）
                    var threshold = _cfg?.Classification?.ConfidenceThreshold ?? 0.65;
                    bool low = it.Confidence < threshold;

                    var (folder, fileName) = _routing.PlanTarget(it.Path!, it.Project, it.Category, low);
                    _routing.EnsureDirectory(folder);

                    var targetPath = Path.Combine(folder, fileName);
                    targetPath = ResolveConflict(targetPath, policy);

                    // 實際 Copy/Move
                    if (moveMode == MoveMode_Copy)
                    {
                        SafeCopy(it.Path!, targetPath, policy);
                    }
                    else
                    {
                        // Move
                        SafeMove(it.Path!, targetPath, policy);
                    }

                    // 更新 DB（指向新位置；狀態改成 auto-sorted）
                    it.Path = targetPath;
                    it.Status = "auto-sorted";
                    _db.UpsertItem(it);

                    ok++;
                }
                catch
                {
                    // 單筆失敗忽略，避免中斷流程
                }
            }

            return ok;
        }

        // ==================== 檔案操作（含覆蓋策略） ====================

        private static readonly string MoveMode_Move = "move";
        private static readonly string MoveMode_Copy = "copy";

        private static string NormalizeMoveMode(object? moveMode)
        {
            if (moveMode == null) return MoveMode_Move;

            // enum -> string
            var s = moveMode.ToString()!.Trim().ToLowerInvariant();
            return (s == "copy") ? MoveMode_Copy : MoveMode_Move;
        }

        private enum ConflictBehavior { Replace, Rename, Skip }

        private static ConflictBehavior NormalizeOverwritePolicy(object? policy)
        {
            var s = (policy?.ToString() ?? "").Trim().ToLowerInvariant();
            return s switch
            {
                "replace" => ConflictBehavior.Replace,
                "skip" => ConflictBehavior.Skip,
                _ => ConflictBehavior.Rename, // 預設 Rename
            };
        }

        private static string ResolveConflict(string targetPath, ConflictBehavior policy)
        {
            if (!File.Exists(targetPath)) return targetPath;

            switch (policy)
            {
                case ConflictBehavior.Replace:
                    // 讓後續 Copy/Move 使用覆蓋方式處理
                    return targetPath;

                case ConflictBehavior.Skip:
                    // 保持原路徑（後續若判斷目標存在就不做）
                    return targetPath;

                case ConflictBehavior.Rename:
                default:
                    // 產生 "name (n).ext" 格式
                    var dir = Path.GetDirectoryName(targetPath)!;
                    var name = Path.GetFileNameWithoutExtension(targetPath);
                    var ext = Path.GetExtension(targetPath);
                    int i = 1;
                    string candidate;
                    do
                    {
                        candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                        i++;
                    } while (File.Exists(candidate));
                    return candidate;
            }
        }

        private static void SafeCopy(string src, string dst, ConflictBehavior policy)
        {
            if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) return;

            if (File.Exists(dst))
            {
                if (policy == ConflictBehavior.Skip) return;
                // Replace or Rename（Rename 已在 ResolveConflict 處理）
                File.Copy(src, dst, overwrite: (policy == ConflictBehavior.Replace));
            }
            else
            {
                File.Copy(src, dst, overwrite: false);
            }
        }

        private static void SafeMove(string src, string dst, ConflictBehavior policy)
        {
            if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) return;

            if (File.Exists(dst))
            {
                if (policy == ConflictBehavior.Skip) return;
                if (policy == ConflictBehavior.Replace)
                {
                    // 先刪除再 Move
                    try { File.Delete(dst); } catch { }
                    File.Move(src, dst);
                }
                else
                {
                    // Rename 已在 ResolveConflict 處理：直接 Move
                    File.Move(src, dst);
                }
            }
            else
            {
                File.Move(src, dst);
            }
        }
    }
}
