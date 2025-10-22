using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class HotFolderService : IDisposable
    {
        private readonly IntakeService _intake;
        private readonly AppConfig _cfg;
        private FileSystemWatcher? _fw;

        public HotFolderService(IntakeService intake, AppConfig cfg)
        {
            _intake = intake;
            _cfg = cfg;
        }

        public void Start()
        {
            try
            {
                if (_cfg.Import.EnableHotFolder != true) return;

                var path = _cfg.Import.HotFolderPath;
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

                _fw = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = _cfg.Import.IncludeSubdirectories,
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
                };
                _fw.Created += OnCreated;
                _fw.Renamed += OnCreated;
            }
            catch { /* ignore */ }
        }

        private async void OnCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (Directory.Exists(e.FullPath)) return;

                var dir = Path.GetDirectoryName(e.FullPath) ?? "";
                var blocks = _cfg.Import.BlacklistFolderNames ?? Array.Empty<string>();
                if (blocks.Any() && blocks.Any(b => !string.IsNullOrWhiteSpace(b) &&
                    dir.Contains(b, StringComparison.OrdinalIgnoreCase)))
                    return;

                await Task.Delay(600); // 等檔案穩定
                await _intake.StageOnlyAsync(e.FullPath, CancellationToken.None);
            }
            catch { /* ignore */ }
        }

        public void Dispose()
        {
            try
            {
                if (_fw != null)
                {
                    _fw.EnableRaisingEvents = false;
                    _fw.Created -= OnCreated;
                    _fw.Renamed -= OnCreated;
                    _fw.Dispose();
                }
            }
            catch { }
        }
    }
}
