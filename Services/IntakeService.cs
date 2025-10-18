using AI.KB.Assistant.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AI.KB.Assistant.Services
{
    public sealed class IntakeService
    {
        private readonly DbService _db;
        private readonly RoutingService _routing;
        private readonly LlmService _llm;
        private readonly AppConfig _cfg;

        public IntakeService(DbService db, RoutingService routing, LlmService llm, AppConfig cfg)
        {
            _db = db;
            _routing = routing;
            _llm = llm;
            _cfg = cfg;
        }

        // ----- Stage only：丟入 DB, 狀態 = inbox -----
        public async Task StageOnlyAsync(string filePath, CancellationToken ct)
        {
            if (!File.Exists(filePath)) return;

            var fn = Path.GetFileName(filePath);
            var ext = Path.GetExtension(filePath);
            var type = RoutingService.FamilyFromExt(ext);
            var cat = DetectCategory(fn, ext);

            _db.UpsertItem(filePath, fn, cat, project: "", status: "inbox", filetype: type, conf: 0.7);
            await Task.Delay(1, ct);
        }

        // ----- Classify only：算分類，不搬 -----
        public async Task ClassifyOnlyAsync(string filePath, CancellationToken ct)
        {
            if (!File.Exists(filePath)) return;

            var fn = Path.GetFileName(filePath);
            var ext = Path.GetExtension(filePath);

            // 先用規則；必要時 LLM
            var cat = DetectCategory(fn, ext, out double conf, out string? reasoning);

            if (_cfg.OpenAI.EnableWhenLowConfidence && conf < _cfg.Classification.ConfidenceThreshold && _llm.IsReady)
            {
                var ai = await _llm.TryClassifyAsync(fn, ct);
                if (ai != null && ai.Value.confidence > conf)
                {
                    conf = ai.Value.confidence;
                    cat = ai.Value.category;
                    reasoning = ai.Value.reasoning;
                }
            }

            _db.UpsertItem(filePath, fn, cat, project: "", status: "pending",
                           filetype: RoutingService.FamilyFromExt(ext), conf: conf, reasoning: reasoning);
        }

        // ----- Commit pending：實際搬檔 -----
        public async Task<int> CommitPendingAsync(CancellationToken ct)
        {
            int moved = 0;
            var list = _db.QueryByStatus("pending").ToList();

            foreach (dynamic it in list)
            {
                ct.ThrowIfCancellationRequested();

                string path = it.path;
                string filename = it.filename;
                string category = it.category ?? _cfg.Classification.FallbackCategory ?? "自整理";
                string project = it.project ?? _cfg.App.ProjectLock ?? "";
                string destDir = _routing.BuildDestination(_cfg, project, category, Path.GetExtension(filename));

                Directory.CreateDirectory(destDir);

                var dest = Path.Combine(destDir, filename);
                if (File.Exists(path))
                {
                    // 同名處理：rename
                    if (File.Exists(dest))
                    {
                        var name = Path.GetFileNameWithoutExtension(dest);
                        var ext = Path.GetExtension(dest);
                        dest = Path.Combine(destDir, $"{name} - {DateTime.Now:HHmmss}{ext}");
                    }
                    File.Move(path, dest);
                }

                _db.UpdateStatus((long)it.id, "auto-sorted");
                moved++;
                await Task.Delay(1, ct);
            }

            return moved;
        }

        // ----- 分類規則（含副檔名整併） -----
        private string DetectCategory(string filename, string? ext) =>
            DetectCategory(filename, ext, out _, out _);

        private string DetectCategory(string filename, string? ext, out double conf, out string? reasoning)
        {
            var name = filename.ToLowerInvariant();
            conf = 0.7;
            reasoning = null;

            // 關鍵字（可從 config KeywordMap 延伸）
            var map = _cfg.Classification.KeywordMap ?? new();
            foreach (var (kw, cat) in map)
            {
                if (!string.IsNullOrWhiteSpace(kw) && name.Contains(kw))
                {
                    conf = 0.9; reasoning = $"keyword:{kw}";
                    return cat;
                }
            }

            // Office / PDF / Adobe / 圖片 / 影片 / 程式…（副檔名整併）
            var type = RoutingService.FamilyFromExt(ext);
            return type switch
            {
                "PDF" => "PDF",
                "Word" => "文件",
                "Excel" => "試算表",
                "PowerPoint" => "簡報",
                "Illustrator" or "Photoshop" or "XD" or "InDesign" or "Figma" or "Sketch" => "設計稿",
                "圖片" => "圖片",
                "影片" => "影音",
                "音訊" => "音訊",
                "程式碼" => "程式",
                _ => _cfg.Classification.FallbackCategory ?? (_cfg.Routing.AutoFolderName ?? "自整理"),
            };
        }
    }
}
